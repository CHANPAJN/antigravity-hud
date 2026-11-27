using System;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace AntigravityHud
{
    internal sealed class UserConfig
    {
        public string AnchorMode = "taskbar-auto"; // "taskbar-auto" | "custom"
        public int CustomX;
        public int CustomY;
        public string HorizontalAlign = "right"; // "right" | "left"
        public string MonitorDeviceName = "";
        public bool PositionLocked = false;
        public string Language = "auto"; // "auto" | "zh" | "en"
        public bool AutoMemoryGuard = false; // Opt-in only
        public string ScaleMode = "auto"; // "auto" | "256k" | "1m"
        public string BarStyle = "compact"; // "compact" | "labeled" | "minimal"
    }

    internal static class ConfigStore
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "AntigravityHUD";

        public static string ConfigDir
        {
            get
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localApp, "AntigravityHUD");
            }
        }

        public static string ConfigFilePath
        {
            get { return Path.Combine(ConfigDir, "config.json"); }
        }

        public static UserConfig Load()
        {
            var cfg = new UserConfig();
            try
            {
                string fp = ConfigFilePath;
                if (!File.Exists(fp)) return cfg;
                string raw = File.ReadAllText(fp, Encoding.UTF8);
                if (string.IsNullOrEmpty(raw)) return cfg;

                string anchor = JsonFastHelper.ExtractString(raw, "anchorMode", 0);
                if (anchor == "custom" || anchor == "taskbar-auto") cfg.AnchorMode = anchor;

                long cx = JsonFastHelper.ExtractLong(raw, "customX");
                long cy = JsonFastHelper.ExtractLong(raw, "customY");
                cfg.CustomX = (int)cx;
                cfg.CustomY = (int)cy;

                string align = JsonFastHelper.ExtractString(raw, "horizontalAlign", 0);
                if (align == "left" || align == "right") cfg.HorizontalAlign = align;

                string mon = JsonFastHelper.ExtractString(raw, "monitorDeviceName", 0);
                if (!string.IsNullOrEmpty(mon)) cfg.MonitorDeviceName = mon;

                cfg.PositionLocked = ExtractBool(raw, "positionLocked", false);

                string lang = JsonFastHelper.ExtractString(raw, "language", 0);
                if (lang == "auto" || lang == "zh" || lang == "en") cfg.Language = lang;

                cfg.AutoMemoryGuard = ExtractBool(raw, "autoMemoryGuard", false);

                string scale = JsonFastHelper.ExtractString(raw, "scaleMode", 0);
                if (scale == "auto" || scale == "256k" || scale == "1m") cfg.ScaleMode = scale;

                string barStyle = JsonFastHelper.ExtractString(raw, "barStyle", 0);
                if (barStyle == "compact" || barStyle == "labeled" || barStyle == "minimal") cfg.BarStyle = barStyle;
            }
            catch { }
            return cfg;
        }

        public static void Save(UserConfig cfg)
        {
            if (cfg == null) return;
            try
            {
                string dir = ConfigDir;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder(320);
                sb.AppendLine("{");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"anchorMode\": \"{0}\",\n", EscapeJson(cfg.AnchorMode ?? "taskbar-auto"));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"customX\": {0},\n", cfg.CustomX);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"customY\": {0},\n", cfg.CustomY);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"horizontalAlign\": \"{0}\",\n", EscapeJson(cfg.HorizontalAlign ?? "right"));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"monitorDeviceName\": \"{0}\",\n", EscapeJson(cfg.MonitorDeviceName ?? ""));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"positionLocked\": {0},\n", cfg.PositionLocked ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"language\": \"{0}\",\n", EscapeJson(cfg.Language ?? "auto"));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"autoMemoryGuard\": {0},\n", cfg.AutoMemoryGuard ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"scaleMode\": \"{0}\",\n", EscapeJson(cfg.ScaleMode ?? "auto"));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"barStyle\": \"{0}\"\n", EscapeJson(cfg.BarStyle ?? "compact"));
                sb.AppendLine("}");

                string fp = ConfigFilePath;
                string tmp = fp + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(fp))
                {
                    File.Delete(fp);
                }
                File.Move(tmp, fp);
            }
            catch { }
        }

        public static bool IsAutostartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    object val = key.GetValue(RunValueName);
                    return val != null && !string.IsNullOrEmpty(val.ToString());
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetAutostart(bool enable, string exePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return false;
                    if (enable)
                    {
                        if (string.IsNullOrEmpty(exePath)) return false;
                        key.SetValue(RunValueName, "\"" + exePath + "\"", RegistryValueKind.String);
                    }
                    else
                    {
                        if (key.GetValue(RunValueName) != null)
                            key.DeleteValue(RunValueName, false);
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool ExtractBool(string raw, string key, bool fallback)
        {
            string needle = "\"" + key + "\"";
            int idx = raw.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return fallback;
            int colon = raw.IndexOf(':', idx + needle.Length);
            if (colon < 0) return fallback;
            int p = colon + 1;
            while (p < raw.Length && (raw[p] == ' ' || raw[p] == '\t' || raw[p] == '\r' || raw[p] == '\n')) p++;
            if (p + 4 <= raw.Length && string.Compare(raw, p, "true", 0, 4, StringComparison.OrdinalIgnoreCase) == 0) return true;
            if (p + 5 <= raw.Length && string.Compare(raw, p, "false", 0, 5, StringComparison.OrdinalIgnoreCase) == 0) return false;
            return fallback;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "");
        }
    }
}
