using System;
using System.Collections.Generic;
using System.Globalization;

namespace AntigravityHud
{
    internal static class I18n
    {
        public const double UsdToCny = 7.10;
        private static string _langPref = "auto";
        private static bool _isZh = DetectSystemZh();

        private static readonly Dictionary<string, string> ToolZhMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "read_file", "读取文件" },
            { "view_file", "查看文件" },
            { "write_file", "写入文件" },
            { "write_to_file", "写入文件" },
            { "edit_file", "编辑代码" },
            { "replace_file_content", "编辑代码" },
            { "multi_replace_file_content", "编辑代码" },
            { "run_command", "终端命令" },
            { "command_status", "终端命令" },
            { "send_command_input", "终端命令" },
            { "search_web", "网页搜索" },
            { "read_url_content", "读取网页" },
            { "invoke_subagent", "子智能体" },
            { "define_subagent", "定义子代理" },
            { "send_message", "代理通信" },
            { "ask_question", "提问卡片" },
            { "call_mcp_tool", "MCP工具" },
            { "search_marketplace", "插件检索" },
            { "list_plugin_accounts", "插件账号" },
            { "list_resources", "MCP资源" },
            { "read_resource", "读取资源" },
            { "schedule", "定时任务" },
            { "manage_task", "任务管理" },
            { "manage_subagents", "子代理管理" },
            { "swarm", "蜂群调度" },
            { "run_workflow", "工作流" },
            { "read_board", "读取看板" },
            { "post_board", "发布看板" },
            { "list_dir", "浏览目录" },
            { "grep_search", "全文检索" },
            { "find_by_name", "查找文件" },
            { "view_file_outline", "文件大纲" },
            { "view_code_item", "查看代码" }
        };

        private static readonly Dictionary<string, string> ToolEnMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "read_file", "Read File" },
            { "view_file", "View File" },
            { "write_file", "Write File" },
            { "write_to_file", "Write File" },
            { "edit_file", "Edit Code" },
            { "replace_file_content", "Edit Code" },
            { "multi_replace_file_content", "Edit Code" },
            { "run_command", "Shell Cmd" },
            { "command_status", "Shell Cmd" },
            { "send_command_input", "Shell Cmd" },
            { "search_web", "Web Search" },
            { "read_url_content", "Read URL" },
            { "invoke_subagent", "Subagent" },
            { "define_subagent", "Define Agent" },
            { "send_message", "Agent Msg" },
            { "ask_question", "Ask User" },
            { "call_mcp_tool", "MCP Tool" },
            { "search_marketplace", "Marketplace" },
            { "list_plugin_accounts", "Accounts" },
            { "list_resources", "MCP Res" },
            { "read_resource", "Read Res" },
            { "schedule", "Schedule" },
            { "manage_task", "Tasks" },
            { "manage_subagents", "Subagents" },
            { "swarm", "Swarm" },
            { "run_workflow", "Workflow" },
            { "read_board", "Read Board" },
            { "post_board", "Post Board" },
            { "list_dir", "List Dir" },
            { "grep_search", "Grep" },
            { "find_by_name", "Find File" },
            { "view_file_outline", "Outline" },
            { "view_code_item", "Code Item" }
        };

        public static bool IsZh
        {
            get { return _isZh; }
        }

        public static string LanguagePreference
        {
            get { return _langPref; }
        }

        public static string UiFontFamily
        {
            get { return _isZh ? "Microsoft YaHei UI" : "Segoe UI"; }
        }

        public static bool DetectSystemZh()
        {
            try
            {
                string name = CultureInfo.CurrentUICulture.Name ?? "";
                return name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static void SetLanguage(string pref)
        {
            if (string.Equals(pref, "zh", StringComparison.OrdinalIgnoreCase))
            {
                _langPref = "zh";
                _isZh = true;
            }
            else if (string.Equals(pref, "en", StringComparison.OrdinalIgnoreCase))
            {
                _langPref = "en";
                _isZh = false;
            }
            else
            {
                _langPref = "auto";
                _isZh = DetectSystemZh();
            }
        }

        public static string ToggleLanguage()
        {
            SetLanguage(_isZh ? "en" : "zh");
            return _langPref;
        }

        public static string T(string zh, string en)
        {
            return _isZh ? zh : en;
        }

        public static string TF(string zhFmt, string enFmt, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, _isZh ? zhFmt : enFmt, args);
        }

        public static string FmtCompact(long n)
        {
            if (n <= 0) return "0";
            if (n >= 1000000000L) return (n / 1000000000.0).ToString("F2", CultureInfo.InvariantCulture) + "B";
            if (n >= 10000000L) return (n / 1000000.0).ToString("F0", CultureInfo.InvariantCulture) + "M";
            if (n >= 1000000L) return (n / 1000000.0).ToString("F2", CultureInfo.InvariantCulture) + "M";
            if (n >= 100000L) return Math.Round(n / 1000.0).ToString("F0", CultureInfo.InvariantCulture) + "k";
            if (n >= 10000L) return (n / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + "k";
            if (n >= 1000L) return (n / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + "k";
            return n.ToString(CultureInfo.InvariantCulture);
        }

        public static string FmtBigTokens(long n)
        {
            if (n <= 0) return "0";
            if (_isZh)
            {
                if (n >= 100000000L) return (n / 100000000.0).ToString("F2", CultureInfo.InvariantCulture) + "亿";
                if (n >= 10000000L) return (n / 10000.0).ToString("F0", CultureInfo.InvariantCulture) + "万";
                if (n >= 10000L) return (n / 10000.0).ToString("F1", CultureInfo.InvariantCulture) + "万";
                return n.ToString("N0", CultureInfo.InvariantCulture);
            }
            else
            {
                if (n >= 1000000000L) return (n / 1000000000.0).ToString("F2", CultureInfo.InvariantCulture) + "B";
                if (n >= 100000000L) return (n / 1000000.0).ToString("F0", CultureInfo.InvariantCulture) + "M";
                if (n >= 1000000L) return (n / 1000000.0).ToString("F1", CultureInfo.InvariantCulture) + "M";
                if (n >= 1000L) return (n / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + "K";
                return n.ToString("N0", CultureInfo.InvariantCulture);
            }
        }

        public static string FmtChartBarTokens(long n)
        {
            if (n <= 0) return "0";
            if (_isZh)
            {
                if (n >= 100000000L) return (n / 100000000.0).ToString("F1", CultureInfo.InvariantCulture) + "亿";
                if (n >= 10000000L) return (n / 10000.0).ToString("F0", CultureInfo.InvariantCulture) + "万";
                if (n >= 10000L) return (n / 10000.0).ToString("F0", CultureInfo.InvariantCulture) + "万";
                return FmtCompact(n);
            }
            else
            {
                if (n >= 1000000000L) return (n / 1000000000.0).ToString("F1", CultureInfo.InvariantCulture) + "B";
                if (n >= 1000000L) return (n / 1000000.0).ToString("F0", CultureInfo.InvariantCulture) + "M";
                if (n >= 1000L) return (n / 1000.0).ToString("F0", CultureInfo.InvariantCulture) + "K";
                return n.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static string FmtCost(double usd, int convCount)
        {
            string u = usd >= 100 ? Math.Round(usd).ToString("F0", CultureInfo.InvariantCulture) : usd.ToString("F1", CultureInfo.InvariantCulture);
            if (_isZh)
            {
                double cny = usd * UsdToCny;
                string c = cny >= 100 ? Math.Round(cny).ToString("F0", CultureInfo.InvariantCulture) : cny.ToString("F1", CultureInfo.InvariantCulture);
                if (convCount > 0)
                    return string.Format(CultureInfo.InvariantCulture, "${0} · \u00A5{1} · {2}窗", u, c, convCount);
                return string.Format(CultureInfo.InvariantCulture, "约 ${0} · \u00A5{1}", u, c);
            }
            else
            {
                if (convCount > 0)
                    return string.Format(CultureInfo.InvariantCulture, "~${0} · {1} {2}", u, convCount, convCount == 1 ? "chat" : "chats");
                return string.Format(CultureInfo.InvariantCulture, "~${0}", u);
            }
        }

        public static string FmtInlineCost(double usd)
        {
            if (_isZh)
            {
                double cny = usd * UsdToCny;
                return string.Format(CultureInfo.InvariantCulture, "\u00A5{0:F1}", cny);
            }
            return string.Format(CultureInfo.InvariantCulture, "${0:F2}", usd);
        }

        public static string FmtSurfaceLabel(string dirKey)
        {
            if (string.Equals(dirKey, "antigravity-insiders", StringComparison.OrdinalIgnoreCase))
                return _isZh ? "预览" : "Insiders";
            if (string.Equals(dirKey, "antigravity-cli", StringComparison.OrdinalIgnoreCase))
                return "CLI";
            return _isZh ? "主端" : "Main";
        }

        public static string GetToolLabel(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return _isZh ? "工具" : "Tool";
            string mapped;
            if (_isZh)
            {
                if (ToolZhMap.TryGetValue(rawName, out mapped)) return mapped;
                if (rawName.StartsWith("mcp_gemini-api", StringComparison.OrdinalIgnoreCase)) return "Gemini文档";
                if (rawName.StartsWith("mcp_chrome", StringComparison.OrdinalIgnoreCase)) return "浏览器扩展";
                if (rawName.StartsWith("mcp_", StringComparison.OrdinalIgnoreCase)) return "MCP工具";
                return rawName.Length > 14 ? rawName.Substring(0, 14) : rawName;
            }
            else
            {
                if (ToolEnMap.TryGetValue(rawName, out mapped)) return mapped;
                if (rawName.StartsWith("mcp_gemini-api", StringComparison.OrdinalIgnoreCase)) return "Gemini Docs";
                if (rawName.StartsWith("mcp_chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome MCP";
                if (rawName.StartsWith("mcp_", StringComparison.OrdinalIgnoreCase)) return "MCP Tool";
                return rawName.Length > 14 ? rawName.Substring(0, 14) : rawName;
            }
        }
    }
}
