using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AntigravityHud
{
    /// <summary>
    /// Modern Win11-style owner-drawn floating context menu for the capsule bar.
    /// Features inline 3-way bar width & theme segmented controls, crisp vector icons,
    /// pill toggles, and native DWM rounded corners in both Light & Dark themes.
    /// </summary>
    internal sealed class ModernContextMenuForm : Form
    {
        private readonly float _dpi;
        private readonly UserConfig _config;
        private readonly Action<string> _onSetBarStyle;
        private readonly Action<string> _onSetThemeMode;
        private readonly Action _onToggleLockPos;
        private readonly Action _onResetPos;
        private readonly Action _onToggleLang;
        private readonly Action _onToggleAutostart;
        private readonly Action _onOpenLogs;
        private readonly Action _onExitApp;
        private readonly Func<bool> _getAutostart;

        private bool _dwmRoundedApplied;
        private Point _mousePt = new Point(-1, -1);

        private readonly Rectangle[] _rcStyleSegs = new Rectangle[3];
        private readonly Rectangle[] _rcThemeSegs = new Rectangle[3];
        private Rectangle _rcLockRow;
        private Rectangle _rcResetRow;
        private Rectangle _rcLangRow;
        private Rectangle _rcAutoStartRow;
        private Rectangle _rcLogsRow;
        private Rectangle _rcExitRow;

        public ModernContextMenuForm(
            float dpiScale,
            UserConfig config,
            Action<string> onSetBarStyle,
            Action<string> onSetThemeMode,
            Action onToggleLockPos,
            Action onResetPos,
            Action onToggleLang,
            Action onToggleAutostart,
            Action onOpenLogs,
            Action onExitApp,
            Func<bool> getAutostart)
        {
            _dpi = dpiScale;
            _config = config ?? new UserConfig();
            _onSetBarStyle = onSetBarStyle;
            _onSetThemeMode = onSetThemeMode;
            _onToggleLockPos = onToggleLockPos;
            _onResetPos = onResetPos;
            _onToggleLang = onToggleLang;
            _onToggleAutostart = onToggleAutostart;
            _onOpenLogs = onOpenLogs;
            _onExitApp = onExitApp;
            _getAutostart = getAutostart;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = ThemeManager.ForApp().CardBg;

            Width = S(246);
            Height = S(346);

            Deactivate += (s, e) => Hide();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
            MouseMove += OnMenuMouseMove;
            MouseLeave += (s, e) => { _mousePt = new Point(-1, -1); Invalidate(); };
            MouseDown += OnMenuMouseDown;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundedCorners();
            RefreshThemeChrome();
        }

        public void RefreshThemeChrome()
        {
            ThemePalette pal = ThemeManager.ForApp();
            BackColor = pal.CardBg;
            if (IsHandleCreated)
            {
                ThemeManager.ApplyDwmTheme(Handle, pal.IsDark);
            }
            if (Visible) Invalidate();
        }

        private void ApplyRoundedCorners()
        {
            try
            {
                int pref = 2; // DWMWCP_ROUND
                int hr = NativeMethods.DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int));
                if (hr == 0)
                {
                    _dwmRoundedApplied = true;
                    return;
                }
            }
            catch { }

            IntPtr rgn = NativeMethods.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, S(12), S(12));
            if (rgn != IntPtr.Zero)
            {
                Region = Region.FromHrgn(rgn);
                NativeMethods.DeleteObject(rgn);
            }
        }

        private int S(float px)
        {
            return (int)Math.Round(px * _dpi);
        }

        public void ShowNearPoint(Point screenPt, Rectangle barRect)
        {
            RefreshThemeChrome();
            Rectangle wa = Screen.FromPoint(screenPt).WorkingArea;
            int gap = S(8);

            int rawX = screenPt.X;
            if (rawX + Width > wa.Right - gap) rawX = wa.Right - Width - gap;
            if (rawX < wa.Left + gap) rawX = wa.Left + gap;

            int anchorTop = barRect.Height > 0 ? barRect.Top : screenPt.Y;
            int anchorBottom = barRect.Height > 0 ? barRect.Bottom : screenPt.Y;
            int rawY;
            if (anchorTop - gap - Height >= wa.Top + gap)
            {
                rawY = anchorTop - gap - Height;
            }
            else if (anchorBottom + gap + Height <= wa.Bottom - gap)
            {
                rawY = anchorBottom + gap;
            }
            else
            {
                rawY = Math.Max(wa.Top + gap, wa.Bottom - Height - gap);
            }

            _mousePt = new Point(-1, -1);
            Location = new Point(rawX, rawY);
            if (!Visible) Show();
            Activate();
            BringToFront();
            Invalidate();
        }

        public void RenderOffscreenToPng(string outPath)
        {
            if (!IsHandleCreated) CreateControl();
            _mousePt = new Point(Width / 2, S(136));
            using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                RenderAll(g);
                bmp.Save(outPath, ImageFormat.Png);
            }
        }

        private void OnMenuMouseMove(object sender, MouseEventArgs e)
        {
            _mousePt = e.Location;
            bool isCustom = string.Equals(_config.AnchorMode, "custom", StringComparison.OrdinalIgnoreCase);
            bool hitHand =
                _rcStyleSegs[0].Contains(e.Location) ||
                _rcStyleSegs[1].Contains(e.Location) ||
                _rcStyleSegs[2].Contains(e.Location) ||
                _rcThemeSegs[0].Contains(e.Location) ||
                _rcThemeSegs[1].Contains(e.Location) ||
                _rcThemeSegs[2].Contains(e.Location) ||
                _rcLockRow.Contains(e.Location) ||
                (isCustom && _rcResetRow.Contains(e.Location)) ||
                _rcLangRow.Contains(e.Location) ||
                _rcAutoStartRow.Contains(e.Location) ||
                _rcLogsRow.Contains(e.Location) ||
                _rcExitRow.Contains(e.Location);
            Cursor = hitHand ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        private void OnMenuMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            string[] styleKeys = { "minimal", "compact", "labeled" };
            for (int i = 0; i < 3; i++)
            {
                if (_rcStyleSegs[i].Contains(e.Location))
                {
                    if (_onSetBarStyle != null) _onSetBarStyle(styleKeys[i]);
                    Invalidate();
                    return;
                }
            }

            string[] themeKeys = { "auto", "light", "dark" };
            for (int i = 0; i < 3; i++)
            {
                if (_rcThemeSegs[i].Contains(e.Location))
                {
                    if (_onSetThemeMode != null) _onSetThemeMode(themeKeys[i]);
                    RefreshThemeChrome();
                    Invalidate();
                    return;
                }
            }

            if (_rcLockRow.Contains(e.Location))
            {
                if (_onToggleLockPos != null) _onToggleLockPos();
                Invalidate();
                return;
            }

            if (_rcResetRow.Contains(e.Location))
            {
                if (string.Equals(_config.AnchorMode, "custom", StringComparison.OrdinalIgnoreCase))
                {
                    if (_onResetPos != null) _onResetPos();
                    Hide();
                }
                return;
            }

            if (_rcLangRow.Contains(e.Location))
            {
                if (_onToggleLang != null) _onToggleLang();
                Invalidate();
                return;
            }

            if (_rcAutoStartRow.Contains(e.Location))
            {
                if (_onToggleAutostart != null) _onToggleAutostart();
                Invalidate();
                return;
            }

            if (_rcLogsRow.Contains(e.Location))
            {
                Hide();
                if (_onOpenLogs != null) _onOpenLogs();
                return;
            }

            if (_rcExitRow.Contains(e.Location))
            {
                Hide();
                if (_onExitApp != null) _onExitApp();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            RenderAll(e.Graphics);
        }

        private void RenderAll(Graphics g)
        {
            ThemePalette pal = ThemeManager.ForApp();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(pal.CardBg);

            int pad = S(10);
            int contentW = Width - pad * 2;
            int y = S(10);
            string ff = I18n.UiFontFamily;

            using (var fCap = new Font(ff, Math.Max(10f, S(10.8f)), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fBody = new Font(ff, Math.Max(11.5f, S(12.2f)), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fBodyBold = new Font(ff, Math.Max(11.5f, S(12.2f)), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fSmall = new Font(ff, Math.Max(10f, S(11.0f)), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                // 1. Inline 3-way capsule width selector
                DrawLeft(g, I18n.T("胶囊样式", "CAPSULE WIDTH"), fCap, pal.TextSub, new Rectangle(pad + S(2), y, contentW, S(15)));
                y += S(18);
                DrawSegmented3(g, pad, y, contentW, S(27), _rcStyleSegs,
                    new[] { I18n.T("极致短", "Min"), I18n.T("干练", "Compact"), I18n.T("带标签", "Labeled") },
                    new[] { "minimal", "compact", "labeled" },
                    _config.BarStyle ?? "compact", pal, fBodyBold, fSmall);
                y += S(34);

                // 2. Inline 3-way theme selector (Auto / Light / Dark)
                DrawLeft(g, I18n.T("外观主题", "APPEARANCE"), fCap, pal.TextSub, new Rectangle(pad + S(2), y, contentW, S(15)));
                y += S(18);
                DrawSegmented3(g, pad, y, contentW, S(27), _rcThemeSegs,
                    new[] { I18n.T("跟随系统", "Auto"), I18n.T("浅色", "Light"), I18n.T("深色", "Dark") },
                    new[] { "auto", "light", "dark" },
                    _config.ThemeMode ?? "auto", pal, fBodyBold, fSmall);
                y += S(34);

                DrawSep(g, pad, y, contentW, pal.CardBorder);
                y += S(6);

                // 3. Position & language section
                int rowH = S(31);

                _rcLockRow = new Rectangle(pad, y, contentW, rowH);
                DrawToggleRow(g, _rcLockRow, "lock", I18n.T("锁定胶囊位置", "Lock Position"), _config.PositionLocked, pal, fBody);
                y += rowH + S(2);

                bool isCustom = string.Equals(_config.AnchorMode, "custom", StringComparison.OrdinalIgnoreCase);
                _rcResetRow = new Rectangle(pad, y, contentW, rowH);
                DrawActionRow(g, _rcResetRow, "reset",
                    I18n.T("重置到默认空位", "Reset Position"),
                    isCustom ? I18n.T("归位", "Reset") : I18n.T("默认中", "Auto"),
                    isCustom, false, pal, fBody, fSmall);
                y += rowH + S(2);

                _rcLangRow = new Rectangle(pad, y, contentW, rowH);
                DrawActionRow(g, _rcLangRow, "lang",
                    I18n.T("界面语言", "Language"),
                    I18n.IsZh ? "中 → EN" : "EN → ZH",
                    true, false, pal, fBody, fSmall);
                y += rowH + S(5);

                DrawSep(g, pad, y, contentW, pal.CardBorder);
                y += S(6);

                // 4. System section
                bool autoStartOn = _getAutostart != null && _getAutostart();
                _rcAutoStartRow = new Rectangle(pad, y, contentW, rowH);
                DrawToggleRow(g, _rcAutoStartRow, "power", I18n.T("开机自动启动", "Launch at Login"), autoStartOn, pal, fBody);
                y += rowH + S(2);

                _rcLogsRow = new Rectangle(pad, y, contentW, rowH);
                DrawActionRow(g, _rcLogsRow, "folder",
                    I18n.T("打开本地日志目录", "Open Logs Folder"),
                    "", true, false, pal, fBody, fSmall);
                y += rowH + S(5);

                DrawSep(g, pad, y, contentW, pal.CardBorder);
                y += S(6);

                // 5. Exit row
                _rcExitRow = new Rectangle(pad, y, contentW, rowH);
                DrawActionRow(g, _rcExitRow, "exit",
                    I18n.T("退出 Antigravity HUD", "Exit Antigravity HUD"),
                    "", true, true, pal, fBody, fSmall);
            }

            if (!_dwmRoundedApplied)
            {
                using (var penOut = new Pen(pal.WinBorder, 1.2f))
                using (GraphicsPath gpOut = MakeRound(new Rectangle(0, 0, Width - 1, Height - 1), S(10)))
                {
                    g.DrawPath(penOut, gpOut);
                }
            }
        }

        private void DrawSegmented3(
            Graphics g, int x, int y, int w, int h, Rectangle[] rects,
            string[] labels, string[] keys, string currentKey,
            ThemePalette pal, Font fBold, Font fSmall)
        {
            var rcWrap = new Rectangle(x, y, w, h);
            Color wrapBg = pal.IsDark ? Color.FromArgb(24, 24, 28) : pal.SubtleFill;
            Color actBg = pal.IsDark ? Color.FromArgb(54, 54, 62) : pal.CardBg;
            Color actBd = pal.IsDark ? Color.FromArgb(78, 78, 88) : pal.CardBorder;
            FillRound(g, rcWrap, S(7), wrapBg, pal.CardBorder);

            int innerPad = S(2.5f);
            int segW = (w - innerPad * 2) / 3;
            for (int i = 0; i < 3; i++)
            {
                int sx = x + innerPad + i * segW;
                int sw = (i == 2) ? (rcWrap.Right - innerPad - sx) : segW;
                rects[i] = new Rectangle(sx, y + innerPad, sw, h - innerPad * 2);
                bool act = string.Equals(currentKey, keys[i], StringComparison.OrdinalIgnoreCase);
                bool hov = rects[i].Contains(_mousePt);

                if (act)
                    FillRound(g, rects[i], S(5), actBg, actBd);
                else if (hov)
                    FillRound(g, rects[i], S(5), pal.HoverFill, pal.HoverFill);

                DrawCenter(g, labels[i], act ? fBold : fSmall, act ? pal.Accent : pal.TextSub, rects[i]);
            }
        }

        private void DrawToggleRow(Graphics g, Rectangle rc, string iconKind, string title, bool isOn, ThemePalette pal, Font fBody)
        {
            if (rc.Contains(_mousePt)) FillRound(g, rc, S(7), pal.HoverFill, pal.HoverFill);

            int cy = rc.Top + rc.Height / 2;
            DrawVectorIcon(g, iconKind, rc.Left + S(14), cy, isOn ? pal.Accent : pal.TextSub);
            DrawLeft(g, title, fBody, pal.TextMain, new Rectangle(rc.Left + S(29), rc.Top, rc.Width - S(72), rc.Height));

            int tw = S(30);
            int th = S(16);
            int tx = rc.Right - S(8) - tw;
            int ty = cy - th / 2;
            var rcTrack = new Rectangle(tx, ty, tw, th);
            Color trkCol = isOn ? pal.Ok : pal.ToggleTrackOff;
            FillRound(g, rcTrack, th / 2, trkCol, trkCol);

            int knobD = th - S(4);
            int kx = isOn ? (rcTrack.Right - knobD - S(2)) : (rcTrack.Left + S(2));
            using (var br = new SolidBrush(pal.ToggleKnob))
            {
                g.FillEllipse(br, kx, ty + S(2), knobD, knobD);
            }
        }

        private void DrawActionRow(Graphics g, Rectangle rc, string iconKind, string title, string rightTag, bool enabled, bool danger, ThemePalette pal, Font fBody, Font fSmall)
        {
            bool hov = enabled && rc.Contains(_mousePt);
            if (hov)
            {
                Color bg = danger ? pal.DangerSoft : pal.HoverFill;
                FillRound(g, rc, S(7), bg, bg);
            }

            int cy = rc.Top + rc.Height / 2;
            Color fg = !enabled ? pal.TextMuted : (danger ? pal.DangerText : pal.TextMain);
            Color ic = !enabled ? pal.TextMuted : (danger ? pal.DangerText : pal.TextSub);
            DrawVectorIcon(g, iconKind, rc.Left + S(14), cy, ic);

            int rightReserve = string.IsNullOrEmpty(rightTag) ? S(10) : S(72);
            DrawLeft(g, title, fBody, fg, new Rectangle(rc.Left + S(29), rc.Top, rc.Width - S(29) - rightReserve, rc.Height));

            if (!string.IsNullOrEmpty(rightTag))
            {
                Color badgeBg = enabled ? (hov ? pal.AccentSoft : pal.SubtleFill) : pal.SubtleFill;
                Color badgeFg = enabled ? pal.Accent : pal.TextMuted;
                int bw = Math.Max(S(38), MeasureW(g, rightTag, fSmall) + S(12));
                int bh = S(18);
                var rcBadge = new Rectangle(rc.Right - S(8) - bw, cy - bh / 2, bw, bh);
                FillRound(g, rcBadge, S(5), badgeBg, badgeBg);
                DrawCenter(g, rightTag, fSmall, badgeFg, rcBadge);
            }
        }

        private void DrawVectorIcon(Graphics g, string kind, int cx, int cy, Color col)
        {
            using (var pen = new Pen(col, S(1.45f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                int r = S(5.5f);
                if (kind == "lock")
                {
                    g.DrawRectangle(pen, cx - S(4.2f), cy - S(0.5f), S(8.4f), S(5.8f));
                    g.DrawArc(pen, cx - S(2.7f), cy - S(5.2f), S(5.4f), S(6.4f), 180, 180);
                }
                else if (kind == "reset")
                {
                    g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, -40, 290);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx + S(1.5f), cy - S(6.2f)),
                        new PointF(cx + S(5.2f), cy - S(4.2f)),
                        new PointF(cx + S(2.4f), cy - S(1.4f))
                    });
                }
                else if (kind == "lang")
                {
                    g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                    g.DrawLine(pen, cx - r, cy, cx + r, cy);
                    g.DrawEllipse(pen, cx - S(2.6f), cy - r, S(5.2f), r * 2);
                }
                else if (kind == "power")
                {
                    g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, -55, 290);
                    g.DrawLine(pen, cx, cy - S(6.2f), cx, cy - S(0.8f));
                }
                else if (kind == "folder")
                {
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx - S(5.8f), cy - S(4.2f)),
                        new PointF(cx - S(1.2f), cy - S(4.2f)),
                        new PointF(cx + S(0.4f), cy - S(2.4f)),
                        new PointF(cx + S(5.8f), cy - S(2.4f)),
                        new PointF(cx + S(5.8f), cy + S(4.6f)),
                        new PointF(cx - S(5.8f), cy + S(4.6f)),
                        new PointF(cx - S(5.8f), cy - S(4.2f))
                    });
                }
                else if (kind == "exit")
                {
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx + S(0.5f), cy - S(5.0f)),
                        new PointF(cx - S(5.0f), cy - S(5.0f)),
                        new PointF(cx - S(5.0f), cy + S(5.0f)),
                        new PointF(cx + S(0.5f), cy + S(5.0f))
                    });
                    g.DrawLine(pen, cx - S(1.2f), cy, cx + S(5.5f), cy);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx + S(3.0f), cy - S(2.6f)),
                        new PointF(cx + S(5.6f), cy),
                        new PointF(cx + S(3.0f), cy + S(2.6f))
                    });
                }
            }
        }

        private static void DrawSep(Graphics g, int x, int y, int w, Color col)
        {
            using (var pen = new Pen(col, 1f))
            {
                g.DrawLine(pen, x + 2, y, x + w - 2, y);
            }
        }

        private static int MeasureW(Graphics g, string txt, Font f)
        {
            if (string.IsNullOrEmpty(txt)) return 0;
            return (int)Math.Ceiling(g.MeasureString(txt, f, 400, StringFormat.GenericTypographic).Width);
        }

        private static void DrawLeft(Graphics g, string txt, Font f, Color c, Rectangle rc)
        {
            using (var br = new SolidBrush(c))
            using (var sf = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                g.DrawString(txt, f, br, rc, sf);
            }
        }

        private static void DrawCenter(Graphics g, string txt, Font f, Color c, Rectangle rc)
        {
            using (var br = new SolidBrush(c))
            using (var sf = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                g.DrawString(txt, f, br, rc, sf);
            }
        }

        private static void FillRound(Graphics g, Rectangle rc, int rad, Color fill, Color border)
        {
            using (GraphicsPath gp = MakeRound(rc, rad))
            {
                using (var br = new SolidBrush(fill)) g.FillPath(br, gp);
                if (border != fill)
                {
                    using (var pen = new Pen(border, 1f)) g.DrawPath(pen, gp);
                }
            }
        }

        private static GraphicsPath MakeRound(Rectangle rc, int rad)
        {
            int d = Math.Min(Math.Max(2, rad * 2), Math.Min(rc.Width, rc.Height));
            var gp = new GraphicsPath();
            gp.AddArc(rc.X, rc.Y, d, d, 180, 90);
            gp.AddArc(rc.Right - d, rc.Y, d, d, 270, 90);
            gp.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0, 90);
            gp.AddArc(rc.X, rc.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }
    }
}
