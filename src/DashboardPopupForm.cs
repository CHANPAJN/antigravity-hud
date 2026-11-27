using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace AntigravityHud
{
    internal sealed class TrayPopupForm : Form
    {
        private sealed class DrillRowHit
        {
            public Rectangle Rect;
            public string ConvKey = "";
            public string HoverDetail = "";
        }

        private readonly float _dpi;
        private readonly Action _onManualRefresh;
        private readonly Action _onOpenLogs;
        private readonly Action _onExitApp;
        private readonly Action<string> _onSelectConv;
        private readonly Func<string, PeriodTab, ModelDrilldownData> _onQueryDrilldown;

        private DashboardData _data = new DashboardData();
        private string _selectedConvKey = ""; // "" = Auto-follow latest active window
        private MainTab _activeTab = MainTab.WindowHud;
        private ScaleMode _scaleMode = ScaleMode.Auto;
        private PeriodTab _ledgerPeriod = PeriodTab.Today;

        // Global ledger drilldown & scroll states
        private LedgerRankMode _ledgerRankMode = LedgerRankMode.ByModel;
        private string _drillModelName = "";
        private ModelDrilldownData _drillData = null;
        private int _drillSubTab = 0; // 0 = By Conversation, 1 = Turn Details
        private bool _drillLowCacheOnly = false;
        private int _overviewModelScroll = 0;
        private int _drillListScroll = 0;
        private int _overviewVisibleCap = 5;
        private int _drillVisibleCap = 7;

        // Window HUD progressive disclosure states (Subagent Workbench / Category Diagnostic / Quick Turn Filter)
        private HudCard2Mode _hudCard2Mode = HudCard2Mode.Turns;
        private string _hudInspectCat = "";
        private string _hudInspectTitle = "";
        private TurnQuickFilter _turnFilter = TurnQuickFilter.All;
        private int _subagentScroll = 0;
        private int _subagentVisCap = 4;
        private string _subagentReturnConvKey = null;

        private bool _suppressDeactivate;
        private int _lastDeactivateTick;
        private readonly Action _onScaleChanged;
        private readonly Action _onToggleAutoGc;
        private readonly Action _onCleanMemNow;
        private readonly Action _onToggleLanguage;
        private readonly Action _onResetPosition;
        private readonly Func<UserConfig> _getConfig;
        private readonly ContextMenuStrip _convMenu;
        private Font _convMenuFont;

        // Interactive hit regions
        private Rectangle _rcConvPicker;
        private Rectangle _rcScaleBtn;
        private Rectangle _rcLangBtn;
        private Rectangle _rcRefreshBtn;
        private Rectangle _rcTabHud;
        private Rectangle _rcTabLedger;
        private readonly Rectangle[] _rcPeriodBtns = new Rectangle[5];
        private readonly List<KeyValuePair<Rectangle, int>> _dayBarClickHits = new List<KeyValuePair<Rectangle, int>>();
        private readonly List<KeyValuePair<Rectangle, string>> _dayBarHoverHits = new List<KeyValuePair<Rectangle, string>>();
        private Rectangle _rcAutoGcBtn;
        private Rectangle _rcCleanMemBtn;
        private Rectangle _rcPosHintBar;
        private Rectangle _rcOpenFolder;
        private Rectangle _rcExitBtn;

        private Rectangle _rcDrillBackBtn;
        private Rectangle _rcDrillTabConv;
        private Rectangle _rcDrillTabTurns;
        private Rectangle _rcDrillLowCacheBtn;
        private Rectangle _rcDrillPrevBtn;
        private Rectangle _rcDrillNextBtn;
        private Rectangle _rcOverviewScrollArea;
        private Rectangle _rcDrillScrollArea;

        private readonly List<KeyValuePair<Rectangle, string>> _modelRowHits = new List<KeyValuePair<Rectangle, string>>();
        private readonly List<KeyValuePair<Rectangle, LedgerConvRankItem>> _taskRowHits = new List<KeyValuePair<Rectangle, LedgerConvRankItem>>();
        private readonly List<KeyValuePair<Rectangle, string>> _toolChipHits = new List<KeyValuePair<Rectangle, string>>();
        private readonly List<DrillRowHit> _drillRowHits = new List<DrillRowHit>();
        private readonly List<KeyValuePair<Rectangle, TurnItem>> _turnRowHits = new List<KeyValuePair<Rectangle, TurnItem>>();
        private readonly List<KeyValuePair<Rectangle, KeyValuePair<string, string>>> _rankRowHits =
            new List<KeyValuePair<Rectangle, KeyValuePair<string, string>>>();
        private readonly List<KeyValuePair<Rectangle, SubagentBriefItem>> _subagentRowHits =
            new List<KeyValuePair<Rectangle, SubagentBriefItem>>();

        private List<TreemapCellLayout> _lastTreemapCells = new List<TreemapCellLayout>();
        private Rectangle _rcHealthBadge = Rectangle.Empty;
        private Rectangle _rcSubagentChip = Rectangle.Empty;
        private Rectangle _rcCard2TabTurns = Rectangle.Empty;
        private Rectangle _rcCard2TabSubs = Rectangle.Empty;
        private Rectangle _rcCard2CloseBtn = Rectangle.Empty;
        private Rectangle _rcTurnFilterWarn = Rectangle.Empty;
        private Rectangle _rcTurnFilterSlow = Rectangle.Empty;
        private Rectangle _rcLedgerModeModel = Rectangle.Empty;
        private Rectangle _rcLedgerModeTask = Rectangle.Empty;
        private Rectangle _rcMemInfoArea = Rectangle.Empty;
        private Rectangle _rcTurnTableArea = Rectangle.Empty;
        private TurnItem _hoveredTurnItem = null;
        private Rectangle _hoveredTurnRowRect = Rectangle.Empty;
        private int _pinnedTurnNo = -1;
        private int _hudTurnScroll = 0;
        private int _hudVisibleCap = 5;
        private bool _dwmRoundedApplied = false;
        private string _lastHudScrollKey = "";
        private string _hoverHint = "";
        private Point _mousePt = new Point(-1, -1);

        // Dynamic Theme Palette (follows Windows Light/Dark or user override via ThemeManager)
        private static ThemePalette Pal { get { return ThemeManager.ForApp(); } }
        private static Color ColWinBg { get { return Pal.WinBg; } }
        private static Color ColWinBorder { get { return Pal.WinBorder; } }
        private static Color ColCardBg { get { return Pal.CardBg; } }
        private static Color ColCardBorder { get { return Pal.CardBorder; } }
        private static Color ColSubtleFill { get { return Pal.SubtleFill; } }
        private static Color ColHoverFill { get { return Pal.HoverFill; } }
        private static Color ColTextMain { get { return Pal.TextMain; } }
        private static Color ColTextSub { get { return Pal.TextSub; } }
        private static Color ColTextMuted { get { return Pal.TextMuted; } }
        private static Color ColAccent { get { return Pal.Accent; } }
        private static Color ColAccentSoft { get { return Pal.AccentSoft; } }
        private static Color ColAccentBtnBg { get { return Pal.IsDark ? Color.FromArgb(37, 99, 235) : Pal.Accent; } }
        private static Color ColAccentBorder { get { return Pal.IsDark ? Color.FromArgb(62, 108, 178) : Color.FromArgb(147, 197, 253); } }
        private static Color ColHoverBorder { get { return Pal.IsDark ? Color.FromArgb(68, 92, 134) : Color.FromArgb(191, 219, 254); } }

        private static Color ColOk { get { return Pal.Ok; } }
        private static Color ColOkText { get { return Pal.OkText; } }
        private static Color ColOkSoft { get { return Pal.OkSoft; } }
        private static Color ColOkBorder { get { return Pal.OkBorder; } }

        private static Color ColWarn { get { return Pal.Warn; } }
        private static Color ColWarnText { get { return Pal.WarnText; } }
        private static Color ColWarnSoft { get { return Pal.WarnSoft; } }
        private static Color ColWarnBorder { get { return Pal.WarnBorder; } }

        private static Color ColDanger { get { return Pal.Danger; } }
        private static Color ColDangerText { get { return Pal.DangerText; } }
        private static Color ColDangerSoft { get { return Pal.DangerSoft; } }
        private static Color ColDangerBorder { get { return Pal.IsDark ? Color.FromArgb(148, 56, 62) : Color.FromArgb(252, 165, 165); } }

        private static Color ColCatSys { get { return Pal.CatSys; } }
        private static Color ColCatTools { get { return Pal.CatTools; } }
        private static Color ColCatUser { get { return Pal.CatUser; } }
        private static Color ColCatResp { get { return Pal.CatResp; } }
        private static Color ColCatRes { get { return Pal.CatRes; } }

        private static Color ColTrackBg { get { return Pal.TrackBg; } }
        private static Color ColBarEmptyTrack { get { return Pal.BarEmptyTrack; } }
        private static Color ColRowAltBg { get { return Pal.IsDark ? Color.FromArgb(39, 39, 45) : Color.FromArgb(249, 250, 252); } }
        private static Color ColRowBorder { get { return Pal.IsDark ? Color.FromArgb(48, 48, 55) : Color.FromArgb(238, 241, 246); } }
        private static Color ColSoftSep { get { return Pal.IsDark ? Color.FromArgb(52, 52, 60) : Color.FromArgb(234, 237, 242); } }
        private static Color ColBodyLabel { get { return Pal.IsDark ? Color.FromArgb(212, 212, 218) : Color.FromArgb(52, 58, 68); } }
        private static Color ColScrollTrack { get { return Pal.IsDark ? Color.FromArgb(44, 44, 50) : Color.FromArgb(236, 240, 246); } }
        private static Color ColScrollThumb { get { return Pal.IsDark ? Color.FromArgb(88, 92, 104) : Color.FromArgb(180, 192, 210); } }
        private static Color ColDisabledArrow { get { return Pal.IsDark ? Color.FromArgb(88, 88, 98) : Color.FromArgb(190, 196, 205); } }

        private sealed class ThemeMenuColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return ColCardBg; } }
            public override Color MenuBorder { get { return ColWinBorder; } }
            public override Color MenuItemBorder { get { return ColHoverBorder; } }
            public override Color MenuItemSelected { get { return ColHoverFill; } }
            public override Color MenuItemSelectedGradientBegin { get { return ColHoverFill; } }
            public override Color MenuItemSelectedGradientEnd { get { return ColHoverFill; } }
            public override Color ImageMarginGradientBegin { get { return ColCardBg; } }
            public override Color ImageMarginGradientMiddle { get { return ColCardBg; } }
            public override Color ImageMarginGradientEnd { get { return ColCardBg; } }
            public override Color SeparatorDark { get { return Pal.Divider; } }
            public override Color SeparatorLight { get { return ColCardBg; } }
        }

        // Shared GDI+ StringFormat instances (zero per-frame allocation / zero handle leak)
        private static readonly StringFormat SfNearEllipsis = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        private static readonly StringFormat SfFarNoWrap = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        private static readonly StringFormat SfFarEllipsis = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        private static readonly StringFormat SfNearCenter = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

        public int LastDeactivateTick
        {
            get { return _lastDeactivateTick; }
        }

        public ScaleMode CurrentScaleMode
        {
            get { return _scaleMode; }
        }

        public void SetScaleMode(ScaleMode mode)
        {
            _scaleMode = mode;
        }

        public bool IsMenuOpen
        {
            get { return _convMenu != null && _convMenu.Visible; }
        }

        public TrayPopupForm(
            float dpiScale,
            Action onManualRefresh,
            Action onOpenLogs,
            Action onExitApp,
            Action<string> onSelectConv,
            Action onScaleChanged = null,
            Action onToggleAutoGc = null,
            Action onCleanMemNow = null,
            Func<string, PeriodTab, ModelDrilldownData> onQueryDrilldown = null,
            Action onToggleLanguage = null,
            Action onResetPosition = null,
            Func<UserConfig> getConfig = null)
        {
            _dpi = dpiScale;
            _onManualRefresh = onManualRefresh;
            _onOpenLogs = onOpenLogs;
            _onExitApp = onExitApp;
            _onSelectConv = onSelectConv;
            _onScaleChanged = onScaleChanged;
            _onToggleAutoGc = onToggleAutoGc;
            _onCleanMemNow = onCleanMemNow;
            _onQueryDrilldown = onQueryDrilldown;
            _onToggleLanguage = onToggleLanguage;
            _onResetPosition = onResetPosition;
            _getConfig = getConfig;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = ColWinBg;

            Width = S(468);
            Height = S(556);

            _convMenuFont = new Font(I18n.UiFontFamily, 9.0f, FontStyle.Regular, GraphicsUnit.Point);
            _convMenu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                Font = _convMenuFont
            };
            _convMenu.Closed += (s, e) => { _suppressDeactivate = false; };

            Deactivate += (s, e) =>
            {
                if (!_suppressDeactivate)
                {
                    _lastDeactivateTick = Environment.TickCount;
                    Hide();
                }
            };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Hide();
                }
                else if (e.KeyCode == Keys.Back && _activeTab == MainTab.GlobalLedger && !string.IsNullOrEmpty(_drillModelName))
                {
                    ExitModelDrilldown();
                }
            };
            MouseMove += OnFormMouseMove;
            MouseLeave += (s, e) =>
            {
                _mousePt = new Point(-1, -1);
                _hoverHint = "";
                _hoveredTurnItem = null;
                Cursor = Cursors.Default;
                Invalidate();
            };
            MouseClick += OnFormMouseClick;
            MouseWheel += OnFormMouseWheel;
        }

        private int S(float logicalPx)
        {
            return (int)Math.Round(logicalPx * _dpi);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyWindowCorners();
        }

        public void RefreshThemeChrome()
        {
            BackColor = ColWinBg;
            if (_convMenu != null && !_convMenu.IsDisposed)
            {
                _convMenu.BackColor = ColCardBg;
                _convMenu.ForeColor = ColTextMain;
                _convMenu.Renderer = new ToolStripProfessionalRenderer(new ThemeMenuColorTable());
            }
            if (IsHandleCreated)
            {
                ApplyWindowCorners();
            }
            if (Visible) Invalidate();
        }

        private void ApplyWindowCorners()
        {
            try
            {
                ThemeManager.ApplyDwmTheme(Handle, Pal.IsDark);
                // Win11 native GPU-composited anti-aliased rounded corners (zero CPU/GDI region overhead + native shadow)
                int cornerPref = 2; // DWMWCP_ROUND
                int hr = NativeMethods.DwmSetWindowAttribute(Handle, 33, ref cornerPref, sizeof(int));
                if (hr == 0)
                {
                    _dwmRoundedApplied = true;
                    Region = null;
                    Color bd = ColWinBorder;
                    int borderColorRef = (bd.B << 16) | (bd.G << 8) | bd.R;
                    NativeMethods.DwmSetWindowAttribute(Handle, 34, ref borderColorRef, sizeof(int));
                    return;
                }
            }
            catch { }

            _dwmRoundedApplied = false;
            IntPtr hrgn = NativeMethods.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, S(24), S(24));
            Region = Region.FromHrgn(hrgn);
            NativeMethods.DeleteObject(hrgn);
        }

        public void SetMainTab(MainTab tab)
        {
            _activeTab = tab;
        }

        public void SetLedgerPeriod(PeriodTab period)
        {
            _ledgerPeriod = period;
            _overviewModelScroll = 0;
            _drillListScroll = 0;
            RefreshDrilldownIfActive();
        }

        public void EnterModelDrilldown(string modelName, int subTab = 0)
        {
            _drillModelName = modelName ?? "";
            _drillSubTab = subTab;
            _drillLowCacheOnly = false;
            _drillListScroll = 0;
            _hoverHint = "";
            RefreshDrilldownIfActive();
            if (Visible) Invalidate();
        }

        private void ExitModelDrilldown()
        {
            _drillModelName = "";
            _drillData = null;
            _drillListScroll = 0;
            _hoverHint = "";
            Invalidate();
        }

        private void RefreshDrilldownIfActive()
        {
            if (string.IsNullOrEmpty(_drillModelName) || _onQueryDrilldown == null)
            {
                _drillData = null;
                return;
            }
            _drillData = _onQueryDrilldown(_drillModelName, _ledgerPeriod);
        }

        public void UpdateData(DashboardData data, string selectedConvKey)
        {
            _data = data ?? new DashboardData();
            _selectedConvKey = selectedConvKey ?? "";
            if (Visible && _activeTab == MainTab.GlobalLedger && !string.IsNullOrEmpty(_drillModelName))
            {
                RefreshDrilldownIfActive();
            }
            if (Visible) Invalidate();
        }

        public void ShowNearBar(Rectangle barScreenRect)
        {
            RefreshThemeChrome();
            Rectangle probe = barScreenRect.Width > 0
                ? barScreenRect
                : new Rectangle(Cursor.Position.X, Cursor.Position.Y, 1, 1);
            Rectangle wa = Screen.FromRectangle(probe).WorkingArea;
            int gap = S(8);

            // Horizontal alignment: align right edge when on right half, left edge when on left half
            int barCenterX = barScreenRect.Width > 0 ? (barScreenRect.Left + barScreenRect.Width / 2) : Cursor.Position.X;
            bool preferRightAlign = barCenterX >= (wa.Left + wa.Width / 2);
            int rawX = barScreenRect.Width > 0
                ? (preferRightAlign ? (barScreenRect.Right - Width) : barScreenRect.Left)
                : (Cursor.Position.X - Width / 2);
            int x = Math.Max(wa.Left + gap, Math.Min(rawX, wa.Right - Width - gap));

            // Vertical placement: prefer opening above bar; automatically flip below bar when near screen top
            int spaceAbove = barScreenRect.Height > 0 ? (barScreenRect.Top - wa.Top) : wa.Height;
            int spaceBelow = barScreenRect.Height > 0 ? (wa.Bottom - barScreenRect.Bottom) : 0;
            int rawY;
            if (barScreenRect.Height <= 0)
            {
                rawY = wa.Bottom - Height - gap;
            }
            else if (spaceAbove >= Height + gap || spaceAbove >= spaceBelow)
            {
                rawY = barScreenRect.Top - gap - Height;
            }
            else
            {
                rawY = barScreenRect.Bottom + gap;
            }
            int y = Math.Max(wa.Top + gap, Math.Min(rawY, wa.Bottom - Height - gap));

            Location = new Point(x, y);
            if (!Visible) Show();
            Activate();
            BringToFront();
            Invalidate();
        }

        private bool _wasHand;

        private void OnFormMouseMove(object sender, MouseEventArgs e)
        {
            _mousePt = e.Location;
            string newHover = "";
            TurnItem newHoveredTurn = null;
            Rectangle newHoveredRect = Rectangle.Empty;

            if (_rcPosHintBar.Contains(e.Location))
            {
                if (_data != null && _data.ExternalApi != null && _data.ExternalApi.HasLedger)
                {
                    newHover = _data.ExternalApi.HoverDetail;
                }
                else
                {
                    UserConfig cfg = _getConfig != null ? _getConfig() : null;
                    bool isCustom = cfg != null && string.Equals(cfg.AnchorMode, "custom", StringComparison.OrdinalIgnoreCase);
                    newHover = isCustom
                        ? I18n.T("点击重置到默认任务栏位置 · 长按悬浮胶囊可随时拖动", "Click to reset to taskbar default · Hold bar to reposition")
                        : I18n.T("长按悬浮胶囊（≥0.3s）可自由拖动到任意位置或副屏", "Hold the floating bar to drag it anywhere across monitors");
                }
            }
            else if (_rcLangBtn.Contains(e.Location))
            {
                newHover = I18n.T("切换界面语言为 English", "Switch interface language to Simplified Chinese");
            }
            else if (_rcMemInfoArea.Contains(e.Location) && _data != null && _data.MemGuard != null)
            {
                MemGuardSnapshot mg = _data.MemGuard;
                newHover = I18n.TF("进程 {0} · 临时缓存 {1} 个 ({2:F1}MB){3}",
                    "{0} procs · {1} temp files ({2:F1}MB){3}",
                    mg.AgProcCount, mg.TempCacheCount, mg.TempCacheMB,
                    mg.TotalFreedMB >= 1.0 ? I18n.TF(" · 累计释放 {0:F0} MB", " · Freed {0:F0} MB", mg.TotalFreedMB) : "");
            }
            else if (_activeTab == MainTab.WindowHud)
            {
                if (_rcSubagentChip.Contains(e.Location) && _data != null && _data.ActiveHud != null)
                {
                    HudSnapshot h = _data.ActiveHud;
                    if (h.IsSubagent && !string.IsNullOrEmpty(h.ParentConvKey))
                        newHover = I18n.TF("点击返回主窗口 ({0})", "Back to main chat ({0})", h.ParentTitle);
                    else if (_hudCard2Mode == HudCard2Mode.Subagents)
                        newHover = I18n.T("收起子任务列表，返回响应明细", "Close subagents and return to turns");
                    else
                        newHover = I18n.TF("查看 {0} 个子任务进度，点行可进入对应窗口", "View {0} subagents; click a row to inspect", h.SubagentCount);
                }
                else if (_rcCard2TabTurns.Contains(e.Location))
                {
                    newHover = I18n.T("查看主窗口逐轮模型响应明细", "View turn-by-turn model responses");
                }
                else if (_rcCard2TabSubs.Contains(e.Location))
                {
                    newHover = I18n.T("查看当前窗口派出的全部子任务进度，支持点行切入子窗口", "View all spawned subagents in this chat");
                }
                else if (_rcCard2CloseBtn.Contains(e.Location))
                {
                    newHover = I18n.T("返回逐轮响应明细表", "Back to turn-by-turn list");
                }
                else if (_rcTurnFilterSlow.Contains(e.Location))
                {
                    newHover = _turnFilter == TurnQuickFilter.SlowWait
                        ? I18n.T("再次点击恢复显示全部轮次", "Click again to show all turns")
                        : I18n.T("仅看触发上下文压缩或等待 ≥ 6s 的轮次", "Filter compacted or slow (>=6s) turns");
                }
                else if (_rcTurnFilterWarn.Contains(e.Location))
                {
                    newHover = _turnFilter == TurnQuickFilter.CacheWarn ? I18n.T("再次点击恢复显示全部轮次", "Click again to show all turns") : I18n.T("仅看切换模型或缓存命中下降的轮次", "Filter model switch or cache-drop turns");
                }

                if (string.IsNullOrEmpty(newHover) && _hudCard2Mode == HudCard2Mode.Subagents)
                {
                    for (int i = 0; i < _subagentRowHits.Count; i++)
                    {
                        if (_subagentRowHits[i].Key.Contains(e.Location))
                        {
                            SubagentBriefItem sb = _subagentRowHits[i].Value;
                            newHover = I18n.TF("点击进入子任务「{0}」· 共 {1} 轮 · 缓存 {2:F1}%", "Inspect subagent [{0}] · {1} turns · cache {2:F1}%",
                                sb.RoleTitle, sb.Turns, sb.CacheHitPct);
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(newHover) && _hudCard2Mode == HudCard2Mode.Turns)
                {
                    for (int i = 0; i < _turnRowHits.Count; i++)
                    {
                        if (_turnRowHits[i].Key.Contains(e.Location))
                        {
                            newHoveredTurn = _turnRowHits[i].Value;
                            newHoveredRect = _turnRowHits[i].Key;
                            bool isPinned = _pinnedTurnNo == newHoveredTurn.TurnNo;
                            newHover = I18n.TF("#{0} 已内嵌展开{1} · 滚轮可翻看近 {2} 轮", "#{0} expanded{1} · scroll last {2} turns",
                                newHoveredTurn.TurnNo,
                                isPinned ? I18n.T(" (已固定，再点收起)", " (pinned, click to close)") : I18n.T(" (单击可固定)", " (click to pin)"),
                                _data != null && _data.ActiveHud != null && _data.ActiveHud.RecentTurns != null ? _data.ActiveHud.RecentTurns.Count : 6);
                            break;
                        }
                    }
                }

                if (newHoveredTurn == null && string.IsNullOrEmpty(newHover) && _rcHealthBadge.Contains(e.Location) && _data != null && _data.ActiveHud != null)
                {
                    HudSnapshot h = _data.ActiveHud;
                    if (h.Compacted && h.CompactPreTokens > 0)
                    {
                        newHover = I18n.TF("对话已折叠：折叠前 {0} → 摘要 ~{1}", "Compacted: {0} -> ~{1}",
                            FmtCompact(h.CompactPreTokens),
                            h.CompactSummaryTokens > 0 ? FmtCompact(h.CompactSummaryTokens) : I18n.T("精简", "Trimmed"));
                    }
                }

                if (newHoveredTurn == null && string.IsNullOrEmpty(newHover))
                {
                    for (int i = 0; i < _lastTreemapCells.Count; i++)
                    {
                        if (_lastTreemapCells[i].Rect.Contains(e.X, e.Y))
                        {
                            newHover = _lastTreemapCells[i].HoverDetail;
                            break;
                        }
                    }
                }
            }
            else if (_activeTab == MainTab.GlobalLedger)
            {
                if (_rcLedgerModeModel.Contains(e.Location))
                {
                    newHover = I18n.T("按模型档位统计总消耗与缓存命中率，点行可下钻", "Token usage & cache hit by model (click to drill down)");
                }
                else if (_rcLedgerModeTask.Contains(e.Location))
                {
                    newHover = I18n.T("按具体会话任务统计消耗排行（自动合并关联子智能体），点行可跳至该窗口", "Usage ranked by conversation (incl. subagents; click to inspect)");
                }
                for (int i = 0; string.IsNullOrEmpty(newHover) && i < _toolChipHits.Count; i++)
                {
                    if (_toolChipHits[i].Key.Contains(e.Location))
                    {
                        newHover = _toolChipHits[i].Value;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(newHover) && string.IsNullOrEmpty(_drillModelName))
                {
                    for (int i = 0; i < _dayBarHoverHits.Count; i++)
                    {
                        if (_dayBarHoverHits[i].Key.Contains(e.Location))
                        {
                            newHover = _dayBarHoverHits[i].Value;
                            break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(newHover))
                {
                    if (string.IsNullOrEmpty(_drillModelName))
                    {
                        if (_ledgerRankMode == LedgerRankMode.ByModel)
                        {
                            for (int i = 0; i < _modelRowHits.Count; i++)
                            {
                                if (_modelRowHits[i].Key.Contains(e.Location))
                                {
                                    newHover = I18n.TF("点击查看「{0}」各对话明细与高频工具", "Inspect [{0}] conversations & top tools", _modelRowHits[i].Value);
                                    break;
                                }
                            }
                        }
                        else
                        {
                            for (int i = 0; i < _taskRowHits.Count; i++)
                            {
                                if (_taskRowHits[i].Key.Contains(e.Location))
                                {
                                    LedgerConvRankItem ti = _taskRowHits[i].Value;
                                    newHover = I18n.TF("点击跳转查看窗口「{0}」实时上下文与轮次明细", "Inspect live HUD for [{0}]", ti.Title);
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < _drillRowHits.Count; i++)
                        {
                            if (_drillRowHits[i].Rect.Contains(e.Location))
                            {
                                newHover = _drillRowHits[i].HoverDetail;
                                break;
                            }
                        }
                    }
                }
            }

            bool hand = _rcConvPicker.Contains(e.Location) ||
                        _rcScaleBtn.Contains(e.Location) ||
                        _rcLangBtn.Contains(e.Location) ||
                        _rcRefreshBtn.Contains(e.Location) ||
                        _rcPosHintBar.Contains(e.Location) ||
                        _rcTabHud.Contains(e.Location) ||
                        _rcTabLedger.Contains(e.Location) ||
                        _rcAutoGcBtn.Contains(e.Location) ||
                        _rcCleanMemBtn.Contains(e.Location) ||
                        (_activeTab == MainTab.WindowHud && (
                            newHoveredTurn != null ||
                            _rcSubagentChip.Contains(e.Location) ||
                            _rcCard2TabTurns.Contains(e.Location) ||
                            _rcCard2TabSubs.Contains(e.Location) ||
                            _rcCard2CloseBtn.Contains(e.Location) ||
                            _rcTurnFilterSlow.Contains(e.Location) ||
                            _rcTurnFilterWarn.Contains(e.Location))) ||
                        _rcOpenFolder.Contains(e.Location) ||
                        _rcExitBtn.Contains(e.Location);

            if (!hand && _activeTab == MainTab.WindowHud)
            {
                for (int i = 0; i < _rankRowHits.Count; i++)
                {
                    if (_rankRowHits[i].Key.Contains(e.Location)) { hand = true; break; }
                }
                if (!hand && _hudCard2Mode == HudCard2Mode.Subagents)
                {
                    for (int i = 0; i < _subagentRowHits.Count; i++)
                    {
                        if (_subagentRowHits[i].Key.Contains(e.Location)) { hand = true; break; }
                    }
                }
            }

            if (!hand && _activeTab == MainTab.GlobalLedger)
            {
                if (string.IsNullOrEmpty(_drillModelName) &&
                    (_rcLedgerModeModel.Contains(e.Location) || _rcLedgerModeTask.Contains(e.Location)))
                {
                    hand = true;
                }
                for (int i = 0; !hand && i < _rcPeriodBtns.Length; i++)
                {
                    if (_rcPeriodBtns[i].Contains(e.Location)) { hand = true; break; }
                }
                if (!hand && string.IsNullOrEmpty(_drillModelName))
                {
                    for (int i = 0; i < _dayBarClickHits.Count; i++)
                    {
                        if (_dayBarClickHits[i].Key.Contains(e.Location)) { hand = true; break; }
                    }
                }
                if (!hand && string.IsNullOrEmpty(_drillModelName))
                {
                    if (_ledgerRankMode == LedgerRankMode.ByModel)
                    {
                        for (int i = 0; i < _modelRowHits.Count; i++)
                        {
                            if (_modelRowHits[i].Key.Contains(e.Location)) { hand = true; break; }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < _taskRowHits.Count; i++)
                        {
                            if (_taskRowHits[i].Key.Contains(e.Location)) { hand = true; break; }
                        }
                    }
                }
                else if (!hand && !string.IsNullOrEmpty(_drillModelName))
                {
                    if (_rcDrillBackBtn.Contains(e.Location) ||
                        _rcDrillTabConv.Contains(e.Location) ||
                        _rcDrillTabTurns.Contains(e.Location) ||
                        _rcDrillLowCacheBtn.Contains(e.Location) ||
                        _rcDrillPrevBtn.Contains(e.Location) ||
                        _rcDrillNextBtn.Contains(e.Location))
                    {
                        hand = true;
                    }
                    else
                    {
                        for (int i = 0; i < _drillRowHits.Count; i++)
                        {
                            if (_drillRowHits[i].Rect.Contains(e.Location)) { hand = true; break; }
                        }
                    }
                }
            }

            Cursor = hand ? Cursors.Hand : Cursors.Default;
            bool turnHoverChanged = ! ReferenceEquals(newHoveredTurn, _hoveredTurnItem);
            if (newHover != _hoverHint || turnHoverChanged || hand || _wasHand)
            {
                _hoverHint = newHover;
                _hoveredTurnItem = newHoveredTurn;
                _hoveredTurnRowRect = newHoveredRect;
                _wasHand = hand;
                Invalidate();
            }
        }

        private void OnFormMouseWheel(object sender, MouseEventArgs e)
        {
            int step = e.Delta > 0 ? -1 : 1;
            if (_activeTab == MainTab.WindowHud)
            {
                if (_rcTurnTableArea.Contains(e.Location) && _data != null && _data.ActiveHud != null)
                {
                    if ((_hudCard2Mode == HudCard2Mode.Subagents || _hudCard2Mode == HudCard2Mode.Breakdown))
                    {
                        int totalRows = 0;
                        if (_hudCard2Mode == HudCard2Mode.Subagents && _data.ActiveHud.Subagents != null)
                            totalRows = _data.ActiveHud.Subagents.Count;
                        else if (_hudCard2Mode == HudCard2Mode.Breakdown && _data.ActiveHud.CategoryBreakdowns != null)
                        {
                            List<BreakdownRowItem> bList;
                            if (_data.ActiveHud.CategoryBreakdowns.TryGetValue(_hudInspectCat, out bList) && bList != null)
                                totalRows = bList.Count;
                        }
                        int maxSubScroll = Math.Max(0, totalRows - _subagentVisCap);
                        if (maxSubScroll > 0)
                        {
                            int nextSub = Math.Max(0, Math.Min(maxSubScroll, _subagentScroll + step));
                            if (nextSub != _subagentScroll)
                            {
                                _subagentScroll = nextSub;
                                Invalidate();
                            }
                        }
                    }
                    else if (_hudCard2Mode == HudCard2Mode.Turns && _data.ActiveHud.RecentTurns != null)
                    {
                        int totalTurns = GetFilteredTurns(_data.ActiveHud).Count;
                        int maxTurnScroll = Math.Max(0, totalTurns - _hudVisibleCap);
                        if (maxTurnScroll > 0)
                        {
                            int nextScroll = Math.Max(0, Math.Min(maxTurnScroll, _hudTurnScroll + step));
                            if (nextScroll != _hudTurnScroll)
                            {
                                _hudTurnScroll = nextScroll;
                                _hoveredTurnItem = null;
                                Invalidate();
                            }
                        }
                    }
                }
                return;
            }

            if (_activeTab != MainTab.GlobalLedger) return;

            if (string.IsNullOrEmpty(_drillModelName))
            {
                PeriodSummary activeSum = GetPeriodSummary(_ledgerPeriod);
                int total = _ledgerRankMode == LedgerRankMode.ByModel
                    ? activeSum.ModelRanking.Count
                    : activeSum.ConvRanking.Count;
                int maxScroll = Math.Max(0, total - _overviewVisibleCap);
                if (maxScroll <= 0) return;
                int next = Math.Max(0, Math.Min(maxScroll, _overviewModelScroll + step));
                if (next != _overviewModelScroll)
                {
                    _overviewModelScroll = next;
                    Invalidate();
                }
            }
            else if (_drillData != null)
            {
                int total = GetActiveDrillTotalCount();
                int stepRows = _drillSubTab == 1 ? step * 3 : step;
                int maxScroll = Math.Max(0, total - _drillVisibleCap);
                if (maxScroll <= 0) return;
                int next = Math.Max(0, Math.Min(maxScroll, _drillListScroll + stepRows));
                if (next != _drillListScroll)
                {
                    _drillListScroll = next;
                    Invalidate();
                }
            }
        }

        private int GetActiveDrillTotalCount()
        {
            if (_drillData == null) return 0;
            if (_drillSubTab == 0)
            {
                if (!_drillLowCacheOnly) return _drillData.ConvRows.Count;
                int c = 0;
                for (int i = 0; i < _drillData.ConvRows.Count; i++)
                {
                    if (_drillData.ConvRows[i].LowCacheCalls > 0 || _drillData.ConvRows[i].CacheHitPct < 50.0) c++;
                }
                return c;
            }
            else
            {
                if (!_drillLowCacheOnly) return _drillData.TurnRows.Count;
                int c = 0;
                for (int i = 0; i < _drillData.TurnRows.Count; i++)
                {
                    if (_drillData.TurnRows[i].CacheHitPct < 50.0) c++;
                }
                return c;
            }
        }

        private PeriodSummary GetPeriodSummary(PeriodTab tab)
        {
            if (tab == PeriodTab.Today) return _data.Today;
            if (tab == PeriodTab.Yesterday) return _data.Yesterday;
            if (tab == PeriodTab.Days7) return _data.Days7;
            if (tab == PeriodTab.Days30) return _data.Days30;
            return _data.All;
        }

        private void OnFormMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            if (_rcConvPicker.Contains(e.Location))
            {
                OpenConversationMenu();
                return;
            }
            if (_rcScaleBtn.Contains(e.Location))
            {
                if (_scaleMode == ScaleMode.Auto) _scaleMode = ScaleMode.K256;
                else if (_scaleMode == ScaleMode.K256) _scaleMode = ScaleMode.M1;
                else _scaleMode = ScaleMode.Auto;
                Invalidate();
                if (_onScaleChanged != null) _onScaleChanged();
                return;
            }
            if (_rcLangBtn.Contains(e.Location))
            {
                if (_onToggleLanguage != null) _onToggleLanguage();
                return;
            }
            if (_rcRefreshBtn.Contains(e.Location))
            {
                if (_onManualRefresh != null) _onManualRefresh();
                return;
            }
            if (_rcPosHintBar.Contains(e.Location))
            {
                bool hasExtApi = _data != null && _data.ExternalApi != null && _data.ExternalApi.HasLedger;
                if (!hasExtApi)
                {
                    UserConfig cfg = _getConfig != null ? _getConfig() : null;
                    if (cfg != null && string.Equals(cfg.AnchorMode, "custom", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_onResetPosition != null) _onResetPosition();
                    }
                }
                return;
            }
            if (_rcTabHud.Contains(e.Location))
            {
                _activeTab = MainTab.WindowHud;
                _hoverHint = "";
                Invalidate();
                return;
            }
            if (_rcTabLedger.Contains(e.Location))
            {
                if (_activeTab == MainTab.GlobalLedger && !string.IsNullOrEmpty(_drillModelName))
                {
                    ExitModelDrilldown();
                    return;
                }
                _activeTab = MainTab.GlobalLedger;
                _hoverHint = "";
                if (!string.IsNullOrEmpty(_drillModelName))
                    RefreshDrilldownIfActive();
                Invalidate();
                return;
            }
            if (_rcAutoGcBtn.Contains(e.Location))
            {
                if (_onToggleAutoGc != null) _onToggleAutoGc();
                return;
            }
            if (_rcCleanMemBtn.Contains(e.Location))
            {
                if (_onCleanMemNow != null) _onCleanMemNow();
                return;
            }
            if (_activeTab == MainTab.WindowHud)
            {
                if (_rcSubagentChip.Contains(e.Location) && _data != null && _data.ActiveHud != null)
                {
                    HudSnapshot h = _data.ActiveHud;
                    if (h.IsSubagent && !string.IsNullOrEmpty(h.ParentConvKey))
                    {
                        string backKey = _subagentReturnConvKey != null ? _subagentReturnConvKey : h.ParentConvKey;
                        _subagentReturnConvKey = null;
                        _hudCard2Mode = HudCard2Mode.Turns;
                        if (_onSelectConv != null) _onSelectConv(backKey);
                        else Invalidate();
                        return;
                    }
                    if (h.SubagentCount > 0)
                    {
                        _hudCard2Mode = (_hudCard2Mode == HudCard2Mode.Subagents) ? HudCard2Mode.Turns : HudCard2Mode.Subagents;
                        _subagentScroll = 0;
                        _hoveredTurnItem = null;
                        Invalidate();
                        return;
                    }
                }
                if (_rcCard2TabTurns.Contains(e.Location))
                {
                    _hudCard2Mode = HudCard2Mode.Turns;
                    _hudInspectCat = "";
                    Invalidate();
                    return;
                }
                if (_rcCard2TabSubs.Contains(e.Location))
                {
                    _hudCard2Mode = HudCard2Mode.Subagents;
                    _hudInspectCat = "";
                    _subagentScroll = 0;
                    _hoveredTurnItem = null;
                    Invalidate();
                    return;
                }
                if (_rcCard2CloseBtn.Contains(e.Location))
                {
                    _hudCard2Mode = HudCard2Mode.Turns;
                    _hudInspectCat = "";
                    Invalidate();
                    return;
                }
                if (_rcTurnFilterSlow.Contains(e.Location))
                {
                    _turnFilter = (_turnFilter == TurnQuickFilter.SlowWait) ? TurnQuickFilter.All : TurnQuickFilter.SlowWait;
                    _hudTurnScroll = 0;
                    _pinnedTurnNo = -1;
                    Invalidate();
                    return;
                }
                if (_rcTurnFilterWarn.Contains(e.Location))
                {
                    _turnFilter = (_turnFilter == TurnQuickFilter.CacheWarn) ? TurnQuickFilter.All : TurnQuickFilter.CacheWarn;
                    _hudTurnScroll = 0;
                    _pinnedTurnNo = -1;
                    Invalidate();
                    return;
                }
                for (int i = 0; i < _rankRowHits.Count; i++)
                {
                    if (_rankRowHits[i].Key.Contains(e.Location))
                    {
                        string cat = _rankRowHits[i].Value.Key;
                        string rTitle = _rankRowHits[i].Value.Value;
                        if (_hudCard2Mode == HudCard2Mode.Breakdown && string.Equals(_hudInspectCat, cat, StringComparison.OrdinalIgnoreCase))
                        {
                            _hudCard2Mode = HudCard2Mode.Turns;
                            _hudInspectCat = "";
                        }
                        else
                        {
                            _hudCard2Mode = HudCard2Mode.Breakdown;
                            _hudInspectCat = cat;
                            _hudInspectTitle = rTitle;
                            _hoveredTurnItem = null;
                        }
                        Invalidate();
                        return;
                    }
                }
                if (_hudCard2Mode == HudCard2Mode.Subagents)
                {
                    for (int i = 0; i < _subagentRowHits.Count; i++)
                    {
                        if (_subagentRowHits[i].Key.Contains(e.Location))
                        {
                            SubagentBriefItem sb = _subagentRowHits[i].Value;
                            if (_data != null && _data.ActiveHud != null && !_data.ActiveHud.IsSubagent)
                                _subagentReturnConvKey = _selectedConvKey;
                            _hudCard2Mode = HudCard2Mode.Turns;
                            _hoverHint = "";
                            if (_onSelectConv != null) _onSelectConv(sb.Key);
                            else Invalidate();
                            return;
                        }
                    }
                }
                if (_hudCard2Mode == HudCard2Mode.Turns)
                {
                    for (int i = 0; i < _turnRowHits.Count; i++)
                    {
                        if (_turnRowHits[i].Key.Contains(e.Location))
                        {
                            int tNo = _turnRowHits[i].Value.TurnNo;
                            _pinnedTurnNo = (_pinnedTurnNo == tNo) ? -1 : tNo;
                            Invalidate();
                            return;
                        }
                    }
                }
            }
            if (_activeTab == MainTab.GlobalLedger)
            {
                if (string.IsNullOrEmpty(_drillModelName))
                {
                    if (_rcLedgerModeModel.Contains(e.Location))
                    {
                        if (_ledgerRankMode != LedgerRankMode.ByModel)
                        {
                            _ledgerRankMode = LedgerRankMode.ByModel;
                            _overviewModelScroll = 0;
                            Invalidate();
                        }
                        return;
                    }
                    if (_rcLedgerModeTask.Contains(e.Location))
                    {
                        if (_ledgerRankMode != LedgerRankMode.ByTask)
                        {
                            _ledgerRankMode = LedgerRankMode.ByTask;
                            _overviewModelScroll = 0;
                            Invalidate();
                        }
                        return;
                    }
                }

                for (int i = 0; i < _rcPeriodBtns.Length; i++)
                {
                    if (_rcPeriodBtns[i].Contains(e.Location))
                    {
                        _ledgerPeriod = (PeriodTab)i;
                        _overviewModelScroll = 0;
                        _drillListScroll = 0;
                        RefreshDrilldownIfActive();
                        Invalidate();
                        return;
                    }
                }

                if (string.IsNullOrEmpty(_drillModelName))
                {
                    for (int i = 0; i < _dayBarClickHits.Count; i++)
                    {
                        if (_dayBarClickHits[i].Key.Contains(e.Location))
                        {
                            _ledgerPeriod = (PeriodTab)_dayBarClickHits[i].Value;
                            _overviewModelScroll = 0;
                            Invalidate();
                            return;
                        }
                    }
                    if (_ledgerRankMode == LedgerRankMode.ByModel)
                    {
                        for (int i = 0; i < _modelRowHits.Count; i++)
                        {
                            if (_modelRowHits[i].Key.Contains(e.Location))
                            {
                                EnterModelDrilldown(_modelRowHits[i].Value, 0);
                                return;
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < _taskRowHits.Count; i++)
                        {
                            if (_taskRowHits[i].Key.Contains(e.Location))
                            {
                                LedgerConvRankItem clickedTask = _taskRowHits[i].Value;
                                string targetKey = clickedTask.ConvKey;
                                if (!string.IsNullOrEmpty(targetKey))
                                {
                                    _subagentReturnConvKey = null;
                                    _activeTab = MainTab.WindowHud;
                                    _hudCard2Mode = clickedTask.SubCount > 0 ? HudCard2Mode.Subagents : HudCard2Mode.Turns;
                                    _subagentScroll = 0;
                                    _hoverHint = "";
                                    if (_onSelectConv != null) _onSelectConv(targetKey);
                                    else Invalidate();
                                }
                                return;
                            }
                        }
                    }
                }
                else
                {
                    if (_rcDrillBackBtn.Contains(e.Location))
                    {
                        ExitModelDrilldown();
                        return;
                    }
                    if (_rcDrillTabConv.Contains(e.Location))
                    {
                        _drillSubTab = 0;
                        _drillListScroll = 0;
                        Invalidate();
                        return;
                    }
                    if (_rcDrillTabTurns.Contains(e.Location))
                    {
                        _drillSubTab = 1;
                        _drillListScroll = 0;
                        Invalidate();
                        return;
                    }
                    if (_rcDrillLowCacheBtn.Contains(e.Location))
                    {
                        _drillLowCacheOnly = !_drillLowCacheOnly;
                        _drillListScroll = 0;
                        Invalidate();
                        return;
                    }
                    if (_rcDrillPrevBtn.Contains(e.Location))
                    {
                        int step = Math.Max(1, _drillVisibleCap - 1);
                        _drillListScroll = Math.Max(0, _drillListScroll - step);
                        Invalidate();
                        return;
                    }
                    if (_rcDrillNextBtn.Contains(e.Location))
                    {
                        int total = GetActiveDrillTotalCount();
                        int maxScroll = Math.Max(0, total - _drillVisibleCap);
                        int step = Math.Max(1, _drillVisibleCap - 1);
                        _drillListScroll = Math.Min(maxScroll, _drillListScroll + step);
                        Invalidate();
                        return;
                    }
                    for (int i = 0; i < _drillRowHits.Count; i++)
                    {
                        if (_drillRowHits[i].Rect.Contains(e.Location))
                        {
                            string targetKey = _drillRowHits[i].ConvKey;
                            if (!string.IsNullOrEmpty(targetKey))
                            {
                                _activeTab = MainTab.WindowHud;
                                _hoverHint = "";
                                if (_onSelectConv != null) _onSelectConv(targetKey);
                                else Invalidate();
                            }
                            return;
                        }
                    }
                }
            }
            if (_rcOpenFolder.Contains(e.Location))
            {
                Hide();
                if (_onOpenLogs != null) _onOpenLogs();
                return;
            }
            if (_rcExitBtn.Contains(e.Location))
            {
                Hide();
            }
        }

        private void OpenConversationMenu()
        {
            _convMenu.Items.Clear();
            _convMenu.BackColor = ColCardBg;
            _convMenu.ForeColor = ColTextMain;
            _convMenu.Renderer = new ToolStripProfessionalRenderer(new ThemeMenuColorTable());

            Font oldMf = _convMenuFont;
            _convMenuFont = new Font(I18n.UiFontFamily, 9.0f, FontStyle.Regular, GraphicsUnit.Point);
            _convMenu.Font = _convMenuFont;
            if (oldMf != null) oldMf.Dispose();

            string latestTitle = _data.RecentOptions.Count > 0 ? _data.RecentOptions[0].Title : I18n.T("当前活跃窗口", "Active Window");
            var miAuto = new ToolStripMenuItem(I18n.TF("自动跟随最新活跃窗口 ({0})", "Auto-follow active window ({0})", latestTitle))
            {
                Checked = string.IsNullOrEmpty(_selectedConvKey)
            };
            miAuto.Click += (s, e) =>
            {
                _subagentReturnConvKey = null;
                if (_onSelectConv != null) _onSelectConv("");
            };
            _convMenu.Items.Add(miAuto);

            HudSnapshot curHud = _data != null ? _data.ActiveHud : null;
            if (curHud != null && curHud.IsSubagent && !string.IsNullOrEmpty(curHud.ParentConvKey))
            {
                string pKey = _subagentReturnConvKey != null ? _subagentReturnConvKey : curHud.ParentConvKey;
                var miBack = new ToolStripMenuItem(I18n.TF("← 返回主窗口 ({0})", "<- Back to Main ({0})", curHud.ParentTitle));
                miBack.Click += (s, e) =>
                {
                    _subagentReturnConvKey = null;
                    _hudCard2Mode = HudCard2Mode.Turns;
                    if (_onSelectConv != null) _onSelectConv(pKey);
                };
                _convMenu.Items.Add(miBack);
            }
            else if (curHud != null && curHud.Subagents != null && curHud.Subagents.Count > 0)
            {
                _convMenu.Items.Add(new ToolStripSeparator());
                var miHeader = new ToolStripMenuItem(I18n.TF("当前窗口子任务 ({0})", "Subagents in This Chat ({0})", curHud.Subagents.Count))
                {
                    Enabled = false
                };
                _convMenu.Items.Add(miHeader);
                int subShown = 0;
                foreach (SubagentBriefItem sb in curHud.Subagents)
                {
                    if (subShown >= 10) break;
                    SubagentBriefItem capSub = sb;
                    string stFlag = capSub.IsWorking ? I18n.T("[运行中] ", "[Running] ") : "";
                    string shortRole = capSub.RoleTitle.Length > 20 ? (capSub.RoleTitle.Substring(0, 19) + "…") : capSub.RoleTitle;
                    string subLabel = I18n.TF("  ↳ {0}{1} ({2}轮 · {3})", "  -> {0}{1} ({2}t · {3})",
                        stFlag, shortRole, capSub.Turns, FmtCompact(capSub.TotalTokens));
                    var miSub = new ToolStripMenuItem(subLabel)
                    {
                        Checked = string.Equals(_selectedConvKey, capSub.Key, StringComparison.OrdinalIgnoreCase)
                    };
                    miSub.Click += (s, e) =>
                    {
                        if (!curHud.IsSubagent) _subagentReturnConvKey = _selectedConvKey;
                        _activeTab = MainTab.WindowHud;
                        _hudCard2Mode = HudCard2Mode.Turns;
                        if (_onSelectConv != null) _onSelectConv(capSub.Key);
                    };
                    _convMenu.Items.Add(miSub);
                    subShown++;
                }
            }

            _convMenu.Items.Add(new ToolStripSeparator());

            foreach (var opt in _data.RecentOptions)
            {
                ConvOptionItem captured = opt;
                string statePrefix = opt.IsWorking ? I18n.T("[运行中] ", "[Running] ") : "";
                string txt = string.Format("[{0:HH:mm}] {1}{2}", opt.LastTime, statePrefix, opt.Title);
                var mi = new ToolStripMenuItem(txt)
                {
                    Checked = string.Equals(_selectedConvKey, opt.Key, StringComparison.OrdinalIgnoreCase)
                };
                mi.Click += (s, e) =>
                {
                    _subagentReturnConvKey = null;
                    if (_onSelectConv != null) _onSelectConv(captured.Key);
                };
                _convMenu.Items.Add(mi);
            }

            _suppressDeactivate = true;
            _convMenu.Show(this, new Point(_rcConvPicker.Left, _rcConvPicker.Bottom + S(2)));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_convMenu != null && !_convMenu.IsDisposed) _convMenu.Dispose();
                if (_convMenuFont != null) { _convMenuFont.Dispose(); _convMenuFont = null; }
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            RenderAll(e.Graphics);
        }

        public void RenderOffscreenToPng(string outPngPath, bool simulateTurnHover = false)
        {
            using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                RenderAll(g);
                if (simulateTurnHover && _turnRowHits.Count > 0)
                {
                    int pick = _turnRowHits.Count > 1 ? 1 : 0;
                    _hoveredTurnItem = _turnRowHits[pick].Value;
                    _hoveredTurnRowRect = _turnRowHits[pick].Key;
                    _hoverHint = I18n.TF("#{0} 已内嵌展开 (单击可固定) · 滚轮可翻看近 {1} 轮",
                        "#{0} expanded (click to pin) · scroll last {1} turns",
                        _hoveredTurnItem.TurnNo,
                        _data != null && _data.ActiveHud != null && _data.ActiveHud.RecentTurns != null ? _data.ActiveHud.RecentTurns.Count : 6);
                    RenderAll(g);
                }
                bmp.Save(outPngPath, ImageFormat.Png);
            }
        }

        public void SetPreviewMode(HudCard2Mode c2Mode, string inspectCat, string inspectTitle, LedgerRankMode lMode, TurnQuickFilter tFilter)
        {
            if (_data != null && _data.ActiveHud != null)
                _lastHudScrollKey = _data.ActiveHud.Key;
            _hudCard2Mode = c2Mode;
            _hudInspectCat = inspectCat ?? "";
            _hudInspectTitle = inspectTitle ?? "";
            _ledgerRankMode = lMode;
            _turnFilter = tFilter;
            Invalidate();
        }

        private List<TurnItem> GetFilteredTurns(HudSnapshot hud)
        {
            var res = new List<TurnItem>();
            if (hud == null || hud.RecentTurns == null) return res;
            for (int i = 0; i < hud.RecentTurns.Count; i++)
            {
                TurnItem t = hud.RecentTurns[i];
                if (_turnFilter == TurnQuickFilter.SlowWait)
                {
                    if (t.WaitSec.HasValue && t.WaitSec.Value >= 6.0)
                        res.Add(t);
                }
                else if (_turnFilter == TurnQuickFilter.CacheWarn)
                {
                    if (t.IsCacheDropWarn || t.ModelSwitchedCold || (t.TurnNo > 1 && t.CacheHitPct < 35.0))
                        res.Add(t);
                }
                else
                {
                    res.Add(t);
                }
            }
            return res;
        }

        private long ResolveEffectiveBudget(HudSnapshot hud)
        {
            if (_scaleMode == ScaleMode.K256) return 256000L;
            if (_scaleMode == ScaleMode.M1) return 983040L;
            long used = (hud != null && hud.Latest != null) ? hud.Latest.TotalUsed : 0L;
            return used > 512000L ? 983040L : 512000L;
        }

        private void RenderAll(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(ColWinBg);

            int pad = S(12);
            int contentW = Width - pad * 2;
            int y = pad;

            // Strict 4-Tier Unified Typography System (100% Microsoft YaHei UI in DPI-locked pixels — zero Consolas fallback, restrained bold)
            string ff = I18n.UiFontFamily;
            using (var fHero = new Font(ff, Math.Max(16f, S(20.0f)), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fTitle = new Font(ff, Math.Max(12f, S(13.2f)), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fBodyBold = new Font(ff, Math.Max(11f, S(12.0f)), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fBody = new Font(ff, Math.Max(11f, S(12.0f)), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fMetaBold = new Font(ff, Math.Max(10f, S(11.2f)), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fMeta = new Font(ff, Math.Max(10f, S(11.0f)), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                HudSnapshot hud = _data.ActiveHud;
                long budget = ResolveEffectiveBudget(hud);

                // ================= 1. CONVERSATION PICKER + SCALE + LANG + REFRESH =================
                int barH = S(30);
                int refreshW = I18n.IsZh ? S(46) : S(56);
                int langW = S(34);
                int scaleW = I18n.IsZh ? S(82) : S(86);
                int gap = S(5);
                int pickerW = contentW - scaleW - langW - refreshW - gap * 3;

                _rcConvPicker = new Rectangle(pad, y, pickerW, barH);
                _rcScaleBtn = new Rectangle(_rcConvPicker.Right + gap, y, scaleW, barH);
                _rcLangBtn = new Rectangle(_rcScaleBtn.Right + gap, y, langW, barH);
                _rcRefreshBtn = new Rectangle(_rcLangBtn.Right + gap, y, refreshW, barH);

                DrawConvPickerBox(g, _rcConvPicker, hud, fBodyBold);

                string scaleTxt = _scaleMode == ScaleMode.Auto
                    ? I18n.T("标尺: 自动", "Cap: Auto")
                    : (_scaleMode == ScaleMode.K256 ? I18n.T("标尺: 256K", "Cap: 256K") : I18n.T("标尺: 1M", "Cap: 1M"));
                bool scaleCustom = _scaleMode != ScaleMode.Auto;
                FillRoundCard(g, _rcScaleBtn, S(8),
                    scaleCustom ? ColAccentSoft : (_rcScaleBtn.Contains(_mousePt) ? ColHoverFill : ColCardBg),
                    scaleCustom ? ColAccent : ColCardBorder);
                DrawCenteredText(g, scaleTxt, fMetaBold, scaleCustom ? ColAccent : ColTextMain, _rcScaleBtn);

                FillRoundCard(g, _rcLangBtn, S(8),
                    _rcLangBtn.Contains(_mousePt) ? ColHoverFill : ColCardBg, ColCardBorder);
                DrawCenteredText(g, I18n.IsZh ? "EN" : "ZH", fMetaBold, ColTextMain, _rcLangBtn);

                FillRoundCard(g, _rcRefreshBtn, S(8),
                    _rcRefreshBtn.Contains(_mousePt) ? ColHoverFill : ColCardBg, ColCardBorder);
                DrawCenteredText(g, I18n.T("刷新", "Refresh"), fMetaBold, ColTextMain, _rcRefreshBtn);

                y += barH + S(7);

                // ================= 2. SEGMENTED TABS + READ-ONLY SHIELD BADGE =================
                int navWrapH = S(32);
                var rcSegWrap = new Rectangle(pad, y, contentW, navWrapH);
                Color segWrapBg = Pal.IsDark ? Color.FromArgb(30, 30, 35) : ColSubtleFill;
                Color segActBg = Pal.IsDark ? Color.FromArgb(52, 52, 60) : ColCardBg;
                Color segActBd = Pal.IsDark ? Color.FromArgb(76, 76, 86) : ColCardBorder;
                FillRoundCard(g, rcSegWrap, S(9), segWrapBg, ColCardBorder);

                int badgeW = I18n.IsZh ? S(108) : S(116);
                int tabAreaW = contentW - badgeW - S(10);
                int halfTab = (tabAreaW - S(6)) / 2;
                _rcTabHud = new Rectangle(pad + S(3), y + S(3), halfTab, navWrapH - S(6));
                _rcTabLedger = new Rectangle(_rcTabHud.Right + S(4), y + S(3), halfTab, navWrapH - S(6));

                if (_activeTab == MainTab.WindowHud)
                    FillRoundCard(g, _rcTabHud, S(6), segActBg, segActBd);
                else if (_rcTabHud.Contains(_mousePt))
                    FillRoundCard(g, _rcTabHud, S(6), ColHoverFill, ColCardBorder);

                if (_activeTab == MainTab.GlobalLedger)
                    FillRoundCard(g, _rcTabLedger, S(6), segActBg, segActBd);
                else if (_rcTabLedger.Contains(_mousePt))
                    FillRoundCard(g, _rcTabLedger, S(6), ColHoverFill, ColCardBorder);

                DrawCenteredText(g, I18n.T("窗口 HUD", "Window HUD"), _activeTab == MainTab.WindowHud ? fBodyBold : fBody, _activeTab == MainTab.WindowHud ? ColTextMain : ColTextSub, _rcTabHud);
                DrawCenteredText(g, I18n.T("全局账本", "Global Ledger"), _activeTab == MainTab.GlobalLedger ? fBodyBold : fBody, _activeTab == MainTab.GlobalLedger ? ColTextMain : ColTextSub, _rcTabLedger);

                var rcSafe = new Rectangle(rcSegWrap.Right - badgeW - S(4), y + S(4), badgeW, navWrapH - S(8));
                FillRoundCard(g, rcSafe, S(12), ColOkSoft, ColOkBorder);
                DrawStatusDot(g, rcSafe.Left + S(11), rcSafe.Top + rcSafe.Height / 2, S(6), ColOk, false);
                DrawTxtL(g, I18n.T("纯本地·0外联", "Local · Offline"), fMetaBold, ColOkText,
                    new Rectangle(rcSafe.Left + S(19), rcSafe.Top, rcSafe.Width - S(22), rcSafe.Height));

                y += navWrapH + S(8);

                // Reserve unified compact ops/API bar + minimal footer height
                int footerH = S(24);
                int opsBarH = S(32);
                int bodyAvailH = Height - y - pad - footerH - opsBarH - S(12);

                // ================= 3. TAB BODY (CLEAN TWO-CARD HUD OR LEDGER) =================
                string curHudKey = hud != null ? hud.Key : "";
                if (curHudKey != _lastHudScrollKey)
                {
                    _lastHudScrollKey = curHudKey;
                    _hudTurnScroll = 0;
                    _subagentScroll = 0;
                    _pinnedTurnNo = -1;
                    _hoveredTurnItem = null;
                    _hudCard2Mode = HudCard2Mode.Turns;
                    _hudInspectCat = "";
                    _turnFilter = TurnQuickFilter.All;
                }

                _rcSubagentChip = Rectangle.Empty;
                _rcCard2TabTurns = Rectangle.Empty;
                _rcCard2TabSubs = Rectangle.Empty;
                _rcCard2CloseBtn = Rectangle.Empty;
                _rcTurnFilterWarn = Rectangle.Empty;
                _rcTurnFilterSlow = Rectangle.Empty;
                _rcLedgerModeModel = Rectangle.Empty;
                _rcLedgerModeTask = Rectangle.Empty;
                _rankRowHits.Clear();
                _subagentRowHits.Clear();

                if (_activeTab == MainTab.WindowHud)
                {
                    _lastTreemapCells.Clear();
                    _turnRowHits.Clear();
                    DrawWindowHudBody(g, pad, y, contentW, bodyAvailH, hud, budget,
                        fTitle, fBodyBold, fBody, fMeta, fMetaBold, fBodyBold, fBody, fHero);
                }
                else
                {
                    _lastTreemapCells.Clear();
                    _turnRowHits.Clear();
                    _hoveredTurnItem = null;
                    DrawGlobalLedgerBody(g, pad, y, contentW, bodyAvailH,
                        fTitle, fBodyBold, fBody, fMeta, fMetaBold, fMetaBold, fMeta, fHero);
                }

                // ================= 4. UNIFIED COMPACT MEM GUARD + API BAR & FOOTER =================
                int fy = Height - pad - footerH;
                int py = fy - S(6) - opsBarH;
                DrawCompactOpsBar(g, pad, py, contentW, opsBarH, _data != null ? _data.MemGuard : null, _data != null ? _data.ExternalApi : null, fMeta, fMetaBold);
                DrawFooterBar(g, pad, fy, contentW, footerH, hud, fMeta, fMetaBold);
            }

            // When Win11 DWM hardware corners are active, DWM draws the anti-aliased border & shadow natively
            if (!_dwmRoundedApplied)
            {
                using (var penOut = new Pen(ColWinBorder, 1.2f))
                using (GraphicsPath gpOut = CreateRoundPath(new Rectangle(0, 0, Width - 1, Height - 1), S(12)))
                {
                    g.DrawPath(penOut, gpOut);
                }
            }
        }

        private void DrawConvPickerBox(Graphics g, Rectangle rc, HudSnapshot hud, Font fBold)
        {
            FillRoundCard(g, rc, S(8), _rcConvPicker.Contains(_mousePt) ? ColHoverFill : ColCardBg, ColCardBorder);

            bool isAuto = string.IsNullOrEmpty(_selectedConvKey);
            Color dotCol = (hud != null && hud.IsRunning) ? ColOk : (isAuto ? ColAccent : ColTextSub);
            int cy = rc.Top + rc.Height / 2;
            DrawStatusDot(g, rc.Left + S(12), cy, S(6), dotCol, hud != null && hud.IsRunning);

            string sideTag = (hud != null && hud.IsSideQuestion) ? I18n.T("[追问] ", "[Side] ") : (hud != null && hud.IsSubagent ? I18n.T("[子任务] ", "[Sub] ") : "");
            string prefix = isAuto ? (I18n.T("自动 · ", "Auto · ") + sideTag) : string.Format("{0:HH:mm} · {1}", hud != null ? hud.UpdatedAt : DateTime.Now, sideTag);
            string title = hud != null ? hud.Title : I18n.T("等待活跃会话...", "Waiting for active session...");
            string fullTxt = prefix + title;

            DrawTxtL(g, fullTxt, fBold, ColTextMain,
                new Rectangle(rc.Left + S(22), rc.Top, rc.Width - S(42), rc.Height));

            // Draw clean vector chevron down
            int cx = rc.Right - S(14);
            using (var pen = new Pen(ColTextSub, S(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLines(pen, new[]
                {
                    new Point(cx - S(4), cy - S(2)),
                    new Point(cx, cy + S(2)),
                    new Point(cx + S(4), cy - S(2))
                });
            }
        }

        private void DrawWindowHudBody(
            Graphics g, int x, int y, int w, int availH,
            HudSnapshot hud, long budget,
            Font fTitle, Font fBodyBold, Font fBody, Font fSmall, Font fSmallBold, Font fMonoBold, Font fMonoReg, Font fBigNum)
        {
            int cardGap = S(7);
            int stripH = S(26);
            int card1H = S(204);
            int card2H = Math.Max(S(142), availH - card1H - stripH - cardGap * 2);

            // ================= CARD 1: PRECISION 36-TICK GAUGE + HORIZONTAL WEIGHT BARS =================
            var rcCardA = new Rectangle(x, y, w, card1H);
            FillRoundCard(g, rcCardA, S(11), ColCardBg, ColCardBorder);

            int padIn = S(14);
            int ix = rcCardA.Left + padIn;
            int iw = rcCardA.Width - padIn * 2;
            int cy = rcCardA.Top + S(10);

            TurnItem lat = hud != null ? hud.Latest : null;
            long used = lat != null ? lat.TotalUsed : 0L;
            double rawPct = budget > 0 ? Math.Min(100.0, Math.Max(0.0, used * 100.0 / budget)) : 0.0;
            int pctRound = (rawPct > 0 && rawPct < 1.0) ? 1 : (int)Math.Round(rawPct);
            long remain = Math.Max(0L, budget - used);

            // Layer 1: Geometric Antigravity Chevron + Model Nameplate | Remaining Headroom Pill
            DrawAntigravityMark(g, ix + S(5), cy + S(10), S(5.5f), ColAccent);
            string modTitle = lat != null ? lat.Model : I18n.T("新对话 · 待命", "New Chat · Idle");
            if (hud != null && hud.IsRunning) modTitle += " · " + hud.ActivePhaseLabel;

            string healthTxt;
            Color hBg, hFg;
            if (rawPct >= 88)
            {
                healthTxt = rawPct >= 100 ? I18n.T("超标尺", "Over Cap") : ("余 " + FmtCompact(remain) + " · 近上限");
                hBg = ColDangerSoft;
                hFg = ColDangerText;
            }
            else if (rawPct >= 70)
            {
                healthTxt = I18n.TF("余 {0} · 高负载", "{0} left · High", FmtCompact(remain));
                hBg = ColWarnSoft;
                hFg = ColWarnText;
            }
            else if (hud != null && hud.Compacted)
            {
                int savedPct = (hud.CompactSummaryTokens > 0 && hud.CompactPreTokens > hud.CompactSummaryTokens)
                    ? (int)Math.Round((1.0 - (double)hud.CompactSummaryTokens / hud.CompactPreTokens) * 100.0)
                    : 0;
                healthTxt = savedPct >= 15 ? I18n.TF("已折叠 -{0}% · 余 {1}", "Compacted -{0}% · {1} left", savedPct, FmtCompact(remain)) : I18n.TF("已折叠 · 余 {0}", "Compacted · {0} left", FmtCompact(remain));
                hBg = ColAccentSoft;
                hFg = ColAccent;
            }
            else
            {
                healthTxt = used > 0 ? ("余 " + FmtCompact(remain) + " · 充裕") : I18n.T("空间充裕", "Healthy");
                hBg = ColOkSoft;
                hFg = ColOkText;
            }

            int pillW = DrawRightAlignedPill(g, healthTxt, fSmallBold, hBg, hFg, rcCardA.Right - padIn, cy + S(10));
            _rcHealthBadge = new Rectangle(rcCardA.Right - padIn - pillW, cy, pillW, S(20));

            DrawTxtL(g, modTitle, fTitle, ColTextMain, new Rectangle(ix + S(16), cy, iw - pillW - S(22), S(20)));
            cy += S(24);

            using (var penDiv = new Pen(ColCardBorder, 1f))
            {
                g.DrawLine(penDiv, ix, cy, ix + iw, cy);
            }
            cy += S(6);

            // Layer 2: Hero Percentage (Left) + Unified Clean Telemetry Readout (Right)
            Color bigPctCol = rawPct >= 88 ? ColDangerText : (rawPct >= 70 ? ColWarnText : ColTextMain);
            string bigPctStr = pctRound + "%";
            int pctW = MeasureTxtW(g, bigPctStr, fBigNum) + S(6);
            DrawTxtL(g, bigPctStr, fBigNum, bigPctCol, new Rectangle(ix, cy - S(1), pctW, S(24)));

            string capPair = string.Format("{0} / {1}", FmtCompact(used), FmtCompact(budget));
            DrawTxtL(g, capPair, fBody, ColTextSub, new Rectangle(ix + pctW + S(4), cy + S(2), S(124), S(20)));

            string rightTelemetry = lat != null
                ? I18n.TF("缓存 {0:F1}% · 入 +{1} / 出 {2}", "Cache {0:F1}% · In +{1} / Out {2}",
                    lat.CacheHitPct, FmtCompact(lat.InTok), FmtCompact(lat.OutTok))
                : I18n.T("等待采样...", "Waiting for telemetry...");
            Color rtCol = lat != null && lat.CacheHitPct >= 75 ? ColOkText : ColTextSub;
            DrawTxtR(g, rightTelemetry, fSmallBold, rtCol, new Rectangle(ix + S(156), cy + S(2), iw - S(156), S(20)));
            cy += S(25);

            // Layer 3: 36-Cell Discrete Precision Tick Gauge
            var rcTicks = new Rectangle(ix, cy, iw, S(10));
            DrawSegmentedTickGauge(g, rcTicks, hud, budget);
            cy += S(16);

            using (var penDiv2 = new Pen(ColCardBorder, 1f))
            {
                g.DrawLine(penDiv2, ix, cy, ix + iw, cy);
            }
            cy += S(6);

            // Layer 4: 4-Row Horizontal Ranked Breakdown Bars
            DrawHorizontalRankRows(g, ix, cy, iw, rcCardA.Bottom - S(6) - cy, hud, used, fBody, fBodyBold, fBodyBold, fBody);

            // ================= COMPACT OFFLINE 5H/7D POOL & SUBTASK STRIP =================
            var rcPoolStrip = new Rectangle(x, rcCardA.Bottom + cardGap, w, stripH);
            bool stripHovered = rcPoolStrip.Contains(_mousePt);
            FillRoundCard(g, rcPoolStrip, S(8), stripHovered ? ColHoverFill : ColCardBg, ColCardBorder);

            string poolTip = _data != null && !string.IsNullOrEmpty(_data.Rolling5hHoverTip) ? _data.Rolling5hHoverTip : I18n.T("纯离线滚动池用量参考", "Local rolling window usage");
            if (hud != null && hud.SubagentCount > 0)
            {
                poolTip = I18n.TF("当前窗口关联 {0} 个子任务 ({1}步 · 共{2} · 缓存{3:F1}%)  |  {4}", "{0} subagents ({1}t · {2} · cache {3:F1}%)  |  {4}",
                    hud.SubagentCount, hud.SubagentTurns, FmtCompact(hud.SubagentTokens), hud.SubagentCachePct, poolTip);
            }
            _lastTreemapCells.Add(new TreemapCellLayout
            {
                Item = new SourceBlock { Name = I18n.T("离线滚动池与子任务统计", "Rolling pool & subagent stats"), Tokens = _data != null ? _data.Rolling5hTokens : 0 },
                Rect = rcPoolStrip,
                HoverDetail = poolTip
            });

            int sx = rcPoolStrip.Left + S(10);
            int scy = rcPoolStrip.Top + rcPoolStrip.Height / 2;
            string stripTxtMain = _data != null && !string.IsNullOrEmpty(_data.Rolling5hStripText)
                ? _data.Rolling5hStripText
                : I18n.T("近5h: 暂无调用", "5h: No calls yet");

            if (hud != null && hud.IsSubagent && !string.IsNullOrEmpty(hud.ParentConvKey))
            {
                string backLbl = I18n.T("← 返回主窗口", "<- Main Chat");
                int chipW = DrawChipTag(g, backLbl, fSmallBold, ColAccentBtnBg, Color.White, sx, scy, S(6));
                _rcSubagentChip = new Rectangle(sx - S(2), rcPoolStrip.Top, chipW + S(4), rcPoolStrip.Height);
                sx += chipW + S(8);
                stripTxtMain = I18n.T("子任务视角 · 归属: ", "Subagent · Parent: ") + hud.ParentTitle;
            }
            else if (hud != null && hud.SubagentCount > 0)
            {
                bool subOpen = (_hudCard2Mode == HudCard2Mode.Subagents);
                string subBadge = hud.SubagentWorkingCount > 0
                    ? I18n.TF("● 子任务 {0}/{1} · {2}", "* Sub {0}/{1} · {2}", hud.SubagentWorkingCount, hud.SubagentCount, FmtCompact(hud.SubagentTokens))
                    : I18n.TF("子任务 {0}个 · {1}", "{0} Subagents · {1}", hud.SubagentCount, FmtCompact(hud.SubagentTokens));
                Color chipBg = subOpen ? ColAccentBtnBg : (hud.SubagentWorkingCount > 0 ? ColOkSoft : ColAccentSoft);
                Color chipFg = subOpen ? Color.White : (hud.SubagentWorkingCount > 0 ? ColOkText : ColAccent);
                int chipW = DrawChipTag(g, subBadge, fSmallBold, chipBg, chipFg, sx, scy, S(6));
                _rcSubagentChip = new Rectangle(sx - S(2), rcPoolStrip.Top, chipW + S(4), rcPoolStrip.Height);
                sx += chipW + S(8);
            }
            else
            {
                sx += DrawChipTag(g, I18n.T("近5h统计", "5h Pool"), fSmallBold, ColSubtleFill, ColTextSub, sx, scy, S(6)) + S(8);
            }

            DrawTxtL(g, stripTxtMain, fSmall, ColTextSub,
                new Rectangle(sx, rcPoolStrip.Top, Math.Max(S(20), rcPoolStrip.Right - S(10) - sx), rcPoolStrip.Height));

            // ================= CARD 2: UNIFIED HEADER WITH EXPLICIT [响应明细 | 子任务 N] TABS =================
            var rcCardB = new Rectangle(x, rcPoolStrip.Bottom + cardGap, w, card2H);
            FillRoundCard(g, rcCardB, S(11), ColCardBg, ColCardBorder);

            int by = rcCardB.Top + S(9);

            if (_hudCard2Mode == HudCard2Mode.Breakdown)
            {
                Color bdDot = _hudInspectCat == "results" ? ColCatRes
                    : (_hudInspectCat == "response" ? ColCatResp
                    : (_hudInspectCat == "user" ? ColCatUser : ColCatSys));
                DrawStatusDot(g, ix + S(5), by + S(11), S(7), bdDot, false);

                string closeTxt = I18n.T("返回明细", "Back");
                int cBtnW = DrawRightAlignedPill(g, closeTxt, fSmallBold,
                    _rcCard2CloseBtn.Contains(_mousePt) ? ColHoverFill : ColSubtleFill, ColTextMain, rcCardB.Right - padIn, by + S(11));
                _rcCard2CloseBtn = new Rectangle(rcCardB.Right - padIn - cBtnW, by + S(1), cBtnW, S(20));

                string bdHead = I18n.T("构成拆解 · ", "Breakdown · ") + (!string.IsNullOrEmpty(_hudInspectTitle) ? _hudInspectTitle : I18n.T("分项明细", "Details"));
                DrawTxtL(g, bdHead, fTitle, ColTextMain, new Rectangle(ix + S(16), by, iw - cBtnW - S(22), S(22)));
                by += S(26);
                using (var penDiv = new Pen(ColCardBorder, 1f)) { g.DrawLine(penDiv, ix, by, ix + iw, by); }
                by += S(6);

                DrawCategoryBreakdown(g, ix, by, iw, rcCardB.Bottom - S(8) - by, hud, fBody, fBodyBold, fBodyBold, fBody);
                return;
            }

            // Right badge (Thinking health when on Turns, or summary when on Subtasks)
            string iqLv = hud != null ? hud.IqLevel : "idle";
            string avgPill = (hud != null && !string.IsNullOrEmpty(hud.IqPill) && hud.IqPill != "--")
                ? (I18n.T("思考 ", "Think ") + hud.IqPill + " · ")
                : "";
            string iqBadgeTxt = iqLv == "ok" ? (avgPill + I18n.T("正常", "OK")) : (iqLv == "warn" ? I18n.T("思考偏低", "Low Think") : (iqLv == "danger" ? I18n.T("无思考链", "No Think") : I18n.T("待命", "Idle")));
            Color iqBadgeBg = iqLv == "ok" ? ColOkSoft : (iqLv == "warn" ? ColWarnSoft : (iqLv == "danger" ? ColDangerSoft : ColSubtleFill));
            Color iqBadgeFg = iqLv == "ok" ? ColOkText : (iqLv == "warn" ? ColWarnText : (iqLv == "danger" ? ColDangerText : ColTextSub));
            int iqPillW = DrawRightAlignedPill(g, iqBadgeTxt, fSmallBold, iqBadgeBg, iqBadgeFg, rcCardB.Right - padIn, by + S(12));

            int fx;
            bool hasSubs = hud != null && hud.SubagentCount > 0;
            if (hasSubs)
            {
                // Explicit Segmented Switcher: [ 响应明细 | 子任务 N ]
                string subTabTxt = hud.SubagentWorkingCount > 0
                    ? I18n.TF("● 子任务 {0}/{1}", "* Sub {0}/{1}", hud.SubagentWorkingCount, hud.SubagentCount)
                    : I18n.TF("子任务 {0}", "Subagents {0}", hud.SubagentCount);
                int wTab1 = MeasureTxtW(g, I18n.T("响应明细", "Turns"), fBodyBold) + S(20);
                int wTab2 = MeasureTxtW(g, subTabTxt, fBodyBold) + S(20);
                int segH = S(24);
                var rcSeg = new Rectangle(ix, by, wTab1 + wTab2 + S(6), segH);
                FillRoundCard(g, rcSeg, S(7), ColSubtleFill, ColCardBorder);

                _rcCard2TabTurns = new Rectangle(rcSeg.Left + S(2), rcSeg.Top + S(2), wTab1, segH - S(4));
                _rcCard2TabSubs = new Rectangle(_rcCard2TabTurns.Right + S(2), rcSeg.Top + S(2), wTab2, segH - S(4));

                bool onTurns = (_hudCard2Mode == HudCard2Mode.Turns);
                if (onTurns) FillRoundCard(g, _rcCard2TabTurns, S(5), ColCardBg, ColCardBorder);
                else if (_rcCard2TabTurns.Contains(_mousePt)) FillRoundCard(g, _rcCard2TabTurns, S(5), ColHoverFill, ColCardBorder);

                if (!onTurns) FillRoundCard(g, _rcCard2TabSubs, S(5), ColCardBg, ColCardBorder);
                else if (_rcCard2TabSubs.Contains(_mousePt)) FillRoundCard(g, _rcCard2TabSubs, S(5), ColHoverFill, ColCardBorder);

                DrawCenteredText(g, I18n.T("响应明细", "Turns"), onTurns ? fBodyBold : fBody, onTurns ? ColTextMain : ColTextSub, _rcCard2TabTurns);
                Color subTabCol = !onTurns ? ColAccent : (hud.SubagentWorkingCount > 0 ? ColOkText : ColTextSub);
                DrawCenteredText(g, subTabTxt, !onTurns ? fBodyBold : fBody, subTabCol, _rcCard2TabSubs);
                fx = rcSeg.Right + S(8);
            }
            else
            {
                Color iqDotCol = iqLv == "ok" ? ColOk : (iqLv == "warn" ? ColWarn : (iqLv == "danger" ? ColDanger : ColTextMuted));
                DrawStatusDot(g, ix + S(5), by + S(12), S(7), iqDotCol, iqLv == "ok");
                DrawTxtL(g, I18n.T("响应明细", "Turns"), fTitle, ColTextMain, new Rectangle(ix + S(16), by, S(76), S(24)));
                fx = ix + S(88);
            }

            if (_hudCard2Mode == HudCard2Mode.Subagents)
            {
                by += S(28);
                using (var penDiv = new Pen(ColCardBorder, 1f)) { g.DrawLine(penDiv, ix, by, ix + iw, by); }
                by += S(6);
                DrawSubagentBench(g, ix, by, iw, rcCardB.Bottom - S(8) - by, hud, fBodyBold, fSmall, fSmallBold, fBodyBold, fBody);
                return;
            }

            // Calm neutral quick filter pills (no loud multi-color background clutter)
            int fMaxRight = rcCardB.Right - padIn - iqPillW - S(8);
            int fcy = by + S(12);
            if (hud != null && (hud.SlowWaitTurns > 0 || _turnFilter == TurnQuickFilter.SlowWait))
            {
                string sLbl = I18n.TF("前置等待 {0}", "Slow {0}", hud.SlowWaitTurns);
                int needW = MeasureTxtW(g, sLbl, fSmall) + S(14);
                if (fx + needW <= fMaxRight)
                {
                    bool onSlow = (_turnFilter == TurnQuickFilter.SlowWait);
                    int fw = DrawChipTag(g, sLbl, onSlow ? fSmallBold : fSmall,
                        onSlow ? ColTextMain : ColSubtleFill,
                        onSlow ? (Pal.IsDark ? ColWinBg : Color.White) : ColTextSub, fx, fcy, S(5));
                    _rcTurnFilterSlow = new Rectangle(fx - S(1), by + S(2), fw + S(2), S(20));
                    fx += fw + S(5);
                }
            }
            if (hud != null && (hud.CacheWarnTurns > 0 || _turnFilter == TurnQuickFilter.CacheWarn))
            {
                string wLbl = I18n.TF("掉缓存 {0}", "Cold {0}", hud.CacheWarnTurns);
                int needW = MeasureTxtW(g, wLbl, fSmall) + S(14);
                if (fx + needW <= fMaxRight)
                {
                    bool onWarn = (_turnFilter == TurnQuickFilter.CacheWarn);
                    int fw = DrawChipTag(g, wLbl, onWarn ? fSmallBold : fSmall,
                        onWarn ? ColTextMain : ColSubtleFill,
                        onWarn ? (Pal.IsDark ? ColWinBg : Color.White) : ColTextSub, fx, fcy, S(5));
                    _rcTurnFilterWarn = new Rectangle(fx - S(1), by + S(2), fw + S(2), S(20));
                }
            }

            by += S(28);
            using (var penDiv = new Pen(ColCardBorder, 1f))
            {
                g.DrawLine(penDiv, ix, by, ix + iw, by);
            }
            by += S(5);

            string bannerLv = (hud != null && !string.IsNullOrEmpty(hud.LiveBannerLevel)) ? hud.LiveBannerLevel : "";
            bool showEventStrip = (!string.IsNullOrEmpty(bannerLv) && !string.IsNullOrEmpty(hud.LiveBannerText)) || iqLv == "danger";
            if (showEventStrip)
            {
                var rcBanner = new Rectangle(ix, by, iw, S(22));
                Color banBg = ColWarnSoft;
                Color banBd = ColWarnBorder;
                Color banFg = ColWarnText;
                string stripTxt = hud != null && !string.IsNullOrEmpty(hud.LiveBannerText) ? hud.LiveBannerText : (hud != null ? hud.IqReason : "");
                if (bannerLv == "compact_done")
                {
                    banBg = ColOkSoft;
                    banBd = ColOkBorder;
                    banFg = ColOkText;
                }
                else if (iqLv == "danger" && string.IsNullOrEmpty(bannerLv))
                {
                    banBg = ColDangerSoft;
                    banBd = ColDangerBorder;
                    banFg = ColDangerText;
                }
                FillRoundCard(g, rcBanner, S(6), banBg, banBd);
                DrawTxtL(g, stripTxt, fSmallBold, banFg, new Rectangle(rcBanner.Left + S(8), rcBanner.Top, rcBanner.Width - S(16), rcBanner.Height));
                by += rcBanner.Height + S(5);
            }

            DrawTurnTable(g, ix, by, iw, rcCardB.Bottom - S(7) - by, hud, budget, fSmallBold, fSmall, fBodyBold, fBody);
        }

        private static void DrawAntigravityMark(Graphics g, float cx, float cy, float r, Color col)
        {
            using (var penMain = new Pen(col, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var penSub = new Pen(Color.FromArgb(130, col), 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(penMain, new[]
                {
                    new PointF(cx - r, cy + r * 0.35f),
                    new PointF(cx, cy - r * 0.85f),
                    new PointF(cx + r, cy + r * 0.35f)
                });
                g.DrawLines(penSub, new[]
                {
                    new PointF(cx - r * 0.62f, cy + r * 0.95f),
                    new PointF(cx, cy + r * 0.22f),
                    new PointF(cx + r * 0.62f, cy + r * 0.95f)
                });
            }
        }

        private void DrawSegmentedTickGauge(Graphics g, Rectangle rc, HudSnapshot hud, long budget)
        {
            const int totalTicks = 36;
            float gap = S(2.2f);
            float cellW = (rc.Width - gap * (totalTicks - 1)) / totalTicks;
            if (cellW < 2f) cellW = 2f;

            long baseTok = hud != null ? (hud.SysTokens + hud.ToolsTokens) : 0L;
            long respTok = hud != null ? hud.RespTokens : 0L;
            long resTok = hud != null ? hud.ResultsTokens : 0L;
            long userTok = hud != null ? hud.UserTokens : 0L;
            long usedSum = baseTok + respTok + resTok + userTok;

            int litTicks = (budget > 0 && usedSum > 0)
                ? Math.Min(totalTicks, Math.Max(1, (int)Math.Round(totalTicks * ((double)usedSum / budget))))
                : 0;

            int c1End = 0, c2End = 0, c3End = 0;
            if (litTicks > 0 && usedSum > 0)
            {
                c1End = (int)Math.Round(litTicks * ((double)baseTok / usedSum));
                c2End = c1End + (int)Math.Round(litTicks * ((double)respTok / usedSum));
                c3End = c2End + (int)Math.Round(litTicks * ((double)resTok / usedSum));
                if (c3End > litTicks) c3End = litTicks;
            }

            using (var brEmpty = new SolidBrush(ColBarEmptyTrack))
            using (var brBase = new SolidBrush(ColCatSys))
            using (var brResp = new SolidBrush(ColCatResp))
            using (var brRes = new SolidBrush(ColCatRes))
            using (var brUser = new SolidBrush(ColCatUser))
            {
                for (int i = 0; i < totalTicks; i++)
                {
                    float tx = rc.Left + i * (cellW + gap);
                    var rcTick = new RectangleF(tx, rc.Top + S(2), cellW, rc.Height - S(2));

                    Brush fillBr = brEmpty;
                    if (i < litTicks)
                    {
                        if (i < c1End) fillBr = brBase;
                        else if (i < c2End) fillBr = brResp;
                        else if (i < c3End) fillBr = brRes;
                        else fillBr = brUser;
                    }

                    using (GraphicsPath gp = CreateRoundPath(Rectangle.Round(rcTick), S(2)))
                    {
                        g.FillPath(fillBr, gp);
                    }
                }
            }

            // Crisp 85% threshold tick pin at top
            int markIdx = (int)Math.Round(totalTicks * 0.85) - 1;
            float mx = rc.Left + markIdx * (cellW + gap) + cellW / 2f;
            using (var penMark = new Pen(ColWarn, 1.5f))
            {
                g.DrawLine(penMark, mx, rc.Top - S(1), mx, rc.Bottom + S(1));
            }
        }

        private void DrawHorizontalRankRows(
            Graphics g, int x, int y, int w, int availH,
            HudSnapshot hud, long totalUsed,
            Font fSmall, Font fSmallBold, Font fMonoBold, Font fMonoReg)
        {
            long sysAndTools = hud != null ? (hud.SysTokens + hud.ToolsTokens) : 0L;
            long respTok = hud != null ? hud.RespTokens : 0L;
            long resTok = hud != null ? hud.ResultsTokens : 0L;
            long userTok = hud != null ? hud.UserTokens : 0L;
            int turnsCnt = hud != null ? hud.TotalTurns : 0;

            string topResDetail = I18n.T("工具执行返回、文件读取与搜索输出", "Tool execution outputs, file reads & searches");
            if (hud != null && hud.TopSources != null)
            {
                var resTop = hud.TopSources.Where(s => s.Category == "results").Take(3).ToList();
                if (resTop.Count > 0)
                {
                    topResDetail = I18n.T("高频来源: ", "Top sources: ") + string.Join(" · ", resTop.Select(s => s.Name + " " + FmtCompact(s.Tokens)).ToArray());
                }
            }

            var rows = new[]
            {
                new
                {
                    Cat = "results",
                    Title = I18n.T("工具返回与读取输出", "Tool Outputs & Reads"),
                    Tok = resTok,
                    Col = ColCatRes,
                    Tip = topResDetail + I18n.T("  ·  单击展开来源排行", "  ·  Click to inspect")
                },
                new
                {
                    Cat = "response",
                    Title = turnsCnt > 0 ? I18n.TF("模型思考与历史回复 ({0}轮)", "Thinking & Replies ({0}t)", turnsCnt) : I18n.T("模型思考与历史回复", "Thinking & Replies"),
                    Tok = respTok,
                    Col = ColCatResp,
                    Tip = I18n.TF("累计 {0} 轮思考与正文输出  ·  单击查看明细", "{0} turns of reasoning & replies  ·  Click to inspect", turnsCnt)
                },
                new
                {
                    Cat = "system",
                    Title = (hud != null && hud.Compacted) ? I18n.T("系统提示与摘要", "System & Summary") : I18n.T("系统规则与工具", "System & Tools"),
                    Tok = sysAndTools,
                    Col = ColCatSys,
                    Tip = (hud != null
                        ? (hud.SysSummaryTokens > 0
                            ? I18n.TF("规则 {0} · 技能 {1} · 系统/MCP {2} · 摘要 {3}", "Rules {0} · Skills {1} · Sys/MCP {2} · Sum {3}",
                                FmtCompact(hud.SysRulesTokens), FmtCompact(hud.SysSkillsTokens), FmtCompact(hud.SysMcpCoreTokens), FmtCompact(hud.SysSummaryTokens))
                            : I18n.TF("规则 {0} · 技能 {1} · 系统/MCP {2}", "Rules {0} · Skills {1} · Sys/MCP {2}",
                                FmtCompact(hud.SysRulesTokens), FmtCompact(hud.SysSkillsTokens), FmtCompact(hud.SysMcpCoreTokens)))
                        : I18n.T("系统规则与工具", "System & Tools")) + I18n.T("  ·  单击查看明细", "  ·  Click to inspect")
                },
                new
                {
                    Cat = "user",
                    Title = I18n.T("用户提问与对话输入", "User Prompts & Inputs"),
                    Tok = userTok,
                    Col = ColCatUser,
                    Tip = I18n.T("当前会话历次用户消息累计占用  ·  单击查看详情", "Cumulative user messages  ·  Click to inspect")
                }
            }.OrderByDescending(r => r.Tok).ToArray();

            long denom = Math.Max(1L, totalUsed > 0 ? totalUsed : rows.Sum(r => r.Tok));
            int rowH = S(24);
            int labelW = S(168);
            int valW = S(92);
            int barX = x + labelW + S(8);
            int barW = Math.Max(S(64), w - labelW - valW - S(16));

            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                int ry = y + i * rowH;
                var rcHit = new Rectangle(x - S(4), ry, w + S(8), rowH);

                _rankRowHits.Add(new KeyValuePair<Rectangle, KeyValuePair<string, string>>(
                    rcHit, new KeyValuePair<string, string>(r.Cat, r.Title)));

                _lastTreemapCells.Add(new TreemapCellLayout
                {
                    Item = new SourceBlock { Name = r.Title, Tokens = r.Tok },
                    Rect = rcHit,
                    HoverDetail = r.Tip
                });

                bool selected = (_hudCard2Mode == HudCard2Mode.Breakdown &&
                                 string.Equals(_hudInspectCat, r.Cat, StringComparison.OrdinalIgnoreCase));
                bool hovered = rcHit.Contains(_mousePt);
                if (selected)
                {
                    FillRoundCard(g, rcHit, S(6), ColAccentSoft, ColAccentBorder);
                }
                else if (hovered)
                {
                    FillRoundCard(g, rcHit, S(6), ColSubtleFill, ColCardBorder);
                }

                var rcInd = new Rectangle(x + S(1), ry + (rowH - S(12)) / 2, S(3), S(12));
                using (var brInd = new SolidBrush(r.Col))
                {
                    g.FillRectangle(brInd, rcInd);
                }

                Color lblCol = selected ? ColAccent : (hovered ? ColTextMain : ColBodyLabel);
                DrawTxtL(g, r.Title, (hovered || selected) ? fSmallBold : fSmall, lblCol,
                    new Rectangle(x + S(9), ry, labelW - S(10), rowH));

                int trackH = S(6);
                var rcTrack = new Rectangle(barX, ry + (rowH - trackH) / 2, barW, trackH);
                FillRoundCard(g, rcTrack, S(3), ColBarEmptyTrack, ColBarEmptyTrack);

                double ratio = totalUsed > 0 ? Math.Min(1.0, Math.Max(0.0, (double)r.Tok / denom)) : 0.0;
                if (r.Tok > 0)
                {
                    int fillW = Math.Max(S(4), (int)Math.Round(barW * ratio));
                    var rcFill = new Rectangle(barX, rcTrack.Top, Math.Min(barW, fillW), trackH);
                    FillRoundCard(g, rcFill, S(3), r.Col, r.Col);
                }

                int pctInt = totalUsed > 0 ? (int)Math.Round(ratio * 100.0) : 0;
                string valStr = string.Format("{0} · {1}%", FmtCompact(r.Tok), pctInt);
                Color valCol = selected ? ColAccent : (i == 0 && totalUsed > 0 ? ColTextMain : ColTextSub);
                DrawTxtR(g, valStr, (i == 0 && totalUsed > 0) || selected ? fMonoBold : fMonoReg, valCol,
                    new Rectangle(x + w - valW, ry, valW, rowH));
            }
        }

        private void DrawSubagentBench(
            Graphics g, int x, int y, int w, int availH,
            HudSnapshot hud, Font fBodyBold, Font fSmall, Font fSmallBold, Font fMonoBold, Font fMonoReg)
        {
            _rcTurnTableArea = new Rectangle(x, y, w, Math.Max(S(40), availH));
            List<SubagentBriefItem> subs = (hud != null && hud.Subagents != null) ? hud.Subagents : new List<SubagentBriefItem>();
            if (subs.Count == 0)
            {
                DrawCenteredText(g, I18n.T("当前会话暂无子任务记录", "No subagents in this session"), fSmall, ColTextSub, _rcTurnTableArea);
                return;
            }

            int rowH = S(42);
            int rowGap = S(5);
            int visCap = Math.Max(1, (availH + rowGap) / (rowH + rowGap));
            _subagentVisCap = visCap;

            int maxScroll = Math.Max(0, subs.Count - visCap);
            if (_subagentScroll > maxScroll) _subagentScroll = maxScroll;
            if (_subagentScroll < 0) _subagentScroll = 0;

            bool hasScroll = subs.Count > visCap;
            int innerW = hasScroll ? (w - S(10)) : w;
            int startY = y;

            int drawn = 0;
            for (int i = _subagentScroll; i < subs.Count && drawn < visCap; i++)
            {
                if (y + rowH > startY + availH + S(2)) break;
                SubagentBriefItem b = subs[i];

                var rcRow = new Rectangle(x - S(2), y, innerW + S(4), rowH);
                _subagentRowHits.Add(new KeyValuePair<Rectangle, SubagentBriefItem>(rcRow, b));

                bool hov = rcRow.Contains(_mousePt);
                FillRoundCard(g, rcRow, S(8),
                    hov ? ColHoverFill : ColRowAltBg,
                    hov ? ColAccentBorder : ColCardBorder);

                int rx = rcRow.Left + S(10);
                int rw = rcRow.Width - S(20);
                int topY = y + S(4);
                int botY = y + S(22);

                Color dotCol = b.IsWorking ? ColOk : ColTextMuted;
                DrawStatusDot(g, rx + S(3), topY + S(8), S(6), dotCol, b.IsWorking);

                string rightTop = I18n.TF("{0} · 窗{1} / 共{2} ›", "{0} · Ctx {1} / Total {2} >",
                    b.ModelBadge,
                    b.ContextUsed > 0 ? FmtCompact(b.ContextUsed) : "—",
                    FmtCompact(b.TotalTokens));
                int rightTopW = MeasureTxtW(g, rightTop, fSmall) + S(8);
                DrawTxtL(g, b.RoleTitle, fBodyBold, hov ? ColAccent : ColTextMain,
                    new Rectangle(rx + S(12), topY, Math.Max(S(50), rw - rightTopW - S(16)), S(17)));
                DrawTxtR(g, rightTop, fSmall, hov ? ColAccent : ColTextSub,
                    new Rectangle(rx + rw - rightTopW, topY, rightTopW, S(17)));

                int miniBarW = S(52);
                var rcMini = new Rectangle(rx + S(12), botY + S(6), miniBarW, S(4));
                FillRoundCard(g, rcMini, S(2), ColTrackBg, ColTrackBg);
                double ctxRatio = Math.Min(1.0, Math.Max(0.0, b.ContextUsed / 512000.0));
                if (b.ContextUsed > 0)
                {
                    int fw = Math.Max(S(4), (int)Math.Round(miniBarW * ctxRatio));
                    Color barCol = ctxRatio >= 0.80 ? ColWarn : ColAccent;
                    FillRoundCard(g, new Rectangle(rcMini.Left, rcMini.Top, Math.Min(miniBarW, fw), S(4)), S(2), barCol, barCol);
                }

                string subStat = I18n.TF("{0}轮 · 缓存{1:F0}%", "{0}t · Cache {1:F0}%", b.Turns, b.CacheHitPct);
                string actStr = b.IsWorking
                    ? I18n.TF("执行中 {0}s · {1}", "Running {0}s · {1}", b.ElapsedSec, b.LastAction)
                    : b.LastAction;
                DrawTxtL(g, subStat, fSmall, ColTextSub, new Rectangle(rcMini.Right + S(8), botY, S(98), S(16)));
                int actLeft = rcMini.Right + S(108);
                DrawTxtR(g, actStr, b.IsWorking ? fSmallBold : fSmall, b.IsWorking ? ColOkText : ColTextSub,
                    new Rectangle(actLeft, botY, Math.Max(S(40), (rx + rw) - actLeft), S(16)));

                y += rowH + rowGap;
                drawn++;
            }

            if (hasScroll)
            {
                DrawMiniScrollBar(g, new Rectangle(x + w - S(4), startY + S(2), S(4), Math.Max(S(24), availH - S(4))),
                    subs.Count, visCap, _subagentScroll);
            }
        }

        private void DrawCategoryBreakdown(
            Graphics g, int x, int y, int w, int availH,
            HudSnapshot hud, Font fSmall, Font fSmallBold, Font fMonoBold, Font fMonoReg)
        {
            _rcTurnTableArea = new Rectangle(x, y, w, Math.Max(S(40), availH));
            List<BreakdownRowItem> items = null;
            if (hud != null && hud.CategoryBreakdowns != null)
                hud.CategoryBreakdowns.TryGetValue(_hudInspectCat, out items);

            if (items == null || items.Count == 0)
            {
                DrawCenteredText(g, I18n.T("该分项暂无可拆解记录", "No breakdown items available"), fSmall, ColTextSub, _rcTurnTableArea);
                return;
            }

            Color catCol = _hudInspectCat == "results" ? ColCatRes
                : (_hudInspectCat == "response" ? ColCatResp
                : (_hudInspectCat == "user" ? ColCatUser : ColCatSys));

            // Fit all 5 system items cleanly in one screen without scrolling (compact 21px rows)
            int rowH = items.Count <= 4 ? S(25) : S(21);
            int rowGap = S(2);
            int visCap = Math.Max(2, (availH + rowGap) / (rowH + rowGap));
            _subagentVisCap = visCap;
            int maxScroll = Math.Max(0, items.Count - visCap);
            if (_subagentScroll > maxScroll) _subagentScroll = maxScroll;
            if (_subagentScroll < 0) _subagentScroll = 0;

            bool hasScroll = items.Count > visCap;
            int innerW = hasScroll ? (w - S(8)) : w;

            StringFormat sfL = SfNearEllipsis;
            StringFormat sfR = SfFarNoWrap;

            int startY = y;
            int valW = S(96);
            int barW = S(78);
            int leftW = Math.Max(S(90), innerW - valW - barW - S(16));

            int drawn = 0;
            for (int i = _subagentScroll; i < items.Count && drawn < visCap; i++)
            {
                if (y + rowH > startY + availH + S(3)) break;
                BreakdownRowItem bi = items[i];

                var rcRow = new Rectangle(x - S(3), y, innerW + S(6), rowH);
                if (rcRow.Contains(_mousePt))
                {
                    FillRoundCard(g, rcRow, S(5), ColSubtleFill, ColCardBorder);
                }

                _lastTreemapCells.Add(new TreemapCellLayout
                {
                    Item = new SourceBlock { Name = bi.Label, Tokens = bi.Tokens },
                    Rect = rcRow,
                    HoverDetail = bi.Label + " — " + bi.SubNote
                });

                var rcInd = new Rectangle(x + S(1), y + (rowH - S(11)) / 2, S(3), S(11));
                using (var brInd = new SolidBrush(catCol))
                {
                    g.FillRectangle(brInd, rcInd);
                }

                DrawTxtL(g, bi.Label, i == 0 ? fSmallBold : fSmall, i == 0 ? ColTextMain : ColBodyLabel,
                    new Rectangle(x + S(9), y, leftW - S(10), rowH));

                int barX = x + leftW + S(4);
                var rcTrk = new Rectangle(barX, y + (rowH - S(5)) / 2, barW, S(5));
                FillRoundCard(g, rcTrk, S(2), ColBarEmptyTrack, ColBarEmptyTrack);
                if (bi.Tokens > 0)
                {
                    int fw = Math.Max(S(4), (int)Math.Round(barW * Math.Min(1.0, Math.Max(0.0, bi.SharePct / 100.0))));
                    FillRoundCard(g, new Rectangle(barX, rcTrk.Top, Math.Min(barW, fw), S(5)), S(2), catCol, catCol);
                }

                string valTxt = string.Format("{0} · {1:F0}%", FmtCompact(bi.Tokens), bi.SharePct);
                DrawTxtR(g, valTxt, i == 0 ? fMonoBold : fMonoReg, i == 0 ? ColTextMain : ColTextSub,
                    new Rectangle(x + innerW - valW, y, valW, rowH));

                y += rowH + rowGap;
                drawn++;
            }

            if (hasScroll)
            {
                DrawMiniScrollBar(g, new Rectangle(x + w - S(4), startY + S(2), S(4), Math.Max(S(20), availH - S(4))),
                    items.Count, visCap, _subagentScroll);
            }
        }

        private void DrawTurnTable(Graphics g, int x, int y, int w, int availH, HudSnapshot hud, long budget, Font fHead, Font fSmall, Font fMonoBold, Font fMonoReg)
        {
            _rcTurnTableArea = new Rectangle(x, y, w, Math.Max(S(40), availH));

            List<TurnItem> filtered = GetFilteredTurns(hud);
            int totalCount = filtered.Count;
            int rowH = S(23);
            int drawerH = S(20);
            int headH = S(19);
            int bodyAvail = Math.Max(S(40), availH - headH - S(4));
            int baseVisibleCap = Math.Max(3, bodyAvail / rowH);
            _hudVisibleCap = baseVisibleCap;

            bool hasScroll = totalCount > baseVisibleCap;
            int innerW = hasScroll ? (w - S(10)) : w;

            int cTurn = S(44);
            int cMod = S(82);
            int cThink = S(56);
            int cOut = S(56);
            int cCache = S(50);
            int cDur = S(48);
            int cSpd = Math.Max(S(58), innerW - cTurn - cMod - cThink - cOut - cCache - cDur);

            int xMod = x + cTurn;
            int xThink = xMod + cMod;
            int xOut = xThink + cThink;
            int xCache = xOut + cOut;
            int xSpd = xCache + cCache;
            int xDur = xSpd + cSpd;

            DrawTxtL(g, I18n.T("轮次", "Turn"), fHead, ColTextSub, new Rectangle(x, y, cTurn, headH));
            DrawTxtL(g, I18n.T("模型", "Model"), fHead, ColTextSub, new Rectangle(xMod, y, cMod, headH));
            DrawTxtR(g, I18n.T("思考", "Think"), fHead, ColTextSub, new Rectangle(xThink, y, cThink, headH));
            DrawTxtR(g, I18n.T("输出", "Out"), fHead, ColTextSub, new Rectangle(xOut, y, cOut, headH));
            DrawTxtR(g, I18n.T("缓存", "Cache"), fHead, ColTextSub, new Rectangle(xCache, y, cCache, headH));
            DrawTxtR(g, I18n.T("速度", "Speed"), fHead, ColTextSub, new Rectangle(xSpd, y, cSpd, headH));
            DrawTxtR(g, I18n.T("耗时", "Time"), fHead, ColTextSub, new Rectangle(xDur, y, cDur, headH));

            y += headH + S(2);
            using (var pen = new Pen(ColCardBorder, 1f))
            {
                g.DrawLine(pen, x, y, x + w, y);
            }
            y += S(3);

            if (totalCount == 0)
            {
                string emptyMsg = _turnFilter != TurnQuickFilter.All ? I18n.T("近 80 轮无命中该筛选条件的异常记录", "No matching anomaly turns in last 80 turns") : I18n.T("暂无响应记录", "No response turns yet");
                DrawCenteredText(g, emptyMsg, fSmall, ColTextSub, new Rectangle(x, y, w, bodyAvail));
                return;
            }

            int maxScroll = Math.Max(0, totalCount - baseVisibleCap);
            if (_hudTurnScroll > maxScroll) _hudTurnScroll = maxScroll;
            if (_hudTurnScroll < 0) _hudTurnScroll = 0;

            int activeTurnNo = _hoveredTurnItem != null ? _hoveredTurnItem.TurnNo : _pinnedTurnNo;
            int rowsStartY = y;
            int bottomLimit = y + bodyAvail + S(2);
            int drawnCount = 0;

            for (int idx = _hudTurnScroll; idx < totalCount; idx++)
            {
                TurnItem t = filtered[idx];
                bool isExpanded = (activeTurnNo == t.TurnNo);
                int curItemH = isExpanded ? (rowH + drawerH) : rowH;

                if (drawnCount > 0 && y + curItemH > bottomLimit)
                {
                    if (isExpanded && y + rowH <= bottomLimit)
                    {
                        isExpanded = false;
                        curItemH = rowH;
                    }
                    else
                    {
                        break;
                    }
                }

                var rcHit = new Rectangle(x - S(4), y, innerW + S(8), curItemH);
                _turnRowHits.Add(new KeyValuePair<Rectangle, TurnItem>(rcHit, t));

                if (isExpanded)
                {
                    bool isPinned = (_pinnedTurnNo == t.TurnNo);
                    Color expBd = isPinned ? (Pal.IsDark ? ColAccentBorder : Color.FromArgb(100, 116, 139)) : Pal.Divider;
                    FillRoundCard(g, rcHit, S(6), Pal.IsDark ? Pal.PinRowBg : Color.FromArgb(248, 250, 252), expBd);

                    var rcTopBand = new Rectangle(rcHit.X + 1, rcHit.Y + 1, rcHit.Width - 2, rowH);
                    using (var brTop = new SolidBrush(ColCardBg))
                    using (GraphicsPath gpTop = CreateRoundPath(rcTopBand, S(5)))
                    {
                        g.FillPath(brTop, gpTop);
                    }
                    using (var penSep = new Pen(Pal.Divider, 1f))
                    {
                        g.DrawLine(penSep, rcHit.Left + S(6), y + rowH, rcHit.Right - S(6), y + rowH);
                    }
                }

                DrawTxtL(g, "#" + t.TurnNo, isExpanded ? fMonoBold : fMonoReg, isExpanded ? ColTextMain : ColTextSub, new Rectangle(x, y, cTurn, rowH));
                DrawTxtL(g, t.ShortTag, fMonoBold, ColTextMain, new Rectangle(xMod, y, cMod, rowH));

                string thinkStr = t.EstThinkTok > 0 ? FmtCompact(t.EstThinkTok) : "—";
                Color thinkCol = t.EstThinkTok > 0 ? (Pal.IsDark ? ColCatResp : Color.FromArgb(15, 118, 110)) : ColTextSub;
                DrawTxtR(g, thinkStr, fMonoReg, thinkCol, new Rectangle(xThink, y, cThink, rowH));
                DrawTxtR(g, FmtCompact(t.OutTok), fMonoReg, ColTextMain, new Rectangle(xOut, y, cOut, rowH));

                Color cacheCol = t.IsPostCompact
                    ? ColAccent
                    : ((t.IsCacheDropWarn || t.ModelSwitchedCold || (t.TurnNo > 1 && t.CacheHitPct < 35.0)) ? ColWarnText : ColTextSub);
                DrawTxtR(g, Math.Round(t.CacheHitPct) + "%", fMonoReg, cacheCol, new Rectangle(xCache, y, cCache, rowH));

                bool isSlowFirst = t.WaitSec.HasValue && t.WaitSec.Value >= 6.0;
                string spdStr = t.Tps.HasValue ? (Math.Round(t.Tps.Value) + " t/s") : "—";
                string genDurStr = t.DurationSec.HasValue
                    ? (t.DurationSec.Value < 10.0 ? (t.DurationSec.Value.ToString("F1") + "s") : (Math.Round(t.DurationSec.Value) + "s"))
                    : "—";
                string durStr = (_turnFilter == TurnQuickFilter.SlowWait && t.WaitSec.HasValue)
                    ? (genDurStr == "—" ? I18n.TF("等{0:F0}s", "W{0:F0}s", t.WaitSec.Value) : I18n.TF("等{0:F0}s+{1}", "W{0:F0}s+{1}", t.WaitSec.Value, genDurStr))
                    : (isSlowFirst ? (genDurStr == "—" ? string.Format("+{0:F0}s", t.WaitSec.Value) : string.Format("+{0:F0}s/{1}", t.WaitSec.Value, genDurStr)) : genDurStr);
                int cDurReal = (_turnFilter == TurnQuickFilter.SlowWait || isSlowFirst) ? S(72) : cDur;
                int cSpdReal = Math.Max(S(46), cSpd - (cDurReal - cDur));
                DrawTxtR(g, spdStr, fMonoReg, ColTextMain, new Rectangle(xSpd, y, cSpdReal, rowH));
                DrawTxtR(g, durStr, fMonoReg, isSlowFirst ? ColWarnText : ColTextSub, new Rectangle(xSpd + cSpdReal, y, cDurReal, rowH));

                if (isExpanded)
                {
                    int dy = y + rowH + S(1);
                    int dx = x + S(2);
                    int dw = innerW - S(4);

                    string leftMeta = I18n.TF("新 {0} · 缓 {1}", "In {0} · Cache {1}", FmtCompact(t.InTok), FmtCompact(t.CacheTok));
                    if (t.WaitSec.HasValue && t.WaitSec.Value >= 0.5)
                        leftMeta += I18n.TF(" · 前置等待 {0:F1}s", " · Queue {0:F1}s", t.WaitSec.Value);
                    if (t.DurationSec.HasValue)
                        leftMeta += I18n.TF(" · 生成 {0:F1}s", " · Gen {0:F1}s", t.DurationSec.Value);
                    if (t.ToolExecSec.HasValue && t.ToolExecSec.Value >= 0.4)
                        leftMeta += I18n.TF(" · 工具 {0:F0}s", " · Tools {0:F0}s", t.ToolExecSec.Value);

                    bool isCold = !string.IsNullOrEmpty(t.ColdCacheReason);
                    string stepTag = t.GroupTotalSteps > 1 ? I18n.TF(" ({0}/{1}步)", " ({0}/{1} steps)", t.GroupStepSeq, t.GroupTotalSteps) : "";
                    string rightMeta = isCold ? t.ColdCacheReason
                        : (!string.IsNullOrEmpty(t.ToolSummary) ? (t.ToolSummary + stepTag) : (I18n.T("直接回复", "Direct reply") + stepTag));

                    Color rightCol = t.IsPostCompact ? ColAccent : (isCold ? ColWarnText : ColTextMain);
                    int leftMetaW = (int)(dw * 0.60f);
                    DrawTxtL(g, leftMeta, fSmall, isSlowFirst ? ColWarnText : ColTextSub, new Rectangle(dx, dy, leftMetaW, drawerH - S(2)));
                    DrawTxtR(g, rightMeta, fSmall, rightCol, new Rectangle(dx + leftMetaW, dy, dw - leftMetaW, drawerH - S(2)));
                }

                y += curItemH;
                drawnCount++;

                if (idx < totalCount - 1 && !isExpanded && y + rowH <= bottomLimit)
                {
                    using (var penDash = new Pen(ColRowBorder, 1f))
                    {
                        g.DrawLine(penDash, x, y, x + innerW, y);
                    }
                }
            }

            if (hasScroll)
            {
                int trackH = Math.Max(S(30), y - rowsStartY - S(4));
                var rcTrack = new Rectangle(x + w - S(4), rowsStartY + S(2), S(4), trackH);
                DrawMiniScrollBar(g, rcTrack, totalCount, baseVisibleCap, _hudTurnScroll);
            }
        }

        private void DrawGlobalLedgerBody(
            Graphics g, int x, int y, int w, int availH,
            Font fTitle, Font fBodyBold, Font fBody, Font fSmall, Font fSmallBold, Font fMonoSmall, Font fMonoReg, Font fBigNum)
        {
            _modelRowHits.Clear();
            _taskRowHits.Clear();
            _toolChipHits.Clear();
            _drillRowHits.Clear();
            _dayBarClickHits.Clear();
            _dayBarHoverHits.Clear();
            _rcDrillBackBtn = Rectangle.Empty;
            _rcDrillTabConv = Rectangle.Empty;
            _rcDrillTabTurns = Rectangle.Empty;
            _rcDrillLowCacheBtn = Rectangle.Empty;
            _rcDrillPrevBtn = Rectangle.Empty;
            _rcDrillNextBtn = Rectangle.Empty;
            _rcOverviewScrollArea = Rectangle.Empty;
            _rcDrillScrollArea = Rectangle.Empty;

            if (!string.IsNullOrEmpty(_drillModelName))
            {
                if (_drillData == null && _onQueryDrilldown != null)
                {
                    _drillData = _onQueryDrilldown(_drillModelName, _ledgerPeriod);
                }
                DrawModelDrilldownBody(g, x, y, w, availH,
                    fTitle, fBodyBold, fBody, fSmall, fSmallBold, fMonoSmall, fMonoReg, fBigNum);
                return;
            }

            // 1. Top 3 Reactive KPI Boxes (Selected Period + 30d / Yesterday Reference + All-Time Throughput)
            int kGap = S(7);
            int kw = (w - kGap * 2) / 3;
            int kh = S(66);

            PeriodSummary activeSum = GetPeriodSummary(_ledgerPeriod);

            string kpi1Label, kpi1Val, kpi1Sub;
            Color kpi1Col;
            bool kpi1Hi = (_ledgerPeriod != PeriodTab.Today);
            if (_ledgerPeriod == PeriodTab.Today)
            {
                kpi1Label = I18n.T("今日实时消耗", "Today Usage");
                kpi1Val = FmtBigZh(_data.Today.TotalTokens);
                kpi1Sub = FmtCostSub(_data.Today.CostUsd, _data.Today.ConvCount);
                kpi1Col = ColOkText;
            }
            else if (_ledgerPeriod == PeriodTab.Yesterday)
            {
                kpi1Label = I18n.T("昨日总消耗", "Yesterday Usage");
                kpi1Val = FmtBigZh(_data.Yesterday.TotalTokens);
                kpi1Sub = FmtCostSub(_data.Yesterday.CostUsd, _data.Yesterday.ConvCount);
                kpi1Col = ColAccent;
            }
            else if (_ledgerPeriod == PeriodTab.Days7)
            {
                kpi1Label = I18n.T("近7日消耗", "Last 7 Days");
                kpi1Val = FmtBigZh(_data.Days7.TotalTokens);
                kpi1Sub = FmtCostSub(_data.Days7.CostUsd, _data.Days7.ConvCount);
                kpi1Col = ColAccent;
            }
            else if (_ledgerPeriod == PeriodTab.Days30)
            {
                kpi1Label = I18n.T("近30日消耗", "Last 30 Days");
                kpi1Val = FmtBigZh(_data.Days30.TotalTokens);
                kpi1Sub = FmtCostSub(_data.Days30.CostUsd, _data.Days30.ConvCount);
                kpi1Col = ColAccent;
            }
            else
            {
                kpi1Label = I18n.T("全量历史消耗", "All-Time Usage");
                kpi1Val = FmtBigZh(_data.AllTimeTokens);
                kpi1Sub = FmtCostSub(_data.AllTimeUsd, -1);
                kpi1Col = ColAccent;
            }

            string kpi2Label, kpi2Val, kpi2Sub;
            if (_ledgerPeriod == PeriodTab.Days30)
            {
                kpi2Label = I18n.T("昨日总消耗", "Yesterday Usage");
                kpi2Val = FmtBigZh(_data.Yesterday.TotalTokens);
                kpi2Sub = FmtCostSub(_data.Yesterday.CostUsd, _data.Yesterday.ConvCount);
            }
            else
            {
                kpi2Label = I18n.T("近30日消耗", "Last 30 Days");
                kpi2Val = FmtBigZh(_data.Days30.TotalTokens);
                kpi2Sub = FmtCostSub(_data.Days30.CostUsd, _data.Days30.ConvCount);
            }

            string kpi3Label, kpi3Val, kpi3Sub;
            Color kpi3Col;
            if (_ledgerPeriod == PeriodTab.All)
            {
                kpi3Label = I18n.T("缓存与活跃窗", "Cache & Chats");
                kpi3Val = string.Format("{0:F1}%", _data.AllTimeCachePct);
                kpi3Sub = I18n.TF("近35日 {0}个活跃会话", "{0} active chats (35d)", _data.All.ConvCount);
                kpi3Col = ColOkText;
            }
            else
            {
                kpi3Label = I18n.T("累计总消耗", "All-Time Total");
                kpi3Val = FmtBigZh(_data.AllTimeTokens);
                kpi3Sub = I18n.TF("总缓存命中 {0:F1}%", "Avg cache hit {0:F1}%", _data.AllTimeCachePct);
                kpi3Col = ColAccent;
            }

            int khNew = S(70);
            DrawKpiBox(g, new Rectangle(x, y, kw, khNew),
                kpi1Label, kpi1Val, kpi1Sub, fSmall, fBigNum, kpi1Col, kpi1Hi);

            DrawKpiBox(g, new Rectangle(x + kw + kGap, y, kw, khNew),
                kpi2Label, kpi2Val, kpi2Sub, fSmall, fBigNum, ColTextMain, false);

            DrawKpiBox(g, new Rectangle(x + (kw + kGap) * 2, y, w - (kw + kGap) * 2, khNew),
                kpi3Label, kpi3Val, kpi3Sub, fSmall, fBigNum, kpi3Col, false);

            y += khNew + S(8);

            // 2. 7-Day Bar Chart Card
            int chartCardH = S(108);
            var rcChart = new Rectangle(x, y, w, chartCardH);
            FillRoundCard(g, rcChart, S(11), ColCardBg, ColCardBorder);

            int ix = rcChart.Left + S(14);
            int iw = rcChart.Width - S(28);
            int cy = rcChart.Top + S(8);

            DrawLeftRightRow(g, I18n.T("近7日消耗趋势", "7-Day Token Trend"), fTitle, ColTextMain,
                I18n.T("点击今日/昨日可快切", "Click Today / Yest to switch"), fSmall, ColTextSub, ix, cy, iw, S(20));
            cy += S(23);
            using (var pen = new Pen(ColCardBorder, 1f)) { g.DrawLine(pen, ix, cy, ix + iw, cy); }
            cy += S(5);

            Draw7DayBars(g, new Rectangle(ix, cy, iw, rcChart.Bottom - S(6) - cy), _data.Trend7d, fSmallBold, fSmall, fSmallBold);

            y += chartCardH + S(8);

            // 3. Model / Task Breakdown Card with Aligned Segmented Switcher & Period Selector
            int modCardH = Math.Max(S(146), availH - khNew - chartCardH - S(16));
            var rcMod = new Rectangle(x, y, w, modCardH);
            FillRoundCard(g, rcMod, S(11), ColCardBg, ColCardBorder);

            int my = rcMod.Top + S(9);
            int segH = S(24);
            int wModBtn = MeasureTxtW(g, I18n.T("主力模型", "Models"), fBodyBold) + S(18);
            int wTaskBtn = MeasureTxtW(g, I18n.T("任务与子任务", "Conversations"), fBodyBold) + S(18);
            var rcModeWrap = new Rectangle(ix, my, wModBtn + wTaskBtn + S(6), segH);
            FillRoundCard(g, rcModeWrap, S(7), ColSubtleFill, ColCardBorder);
            _rcLedgerModeModel = new Rectangle(rcModeWrap.Left + S(2), rcModeWrap.Top + S(2), wModBtn, segH - S(4));
            _rcLedgerModeTask = new Rectangle(_rcLedgerModeModel.Right + S(2), rcModeWrap.Top + S(2), wTaskBtn, segH - S(4));

            bool onByMod = (_ledgerRankMode == LedgerRankMode.ByModel);
            if (onByMod)
                FillRoundCard(g, _rcLedgerModeModel, S(5), ColCardBg, ColCardBorder);
            else if (_rcLedgerModeModel.Contains(_mousePt))
                FillRoundCard(g, _rcLedgerModeModel, S(5), ColHoverFill, ColCardBorder);

            if (!onByMod)
                FillRoundCard(g, _rcLedgerModeTask, S(5), ColCardBg, ColCardBorder);
            else if (_rcLedgerModeTask.Contains(_mousePt))
                FillRoundCard(g, _rcLedgerModeTask, S(5), ColHoverFill, ColCardBorder);

            DrawCenteredText(g, I18n.T("主力模型", "Models"), onByMod ? fBodyBold : fSmall, onByMod ? ColTextMain : ColTextSub, _rcLedgerModeModel);
            DrawCenteredText(g, I18n.T("任务与子任务", "Conversations"), !onByMod ? fBodyBold : fSmall, !onByMod ? ColAccent : ColTextSub, _rcLedgerModeTask);

            DrawPeriodButtons(g, rcMod.Right - S(14), my, fSmallBold);

            my += segH + S(7);
            using (var pen = new Pen(ColCardBorder, 1f)) { g.DrawLine(pen, ix, my, ix + iw, my); }
            my += S(6);

            // 3a. Compact Global High-Frequency Tools Strip
            int toolRowH = S(24);
            DrawTxtL(g, I18n.T("高频工具", "Top Tools"), fSmallBold, ColTextSub, new Rectangle(ix, my, S(56), toolRowH));
            if (activeSum.ToolRanking.Count == 0)
            {
                DrawTxtL(g, I18n.T("该周期暂无工具调用记录", "No tool calls in this period"), fSmall, ColTextSub, new Rectangle(ix + S(58), my, iw - S(58), toolRowH));
            }
            else
            {
                DrawToolChipStrip(g, ix + S(58), my, iw - S(58), toolRowH, activeSum.ToolRanking, activeSum.ToolCalls, activeSum.Label, fSmall, fSmallBold);
            }
            my += toolRowH + S(6);

            using (var penDash = new Pen(ColSoftSep, 1f) { DashStyle = DashStyle.Dot })
            {
                g.DrawLine(penDash, ix, my, ix + iw, my);
            }
            my += S(6);

            // 3b. Clickable + Scrollable Model / Task Ranking List
            int listH = rcMod.Bottom - S(8) - my;
            _rcOverviewScrollArea = new Rectangle(ix - S(4), my, iw + S(8), Math.Max(1, listH));

            if (_ledgerRankMode == LedgerRankMode.ByTask)
            {
                if (activeSum.ConvRanking.Count == 0)
                {
                    DrawCenteredText(g, I18n.T("该周期暂无会话任务记录", "No conversations in this period"), fBody, ColTextSub, new Rectangle(ix, my, iw, Math.Max(S(40), listH)));
                }
                else
                {
                    int rowH = S(50);
                    int rowGap = S(6);
                    int visCap = Math.Max(1, (listH + rowGap) / (rowH + rowGap));
                    _overviewVisibleCap = visCap;

                    int totalItems = activeSum.ConvRanking.Count;
                    int maxScroll = Math.Max(0, totalItems - visCap);
                    if (_overviewModelScroll > maxScroll) _overviewModelScroll = maxScroll;
                    if (_overviewModelScroll < 0) _overviewModelScroll = 0;

                    bool hasScroll = totalItems > visCap;
                    int rowW = hasScroll ? (iw - S(10)) : iw;
                    long sumTotal = Math.Max(1L, activeSum.TotalTokens > 0 ? activeSum.TotalTokens : activeSum.ConvRanking.Sum(ci => ci.TotalTokens));

                    int drawn = 0;
                    for (int i = _overviewModelScroll; i < totalItems && drawn < visCap; i++)
                    {
                        if (my + rowH > rcMod.Bottom - S(4)) break;

                        LedgerConvRankItem item = activeSum.ConvRanking[i];
                        double share = item.TotalTokens * 100.0 / sumTotal;
                        double cachePct = item.CacheHitPct;

                        var rcRow = new Rectangle(ix - S(4), my, rowW + S(8), rowH);
                        _taskRowHits.Add(new KeyValuePair<Rectangle, LedgerConvRankItem>(rcRow, item));

                        bool hov = rcRow.Contains(_mousePt);
                        if (hov)
                        {
                            FillRoundCard(g, rcRow, S(8), ColHoverFill, ColHoverBorder);
                        }

                        int rx = ix + S(2);
                        int rw = rowW - S(4);
                        int ry = my + S(4);

                        string titlePrefix = item.IsWorking ? "● " : "";
                        string leftTitle = titlePrefix + item.Title;
                        string rightTop = I18n.TF("{0} ({1:F1}%) · {2}轮 ›", "{0} ({1:F1}%) · {2}t >", FmtBigZh(item.TotalTokens), share, item.Calls);
                        DrawLeftRightRow(g, leftTitle, fBodyBold, hov ? ColAccent : (item.IsWorking ? ColOkText : ColTextMain),
                            rightTop, fSmallBold, hov ? ColAccent : ColTextSub, rx, ry, rw, S(18));
                        ry += S(19);

                        var rcProg = new Rectangle(rx, ry, rw, S(5));
                        FillRoundCard(g, rcProg, S(2), ColTrackBg, ColTrackBg);
                        int fillW = Math.Max(S(6), (int)Math.Round(rw * Math.Min(1.0, share / 100.0)));
                        using (var brProg = new LinearGradientBrush(new Rectangle(rx, ry, fillW, S(5)), ColAccent, ColCatResp, 0f))
                        using (GraphicsPath gp = CreateRoundPath(new Rectangle(rx, ry, fillW, S(5)), S(2)))
                        {
                            g.FillPath(brProg, gp);
                        }
                        ry += S(8);

                        string leftIo = item.SubCount > 0
                            ? I18n.TF("{0}个子任务 ({1}) · 缓 {2}", "{0} sub ({1}) · Cache {2}", item.SubCount, FmtBigZh(item.SubTokens), FmtCompact(item.CacheTokens))
                            : I18n.TF("新入 {0} · 缓存 {1} · 出 {2}", "In {0} · Cache {1} · Out {2}", FmtCompact(item.InputTokens), FmtCompact(item.CacheTokens), FmtCompact(item.OutputTokens));
                        Color cCol = cachePct >= 75.0 ? ColOkText : (cachePct >= 40.0 ? ColWarnText : ColDangerText);
                        DrawLeftRightRow(g, leftIo, item.SubCount > 0 ? fSmallBold : fSmall, item.SubCount > 0 ? ColAccent : ColTextSub,
                            I18n.TF("命中 {0:F1}%", "Hit {0:F1}%", cachePct), fSmallBold, cCol, rx, ry, rw, S(16));

                        my += rowH + rowGap;
                        drawn++;
                    }

                    if (hasScroll)
                    {
                        DrawMiniScrollBar(g, new Rectangle(ix + iw - S(4), _rcOverviewScrollArea.Top + S(2), S(4), Math.Max(S(24), listH - S(4))),
                            totalItems, visCap, _overviewModelScroll);
                    }
                }
            }
            else if (activeSum.ModelRanking.Count == 0)
            {
                DrawCenteredText(g, I18n.T("该周期暂无本地模型记录", "No local model records in this period"), fBody, ColTextSub, new Rectangle(ix, my, iw, Math.Max(S(40), listH)));
            }
            else
            {
                int rowH = S(48);
                int rowGap = S(5);
                int visCap = Math.Max(1, (listH + rowGap) / (rowH + rowGap));
                _overviewVisibleCap = visCap;

                int totalItems = activeSum.ModelRanking.Count;
                int maxScroll = Math.Max(0, totalItems - visCap);
                if (_overviewModelScroll > maxScroll) _overviewModelScroll = maxScroll;
                if (_overviewModelScroll < 0) _overviewModelScroll = 0;

                bool hasScroll = totalItems > visCap;
                int rowW = hasScroll ? (iw - S(8)) : iw;
                long liveModelSum = 0L;
                for (int mi = 0; mi < totalItems; mi++) liveModelSum += activeSum.ModelRanking[mi].Value.TotalTokens;
                long sumTotal = Math.Max(1L, liveModelSum);

                int drawn = 0;
                for (int i = _overviewModelScroll; i < totalItems && drawn < visCap; i++)
                {
                    if (my + rowH > rcMod.Bottom - S(4)) break;

                    var kv = activeSum.ModelRanking[i];
                    ModelTokenStat ms = kv.Value;
                    double share = ms.TotalTokens * 100.0 / sumTotal;
                    long promptTot = ms.InputTokens + ms.CacheTokens;
                    double cachePct = promptTot > 0 ? (ms.CacheTokens * 100.0 / promptTot) : 0.0;

                    var rcRow = new Rectangle(ix - S(4), my, rowW + S(8), rowH);
                    _modelRowHits.Add(new KeyValuePair<Rectangle, string>(rcRow, kv.Key));

                    bool hov = rcRow.Contains(_mousePt);
                    if (hov)
                    {
                        FillRoundCard(g, rcRow, S(8), ColHoverFill, ColHoverBorder);
                    }

                    int rx = ix + S(2);
                    int rw = rowW - S(4);
                    int ry = my + S(4);

                    // Line 1: Model Name | Total (Share%) · Turns ›
                    string rightTop = I18n.TF("{0} ({1:F1}%) · {2}轮 ›", "{0} ({1:F1}%) · {2}t >", FmtBigZh(ms.TotalTokens), share, ms.Calls);
                    DrawLeftRightRow(g, kv.Key, fBodyBold, hov ? ColAccent : ColTextMain,
                        rightTop, fSmallBold, hov ? ColAccent : ColTextSub, rx, ry, rw, S(16));
                    ry += S(17);

                    // Line 2: Gradient Progress Bar
                    var rcProg = new Rectangle(rx, ry, rw, S(5));
                    FillRoundCard(g, rcProg, S(2), ColTrackBg, ColTrackBg);
                    int fillW = Math.Max(S(6), (int)Math.Round(rw * Math.Min(1.0, share / 100.0)));
                    using (var brProg = new LinearGradientBrush(new Rectangle(rx, ry, fillW, S(5)), ColAccent, Pal.IsDark ? Color.FromArgb(56, 189, 248) : Color.FromArgb(14, 165, 233), 0f))
                    using (GraphicsPath gp = CreateRoundPath(new Rectangle(rx, ry, fillW, S(5)), S(2)))
                    {
                        g.FillPath(brProg, gp);
                    }
                    ry += S(8);

                    // Line 3: IO details + per-model Cache Hit Rate
                    string leftIo = I18n.TF("新入 {0} · 缓存 {1} · 出 {2}", "In {0} · Cache {1} · Out {2}",
                        FmtCompact(ms.InputTokens), FmtCompact(ms.CacheTokens), FmtCompact(ms.OutputTokens));
                    Color cCol = cachePct >= 75.0 ? ColOkText : (cachePct >= 40.0 ? ColWarnText : ColDangerText);
                    DrawLeftRightRow(g, leftIo, fSmall, ColTextSub,
                        I18n.TF("命中 {0:F1}%", "Hit {0:F1}%", cachePct), fSmallBold, cCol, rx, ry, rw, S(15));

                    my += rowH + rowGap;
                    drawn++;
                }

                if (hasScroll)
                {
                    DrawMiniScrollBar(g, new Rectangle(ix + iw - S(4), _rcOverviewScrollArea.Top + S(2), S(4), Math.Max(S(24), listH - S(4))),
                        totalItems, visCap, _overviewModelScroll);
                }
            }
        }

        private void DrawModelDrilldownBody(
            Graphics g, int x, int y, int w, int availH,
            Font fTitle, Font fBodyBold, Font fBody, Font fSmall, Font fSmallBold, Font fMonoSmall, Font fMonoReg, Font fBigNum)
        {
            ModelDrilldownData dd = _drillData ?? new ModelDrilldownData { ModelName = _drillModelName, Period = _ledgerPeriod };

            // 1. Top Drilldown Header Bar (← 返回 | Model Name | Period Buttons)
            int headH = S(30);
            var rcHead = new Rectangle(x, y, w, headH);
            FillRoundCard(g, rcHead, S(10), ColCardBg, ColCardBorder);

            _rcDrillBackBtn = new Rectangle(rcHead.Left + S(5), rcHead.Top + S(4), S(58), S(22));
            bool backHov = _rcDrillBackBtn.Contains(_mousePt);
            FillRoundCard(g, _rcDrillBackBtn, S(6),
                backHov ? ColAccentSoft : ColSubtleFill,
                backHov ? ColAccent : ColCardBorder);
            DrawCenteredText(g, I18n.T("← 返回", "<- Back"), fSmallBold, backHov ? ColAccent : ColTextMain, _rcDrillBackBtn);

            int pTotalW = DrawPeriodButtons(g, rcHead.Right - S(6), rcHead.Top + S(4), fSmallBold);

            int titleLeft = _rcDrillBackBtn.Right + S(8);
            int titleW = Math.Max(S(60), rcHead.Right - S(6) - pTotalW - S(6) - titleLeft);
            using (var brMain = new SolidBrush(ColTextMain))
            {
                g.DrawString(dd.ModelName, fBodyBold, brMain, new RectangleF(titleLeft, rcHead.Top, titleW, headH), SfNearEllipsis);
            }

            y += headH + S(8);

            // 2. Model 3 KPI Boxes
            int kGap = S(7);
            int kw = (w - kGap * 2) / 3;
            int kh = S(66);

            DrawKpiBox(g, new Rectangle(x, y, kw, kh),
                dd.PeriodLabel + I18n.T("模型消耗", "Model Tokens"),
                FmtBigZh(dd.TotalTokens),
                I18n.TF("${0} · 均轮 {1}", "~${0} · Avg {1}", dd.CostUsd >= 100.0 ? Math.Round(dd.CostUsd).ToString("F0") : dd.CostUsd.ToString("F1"), FmtCompact(dd.AvgTokensPerTurn)),
                fSmall, fBigNum, ColTextMain, false);

            Color cacheValCol = dd.Turns == 0 ? ColTextSub
                : (dd.CacheHitPct >= 75.0 ? ColOkText : (dd.CacheHitPct >= 40.0 ? ColWarnText : ColDangerText));
            DrawKpiBox(g, new Rectangle(x + kw + kGap, y, kw, kh),
                I18n.T("缓存命中率", "Cache Hit Rate"),
                dd.Turns > 0 ? string.Format("{0:F1}%", dd.CacheHitPct) : "--",
                I18n.TF("缓 {0} · 入 {1}", "Cache {0} · In {1}", FmtCompact(dd.CacheTokens), FmtCompact(dd.InputTokens)),
                fSmall, fBigNum, cacheValCol, false);

            DrawKpiBox(g, new Rectangle(x + (kw + kGap) * 2, y, w - (kw + kGap) * 2, kh),
                I18n.T("关联会话与工具", "Chats & Tools"),
                dd.ConvCount + I18n.T(" 会话", " chats"),
                I18n.TF("{0} 轮 · {1} 次工具", "{0} turns · {1} tools", dd.Turns, dd.ToolCallCount),
                fSmall, fBigNum, ColAccent, false);

            y += kh + S(7);

            // 3. Per-Model High-Frequency Tools Card
            int toolCardH = S(52);
            var rcToolCard = new Rectangle(x, y, w, toolCardH);
            FillRoundCard(g, rcToolCard, S(11), ColCardBg, ColCardBorder);

            int ix = rcToolCard.Left + S(11);
            int iw = rcToolCard.Width - S(22);
            int ty = rcToolCard.Top + S(6);

            string toolSummaryTxt = dd.ToolCallCount > 0
                ? I18n.TF("共 {0} 次 · {1} 类工具 ({2})", "{0} calls · {1} tools ({2})", dd.ToolCallCount, dd.TopTools.Count, dd.DateRangeText)
                : dd.DateRangeText;
            DrawLeftRightRow(g, I18n.T("高频工具分布", "Top Tools Breakdown"), fSmallBold, ColTextMain,
                toolSummaryTxt, fSmall, ColTextSub, ix, ty, iw, S(16));
            ty += S(19);

            if (dd.TopTools.Count == 0)
            {
                using (var brSub = new SolidBrush(ColTextSub))
                {
                    g.DrawString(I18n.T("该周期内该模型未调用工具", "No tools invoked by this model"), fSmall, brSub, new RectangleF(ix, ty, iw, S(21)), SfNearCenter);
                }
            }
            else
            {
                DrawToolChipStrip(g, ix, ty, iw, S(21), dd.TopTools, dd.ToolCallCount, dd.ModelName, fSmall, fMonoSmall);
            }

            y += toolCardH + S(8);

            // 4. Conversations / Turns Drilldown List Card
            int listCardH = Math.Max(S(180), availH - headH - kh - toolCardH - S(24));
            var rcListCard = new Rectangle(x, y, w, listCardH);
            FillRoundCard(g, rcListCard, S(12), ColCardBg, ColCardBorder);

            int ly = rcListCard.Top + S(8);

            // Sub-tab pills (按对话汇总 / 调用明细)
            int tabH = S(22);
            int convTabW = S(76);
            int turnTabW = S(82);
            _rcDrillTabConv = new Rectangle(ix, ly, convTabW, tabH);
            _rcDrillTabTurns = new Rectangle(_rcDrillTabConv.Right + S(5), ly, turnTabW, tabH);

            bool selConv = (_drillSubTab == 0);
            FillRoundCard(g, _rcDrillTabConv, S(6),
                selConv ? ColAccentBtnBg : (_rcDrillTabConv.Contains(_mousePt) ? ColHoverFill : ColSubtleFill),
                selConv ? ColAccentBtnBg : ColCardBorder);
            DrawCenteredText(g, I18n.TF("按对话 ({0})", "By Chat ({0})", dd.ConvCount), fSmallBold, selConv ? Color.White : ColTextSub, _rcDrillTabConv);

            bool selTurns = (_drillSubTab == 1);
            FillRoundCard(g, _rcDrillTabTurns, S(6),
                selTurns ? ColAccentBtnBg : (_rcDrillTabTurns.Contains(_mousePt) ? ColHoverFill : ColSubtleFill),
                selTurns ? ColAccentBtnBg : ColCardBorder);
            DrawCenteredText(g, I18n.TF("明细 ({0})", "Turns ({0})", dd.Turns), fSmallBold, selTurns ? Color.White : ColTextSub, _rcDrillTabTurns);

            // Right controls: Low-Cache Filter + Prev/Next page micro-buttons
            int navBtnW = S(20);
            _rcDrillNextBtn = new Rectangle(rcListCard.Right - S(11) - navBtnW, ly, navBtnW, tabH);
            _rcDrillPrevBtn = new Rectangle(_rcDrillNextBtn.Left - S(60) - navBtnW, ly, navBtnW, tabH);
            _rcDrillLowCacheBtn = new Rectangle(_rcDrillPrevBtn.Left - S(6) - S(60), ly, S(60), tabH);

            Color lcBg = _drillLowCacheOnly ? ColDangerSoft : (_rcDrillLowCacheBtn.Contains(_mousePt) ? ColHoverFill : ColSubtleFill);
            Color lcBd = _drillLowCacheOnly ? ColDangerBorder : ColCardBorder;
            Color lcFg = _drillLowCacheOnly ? ColDangerText : ColTextSub;
            FillRoundCard(g, _rcDrillLowCacheBtn, S(6), lcBg, lcBd);
            DrawCenteredText(g, _drillLowCacheOnly ? I18n.T("低缓: 开", "LowCache: On") : I18n.T("仅低缓存", "Low Cache"), fSmallBold, lcFg, _rcDrillLowCacheBtn);

            ly += tabH + S(7);
            using (var pen = new Pen(ColCardBorder, 1f)) { g.DrawLine(pen, ix, ly, ix + iw, ly); }
            ly += S(6);

            int bodyH = rcListCard.Bottom - S(8) - ly;
            _rcDrillScrollArea = new Rectangle(ix - S(4), ly, iw + S(8), Math.Max(1, bodyH));

            if (_drillSubTab == 0)
            {
                // Filtered Conversation Summary Rows
                List<ModelDrilldownConvRow> rows = _drillLowCacheOnly
                    ? dd.ConvRows.Where(r => r.LowCacheCalls > 0 || r.CacheHitPct < 50.0).ToList()
                    : dd.ConvRows;

                int rowH = S(44);
                int rowGap = S(4);
                int visCap = Math.Max(1, (bodyH + rowGap) / (rowH + rowGap));
                _drillVisibleCap = visCap;

                int total = rows.Count;
                int maxScroll = Math.Max(0, total - visCap);
                if (_drillListScroll > maxScroll) _drillListScroll = maxScroll;
                if (_drillListScroll < 0) _drillListScroll = 0;

                DrawDrillPagerControls(g, total, visCap, _drillListScroll, fSmallBold, fMonoReg);

                if (total == 0)
                {
                    DrawCenteredText(g, _drillLowCacheOnly ? I18n.T("无低缓存(<50%)异常对话", "No low-cache (<50%) chats") : I18n.T("该周期内暂无此模型的调用记录", "No calls for this model in this period"),
                        fBody, ColTextSub, new Rectangle(ix, ly, iw, Math.Max(S(40), bodyH)));
                    return;
                }

                bool hasScroll = total > visCap;
                int rowW = hasScroll ? (iw - S(8)) : iw;
                int drawn = 0;

                for (int i = _drillListScroll; i < total && drawn < visCap; i++)
                {
                    if (ly + rowH > rcListCard.Bottom - S(4)) break;
                    ModelDrilldownConvRow r = rows[i];

                    var rcRow = new Rectangle(ix - S(4), ly, rowW + S(8), rowH);
                    string hoverDetail = I18n.TF("点击切入窗口HUD · 新入 {0} / 缓存 {1} / 出 {2} · 折算 {3}", "Open HUD · In {0} / Cache {1} / Out {2} · {3}", FmtCompact(r.InputTokens), FmtCompact(r.CacheTokens), FmtCompact(r.OutputTokens), I18n.FmtInlineCost(r.CostUsd));
                    _drillRowHits.Add(new DrillRowHit { Rect = rcRow, ConvKey = r.ConvKey, HoverDetail = hoverDetail });

                    bool hov = rcRow.Contains(_mousePt);
                    FillRoundCard(g, rcRow, S(7),
                        hov ? ColHoverFill : (drawn % 2 == 1 ? ColRowAltBg : ColCardBg),
                        hov ? ColHoverBorder : ColRowBorder);

                    int rx = ix + S(2);
                    int rw = rowW - S(4);
                    int ry = ly + S(4);

                    // Top line: Title | TotalTokens + CachePct Pill
                    Color cpBg = r.CacheHitPct >= 75.0 ? ColOkSoft : (r.CacheHitPct >= 40.0 ? ColWarnSoft : ColDangerSoft);
                    Color cpFg = r.CacheHitPct >= 75.0 ? ColOkText : (r.CacheHitPct >= 40.0 ? ColWarnText : ColDangerText);
                    string cpTxt = string.Format("{0:F0}%", r.CacheHitPct);
                    int cpW = DrawRightAlignedPill(g, cpTxt, fMonoSmall, cpBg, cpFg, rx + rw, ry + S(8));

                    string totTxt = FmtBigZh(r.TotalTokens);
                    SizeF totSz = g.MeasureString(totTxt, fMonoSmall);
                    int totW = (int)Math.Ceiling(totSz.Width) + S(4);
                    int titleMaxW = Math.Max(S(60), rw - cpW - totW - S(8));

                    using (var brMain = new SolidBrush(hov ? ColAccent : ColTextMain))
                    using (var brTot = new SolidBrush(ColTextMain))
                    {
                        g.DrawString(r.Title, fBodyBold, brMain, new RectangleF(rx, ry, titleMaxW, S(16)), SfNearEllipsis);
                        g.DrawString(totTxt, fMonoSmall, brTot, new RectangleF(rx + titleMaxW, ry, totW, S(16)), SfFarNoWrap);
                    }
                    ry += S(19);

                    // Bottom line: Time range · Calls · Tools · Cost | Low-cache badge
                    string timeSpan = r.FirstTime.Date == r.LastTime.Date
                        ? string.Format("{0:MM-dd HH:mm}~{1:HH:mm}", r.FirstTime, r.LastTime)
                        : string.Format("{0:MM-dd}~{1:MM-dd}", r.FirstTime, r.LastTime);
                    string leftSub = I18n.TF("{0} · {1}轮 · {2}工具 · {3}", "{0} · {1}t · {2} tools · {3}", timeSpan, r.Calls, r.ToolCalls, I18n.FmtInlineCost(r.CostUsd));
                    string rightSub = r.LowCacheCalls > 0 ? (I18n.T("低缓×", "Low*") + r.LowCacheCalls) : (I18n.T("出 ", "Out ") + FmtCompact(r.OutputTokens));
                    Color rightSubCol = r.LowCacheCalls > 0 ? ColDangerText : ColTextSub;
                    DrawLeftRightRow(g, leftSub, fSmall, ColTextSub, rightSub, fSmallBold, rightSubCol, rx, ry, rw, S(15));

                    ly += rowH + rowGap;
                    drawn++;
                }

                if (hasScroll)
                {
                    DrawMiniScrollBar(g, new Rectangle(ix + iw - S(4), _rcDrillScrollArea.Top + S(2), S(4), Math.Max(S(24), bodyH - S(4))),
                        total, visCap, _drillListScroll);
                }
            }
            else
            {
                // Filtered Per-Turn Detail Table
                List<ModelDrilldownTurnRow> rows = _drillLowCacheOnly
                    ? dd.TurnRows.Where(r => r.CacheHitPct < 50.0).ToList()
                    : dd.TurnRows;

                int headRowH = S(18);
                int rowH = S(25);
                int tblAvailH = Math.Max(S(25), bodyH - headRowH - S(4));
                int visCap = Math.Max(1, tblAvailH / rowH);
                _drillVisibleCap = visCap;

                int total = rows.Count;
                int maxScroll = Math.Max(0, total - visCap);
                if (_drillListScroll > maxScroll) _drillListScroll = maxScroll;
                if (_drillListScroll < 0) _drillListScroll = 0;

                DrawDrillPagerControls(g, total, visCap, _drillListScroll, fSmallBold, fMonoReg);

                bool hasScroll = total > visCap;
                int tblW = hasScroll ? (iw - S(8)) : iw;

                int cTime = S(74);
                int cTot = S(48);
                int cCache = S(44);
                int cTool = S(84);
                int cTitle = Math.Max(S(60), tblW - cTime - cTot - cCache - cTool);

                StringFormat sfL = SfNearEllipsis;
                StringFormat sfR = SfFarEllipsis;

                using (var brSub = new SolidBrush(ColTextSub))
                {
                    g.DrawString(I18n.T("时间", "Time"), fSmallBold, brSub, new RectangleF(ix, ly, cTime, headRowH), sfL);
                    g.DrawString(I18n.T("所属对话", "Conversation"), fSmallBold, brSub, new RectangleF(ix + cTime, ly, cTitle, headRowH), sfL);
                    g.DrawString(I18n.T("总消耗", "Tokens"), fSmallBold, brSub, new RectangleF(ix + cTime + cTitle, ly, cTot, headRowH), sfR);
                    g.DrawString(I18n.T("缓存率", "Cache"), fSmallBold, brSub, new RectangleF(ix + cTime + cTitle + cTot, ly, cCache, headRowH), sfR);
                    g.DrawString(I18n.T("工具", "Tools"), fSmallBold, brSub, new RectangleF(ix + cTime + cTitle + cTot + cCache, ly, cTool, headRowH), sfR);
                }
                ly += headRowH + S(2);
                using (var pen = new Pen(ColCardBorder, 1f)) { g.DrawLine(pen, ix, ly, ix + tblW, ly); }
                ly += S(2);

                if (total == 0)
                {
                    DrawCenteredText(g, _drillLowCacheOnly ? I18n.T("无低缓存(<50%)异常轮次", "No low-cache (<50%) turns") : I18n.T("该周期内暂无调用明细", "No turn details in this period"),
                        fBody, ColTextSub, new Rectangle(ix, ly, tblW, Math.Max(S(40), tblAvailH)));
                    return;
                }

                int rowsTop = ly;
                int drawn = 0;
                for (int i = _drillListScroll; i < total && drawn < visCap; i++)
                {
                    if (ly + rowH > rcListCard.Bottom - S(3)) break;
                    ModelDrilldownTurnRow r = rows[i];

                    var rcRow = new Rectangle(ix - S(3), ly, tblW + S(6), rowH);
                    string hoverDetail = I18n.TF("{0:MM-dd HH:mm:ss} · {1} · 新入 {2}/缓 {3}/出 {4} · {5}", "{0:MM-dd HH:mm:ss} · {1} · In {2}/C {3}/Out {4} · {5}",
                        r.TimeLocal, r.ConvTitle,
                        FmtCompact(r.InputTokens), FmtCompact(r.CacheTokens), FmtCompact(r.OutputTokens),
                        r.ToolsFull);
                    _drillRowHits.Add(new DrillRowHit { Rect = rcRow, ConvKey = r.ConvKey, HoverDetail = hoverDetail });

                    bool hov = rcRow.Contains(_mousePt);
                    if (hov)
                    {
                        FillRoundCard(g, rcRow, S(5), ColHoverFill, ColHoverBorder);
                    }
                    else if (drawn % 2 == 1)
                    {
                        using (var brAlt = new SolidBrush(ColRowAltBg))
                        {
                            g.FillRectangle(brAlt, rcRow);
                        }
                    }

                    Color cCol = r.CacheHitPct >= 75.0 ? ColOkText : (r.CacheHitPct >= 40.0 ? ColWarnText : ColDangerText);
                    using (var brSub = new SolidBrush(ColTextSub))
                    using (var brMain = new SolidBrush(hov ? ColAccent : ColTextMain))
                    using (var brCache = new SolidBrush(cCol))
                    using (var brTool = new SolidBrush(r.ToolCount > 0 ? ColAccent : ColTextSub))
                    {
                        g.DrawString(r.TimeLocal.ToString("MM-dd HH:mm"), fMonoReg, brSub, new RectangleF(ix, ly, cTime, rowH), sfL);
                        g.DrawString(r.ConvTitle, fSmall, brMain, new RectangleF(ix + cTime, ly, cTitle - S(4), rowH), sfL);
                        g.DrawString(FmtCompact(r.TotalTokens), fMonoSmall, brMain, new RectangleF(ix + cTime + cTitle, ly, cTot, rowH), sfR);
                        g.DrawString(string.Format("{0:F0}%", r.CacheHitPct), fMonoSmall, brCache, new RectangleF(ix + cTime + cTitle + cTot, ly, cCache, rowH), sfR);
                        g.DrawString(r.ToolsShort, fSmall, brTool, new RectangleF(ix + cTime + cTitle + cTot + cCache + S(4), ly, cTool - S(4), rowH), sfR);
                    }

                    ly += rowH;
                    drawn++;
                }

                if (hasScroll)
                {
                    DrawMiniScrollBar(g, new Rectangle(ix + iw - S(4), rowsTop + S(2), S(4), Math.Max(S(24), rcListCard.Bottom - S(8) - rowsTop)),
                        total, visCap, _drillListScroll);
                }
            }
        }

        private int DrawPeriodButtons(Graphics g, int rightEdge, int topY, Font fSmallBold)
        {
            string[] pLabels = { I18n.T("今日", "Today"), I18n.T("昨日", "Yest"), I18n.T("7天", "7d"), I18n.T("30天", "30d"), I18n.T("全部", "All") };
            int pBtnW = S(36);
            int pBtnH = S(24);
            int pGap = S(3);
            int totalW = pBtnW * 5 + pGap * 4;
            int pxStart = rightEdge - totalW;
            for (int i = 0; i < 5; i++)
            {
                var rP = new Rectangle(pxStart + i * (pBtnW + pGap), topY, pBtnW, pBtnH);
                _rcPeriodBtns[i] = rP;
                bool sel = ((int)_ledgerPeriod == i);
                FillRoundCard(g, rP, S(6),
                    sel ? ColAccentBtnBg : (rP.Contains(_mousePt) ? ColHoverFill : ColSubtleFill),
                    sel ? ColAccentBtnBg : ColCardBorder);
                DrawCenteredText(g, pLabels[i], fSmallBold, sel ? Color.White : ColTextSub, rP);
            }
            return totalW;
        }

        private void DrawToolChipStrip(
            Graphics g, int x, int y, int maxW, int h,
            List<KeyValuePair<string, int>> tools, int totalCalls, string scopeLabel,
            Font fNameFont, Font fMonoBold)
        {
            int curX = x;
            int rightLimit = x + maxW;
            int shown = 0;

            for (int i = 0; i < tools.Count && shown < 6; i++)
            {
                string tName = tools[i].Key;
                int tCnt = tools[i].Value;
                string cntStr = tCnt.ToString();

                int nameW = MeasureTxtW(g, tName, fNameFont);
                int cntW = MeasureTxtW(g, cntStr, fMonoBold);
                int padX = S(7);
                int innerGap = S(5);
                int chipW = padX * 2 + nameW + innerGap + cntW;

                if (curX + chipW > rightLimit)
                {
                    if (shown == 0)
                        chipW = Math.Max(S(50), rightLimit - curX);
                    else
                        break;
                }

                var rcChip = new Rectangle(curX, y + S(1), chipW, h - S(2));
                bool hov = rcChip.Contains(_mousePt);
                FillRoundCard(g, rcChip, S(6),
                    hov ? ColAccentSoft : ColSubtleFill,
                    hov ? ColAccent : ColCardBorder);

                DrawTxtL(g, tName, fNameFont, ColTextMain,
                    new Rectangle(rcChip.Left + padX, rcChip.Top, Math.Min(nameW + S(2), rcChip.Width - padX * 2), rcChip.Height));
                if (rcChip.Width >= padX * 2 + nameW + innerGap + cntW - S(2))
                {
                    DrawTxtR(g, cntStr, fMonoBold, ColAccent,
                        new Rectangle(rcChip.Right - padX - cntW - S(2), rcChip.Top, cntW + S(2), rcChip.Height));
                }

                double pct = totalCalls > 0 ? (tCnt * 100.0 / totalCalls) : 0.0;
                string hoverTxt = I18n.TF("工具「{0}」· {1} 共调用 {2} 次 (占 {3:F1}%)", "Tool [{0}] · {2} calls in {1} ({3:F1}%)", tName, scopeLabel, tCnt, pct);
                _toolChipHits.Add(new KeyValuePair<Rectangle, string>(rcChip, hoverTxt));

                curX += chipW + S(5);
                shown++;
            }
        }

        private void DrawDrillPagerControls(Graphics g, int total, int visCap, int scroll, Font fSmallBold, Font fMonoReg)
        {
            bool canPrev = scroll > 0;
            bool canNext = (scroll + visCap) < total;

            FillRoundCard(g, _rcDrillPrevBtn, S(5),
                (canPrev && _rcDrillPrevBtn.Contains(_mousePt)) ? ColHoverFill : ColSubtleFill,
                ColCardBorder);
            DrawCenteredText(g, "<", fSmallBold, canPrev ? ColTextMain : ColDisabledArrow, _rcDrillPrevBtn);

            FillRoundCard(g, _rcDrillNextBtn, S(5),
                (canNext && _rcDrillNextBtn.Contains(_mousePt)) ? ColHoverFill : ColSubtleFill,
                ColCardBorder);
            DrawCenteredText(g, ">", fSmallBold, canNext ? ColTextMain : ColDisabledArrow, _rcDrillNextBtn);

            string posTxt;
            if (total <= 0) posTxt = "0/0";
            else
            {
                int from = scroll + 1;
                int to = Math.Min(total, scroll + visCap);
                posTxt = string.Format("{0}-{1}/{2}", from, to, total);
            }
            var rcPos = new Rectangle(_rcDrillPrevBtn.Right + S(1), _rcDrillPrevBtn.Top, _rcDrillNextBtn.Left - _rcDrillPrevBtn.Right - S(2), _rcDrillPrevBtn.Height);
            DrawCenteredText(g, posTxt, fMonoReg, ColTextSub, rcPos);
        }

        private void DrawMiniScrollBar(Graphics g, Rectangle track, int total, int visible, int scroll)
        {
            if (total <= visible || track.Height < S(16)) return;
            FillRoundCard(g, track, S(2), ColScrollTrack, ColScrollTrack);
            int thumbH = Math.Max(S(18), (int)Math.Round(track.Height * ((double)visible / total)));
            int maxScroll = Math.Max(1, total - visible);
            int thumbY = track.Top + (int)Math.Round((track.Height - thumbH) * ((double)scroll / maxScroll));
            var rcThumb = new Rectangle(track.Left, thumbY, track.Width, thumbH);
            FillRoundCard(g, rcThumb, S(2), ColScrollThumb, ColScrollThumb);
        }

        private void DrawKpiBox(Graphics g, Rectangle rc, string label, string val, string sub, Font fSmall, Font fBig, Color valCol, bool highlight = false)
        {
            FillRoundCard(g, rc, S(10),
                highlight ? Pal.RowHighlight : ColCardBg,
                highlight ? ColAccentBorder : ColCardBorder);
            int px = rc.Left + S(11);
            int pw = rc.Width - S(18);
            DrawTxtL(g, label, fSmall, highlight ? ColAccent : ColTextSub, new Rectangle(px, rc.Top + S(7), pw, S(16)));
            DrawTxtL(g, val, fBig, valCol, new Rectangle(px, rc.Top + S(23), pw + S(4), S(26)));
            DrawTxtL(g, sub, fSmall, ColTextSub, new Rectangle(px, rc.Bottom - S(21), pw + S(2), S(16)));
        }

        private void Draw7DayBars(Graphics g, Rectangle rc, List<DayBarPoint> days, Font fNum, Font fLbl, Font fLblBold)
        {
            if (days == null || days.Count == 0) return;
            long maxT = Math.Max(1L, days.Max(d => d.Tokens));
            int n = days.Count;
            int colW = rc.Width / n;

            for (int i = 0; i < n; i++)
            {
                DayBarPoint d = days[i];
                bool isYest = (d.Label == "昨日" || d.Label == "Yest");
                bool isFocusedBar = (d.IsToday && _ledgerPeriod == PeriodTab.Today) ||
                                    (isYest && _ledgerPeriod == PeriodTab.Yesterday);

                int cx = rc.Left + i * colW;
                var rcColHit = new Rectangle(cx, rc.Top, colW, rc.Height);
                bool colHov = rcColHit.Contains(_mousePt);

                if (d.IsToday)
                {
                    _dayBarClickHits.Add(new KeyValuePair<Rectangle, int>(rcColHit, (int)PeriodTab.Today));
                    _dayBarHoverHits.Add(new KeyValuePair<Rectangle, string>(rcColHit,
                        I18n.TF("{0} (今日) · {1} · {2}个活跃窗口 · 点击切换今日账本", "{0} (Today) · {1} · {2} chats · Click to view Today", d.DateKey, FmtBigZh(d.Tokens), d.Sessions)));
                }
                else if (isYest)
                {
                    _dayBarClickHits.Add(new KeyValuePair<Rectangle, int>(rcColHit, (int)PeriodTab.Yesterday));
                    _dayBarHoverHits.Add(new KeyValuePair<Rectangle, string>(rcColHit,
                        I18n.TF("{0} (昨日) · {1} · {2}个活跃窗口 · 点击切换昨日账本", "{0} (Yest) · {1} · {2} chats · Click to view Yesterday", d.DateKey, FmtBigZh(d.Tokens), d.Sessions)));
                }
                else
                {
                    _dayBarHoverHits.Add(new KeyValuePair<Rectangle, string>(rcColHit,
                        I18n.TF("{0} · {1} · {2}个活跃窗口", "{0} · {1} · {2} chats", d.DateKey, FmtBigZh(d.Tokens), d.Sessions)));
                }

                if ((d.IsToday || isYest) && colHov)
                {
                    FillRoundCard(g, new Rectangle(cx + S(3), rc.Top, colW - S(6), rc.Height), S(6), ColHoverFill, ColHoverBorder);
                }

                int trackW = Math.Min(S(24), colW - S(14));
                int trackX = cx + (colW - trackW) / 2;
                int topTxtH = S(16);
                int botTxtH = S(16);
                int trackH = Math.Max(S(24), rc.Height - topTxtH - botTxtH - S(6));
                int trackY = rc.Top + topTxtH + S(2);

                string topStr = d.Tokens > 0 ? FmtShortCn(d.Tokens) : "-";
                Color topCol = d.IsToday ? ColOkText : (isFocusedBar ? ColAccent : ColTextSub);
                DrawTxtC(g, topStr, fNum, topCol, new Rectangle(cx, rc.Top, colW, topTxtH));

                var rcTrack = new Rectangle(trackX, trackY, trackW, trackH);
                FillRoundCard(g, rcTrack, S(5),
                    isFocusedBar ? ColAccentSoft : Pal.ChartBarEmptyBg,
                    isFocusedBar ? ColHoverBorder : Pal.ChartBarEmptyBg);

                if (d.Tokens > 0)
                {
                    int fh = Math.Max(S(6), (int)Math.Round(trackH * ((double)d.Tokens / maxT)));
                    var rcFill = new Rectangle(trackX, trackY + trackH - fh, trackW, fh);
                    Color cTop, cBot;
                    if (d.IsToday)
                    {
                        cTop = Pal.IsDark ? Color.FromArgb(45, 212, 191) : Color.FromArgb(13, 148, 136);
                        cBot = Pal.IsDark ? Color.FromArgb(20, 184, 166) : Color.FromArgb(15, 118, 110);
                    }
                    else if (isFocusedBar || (isYest && _ledgerPeriod == PeriodTab.Today))
                    {
                        cTop = Pal.IsDark ? Color.FromArgb(96, 165, 250) : Color.FromArgb(37, 99, 235);
                        cBot = Pal.IsDark ? Color.FromArgb(59, 130, 246) : Color.FromArgb(29, 78, 216);
                    }
                    else
                    {
                        cTop = Pal.IsDark ? Color.FromArgb(124, 134, 150) : Color.FromArgb(100, 116, 139);
                        cBot = Pal.IsDark ? Color.FromArgb(92, 100, 114) : Color.FromArgb(71, 85, 105);
                    }
                    using (var br = new LinearGradientBrush(rcFill, cTop, cBot, 90f))
                    using (GraphicsPath gp = CreateRoundPath(rcFill, S(5)))
                    {
                        g.FillPath(br, gp);
                    }
                }

                Color lblCol = isFocusedBar ? (d.IsToday ? ColOkText : ColAccent) : ((d.IsToday || isYest) ? ColTextMain : ColTextSub);
                Font useLblFont = (isFocusedBar || d.IsToday || isYest) ? fLblBold : fLbl;
                DrawTxtC(g, d.Label, useLblFont, lblCol, new Rectangle(cx, trackY + trackH + S(3), colW, botTxtH));
            }
        }

        private void DrawCompactOpsBar(
            Graphics g, int x, int y, int w, int h,
            MemGuardSnapshot mgSpec,
            ExternalApiSnapshot extApi,
            Font fSmall, Font fSmallBold)
        {
            MemGuardSnapshot mg = mgSpec ?? new MemGuardSnapshot();
            bool hasMemWarn = mg.IsMemHigh || mg.TempCacheCount > 0;
            bool hasExtLedger = extApi != null && extApi.HasLedger;
            bool hasExtConv = hasExtLedger && extApi.ConvUsd > 0.0001;
            bool hasExtToday = hasExtLedger && extApi.TodayUsd > 0.0001;
            UserConfig cfg = _getConfig != null ? _getConfig() : null;
            bool isCustomPos = cfg != null && string.Equals(cfg.AnchorMode, "custom", StringComparison.OrdinalIgnoreCase);
            bool isPosLocked = cfg != null && cfg.PositionLocked;

            var rcBar = new Rectangle(x, y, w, h);
            FillRoundCard(g, rcBar, S(8), ColCardBg, hasMemWarn ? ColWarnBorder : ColCardBorder);

            int cy = y + h / 2;
            Color mgDotCol = mg.IsMemHigh ? ColDanger : (hasMemWarn ? ColWarn : ColOk);
            DrawStatusDot(g, x + S(10), cy, S(6), mgDotCol, false);

            string memStr = mg.AgTotalMB >= 1024.0
                ? ((mg.AgTotalMB / 1024.0).ToString("F2") + "G")
                : (Math.Round(mg.AgTotalMB) + "M");
            bool justCleaned = mg.LastActionAt != DateTime.MinValue && (DateTime.Now - mg.LastActionAt).TotalSeconds <= 15.0;
            string memLbl = justCleaned
                ? (mg.LastFreedMB > 0.5 ? string.Format("-{0:F0}M", mg.LastFreedMB) : I18n.T("已回收", "Freed"))
                : (I18n.T("内存 ", "Mem ") + memStr);

            int btnH = S(22);
            int btnY = cy - btnH / 2;
            string guardTxt = I18n.T("省存:", "AutoTrim:") + (mg.AutoGuardEnabled ? I18n.T("开", "On") : I18n.T("关", "Off"));
            string cleanTxt = hasMemWarn ? I18n.T("清缓存", "Clean") : I18n.T("释存", "Trim");
            int btnAutoW = Math.Max(S(54), MeasureTxtW(g, guardTxt, fSmallBold) + S(14));
            int btnCleanW = Math.Max(S(44), MeasureTxtW(g, cleanTxt, fSmallBold) + S(14));
            int memTxtW = MeasureTxtW(g, memLbl, fSmallBold) + S(6);
            int compactLeftW = S(18) + memTxtW + S(6) + btnAutoW + S(4) + btnCleanW + S(8);
            int maxLeftW = (int)Math.Round(w * (hasExtConv ? 0.48 : 0.56));
            int leftW = Math.Max(S(180), Math.Min(maxLeftW, compactLeftW));
            int divX = x + leftW;

            _rcCleanMemBtn = new Rectangle(divX - S(6) - btnCleanW, btnY, btnCleanW, btnH);
            _rcAutoGcBtn = new Rectangle(_rcCleanMemBtn.Left - S(4) - btnAutoW, btnY, btnAutoW, btnH);
            _rcMemInfoArea = new Rectangle(x, y, _rcAutoGcBtn.Left - x - S(2), h);

            Color memTextCol = justCleaned ? ColOkText : (mg.IsMemHigh ? ColDangerText : ColTextMain);
            DrawTxtL(g, memLbl, fSmallBold, memTextCol, new Rectangle(x + S(18), y, Math.Max(S(20), _rcAutoGcBtn.Left - (x + S(20))), h));

            bool autoHover = _rcAutoGcBtn.Contains(_mousePt);
            Color autoBg = mg.AutoGuardEnabled
                ? (autoHover ? (Pal.IsDark ? Color.FromArgb(30, 72, 58) : Color.FromArgb(198, 244, 224)) : ColOkSoft)
                : (autoHover ? ColHoverFill : ColSubtleFill);
            Color autoBd = mg.AutoGuardEnabled ? ColOkBorder : ColCardBorder;
            Color autoFg = mg.AutoGuardEnabled ? ColOkText : ColTextSub;
            FillRoundCard(g, _rcAutoGcBtn, S(5), autoBg, autoBd);
            DrawCenteredText(g, guardTxt, fSmallBold, autoFg, _rcAutoGcBtn);

            bool cleanHover = _rcCleanMemBtn.Contains(_mousePt);
            Color clBg = hasMemWarn
                ? (cleanHover ? (Pal.IsDark ? Color.FromArgb(84, 62, 26) : Color.FromArgb(253, 230, 138)) : ColWarnSoft)
                : (cleanHover ? ColAccentSoft : ColSubtleFill);
            Color clBd = hasMemWarn ? ColWarnBorder : (cleanHover ? ColAccent : ColCardBorder);
            Color clFg = hasMemWarn ? ColWarnText : (cleanHover ? ColAccent : ColTextMain);
            FillRoundCard(g, _rcCleanMemBtn, S(5), clBg, clBd);
            DrawCenteredText(g, cleanTxt, fSmallBold, clFg, _rcCleanMemBtn);

            using (var penDiv = new Pen(ColCardBorder, 1f))
            {
                g.DrawLine(penDiv, divX, y + S(6), divX, y + h - S(6));
            }

            _rcPosHintBar = new Rectangle(divX + 1, y, x + w - divX - 1, h);
            if ((hasExtLedger || isCustomPos) && _rcPosHintBar.Contains(_mousePt))
            {
                var rcRightHi = new Rectangle(divX + S(2), y + S(2), x + w - divX - S(4), h - S(4));
                FillRoundCard(g, rcRightHi, S(6), ColHoverFill, ColHoverFill);
            }

            int rightTxtX = divX + S(18);
            int rightAvailW = Math.Max(S(40), (x + w - S(8)) - rightTxtX);

            if (hasExtLedger)
            {
                Color apiDotCol = hasExtConv ? ColWarn : (hasExtToday ? ColOk : ColTextMuted);
                DrawStatusDot(g, divX + S(10), cy, S(6), apiDotCol, false);

                Font apiFont = (hasExtConv || hasExtToday) ? fSmallBold : fSmall;
                string apiTxt;
                Color apiValCol;
                if (hasExtConv)
                {
                    apiValCol = ColWarnText;
                    string c1 = I18n.TF("窗 ${0:F2} · 日 ${1:F2} · 月 ${2:F2}", "Chat ${0:F2} · D ${1:F2} · M ${2:F2}", extApi.ConvUsd, extApi.TodayUsd, extApi.MonthUsd);
                    string c2 = I18n.TF("窗${0:F2} · 日${1:F2} · 月${2:F2}", "C${0:F2} · D${1:F2} · M${2:F2}", extApi.ConvUsd, extApi.TodayUsd, extApi.MonthUsd);
                    apiTxt = MeasureTxtW(g, c1, apiFont) <= rightAvailW ? c1 : c2;
                }
                else
                {
                    apiTxt = I18n.TF("API 日 ${0:F2} · 月 ${1:F2}", "API Day ${0:F2} · Mo ${1:F2}", extApi.TodayUsd, extApi.MonthUsd);
                    apiValCol = hasExtToday ? ColOkText : ColTextMain;
                }
                DrawTxtR(g, apiTxt, apiFont, apiValCol, new Rectangle(rightTxtX, y, rightAvailW, h));
                return;
            }

            Color posDotCol = isCustomPos ? ColAccent : (isPosLocked ? ColWarn : ColTextMuted);
            DrawStatusDot(g, divX + S(10), cy, S(6), posDotCol, false);

            string rightTxt;
            Color rightCol = ColTextSub;
            if (isCustomPos)
            {
                rightTxt = I18n.T("自由位置 · 点此复位", "Custom pos · Reset");
                rightCol = ColAccent;
            }
            else if (isPosLocked)
            {
                rightTxt = I18n.T("胶囊位置已锁定", "Bar position locked");
            }
            else if (mg.TempCacheCount > 0)
            {
                rightTxt = I18n.TF("缓存 {0:F0}M · 长按可移", "Temp {0:F0}M · Hold bar", mg.TempCacheMB);
            }
            else
            {
                rightTxt = I18n.T("长按胶囊移动位置", "Hold bar to move");
            }

            DrawTxtR(g, rightTxt, isCustomPos ? fSmallBold : fSmall, rightCol, new Rectangle(rightTxtX, y, rightAvailW, h));
        }

        private void DrawFooterBar(Graphics g, int x, int y, int w, int h, HudSnapshot hud, Font fSmall, Font fSmallBold)
        {
            int btnExitW = S(52);
            int btnDirW = S(68);
            _rcExitBtn = new Rectangle(x + w - btnExitW, y, btnExitW, h);
            _rcOpenFolder = new Rectangle(_rcExitBtn.Left - S(6) - btnDirW, y, btnDirW, h);

            FillRoundCard(g, _rcOpenFolder, S(6), _rcOpenFolder.Contains(_mousePt) ? ColHoverFill : ColCardBg, ColCardBorder);
            DrawCenteredText(g, I18n.T("日志目录", "Logs Dir"), fSmallBold, ColTextSub, _rcOpenFolder);

            FillRoundCard(g, _rcExitBtn, S(6), _rcExitBtn.Contains(_mousePt) ? ColAccentSoft : ColCardBg, _rcExitBtn.Contains(_mousePt) ? ColAccent : ColCardBorder);
            DrawCenteredText(g, I18n.T("收起", "Hide"), fSmallBold, _rcExitBtn.Contains(_mousePt) ? ColAccent : ColTextMain, _rcExitBtn);

            string leftStatus;
            Color statusCol = ColTextSub;
            if (!string.IsNullOrEmpty(_hoverHint))
            {
                leftStatus = _hoverHint;
                statusCol = ColAccent;
            }
            else if (hud != null)
            {
                leftStatus = I18n.TF("会话 {0} ({1}) · 第{2}步 · 同步于 {3:HH:mm:ss}", "Chat {0} ({1}) · Step {2} · {3:HH:mm:ss}",
                    hud.ShortId, hud.Surface, hud.TotalSteps, _data.RefreshedAt);
            }
            else
            {
                leftStatus = I18n.TF("本地监控就绪 · 同步于 {0:HH:mm:ss}", "Local telemetry ready · {0:HH:mm:ss}", _data.RefreshedAt);
            }

            DrawTxtL(g, leftStatus, fSmall, statusCol, new Rectangle(x + S(2), y, _rcOpenFolder.Left - x - S(8), h));
        }

        // ================= VECTOR & DRAWING HELPERS =================
        private static int MeasureTxtW(Graphics g, string txt, Font font)
        {
            if (string.IsNullOrEmpty(txt)) return 0;
            SizeF sz = g.MeasureString(txt, font, 2400, StringFormat.GenericTypographic);
            return (int)Math.Ceiling(sz.Width) + 3;
        }

        private static void DrawTxtL(Graphics g, string txt, Font font, Color col, Rectangle rc)
        {
            if (string.IsNullOrEmpty(txt) || rc.Width <= 2 || rc.Height <= 2) return;
            using (var br = new SolidBrush(col))
            using (var sf = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                g.DrawString(txt, font, br, new RectangleF(rc.X, rc.Y, rc.Width, rc.Height), sf);
            }
        }

        private static void DrawTxtR(Graphics g, string txt, Font font, Color col, Rectangle rc)
        {
            if (string.IsNullOrEmpty(txt) || rc.Width <= 2 || rc.Height <= 2) return;
            using (var br = new SolidBrush(col))
            using (var sf = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Far,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                g.DrawString(txt, font, br, new RectangleF(rc.X, rc.Y, rc.Width, rc.Height), sf);
            }
        }

        private static void DrawTxtC(Graphics g, string txt, Font font, Color col, Rectangle rc)
        {
            if (string.IsNullOrEmpty(txt) || rc.Width <= 2 || rc.Height <= 2) return;
            using (var br = new SolidBrush(col))
            using (var sf = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                g.DrawString(txt, font, br, new RectangleF(rc.X, rc.Y, rc.Width, rc.Height), sf);
            }
        }

        private int DrawInlineText(Graphics g, string txt, Font font, Color col, int x, int centerY)
        {
            int w = MeasureTxtW(g, txt, font) + S(2);
            DrawTxtL(g, txt, font, col, new Rectangle(x, centerY - S(10), w, S(20)));
            return w;
        }

        private int DrawChipTag(Graphics g, string txt, Font font, Color bg, Color fg, int x, int centerY, int radius)
        {
            int w = MeasureTxtW(g, txt, font) + S(12);
            int h = S(20);
            var rc = new Rectangle(x, centerY - h / 2, w, h);
            using (var brBg = new SolidBrush(bg))
            using (GraphicsPath gp = CreateRoundPath(rc, radius))
            {
                g.FillPath(brBg, gp);
            }
            DrawTxtC(g, txt, font, fg, rc);
            return w;
        }

        private int DrawRightAlignedPill(Graphics g, string txt, Font font, Color bg, Color fg, int rightX, int centerY)
        {
            int w = MeasureTxtW(g, txt, font) + S(14);
            int h = S(20);
            var rc = new Rectangle(rightX - w, centerY - h / 2, w, h);
            using (var brBg = new SolidBrush(bg))
            using (GraphicsPath gp = CreateRoundPath(rc, S(10)))
            {
                g.FillPath(brBg, gp);
            }
            DrawTxtC(g, txt, font, fg, rc);
            return w;
        }

        private static int DrawPillDivider(Graphics g, int x, int centerY, int h)
        {
            using (var pen = new Pen(ColCardBorder, 1f))
            {
                g.DrawLine(pen, x, centerY - h / 2, x, centerY + h / 2);
            }
            return x + 7;
        }

        private static void DrawStatusDot(Graphics g, int cx, int cy, int diameter, Color col, bool glow)
        {
            if (glow)
            {
                int gd = diameter + 5;
                using (var brGlow = new SolidBrush(Color.FromArgb(48, col)))
                {
                    g.FillEllipse(brGlow, cx - gd / 2f, cy - gd / 2f, gd, gd);
                }
            }
            using (var br = new SolidBrush(col))
            {
                g.FillEllipse(br, cx - diameter / 2f, cy - diameter / 2f, diameter, diameter);
            }
        }


        private void DrawLeftRightRow(Graphics g, string leftTxt, Font fL, Color cL, string rightTxt, Font fR, Color cR, int x, int y, int w, int h)
        {
            int rightW = MeasureTxtW(g, rightTxt, fR) + S(14);
            DrawTxtL(g, leftTxt, fL, cL, new Rectangle(x, y, Math.Max(24, w - rightW), h));
            DrawTxtR(g, rightTxt, fR, cR, new Rectangle(x + w - rightW, y, rightW, h));
        }

        private static void DrawCenteredText(Graphics g, string txt, Font font, Color col, Rectangle rc)
        {
            DrawTxtC(g, txt, font, col, rc);
        }

        private static void FillRoundCard(Graphics g, Rectangle rc, int radius, Color fill, Color border)
        {
            if (rc.Width <= 1 || rc.Height <= 1) return;
            using (GraphicsPath gp = CreateRoundPath(rc, radius))
            {
                using (var br = new SolidBrush(fill))
                {
                    g.FillPath(br, gp);
                }
                using (var pen = new Pen(border, 1f))
                {
                    g.DrawPath(pen, gp);
                }
            }
        }

        private static GraphicsPath CreateRoundPath(Rectangle bounds, int radius)
        {
            int d = Math.Min(Math.Max(2, radius * 2), Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void GetToneColors(string tone, out Color bg, out Color fg)
        {
            if (tone == "carbon") { bg = Pal.ToneCarbonBg; fg = Pal.ToneCarbonFg; }
            else if (tone == "neon") { bg = Pal.ToneNeonBg; fg = Pal.ToneNeonFg; }
            else if (tone == "argon") { bg = Pal.ToneArgonBg; fg = Pal.ToneArgonFg; }
            else if (tone == "fast") { bg = Pal.ToneFastBg; fg = Pal.ToneFastFg; }
            else { bg = Pal.ToneMutedBg; fg = Pal.ToneMutedFg; }
        }

        private static string FmtCostSub(double usd, int convCount)
        {
            return I18n.FmtCost(usd, convCount);
        }

        public static string FmtCompact(long v)
        {
            return v < 0 ? "—" : I18n.FmtCompact(v);
        }

        public static string FmtBigZh(long v)
        {
            return I18n.FmtBigTokens(v);
        }

        private static string FmtShortCn(long v)
        {
            return I18n.FmtChartBarTokens(v);
        }
    }
    // =========================================================================
}
