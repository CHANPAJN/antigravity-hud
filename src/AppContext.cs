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
        private readonly ModernContextMenuForm _contextMenu;
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

        public TaskbarAppContext()
        {
            float dpiScale = NativeMethods.GetSystemDpiScale();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _agRoot = Path.Combine(userProfile, ".gemini", "antigravity");

            _config = ConfigStore.Load();
            I18n.SetLanguage(_config.Language);
            ThemeManager.SetMode(_config.ThemeMode);

            _engine = new LocalTelemetryEngine(userProfile, _config.AutoMemoryGuard);

            _contextMenu = new ModernContextMenuForm(
                dpiScale,
                _config,
                onSetBarStyle: SetBarStyle,
                onSetThemeMode: SetThemeMode,
                onToggleLockPos: () =>
                {
                    _config.PositionLocked = !_config.PositionLocked;
                    ConfigStore.Save(_config);
                    if (_popup != null && _popup.Visible) _popup.Invalidate();
                },
                onResetPos: ResetBarPosition,
                onToggleLang: ToggleLanguageAndRefresh,
                onToggleAutostart: () =>
                {
                    ConfigStore.SetAutostart(!ConfigStore.IsAutostartEnabled(), Application.ExecutablePath);
                },
                onOpenLogs: OpenBrainFolder,
                onExitApp: ExitCleanly,
                getAutostart: ConfigStore.IsAutostartEnabled
            );

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
                ShowModernContextMenu);
            _bar.Show();

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

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

        private void ShowModernContextMenu(Point screenPt)
        {
            if (_popup != null && _popup.Visible) _popup.Hide();
            if (_contextMenu != null && !_contextMenu.IsDisposed)
            {
                _contextMenu.ShowNearPoint(screenPt, _bar.GetActualScreenBounds());
            }
        }

        private void OnBarPositionChanged()
        {
            ConfigStore.Save(_config);
            if (_contextMenu != null && _contextMenu.Visible) _contextMenu.Invalidate();
            if (_popup != null && _popup.Visible) _popup.Invalidate();
        }

        private void SetBarStyle(string style)
        {
            _config.BarStyle = style;
            ConfigStore.Save(_config);
            if (_bar != null && !_bar.IsDisposed)
            {
                _bar.RenderLayered(true);
                _bar.EnsureDockedAndZOrder();
            }
        }

        private void SetThemeMode(string themeMode)
        {
            _config.ThemeMode = themeMode;
            ConfigStore.Save(_config);
            ThemeManager.SetMode(themeMode);
            ApplyThemeAcrossWindows();
        }

        private void ApplyThemeAcrossWindows()
        {
            if (_bar != null && !_bar.IsDisposed) _bar.RenderLayered(true);
            if (_popup != null && !_popup.IsDisposed) _popup.RefreshThemeChrome();
            if (_contextMenu != null && !_contextMenu.IsDisposed) _contextMenu.RefreshThemeChrome();
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
            {
                ThemeManager.RefreshSystemTheme(true);
                if (_bar != null && !_bar.IsDisposed && _bar.IsHandleCreated)
                {
                    try { _bar.BeginInvoke(new Action(ApplyThemeAcrossWindows)); } catch { }
                }
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
            RefreshAll(false);
            if (_bar != null && !_bar.IsDisposed) _bar.RenderLayered(true);
            if (_popup != null && !_popup.IsDisposed) _popup.Invalidate();
            if (_contextMenu != null && !_contextMenu.IsDisposed) _contextMenu.Invalidate();
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
            if (_contextMenu != null && _contextMenu.Visible) _contextMenu.Hide();
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
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _timer.Stop();
            _winTitleWatchTimer.Stop();
            _outsideClickTimer.Stop();
            if (_winEventHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }
            if (_contextMenu != null && !_contextMenu.IsDisposed) _contextMenu.Close();
            if (_popup != null && !_popup.IsDisposed) _popup.Close();
            if (_bar != null && !_bar.IsDisposed) _bar.Close();
            Application.Exit();
        }
    }
}
