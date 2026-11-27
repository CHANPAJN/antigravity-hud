using System;
using System.Drawing;
using Microsoft.Win32;

namespace AntigravityHud
{
    internal sealed class ThemePalette
    {
        public bool IsDark;

        // Core surfaces
        public Color WinBg;
        public Color WinBorder;
        public Color CardBg;
        public Color CardBorder;
        public Color SubtleFill;
        public Color HoverFill;
        public Color Divider;
        public Color TrackBg;
        public Color BarEmptyTrack;

        // Typography
        public Color TextMain;
        public Color TextSub;
        public Color TextMuted;

        // Semantic accents
        public Color Accent;
        public Color AccentSoft;
        public Color Ok;
        public Color OkText;
        public Color OkSoft;
        public Color OkBorder;
        public Color Warn;
        public Color WarnText;
        public Color WarnSoft;
        public Color WarnBorder;
        public Color Danger;
        public Color DangerText;
        public Color DangerSoft;

        // Context category bars
        public Color CatSys;
        public Color CatTools;
        public Color CatUser;
        public Color CatResp;
        public Color CatRes;

        // Model badges & IQ chips
        public Color ToneCarbonBg, ToneCarbonFg;
        public Color ToneNeonBg, ToneNeonFg;
        public Color ToneArgonBg, ToneArgonFg;
        public Color ToneFastBg, ToneFastFg;
        public Color ToneMutedBg, ToneMutedFg;
        public Color IqOkBg, IqOkFg;
        public Color IqIdleBg, IqIdleFg;

        // Floating capsule pill
        public Color PillBg;
        public Color PillBgHover;
        public Color PillBgOpen;
        public Color PillBgDrag;
        public Color PillBorderIdle;
        public Color PillBorderRun;
        public Color PillBorderActive;
        public Color PillBorderDrag;

        // Right-click menu
        public Color ToggleTrackOff;
        public Color ToggleKnob;
        public Color ChartBarIdle;
        public Color ChartBarEmptyBg;
        public Color RowHighlight;
        public Color PinRowBg;

        public static readonly ThemePalette Light = new ThemePalette
        {
            IsDark = false,
            WinBg = Color.FromArgb(246, 247, 249),
            WinBorder = Color.FromArgb(216, 221, 229),
            CardBg = Color.FromArgb(255, 255, 255),
            CardBorder = Color.FromArgb(228, 231, 236),
            SubtleFill = Color.FromArgb(241, 244, 248),
            HoverFill = Color.FromArgb(234, 240, 250),
            Divider = Color.FromArgb(216, 222, 230),
            TrackBg = Color.FromArgb(226, 232, 240),
            BarEmptyTrack = Color.FromArgb(229, 233, 240),

            TextMain = Color.FromArgb(24, 24, 27),
            TextSub = Color.FromArgb(113, 113, 122),
            TextMuted = Color.FromArgb(161, 161, 170),

            Accent = Color.FromArgb(37, 99, 235),
            AccentSoft = Color.FromArgb(232, 240, 254),
            Ok = Color.FromArgb(16, 185, 129),
            OkText = Color.FromArgb(5, 150, 105),
            OkSoft = Color.FromArgb(218, 248, 236),
            OkBorder = Color.FromArgb(167, 243, 208),
            Warn = Color.FromArgb(245, 158, 11),
            WarnText = Color.FromArgb(180, 83, 9),
            WarnSoft = Color.FromArgb(254, 243, 199),
            WarnBorder = Color.FromArgb(252, 211, 77),
            Danger = Color.FromArgb(239, 68, 68),
            DangerText = Color.FromArgb(220, 38, 38),
            DangerSoft = Color.FromArgb(254, 226, 226),

            CatSys = Color.FromArgb(71, 85, 105),
            CatTools = Color.FromArgb(100, 116, 139),
            CatUser = Color.FromArgb(217, 119, 6),
            CatResp = Color.FromArgb(13, 148, 136),
            CatRes = Color.FromArgb(37, 99, 235),

            ToneCarbonBg = Color.FromArgb(226, 232, 240),
            ToneCarbonFg = Color.FromArgb(15, 23, 42),
            ToneNeonBg = Color.FromArgb(204, 251, 241),
            ToneNeonFg = Color.FromArgb(13, 148, 136),
            ToneArgonBg = Color.FromArgb(232, 240, 254),
            ToneArgonFg = Color.FromArgb(37, 99, 235),
            ToneFastBg = Color.FromArgb(218, 248, 236),
            ToneFastFg = Color.FromArgb(5, 150, 105),
            ToneMutedBg = Color.FromArgb(234, 237, 242),
            ToneMutedFg = Color.FromArgb(100, 106, 118),
            IqOkBg = Color.FromArgb(224, 242, 254),
            IqOkFg = Color.FromArgb(2, 132, 199),
            IqIdleBg = Color.FromArgb(228, 232, 238),
            IqIdleFg = Color.FromArgb(113, 113, 122),

            PillBg = Color.FromArgb(246, 255, 255, 255),
            PillBgHover = Color.FromArgb(252, 255, 255, 255),
            PillBgOpen = Color.FromArgb(250, 240, 247, 255),
            PillBgDrag = Color.FromArgb(252, 239, 246, 255),
            PillBorderIdle = Color.FromArgb(215, 198, 208, 220),
            PillBorderRun = Color.FromArgb(220, 16, 185, 129),
            PillBorderActive = Color.FromArgb(245, 59, 130, 246),
            PillBorderDrag = Color.FromArgb(255, 37, 99, 235),

            ToggleTrackOff = Color.FromArgb(203, 213, 225),
            ToggleKnob = Color.FromArgb(255, 255, 255),
            ChartBarIdle = Color.FromArgb(90, 101, 118),
            ChartBarEmptyBg = Color.FromArgb(232, 236, 242),
            RowHighlight = Color.FromArgb(245, 248, 255),
            PinRowBg = Color.FromArgb(240, 245, 255)
        };

        // Neutral Graphite/Zinc Dark Palette (matches Win11 dark Mica — zero dark-navy/neon-cyan AI slop)
        public static readonly ThemePalette Dark = new ThemePalette
        {
            IsDark = true,
            WinBg = Color.FromArgb(24, 24, 27),
            WinBorder = Color.FromArgb(56, 56, 62),
            CardBg = Color.FromArgb(34, 34, 38),
            CardBorder = Color.FromArgb(52, 52, 58),
            SubtleFill = Color.FromArgb(42, 42, 47),
            HoverFill = Color.FromArgb(52, 54, 62),
            Divider = Color.FromArgb(62, 62, 70),
            TrackBg = Color.FromArgb(52, 52, 59),
            BarEmptyTrack = Color.FromArgb(50, 50, 57),

            TextMain = Color.FromArgb(244, 244, 245),
            TextSub = Color.FromArgb(161, 161, 170),
            TextMuted = Color.FromArgb(113, 113, 122),

            Accent = Color.FromArgb(96, 165, 250),
            AccentSoft = Color.FromArgb(32, 46, 72),
            Ok = Color.FromArgb(52, 211, 153),
            OkText = Color.FromArgb(52, 211, 153),
            OkSoft = Color.FromArgb(24, 54, 46),
            OkBorder = Color.FromArgb(38, 92, 76),
            Warn = Color.FromArgb(251, 191, 36),
            WarnText = Color.FromArgb(251, 191, 36),
            WarnSoft = Color.FromArgb(62, 47, 22),
            WarnBorder = Color.FromArgb(124, 88, 28),
            Danger = Color.FromArgb(248, 113, 113),
            DangerText = Color.FromArgb(248, 113, 113),
            DangerSoft = Color.FromArgb(68, 30, 34),

            CatSys = Color.FromArgb(148, 163, 184),
            CatTools = Color.FromArgb(161, 161, 170),
            CatUser = Color.FromArgb(245, 158, 11),
            CatResp = Color.FromArgb(45, 212, 191),
            CatRes = Color.FromArgb(96, 165, 250),

            ToneCarbonBg = Color.FromArgb(52, 55, 64),
            ToneCarbonFg = Color.FromArgb(228, 232, 240),
            ToneNeonBg = Color.FromArgb(22, 62, 58),
            ToneNeonFg = Color.FromArgb(94, 234, 212),
            ToneArgonBg = Color.FromArgb(32, 48, 76),
            ToneArgonFg = Color.FromArgb(147, 197, 253),
            ToneFastBg = Color.FromArgb(24, 58, 46),
            ToneFastFg = Color.FromArgb(110, 231, 183),
            ToneMutedBg = Color.FromArgb(48, 48, 54),
            ToneMutedFg = Color.FromArgb(161, 161, 170),
            IqOkBg = Color.FromArgb(28, 52, 76),
            IqOkFg = Color.FromArgb(125, 211, 252),
            IqIdleBg = Color.FromArgb(48, 48, 54),
            IqIdleFg = Color.FromArgb(161, 161, 170),

            PillBg = Color.FromArgb(240, 28, 28, 32),
            PillBgHover = Color.FromArgb(248, 36, 36, 41),
            PillBgOpen = Color.FromArgb(248, 34, 42, 56),
            PillBgDrag = Color.FromArgb(250, 32, 44, 66),
            PillBorderIdle = Color.FromArgb(215, 68, 68, 76),
            PillBorderRun = Color.FromArgb(220, 52, 211, 153),
            PillBorderActive = Color.FromArgb(245, 96, 165, 250),
            PillBorderDrag = Color.FromArgb(255, 96, 165, 250),

            ToggleTrackOff = Color.FromArgb(72, 72, 80),
            ToggleKnob = Color.FromArgb(244, 244, 245),
            ChartBarIdle = Color.FromArgb(124, 134, 150),
            ChartBarEmptyBg = Color.FromArgb(46, 46, 52),
            RowHighlight = Color.FromArgb(42, 46, 56),
            PinRowBg = Color.FromArgb(38, 44, 58)
        };
    }

    internal static class ThemeManager
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        private static string _mode = "auto"; // "auto" | "light" | "dark"
        private static string _previewOverride = ""; // "" | "light" | "dark"
        private static bool _sysTaskbarDark;
        private static bool _sysAppsDark;
        private static DateTime _lastPollUtc = DateTime.MinValue;

        public static string Mode
        {
            get { return _mode; }
        }

        public static void SetMode(string mode)
        {
            if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase)) _mode = "light";
            else if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase)) _mode = "dark";
            else _mode = "auto";
            RefreshSystemTheme(true);
        }

        public static void SetPreviewOverride(string theme)
        {
            if (string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase)) _previewOverride = "dark";
            else if (string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase)) _previewOverride = "light";
            else _previewOverride = "";
        }

        public static void RefreshSystemTheme(bool force = false)
        {
            DateTime now = DateTime.UtcNow;
            if (!force && _lastPollUtc != DateTime.MinValue && (now - _lastPollUtc).TotalSeconds < 10.0)
                return;
            _lastPollUtc = now;

            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false))
                {
                    if (k != null)
                    {
                        object sysVal = k.GetValue("SystemUsesLightTheme");
                        object appVal = k.GetValue("AppsUseLightTheme");
                        _sysTaskbarDark = sysVal != null && Convert.ToInt32(sysVal) == 0;
                        _sysAppsDark = appVal != null && Convert.ToInt32(appVal) == 0;
                    }
                }
            }
            catch { }
        }

        public static bool IsBarDark
        {
            get
            {
                if (_previewOverride == "dark") return true;
                if (_previewOverride == "light") return false;
                if (_mode == "dark") return true;
                if (_mode == "light") return false;
                RefreshSystemTheme(false);
                return _sysTaskbarDark;
            }
        }

        public static bool IsAppDark
        {
            get
            {
                if (_previewOverride == "dark") return true;
                if (_previewOverride == "light") return false;
                if (_mode == "dark") return true;
                if (_mode == "light") return false;
                RefreshSystemTheme(false);
                return _sysAppsDark;
            }
        }

        public static ThemePalette ForBar()
        {
            return IsBarDark ? ThemePalette.Dark : ThemePalette.Light;
        }

        public static ThemePalette ForApp()
        {
            return IsAppDark ? ThemePalette.Dark : ThemePalette.Light;
        }

        public static void ApplyDwmTheme(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int v = dark ? 1 : 0;
                NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref v, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
            }
            catch { }
        }
    }
}
