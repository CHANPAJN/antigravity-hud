using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AntigravityHud
{
    internal sealed class TaskbarCapsuleBarForm : Form
    {
        private const int LongPressThresholdMs = 260;
        private const int DragStartPixels = 6;
        private const int SnapThresholdPx = 10;
        private const int EdgeMarginLogicalPx = 6;

        private readonly float _dpi;
        private readonly Action _onLeftTogglePopup;
        private readonly Action _onPositionChanged;
        private readonly ContextMenuStrip _rightMenu;
        private readonly UserConfig _config;
        private readonly System.Windows.Forms.Timer _longPressTimer;

        private DashboardData _data = new DashboardData();
        private ScaleMode _scaleMode = ScaleMode.Auto;
        private bool _hovered;
        private bool _popupOpen;
        private string _lastRenderSig = "";
        private int _barWidth;
        private readonly int _barHeight;

        // Long-press drag state machine
        private bool _leftPressActive;
        private bool _isDragging;
        private Point _pressStartScreenPt;
        private Rectangle _pressStartRect;
        private Point _dragCurrentTopLeft;

        public TaskbarCapsuleBarForm(
            float dpiScale,
            UserConfig config,
            Action onLeftTogglePopup,
            Action onPositionChanged,
            ContextMenuStrip rightMenu)
        {
            _dpi = dpiScale;
            _config = config ?? new UserConfig();
            _onLeftTogglePopup = onLeftTogglePopup;
            _onPositionChanged = onPositionChanged;
            _rightMenu = rightMenu;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            _barHeight = S(30);
            _barWidth = S(380);
            Size = new Size(_barWidth, _barHeight);
            Cursor = Cursors.Hand;

            _longPressTimer = new System.Windows.Forms.Timer { Interval = LongPressThresholdMs };
            _longPressTimer.Tick += OnLongPressTimerTick;

            MouseEnter += (s, e) =>
            {
                if (!_hovered) { _hovered = true; RenderLayered(true); }
            };
            MouseLeave += (s, e) =>
            {
                if (!_isDragging && _hovered) { _hovered = false; RenderLayered(true); }
            };
            MouseDown += OnBarMouseDown;
            MouseMove += OnBarMouseMove;
            MouseUp += OnBarMouseUp;
            MouseCaptureChanged += OnBarCaptureChanged;
        }

        public bool IsDragging
        {
            get { return _isDragging; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00080000 | 0x00000080 | 0x00000008 | 0x08000000;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x0021;
            const int MA_NOACTIVATE = 3;
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = new IntPtr(MA_NOACTIVATE);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _longPressTimer != null)
            {
                _longPressTimer.Stop();
                _longPressTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private int S(float logicalPx)
        {
            return (int)Math.Round(logicalPx * _dpi);
        }

        public void SetPopupOpenState(bool isOpen)
        {
            if (_popupOpen != isOpen)
            {
                _popupOpen = isOpen;
                RenderLayered(true);
            }
        }

        public void UpdateTelemetry(DashboardData data, ScaleMode scaleMode)
        {
            _data = data ?? new DashboardData();
            _scaleMode = scaleMode;
            if (!_isDragging)
            {
                RenderLayered(false);
            }
        }

        public void ResetToTaskbarDefault()
        {
            _config.AnchorMode = "taskbar-auto";
            _config.HorizontalAlign = "right";
            _config.MonitorDeviceName = "";
            if (_onPositionChanged != null) _onPositionChanged();
            RenderLayered(true);
            EnsureDockedAndZOrder();
        }

        public void HandleDisplaySettingsChanged()
        {
            if (_isDragging) return;
            RenderLayered(true);
            EnsureDockedAndZOrder();
        }

        private void OnBarMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _leftPressActive = true;
                _isDragging = false;
                _pressStartScreenPt = Cursor.Position;
                _pressStartRect = GetActualScreenBounds();
                _dragCurrentTopLeft = new Point(_pressStartRect.X, _pressStartRect.Y);

                if (!_config.PositionLocked)
                {
                    _longPressTimer.Stop();
                    _longPressTimer.Start();
                }
            }
            else if (e.Button == MouseButtons.Right && _rightMenu != null)
            {
                CancelPendingPress();
                _rightMenu.Show(Cursor.Position);
            }
        }

        private void OnLongPressTimerTick(object sender, EventArgs e)
        {
            _longPressTimer.Stop();
            if (_leftPressActive && !_isDragging && !_config.PositionLocked)
            {
                BeginDrag();
            }
        }

        private void BeginDrag()
        {
            _isDragging = true;
            _dragCurrentTopLeft = new Point(_pressStartRect.X, _pressStartRect.Y);
            Capture = true;
            Cursor = Cursors.SizeAll;

            // Hide popup while repositioning so nothing overlaps awkwardly
            if (_popupOpen && _onLeftTogglePopup != null)
            {
                _onLeftTogglePopup();
            }

            RenderLayered(true);
        }

        private void OnBarMouseMove(object sender, MouseEventArgs e)
        {
            if (!_leftPressActive) return;

            Point curPt = Cursor.Position;
            int dx = curPt.X - _pressStartScreenPt.X;
            int dy = curPt.Y - _pressStartScreenPt.Y;

            if (!_isDragging)
            {
                if (!_config.PositionLocked && (Math.Abs(dx) >= DragStartPixels || Math.Abs(dy) >= DragStartPixels))
                {
                    _longPressTimer.Stop();
                    BeginDrag();
                }
                else
                {
                    return;
                }
            }

            int rawX = _pressStartRect.X + dx;
            int rawY = _pressStartRect.Y + dy;
            Point snapped = ApplySnapAndClamp(rawX, rawY, _barWidth, _barHeight, curPt, (ModifierKeys & Keys.Alt) == 0);
            _dragCurrentTopLeft = snapped;

            NativeMethods.SetWindowPos(
                Handle, NativeMethods.HWND_TOPMOST,
                snapped.X, snapped.Y, _barWidth, _barHeight,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }

        private void OnBarMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !_leftPressActive) return;

            _longPressTimer.Stop();
            _leftPressActive = false;

            if (_isDragging)
            {
                FinishDrag(true);
            }
            else
            {
                if (_onLeftTogglePopup != null)
                {
                    _onLeftTogglePopup();
                }
            }
        }

        private void OnBarCaptureChanged(object sender, EventArgs e)
        {
            if (_isDragging && !Capture)
            {
                _longPressTimer.Stop();
                _leftPressActive = false;
                FinishDrag(true);
            }
        }

        private void CancelPendingPress()
        {
            _longPressTimer.Stop();
            _leftPressActive = false;
            if (_isDragging)
            {
                FinishDrag(false);
            }
        }

        private void FinishDrag(bool commitPosition)
        {
            _isDragging = false;
            if (Capture) Capture = false;
            Cursor = Cursors.Hand;

            if (commitPosition)
            {
                Rectangle cur = GetActualScreenBounds();
                Screen scr = Screen.FromRectangle(cur);
                int centerX = cur.Left + cur.Width / 2;
                int scrCenterX = scr.Bounds.Left + scr.Bounds.Width / 2;
                bool alignRight = centerX >= scrCenterX;

                _config.AnchorMode = "custom";
                _config.MonitorDeviceName = scr.DeviceName ?? "";
                _config.HorizontalAlign = alignRight ? "right" : "left";
                // Anchor to right edge when placed on the right half so dynamic width changes never drift off-screen
                _config.CustomX = alignRight ? cur.Right : cur.Left;
                _config.CustomY = cur.Top;

                if (_onPositionChanged != null)
                {
                    _onPositionChanged();
                }
            }

            RenderLayered(true);
            EnsureDockedAndZOrder();
        }

        private Point ApplySnapAndClamp(int rawX, int rawY, int barW, int barH, Point cursorScreenPt, bool enableSnap)
        {
            Screen scr = Screen.FromPoint(cursorScreenPt);
            Rectangle sb = scr.Bounds;
            Rectangle wa = scr.WorkingArea;
            int margin = S(EdgeMarginLogicalPx);
            int snap = S(SnapThresholdPx);

            int x = rawX;
            int y = rawY;

            if (enableSnap)
            {
                // Snap to working area edges
                if (Math.Abs(x - (wa.Left + margin)) <= snap) x = wa.Left + margin;
                else if (Math.Abs((x + barW) - (wa.Right - margin)) <= snap) x = wa.Right - margin - barW;

                if (Math.Abs(y - (wa.Top + margin)) <= snap) y = wa.Top + margin;
                else if (Math.Abs((y + barH) - (wa.Bottom - margin)) <= snap) y = wa.Bottom - margin - barH;

                // Snap vertically onto primary taskbar center line when dragging near taskbar
                IntPtr hTray = NativeMethods.FindWindow("Shell_TrayWnd", null);
                if (hTray != IntPtr.Zero)
                {
                    NativeMethods.RECT rcTray;
                    if (NativeMethods.GetWindowRect(hTray, out rcTray) && (rcTray.Bottom - rcTray.Top) >= 24)
                    {
                        if (cursorScreenPt.X >= rcTray.Left && cursorScreenPt.X <= rcTray.Right)
                        {
                            int trayCenterY = rcTray.Top + ((rcTray.Bottom - rcTray.Top) - barH) / 2;
                            if (Math.Abs(y - trayCenterY) <= snap + S(4))
                            {
                                y = trayCenterY;
                            }
                        }
                    }
                }
            }

            // Hard clamp inside active screen bounds so the capsule can never get lost off-screen
            int minX = sb.Left + S(2);
            int maxX = Math.Max(minX, sb.Right - barW - S(2));
            int minY = sb.Top + S(2);
            int maxY = Math.Max(minY, sb.Bottom - barH - S(2));

            x = Math.Max(minX, Math.Min(x, maxX));
            y = Math.Max(minY, Math.Min(y, maxY));
            return new Point(x, y);
        }

        private long ResolveBudget(HudSnapshot hud)
        {
            if (_scaleMode == ScaleMode.K256) return 256000L;
            if (_scaleMode == ScaleMode.M1) return 983040L;
            long used = (hud != null && hud.Latest != null) ? hud.Latest.TotalUsed : 0L;
            return used > 512000L ? 983040L : 512000L;
        }

        public Bitmap BuildBarBitmap(out int computedW)
        {
            HudSnapshot hud = _data != null ? _data.ActiveHud : null;
            TurnItem lat = hud != null ? hud.Latest : null;
            long budget = ResolveBudget(hud);
            long used = lat != null ? lat.TotalUsed : 0L;
            double rawPct = budget > 0 ? Math.Min(100.0, Math.Max(0.0, used * 100.0 / budget)) : 0.0;
            int pctRound = (rawPct > 0 && rawPct < 1.0) ? 1 : (int)Math.Round(rawPct);

            string sPct = pctRound + "%";
            string sUsed = used > 0 ? I18n.FmtCompact(used) : "0";
            double cPct = lat != null ? lat.CacheHitPct : 0.0;
            string sCache = (hud != null && hud.TotalTurns <= 1 && cPct < 0.5)
                ? I18n.T("首轮", "1st")
                : (Math.Round(cPct) + "%");
            bool activeHere = hud != null && hud.IsRunning && hud.ActiveElapsedSec > 0;
            string sTps;
            if (activeHere)
            {
                sTps = hud.ActivePhaseLabel + " · " + hud.ActiveElapsedSec + "s";
            }
            else if (lat != null && lat.Tps.HasValue)
            {
                sTps = Math.Round(lat.Tps.Value) + " t/s" + (lat.DurationSec.HasValue ? (" · " + Math.Round(lat.DurationSec.Value) + "s") : "");
            }
            else if (lat != null && lat.DurationSec.HasValue)
            {
                sTps = Math.Round(lat.DurationSec.Value) + "s";
            }
            else
            {
                sTps = "-- t/s";
            }
            string mTag = lat != null ? lat.ShortTag : I18n.T("待命", "Idle");
            string mTone = lat != null ? lat.Tone : "muted";
            string sTurns = (hud != null && hud.TotalTurns > 0)
                ? I18n.TF("{0}轮", "{0}t", hud.TotalTurns)
                : "";
            string iqLbl = hud != null ? hud.IqLabel : I18n.T("思考", "Think");
            string iqPill = hud != null ? hud.IqPill : "--";
            string iqLv = hud != null ? hud.IqLevel : "idle";
            bool running = (hud != null && hud.IsRunning) || (_data != null && _data.ActiveWorkingCount > 0);
            string fontFamily = I18n.UiFontFamily;

            using (var fBold = new Font(fontFamily, Math.Max(11f, S(12.2f)), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fReg = new Font(fontFamily, Math.Max(11f, S(12.0f)), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fMono = new Font(fontFamily, Math.Max(10.5f, S(11.5f)), FontStyle.Bold, GraphicsUnit.Pixel))
            {
                int wPass;
                using (var tmpBmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb))
                using (Graphics gm = Graphics.FromImage(tmpBmp))
                {
                    gm.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    wPass = DrawCapsuleContents(gm, 0, _barHeight, false,
                        rawPct, sPct, sUsed, cPct, hud != null ? hud.TotalTurns : 0, sCache,
                        sTps, mTag, mTone, sTurns, iqLbl, iqPill, iqLv, running, _isDragging,
                        fBold, fReg, fMono);
                }

                computedW = Math.Max(S(240), wPass);
                var bmp = new Bitmap(computedW, _barHeight, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);

                    var rcPill = new Rectangle(1, 1, computedW - 2, _barHeight - 2);
                    Color bgCol;
                    Color bdCol;
                    float bdWidth;

                    if (_isDragging)
                    {
                        bgCol = Color.FromArgb(252, 239, 246, 255);
                        bdCol = Color.FromArgb(255, 37, 99, 235);
                        bdWidth = 1.9f;
                    }
                    else if (_popupOpen)
                    {
                        bgCol = Color.FromArgb(250, 240, 247, 255);
                        bdCol = Color.FromArgb(245, 59, 130, 246);
                        bdWidth = 1.5f;
                    }
                    else if (_hovered)
                    {
                        bgCol = Color.FromArgb(252, 255, 255, 255);
                        bdCol = Color.FromArgb(245, 59, 130, 246);
                        bdWidth = 1.5f;
                    }
                    else
                    {
                        bgCol = Color.FromArgb(246, 255, 255, 255);
                        bdCol = running ? Color.FromArgb(220, 16, 185, 129) : Color.FromArgb(215, 198, 208, 220);
                        bdWidth = 1.1f;
                    }

                    using (GraphicsPath gp = MakeRoundRect(rcPill, (_barHeight - 2) / 2))
                    {
                        using (var brBg = new SolidBrush(bgCol))
                        {
                            g.FillPath(brBg, gp);
                        }
                        using (var penBd = new Pen(bdCol, bdWidth))
                        {
                            g.DrawPath(penBd, gp);
                        }
                    }

                    DrawCapsuleContents(g, computedW, _barHeight, true,
                        rawPct, sPct, sUsed, cPct, hud != null ? hud.TotalTurns : 0, sCache,
                        sTps, mTag, mTone, sTurns, iqLbl, iqPill, iqLv, running, _isDragging,
                        fBold, fReg, fMono);
                }
                return bmp;
            }
        }

        private int DrawCapsuleContents(
            Graphics g, int totalW, int totalH, bool draw,
            double rawPct, string sPct, string sUsed, double cPct, int totalTurns, string sCache,
            string sTps, string mTag, string mTone, string sTurns,
            string iqLbl, string iqPill, string iqLv, bool running, bool dragging,
            Font fBold, Font fReg, Font fMono)
        {
            Color colMain = Color.FromArgb(24, 24, 27);
            Color colSub = Color.FromArgb(100, 106, 118);
            Color colDiv = Color.FromArgb(216, 222, 230);
            Color colOk = Color.FromArgb(16, 185, 129);
            Color colWarn = Color.FromArgb(245, 158, 11);
            Color colDanger = Color.FromArgb(239, 68, 68);

            int cy = totalH / 2;
            int x = S(11);

            // Subtle 6-dot drag grip handle while actively dragging
            if (dragging)
            {
                if (draw)
                {
                    int dotSz = Math.Max(2, S(2.1f));
                    int gx = x;
                    int gy0 = cy - S(4.2f);
                    using (var brGrip = new SolidBrush(Color.FromArgb(210, 37, 99, 235)))
                    {
                        for (int r = 0; r < 3; r++)
                        {
                            for (int c = 0; c < 2; c++)
                            {
                                g.FillEllipse(brGrip, gx + c * S(3.5f), gy0 + r * S(3.5f), dotSz, dotSz);
                            }
                        }
                    }
                }
                x += S(10);
            }

            // 1. Progress Ring + Pct + Used
            int ringD = S(14);
            if (draw)
            {
                var rcRing = new Rectangle(x, cy - ringD / 2, ringD, ringD);
                using (var penTrack = new Pen(Color.FromArgb(212, 218, 226), S(2.1f)))
                {
                    g.DrawEllipse(penTrack, rcRing);
                }
                Color ringCol = rawPct >= 88 ? colDanger : (rawPct >= 72 ? colWarn : colOk);
                if (rawPct > 0.5)
                {
                    using (var penArc = new Pen(ringCol, S(2.1f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawArc(penArc, rcRing, -90f, (float)(360.0 * rawPct / 100.0));
                    }
                }
                if (running)
                {
                    using (var brCore = new SolidBrush(colOk))
                    {
                        int cd = S(4);
                        g.FillEllipse(brCore, x + (ringD - cd) / 2f, cy - cd / 2f, cd, cd);
                    }
                }
            }
            x += ringD + S(5);
            x += MeasureOrDrawStr(g, sPct, fBold, colMain, x, cy, draw) + S(3);
            x += MeasureOrDrawStr(g, sUsed, fReg, colSub, x, cy, draw);

            x = StepSep(g, x + S(6), cy, S(12), colDiv, draw);

            // 2. Cache Hit %
            int dotD = S(6);
            if (draw)
            {
                Color cDot = totalTurns <= 1 ? Color.FromArgb(156, 163, 175)
                    : (cPct >= 75 ? colOk : (cPct >= 35 ? colWarn : colDanger));
                using (var br = new SolidBrush(cDot))
                {
                    g.FillEllipse(br, x, cy - dotD / 2f, dotD, dotD);
                }
            }
            x += dotD + S(4);
            x += MeasureOrDrawStr(g, I18n.T("缓存 ", "Cache "), fReg, colSub, x, cy, draw);
            x += MeasureOrDrawStr(g, sCache, fBold, colMain, x, cy, draw);

            x = StepSep(g, x + S(6), cy, S(12), colDiv, draw);

            // 3. Speed t/s or Live Thinking Elapsed Counter
            Color tpsCol = hudIsRunningColor(running, sTps) ? Color.FromArgb(5, 150, 105) : colMain;
            x += MeasureOrDrawStr(g, sTps, fBold, tpsCol, x, cy, draw);

            x = StepSep(g, x + S(6), cy, S(12), colDiv, draw);

            // 4. Model Badge + Turn Count
            Color tagBg, tagFg;
            if (mTone == "carbon") { tagBg = Color.FromArgb(226, 232, 240); tagFg = Color.FromArgb(15, 23, 42); }
            else if (mTone == "neon") { tagBg = Color.FromArgb(204, 251, 241); tagFg = Color.FromArgb(13, 148, 136); }
            else if (mTone == "argon") { tagBg = Color.FromArgb(232, 240, 254); tagFg = Color.FromArgb(37, 99, 235); }
            else if (mTone == "fast") { tagBg = Color.FromArgb(218, 248, 236); tagFg = Color.FromArgb(5, 150, 105); }
            else { tagBg = Color.FromArgb(234, 237, 242); tagFg = colSub; }

            x += MeasureOrDrawChip(g, mTag, fMono, tagBg, tagFg, x, cy, S(5), draw);
            if (!string.IsNullOrEmpty(sTurns))
            {
                x += S(4);
                x += MeasureOrDrawStr(g, sTurns, fReg, colSub, x, cy, draw);
            }

            x = StepSep(g, x + S(6), cy, S(12), colDiv, draw);

            // 5. Thinking IQ Chip
            x += MeasureOrDrawStr(g, iqLbl + " ", fReg, colSub, x, cy, draw);
            Color iqBg = Color.FromArgb(232, 236, 242);
            Color iqFg = colSub;
            if (iqLv == "ok") { iqBg = Color.FromArgb(224, 242, 254); iqFg = Color.FromArgb(2, 132, 199); }
            else if (iqLv == "warn") { iqBg = Color.FromArgb(254, 243, 199); iqFg = Color.FromArgb(180, 83, 9); }
            else if (iqLv == "danger") { iqBg = Color.FromArgb(254, 226, 226); iqFg = Color.FromArgb(220, 38, 38); }

            x += MeasureOrDrawChip(g, iqPill, fMono, iqBg, iqFg, x, cy, S(8), draw);
            x += S(11);
            return x;
        }

        private static bool hudIsRunningColor(bool running, string sTps)
        {
            return running && !string.IsNullOrEmpty(sTps) && sTps.IndexOf("t/s", StringComparison.Ordinal) < 0;
        }

        private int MeasureOrDrawStr(Graphics g, string txt, Font font, Color col, int x, int cy, bool draw)
        {
            SizeF sz = g.MeasureString(txt, font, 1000, StringFormat.GenericTypographic);
            int w = (int)Math.Ceiling(sz.Width) + S(1.5f);
            if (draw)
            {
                using (var br = new SolidBrush(col))
                {
                    var sf = new StringFormat(StringFormat.GenericTypographic)
                    {
                        Alignment = StringAlignment.Near,
                        LineAlignment = StringAlignment.Center,
                        FormatFlags = StringFormatFlags.NoWrap
                    };
                    g.DrawString(txt, font, br, new PointF(x, cy), sf);
                }
            }
            return w;
        }

        private int MeasureOrDrawChip(Graphics g, string txt, Font font, Color bg, Color fg, int x, int cy, int radius, bool draw)
        {
            SizeF sz = g.MeasureString(txt, font, 1000, StringFormat.GenericTypographic);
            int w = (int)Math.Ceiling(sz.Width) + S(10);
            int h = S(17);
            if (draw)
            {
                var rc = new Rectangle(x, cy - h / 2, w, h);
                using (GraphicsPath gp = MakeRoundRect(rc, radius))
                using (var brBg = new SolidBrush(bg))
                {
                    g.FillPath(brBg, gp);
                }
                using (var brFg = new SolidBrush(fg))
                {
                    var sf = new StringFormat(StringFormat.GenericTypographic)
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        FormatFlags = StringFormatFlags.NoWrap
                    };
                    g.DrawString(txt, font, brFg, new PointF(rc.Left + rc.Width / 2f, cy), sf);
                }
            }
            return w;
        }

        private int StepSep(Graphics g, int x, int cy, int h, Color col, bool draw)
        {
            if (draw)
            {
                using (var pen = new Pen(col, 1f))
                {
                    g.DrawLine(pen, x, cy - h / 2, x, cy + h / 2);
                }
            }
            return x + S(6);
        }

        private static GraphicsPath MakeRoundRect(Rectangle rc, int radius)
        {
            int d = Math.Min(Math.Max(2, radius * 2), Math.Min(rc.Width, rc.Height));
            var gp = new GraphicsPath();
            gp.AddArc(rc.X, rc.Y, d, d, 180, 90);
            gp.AddArc(rc.Right - d, rc.Y, d, d, 270, 90);
            gp.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0, 90);
            gp.AddArc(rc.X, rc.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        public void RenderLayered(bool force)
        {
            if (!IsHandleCreated || IsDisposed) return;

            HudSnapshot h = _data != null ? _data.ActiveHud : null;
            TurnItem l = h != null ? h.Latest : null;
            string sig = string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}",
                l != null ? l.TotalUsed : 0,
                l != null ? l.CacheHitPct : 0,
                l != null && l.Tps.HasValue ? l.Tps.Value : -1,
                l != null ? l.ShortTag : "",
                h != null ? h.TotalTurns : 0,
                h != null ? h.IqPill : "",
                (h != null && h.IsRunning) || (_data != null && _data.ActiveWorkingCount > 0),
                h != null && h.IsRunning ? h.ActiveElapsedSec : 0,
                _scaleMode,
                I18n.IsZh ? "ZH" : "EN",
                (_hovered ? "H" : "") + (_popupOpen ? "P" : "") + (_isDragging ? "D" : ""));

            if (!force && sig == _lastRenderSig) return;
            _lastRenderSig = sig;

            int newW;
            using (Bitmap bmp = BuildBarBitmap(out newW))
            {
                _barWidth = newW;
                Point targetPt = _isDragging
                    ? _dragCurrentTopLeft
                    : ComputeAnchorPoint(newW, _barHeight);

                IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
                IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
                IntPtr hBmp = IntPtr.Zero;
                IntPtr oldBmp = IntPtr.Zero;
                try
                {
                    hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                    oldBmp = NativeMethods.SelectObject(memDc, hBmp);

                    var dstPt = new NativeMethods.POINT { X = targetPt.X, Y = targetPt.Y };
                    var sz = new NativeMethods.SIZE { Width = newW, Height = _barHeight };
                    var srcPt = new NativeMethods.POINT { X = 0, Y = 0 };
                    var blend = new NativeMethods.BLENDFUNCTION
                    {
                        BlendOp = NativeMethods.AC_SRC_OVER,
                        BlendFlags = 0,
                        SourceConstantAlpha = 255,
                        AlphaFormat = NativeMethods.AC_SRC_ALPHA
                    };

                    NativeMethods.UpdateLayeredWindow(
                        Handle, screenDc, ref dstPt, ref sz,
                        memDc, ref srcPt, 0, ref blend, NativeMethods.ULW_ALPHA);
                }
                finally
                {
                    if (oldBmp != IntPtr.Zero) NativeMethods.SelectObject(memDc, oldBmp);
                    if (hBmp != IntPtr.Zero) NativeMethods.DeleteObject(hBmp);
                    if (memDc != IntPtr.Zero) NativeMethods.DeleteDC(memDc);
                    if (screenDc != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        public Point ComputeAnchorPoint(int barW, int barH)
        {
            if (string.Equals(_config.AnchorMode, "custom", StringComparison.OrdinalIgnoreCase))
            {
                Point customPt;
                if (TryResolveCustomAnchorPoint(barW, barH, out customPt))
                {
                    return customPt;
                }
            }
            return ComputeTaskbarAnchorPoint(barW, barH);
        }

        private bool TryResolveCustomAnchorPoint(int barW, int barH, out Point resolved)
        {
            resolved = Point.Empty;
            int x = string.Equals(_config.HorizontalAlign, "left", StringComparison.OrdinalIgnoreCase)
                ? _config.CustomX
                : (_config.CustomX - barW);
            int y = _config.CustomY;

            // Locate matching monitor if still connected, otherwise check if point falls on any active monitor
            Screen targetScr = null;
            if (!string.IsNullOrEmpty(_config.MonitorDeviceName))
            {
                foreach (Screen s in Screen.AllScreens)
                {
                    if (string.Equals(s.DeviceName, _config.MonitorDeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetScr = s;
                        break;
                    }
                }
            }

            if (targetScr == null)
            {
                var probeRect = new Rectangle(x, y, Math.Max(40, barW), Math.Max(20, barH));
                foreach (Screen s in Screen.AllScreens)
                {
                    if (s.Bounds.IntersectsWith(probeRect))
                    {
                        targetScr = s;
                        break;
                    }
                }
            }

            if (targetScr == null)
            {
                return false;
            }

            Rectangle sb = targetScr.Bounds;
            int minX = sb.Left + S(2);
            int maxX = Math.Max(minX, sb.Right - barW - S(2));
            int minY = sb.Top + S(2);
            int maxY = Math.Max(minY, sb.Bottom - barH - S(2));

            resolved = new Point(
                Math.Max(minX, Math.Min(x, maxX)),
                Math.Max(minY, Math.Min(y, maxY)));
            return true;
        }

        // Cached taskbar layout preferences (refreshed every 15s, zero overhead)
        private DateTime _lastTaskbarPrefUtc = DateTime.MinValue;
        private bool _cachedIsWin11;
        private bool _cachedIsCenteredTaskbar;
        private bool _cachedWidgetsVisible;

        private void RefreshTaskbarPrefsIfNeeded()
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (_lastTaskbarPrefUtc != DateTime.MinValue && (nowUtc - _lastTaskbarPrefUtc).TotalSeconds < 15.0)
                return;
            _lastTaskbarPrefUtc = nowUtc;

            int build = 0;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                {
                    object bv = k != null ? k.GetValue("CurrentBuildNumber") : null;
                    if (bv != null) int.TryParse(bv.ToString(), out build);
                }
            }
            catch { }

            _cachedIsWin11 = build >= 22000;
            bool centered = _cachedIsWin11;
            bool widgetsOn = false;

            try
            {
                using (RegistryKey adv = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", false))
                {
                    if (adv != null)
                    {
                        object al = adv.GetValue("TaskbarAl");
                        if (al != null) centered = Convert.ToInt32(al) != 0;

                        object da = adv.GetValue("TaskbarDa");
                        if (da != null) widgetsOn = Convert.ToInt32(da) != 0;
                    }
                }
            }
            catch { }

            if (widgetsOn)
            {
                try
                {
                    using (RegistryKey pol = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Dsh", false))
                    {
                        object allow = pol != null ? pol.GetValue("AllowNewsAndInterests") : null;
                        if (allow != null && Convert.ToInt32(allow) == 0) widgetsOn = false;
                    }
                }
                catch { }
            }

            _cachedIsCenteredTaskbar = _cachedIsWin11 && centered;
            _cachedWidgetsVisible = _cachedIsWin11 && widgetsOn;
        }

        public Point ComputeTaskbarAnchorPoint(int barW, int barH)
        {
            RefreshTaskbarPrefsIfNeeded();

            Rectangle scr = Screen.PrimaryScreen.Bounds;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int margin = S(10);

            IntPtr hTray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (hTray != IntPtr.Zero)
            {
                NativeMethods.RECT rcTray;
                if (NativeMethods.GetWindowRect(hTray, out rcTray))
                {
                    int trayW = rcTray.Right - rcTray.Left;
                    int trayH = rcTray.Bottom - rcTray.Top;
                    bool isBottomHorizontal = trayW > trayH && trayH >= 24 && rcTray.Top >= scr.Top + scr.Height / 2;

                    if (isBottomHorizontal)
                    {
                        int yOnTray = rcTray.Top + (trayH - barH) / 2;

                        int notifyLeft = rcTray.Right - S(160);
                        IntPtr hNotify = NativeMethods.FindWindowEx(hTray, IntPtr.Zero, "TrayNotifyWnd", null);
                        NativeMethods.RECT rcNotify;
                        if (hNotify != IntPtr.Zero && NativeMethods.GetWindowRect(hNotify, out rcNotify) && rcNotify.Left > rcTray.Left)
                        {
                            notifyLeft = rcNotify.Left;
                        }

                        if (_cachedIsCenteredTaskbar)
                        {
                            // Windows 11 centered taskbar: estimate centered icon cluster span
                            int centerEstW = Math.Min((int)Math.Round(trayW * 0.44), S(540));
                            int trayMid = rcTray.Left + trayW / 2;
                            int centerLeft = trayMid - centerEstW / 2;
                            int centerRight = trayMid + centerEstW / 2;

                            int leftStart = rcTray.Left + (_cachedWidgetsVisible ? S(162) : margin);
                            int leftEnd = centerLeft - S(10);
                            int leftGap = leftEnd - leftStart;

                            int rightStart = centerRight + S(10);
                            int rightEnd = notifyLeft - S(8);
                            int rightGap = rightEnd - rightStart;

                            // Prefer left free slot first (avoids crowded tray notifications); fall back to right if wider
                            if (leftGap >= barW)
                            {
                                return new Point(leftStart, yOnTray);
                            }
                            if (rightGap >= barW)
                            {
                                return new Point(rightEnd - barW, yOnTray);
                            }

                            // Compact/small screen protection: neither taskbar side fits cleanly without covering icons,
                            // so float neatly just above the bottom-left edge of the taskbar.
                            return new Point(wa.Left + margin, Math.Max(wa.Top + S(6), wa.Bottom - barH - S(8)));
                        }
                        else
                        {
                            // Windows 10 or Win11 left-aligned taskbar: left side holds Start + pinned/running icons,
                            // so check free space between ReBarWindow32 and TrayNotifyWnd on the right.
                            int minLeft = rcTray.Left + S(12);
                            IntPtr hRebar = NativeMethods.FindWindowEx(hTray, IntPtr.Zero, "ReBarWindow32", null);
                            NativeMethods.RECT rcRebar;
                            if (hRebar != IntPtr.Zero && NativeMethods.GetWindowRect(hRebar, out rcRebar) && rcRebar.Right > minLeft)
                            {
                                minLeft = rcRebar.Right + S(10);
                            }

                            int rightEnd = notifyLeft - S(8);
                            int rightGap = rightEnd - minLeft;
                            if (rightGap >= barW)
                            {
                                return new Point(rightEnd - barW, yOnTray);
                            }

                            // Compact/small screen protection: float neatly above bottom-right edge without overlapping icons
                            return new Point(Math.Max(wa.Left + margin, wa.Right - barW - margin), Math.Max(wa.Top + S(6), wa.Bottom - barH - S(8)));
                        }
                    }
                }
            }

            // Fallback when taskbar is auto-hidden or docked vertically/top: safe bottom-left above working area
            return new Point(wa.Left + margin, Math.Max(wa.Top + S(6), wa.Bottom - barH - S(8)));
        }

        public Rectangle GetActualScreenBounds()
        {
            if (IsHandleCreated && !IsDisposed)
            {
                NativeMethods.RECT rc;
                if (NativeMethods.GetWindowRect(Handle, out rc))
                {
                    return new Rectangle(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
                }
            }
            return Bounds;
        }

        public void EnsureDockedAndZOrder()
        {
            if (!IsHandleCreated || IsDisposed || _isDragging) return;

            if (IsExclusiveFullscreenActive())
            {
                if (Visible) Hide();
                return;
            }

            if (!Visible)
            {
                Show();
                RenderLayered(true);
            }

            Point target = ComputeAnchorPoint(_barWidth, _barHeight);
            Rectangle cur = GetActualScreenBounds();
            bool posChanged = (cur.X != target.X || cur.Y != target.Y || cur.Width != _barWidth);

            // Check if Shell_TrayWnd jumped ahead of us in Z-order
            bool trayAboveUs = false;
            IntPtr hTray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (hTray != IntPtr.Zero)
            {
                IntPtr prev = NativeMethods.GetWindow(Handle, NativeMethods.GW_HWNDPREV);
                int guard = 0;
                while (prev != IntPtr.Zero && guard++ < 24)
                {
                    if (prev == hTray) { trayAboveUs = true; break; }
                    prev = NativeMethods.GetWindow(prev, NativeMethods.GW_HWNDPREV);
                }
            }

            if (posChanged || trayAboveUs)
            {
                NativeMethods.SetWindowPos(
                    Handle, NativeMethods.HWND_TOPMOST,
                    target.X, target.Y, _barWidth, _barHeight,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            }
        }

        private bool IsExclusiveFullscreenActive()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == Handle) return false;

            var sb = new StringBuilder(128);
            NativeMethods.GetClassName(fg, sb, sb.Capacity);
            string cls = sb.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
                return false;

            NativeMethods.RECT rc;
            if (!NativeMethods.GetWindowRect(fg, out rc)) return false;

            // Check only the monitor currently hosting this capsule
            Rectangle barRc = GetActualScreenBounds();
            Rectangle scr = Screen.FromRectangle(barRc.Width > 0 ? barRc : Screen.PrimaryScreen.Bounds).Bounds;
            return rc.Left <= scr.Left && rc.Top <= scr.Top &&
                   rc.Right >= scr.Right && rc.Bottom >= scr.Bottom;
        }
    }
}
