using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AntigravityHud
{
    internal sealed class TaskbarAppContext : ApplicationContext
    {
        private readonly UserConfig _config;
        private readonly LocalTelemetryEngine _engine;
        private readonly TrayPopupForm _popup;
        private readonly TaskbarCapsuleBarForm _bar;
        private readonly ContextMenuStrip _trayMenu;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly System.Windows.Forms.Timer _winTitleWatchTimer;
        private readonly System.Windows.Forms.Timer _outsideClickTimer;
        private readonly string _agRoot;
        private readonly NativeMethods.WinEventDelegate _winEventDelegate;
        private IntPtr _winEventHook = IntPtr.Zero;

        private readonly HashSet<uint> _agPidCache = new HashSet<uint>();
        private DateTime _lastPidScanUtc = DateTime.MinValue;
        private IntPtr _lastActiveAgHwnd = IntPtr.Zero;
        private string _currentAgWinTitle = "";

        private string _selectedConvKey = "";
        private bool _wasMouseDown;
        private Font _menuFontReg;
        private Font _menuFontBold;

        public TaskbarAppContext()
        {
            float dpiScale = NativeMethods.GetSystemDpiScale();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _agRoot = Path.Combine(userProfile, ".gemini", "antigravity");

            _config = ConfigStore.Load();
            I18n.SetLanguage(_config.Language);

            _engine = new LocalTelemetryEngine(userProfile, _config.AutoMemoryGuard);

            _trayMenu = new ContextMenuStrip { ShowImageMargin = true };
            RebuildTrayMenu();

            _popup = new TrayPopupForm(
                dpiScale,
                onManualRefresh: () => RefreshAll(true),
                onOpenLogs: OpenBrainFolder,
                onExitApp: ExitCleanly,
                onSelectConv: (key) =>
                {
                    _selectedConvKey = key ?? "";
                    RefreshAll(false);
                },
                onScaleChanged: () =>
                {
                    SaveScaleToConfig(_popup.CurrentScaleMode);
                    RefreshAll(false);
                },
                onToggleAutoGc: () =>
                {
                    bool nowOn = _engine.MemGuard.ToggleAutoGuard();
                    _config.AutoMemoryGuard = nowOn;
                    ConfigStore.Save(_config);
                    RefreshAll(false);
                },
                onCleanMemNow: () =>
                {
                    _engine.MemGuard.CleanAndTrimNow(false);
                    RefreshAll(true);
                },
                onQueryDrilldown: (mName, per) => _engine.QueryModelDrilldown(mName, per),
                onToggleLanguage: ToggleLanguageAndRefresh,
                onResetPosition: ResetBarPosition,
                getConfig: () => _config
            );
            _popup.SetScaleMode(ParseScaleMode(_config.ScaleMode));

            _popup.VisibleChanged += (s, e) =>
            {
                if (_bar != null && !_bar.IsDisposed)
                    _bar.SetPopupOpenState(_popup.Visible);

                if (_popup.Visible)
                {
                    _wasMouseDown = ((NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0) ||
                                    ((NativeMethods.GetAsyncKeyState(0x02) & 0x8000) != 0);
                    _outsideClickTimer.Start();
                }
                else
                {
                    _outsideClickTimer.Stop();
                }
            };

            _outsideClickTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _outsideClickTimer.Tick += OnOutsideClickWatchTick;

            _bar = new TaskbarCapsuleBarForm(
                dpiScale,
                _config,
                TogglePopupFromBar,
                OnBarPositionChanged,
                _trayMenu);
            _bar.Show();

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            PollActiveAntigravityWindowTitle(true);
            RefreshAll(true);
            _bar.RenderLayered(true);
            _bar.EnsureDockedAndZOrder();

            // Event-driven Z-order & instant window switch detection
            _winEventDelegate = OnWinEventForeground;
            _winEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _winEventDelegate, 0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);

            // Sub-200ms lightweight Win32 GetWindowText check for instant window title tracking
            _winTitleWatchTimer = new System.Windows.Forms.Timer { Interval = 180 };
            _winTitleWatchTimer.Tick += (s, e) =>
            {
                if (PollActiveAntigravityWindowTitle(false))
                {
                    RefreshAll(false);
                }
            };
            _winTitleWatchTimer.Start();

            // Real-time 1-second incremental token telemetry refresh (0 disk read when unchanged)
            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        private static ScaleMode ParseScaleMode(string s)
        {
            if (string.Equals(s, "256k", StringComparison.OrdinalIgnoreCase)) return ScaleMode.K256;
            if (string.Equals(s, "1m", StringComparison.OrdinalIgnoreCase)) return ScaleMode.M1;
            return ScaleMode.Auto;
        }

        private void SaveScaleToConfig(ScaleMode mode)
        {
            _config.ScaleMode = mode == ScaleMode.K256 ? "256k" : (mode == ScaleMode.M1 ? "1m" : "auto");
            ConfigStore.Save(_config);
        }

        private void OnBarPositionChanged()
        {
            ConfigStore.Save(_config);
            RebuildTrayMenu();
            if (_popup != null && _popup.Visible) _popup.Invalidate();
        }

        private void SetBarStyle(string style)
        {
            _config.BarStyle = style;
            ConfigStore.Save(_config);
            RebuildTrayMenu();
            if (_bar != null && !_bar.IsDisposed)
            {
                _bar.RenderLayered(true);
                _bar.EnsureDockedAndZOrder();
            }
        }

        private void ResetBarPosition()
        {
            if (_bar != null && !_bar.IsDisposed)
            {
                _bar.ResetToTaskbarDefault();
            }
            if (_popup != null && _popup.Visible)
            {
                _popup.ShowNearBar(_bar.GetActualScreenBounds());
                _popup.Invalidate();
            }
        }

        private void ToggleLanguageAndRefresh()
        {
            string nextLang = I18n.ToggleLanguage();
            _config.Language = nextLang;
            ConfigStore.Save(_config);
            _engine.InvalidateLanguageCache();
            RebuildTrayMenu();
            RefreshAll(false);
            if (_bar != null && !_bar.IsDisposed) _bar.RenderLayered(true);
            if (_popup != null && !_popup.IsDisposed) _popup.Invalidate();
        }

        private void RebuildTrayMenu()
        {
            _trayMenu.Items.Clear();
            Font oldReg = _menuFontReg;
            Font oldBold = _menuFontBold;
            _menuFontReg = new Font(I18n.UiFontFamily, 9.0f, FontStyle.Regular, GraphicsUnit.Point);
            _menuFontBold = new Font(_menuFontReg, FontStyle.Bold);
            _trayMenu.Font = _menuFontReg;
            if (oldBold != null) oldBold.Dispose();
            if (oldReg != null) oldReg.Dispose();

            var miOpen = new ToolStripMenuItem(I18n.T("展开详细 HUD 看板", "Open HUD Dashboard"))
            {
                Font = _menuFontBold
            };
            miOpen.Click += (s, e) => OpenPopupNearBar();

            var miLockPos = new ToolStripMenuItem(I18n.T("锁定胶囊位置", "Lock Bar Position"))
            {
                Checked = _config.PositionLocked
            };
            miLockPos.Click += (s, e) =>
            {
                _config.PositionLocked = !_config.PositionLocked;
                ConfigStore.Save(_config);
                RebuildTrayMenu();
                if (_popup != null && _popup.Visible) _popup.Invalidate();
            };

            var miResetPos = new ToolStripMenuItem(I18n.T("重置到默认任务栏位置", "Reset to Taskbar Default"))
            {
                Enabled = string.Equals(_config.AnchorMode, "custom", StringComparison.OrdinalIgnoreCase)
            };
            miResetPos.Click += (s, e) => ResetBarPosition();

            string curStyle = _config.BarStyle ?? "compact";
            var miStyleRoot = new ToolStripMenuItem(I18n.T("胶囊宽度样式", "Capsule Bar Style"));
            var miStyleCompact = new ToolStripMenuItem(I18n.T("干练四项 (默认)", "Compact (Default)"))
            {
                Checked = string.Equals(curStyle, "compact", StringComparison.OrdinalIgnoreCase)
            };
            miStyleCompact.Click += (s, e) => SetBarStyle("compact");

            var miStyleLabeled = new ToolStripMenuItem(I18n.T("带文字标签 (缓存/思考)", "Labeled (Cache/Think)"))
            {
                Checked = string.Equals(curStyle, "labeled", StringComparison.OrdinalIgnoreCase)
            };
            miStyleLabeled.Click += (s, e) => SetBarStyle("labeled");

            var miStyleMinimal = new ToolStripMenuItem(I18n.T("极致短小 (小屏)", "Minimal (Ultra-Short)"))
            {
                Checked = string.Equals(curStyle, "minimal", StringComparison.OrdinalIgnoreCase)
            };
            miStyleMinimal.Click += (s, e) => SetBarStyle("minimal");

            miStyleRoot.DropDownItems.Add(miStyleCompact);
            miStyleRoot.DropDownItems.Add(miStyleLabeled);
            miStyleRoot.DropDownItems.Add(miStyleMinimal);

            var miLang = new ToolStripMenuItem(I18n.T("Switch to English (EN)", "切换为简体中文 (中文)"));
            miLang.Click += (s, e) => ToggleLanguageAndRefresh();

            var miCleanMem = new ToolStripMenuItem(I18n.T("释放内存与临时缓存", "Trim Memory & Temp Cache"));
            miCleanMem.Click += (s, e) =>
            {
                _engine.MemGuard.CleanAndTrimNow(false);
                RefreshAll(true);
            };

            var miRefresh = new ToolStripMenuItem(I18n.T("立即刷新数据", "Refresh Now"));
            miRefresh.Click += (s, e) => RefreshAll(true);

            var miFolder = new ToolStripMenuItem(I18n.T("打开本地会话日志目录", "Open Local Logs Folder"));
            miFolder.Click += (s, e) => OpenBrainFolder();

            bool autoStartOn = ConfigStore.IsAutostartEnabled();
            var miAutostart = new ToolStripMenuItem(I18n.T("开机自动启动", "Launch at Windows Login"))
            {
                Checked = autoStartOn
            };
            miAutostart.Click += (s, e) =>
            {
                ConfigStore.SetAutostart(!ConfigStore.IsAutostartEnabled(), Application.ExecutablePath);
                RebuildTrayMenu();
            };

            var miExit = new ToolStripMenuItem(I18n.T("退出 Antigravity HUD", "Exit Antigravity HUD"));
            miExit.Click += (s, e) => ExitCleanly();

            _trayMenu.Items.Add(miOpen);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(miStyleRoot);
            _trayMenu.Items.Add(miLockPos);
            _trayMenu.Items.Add(miResetPos);
            _trayMenu.Items.Add(miLang);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(miCleanMem);
            _trayMenu.Items.Add(miRefresh);
            _trayMenu.Items.Add(miFolder);
            _trayMenu.Items.Add(miAutostart);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(miExit);
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            if (_bar != null && !_bar.IsDisposed)
            {
                _bar.BeginInvoke(new Action(() =>
                {
                    if (!_bar.IsDisposed) _bar.HandleDisplaySettingsChanged();
                }));
            }
        }

        private void OnOutsideClickWatchTick(object sender, EventArgs e)
        {
            if (_popup == null || !_popup.Visible)
            {
                _outsideClickTimer.Stop();
                return;
            }

            if ((NativeMethods.GetAsyncKeyState(0x1B) & 0x8000) != 0)
            {
                _popup.Hide();
                return;
            }

            bool isDown = ((NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0) ||
                          ((NativeMethods.GetAsyncKeyState(0x02) & 0x8000) != 0);

            if (isDown && !_wasMouseDown)
            {
                if (!_popup.IsMenuOpen)
                {
                    Point pt = Cursor.Position;
                    Rectangle barRc = _bar != null ? _bar.GetActualScreenBounds() : Rectangle.Empty;
                    if (!_popup.Bounds.Contains(pt) && !barRc.Contains(pt))
                    {
                        _popup.Hide();
                    }
                }
            }
            _wasMouseDown = isDown;
        }

        private void OnWinEventForeground(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (_bar != null && !_bar.IsDisposed && !_bar.IsDragging)
                _bar.EnsureDockedAndZOrder();

            if (hwnd != IntPtr.Zero && IsAntigravityMainWindow(hwnd))
            {
                _lastActiveAgHwnd = hwnd;
                if (PollActiveAntigravityWindowTitle(false))
                    RefreshAll(false);
            }
        }

        private void RefreshAgPidsIfNeeded(bool force)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (!force && _agPidCache.Count > 0 && (nowUtc - _lastPidScanUtc).TotalSeconds < 12.0)
                return;

            _lastPidScanUtc = nowUtc;
            _agPidCache.Clear();
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    using (p)
                    {
                        string nm = p.ProcessName ?? "";
                        if (nm.IndexOf("Antigravity", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _agPidCache.Add((uint)p.Id);
                        }
                    }
                }
            }
            catch { }
        }

        private bool IsAntigravityMainWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hWnd)) return false;
            var cls = new StringBuilder(128);
            NativeMethods.GetClassName(hWnd, cls, cls.Capacity);
            if (cls.ToString() != "Chrome_WidgetWin_1") return false;

            uint pid;
            NativeMethods.GetWindowThreadProcessId(hWnd, out pid);
            if (_agPidCache.Contains(pid)) return true;

            RefreshAgPidsIfNeeded(true);
            return _agPidCache.Contains(pid);
        }

        private bool PollActiveAntigravityWindowTitle(bool forceDiscover)
        {
            RefreshAgPidsIfNeeded(forceDiscover);

            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero && IsAntigravityMainWindow(fg))
            {
                _lastActiveAgHwnd = fg;
            }
            else if (_lastActiveAgHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_lastActiveAgHwnd) || !NativeMethods.IsWindowVisible(_lastActiveAgHwnd))
            {
                IntPtr found = IntPtr.Zero;
                NativeMethods.EnumWindows((h, l) =>
                {
                    if (IsAntigravityMainWindow(h))
                    {
                        var tb = new StringBuilder(256);
                        NativeMethods.GetWindowText(h, tb, tb.Capacity);
                        if (tb.Length > 0)
                        {
                            found = h;
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
                _lastActiveAgHwnd = found;
            }

            if (_lastActiveAgHwnd != IntPtr.Zero)
            {
                var sb = new StringBuilder(512);
                NativeMethods.GetWindowText(_lastActiveAgHwnd, sb, sb.Capacity);
                string newTitle = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(newTitle) && newTitle != _currentAgWinTitle)
                {
                    _currentAgWinTitle = newTitle;
                    return true;
                }
            }
            return false;
        }

        private void TogglePopupFromBar()
        {
            if (_popup.Visible)
            {
                _popup.Hide();
                return;
            }
            OpenPopupNearBar();
        }

        private void OpenPopupNearBar()
        {
            RefreshAll(false);
            _popup.ShowNearBar(_bar.GetActualScreenBounds());
            _bar.EnsureDockedAndZOrder();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (_bar != null && !_bar.IsDisposed && !_bar.IsDragging)
            {
                _bar.EnsureDockedAndZOrder();
            }
            RefreshAll(false);
        }

        private void RefreshAll(bool forceFull)
        {
            DashboardData data = _engine.Query(_selectedConvKey, forceFull, _currentAgWinTitle);
            _popup.UpdateData(data, _selectedConvKey);
            _bar.UpdateTelemetry(data, _popup.CurrentScaleMode);
        }

        private void OpenBrainFolder()
        {
            string dir = Path.Combine(_agRoot, "brain");
            if (!Directory.Exists(dir))
            {
                string insDir = Path.Combine(Path.GetDirectoryName(_agRoot) ?? _agRoot, "antigravity-insiders", "brain");
                if (Directory.Exists(insDir)) dir = insDir;
            }
            if (Directory.Exists(dir))
            {
                try { Process.Start("explorer.exe", dir); } catch { }
            }
        }

        private void ExitCleanly()
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _timer.Stop();
            _winTitleWatchTimer.Stop();
            _outsideClickTimer.Stop();
            if (_winEventHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }
            if (_popup != null && !_popup.IsDisposed) _popup.Close();
            if (_bar != null && !_bar.IsDisposed) _bar.Close();
            if (_trayMenu != null && !_trayMenu.IsDisposed) _trayMenu.Dispose();
            if (_menuFontBold != null) { _menuFontBold.Dispose(); _menuFontBold = null; }
            if (_menuFontReg != null) { _menuFontReg.Dispose(); _menuFontReg = null; }
            Application.Exit();
        }
    }
}
