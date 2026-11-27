using System;
using System.Collections.Generic;

namespace AntigravityHud
{
    /// <summary>
    /// Generates realistic, 100% privacy-sanitized sample telemetry for --demo previews,
    /// visual QA checks, and documentation screenshots (no real user logs or titles).
    /// </summary>
    internal static class DemoDataFactory
    {
        public static DashboardData BuildDemoDashboard(bool autoMemGuard = false)
        {
            DateTime now = DateTime.Now;
            string todayKey = now.ToString("yyyy-MM-dd");
            string convTitle = I18n.T("重构 Win32 悬浮窗拖拽与磁吸停靠", "Refactor Win32 Floating Bar Drag & Snap");
            string surface = I18n.FmtSurfaceLabel("antigravity-insiders");

            var turns = new List<TurnItem>
            {
                new TurnItem
                {
                    TurnNo = 21,
                    StepIndex = 84,
                    Model = "Gemini 4 Argon (High)",
                    ShortTag = "Argon·H",
                    Tone = "argon",
                    InTok = 14820,
                    CacheTok = 128400,
                    PromptTotal = 143220,
                    OutTok = 1620,
                    TotalUsed = 144840,
                    ThinkChars = 1480,
                    EstThinkTok = 410,
                    CacheHitPct = 89.7,
                    DurationSec = 11.4,
                    ToolExecSec = 1.8,
                    E2ESec = 13.2,
                    Tps = 38.2,
                    ToolSummary = I18n.GetToolLabel("read_file") + " · " + I18n.GetToolLabel("grep_search")
                },
                new TurnItem
                {
                    TurnNo = 22,
                    StepIndex = 96,
                    Model = "Gemini 4 Carbon (High)",
                    ShortTag = "Carbon·H",
                    Tone = "carbon",
                    InTok = 21400,
                    CacheTok = 143200,
                    PromptTotal = 164600,
                    OutTok = 2380,
                    TotalUsed = 166980,
                    ThinkChars = 2820,
                    EstThinkTok = 780,
                    CacheHitPct = 87.0,
                    DurationSec = 16.2,
                    ToolExecSec = 2.4,
                    E2ESec = 18.6,
                    Tps = 27.6,
                    ModelSwitchedCold = true,
                    IsCacheDropWarn = true,
                    SwitchedFromTag = "Argon·H",
                    ColdCacheReason = I18n.TF("换模重热 ({0}→{1}) · 载入 {2}", "Model warm-up ({0} -> {1}) · {2}", "Argon·H", "Carbon·H", "165k"),
                    ToolSummary = I18n.GetToolLabel("edit_file") + " · " + I18n.GetToolLabel("run_command")
                },
                new TurnItem
                {
                    TurnNo = 23,
                    StepIndex = 108,
                    Model = "Gemini 4 Carbon (High)",
                    ShortTag = "Carbon·H",
                    Tone = "carbon",
                    InTok = 6200,
                    CacheTok = 189400,
                    PromptTotal = 195600,
                    OutTok = 1940,
                    TotalUsed = 197540,
                    ThinkChars = 2310,
                    EstThinkTok = 640,
                    CacheHitPct = 96.8,
                    DurationSec = 13.8,
                    ToolExecSec = 1.2,
                    E2ESec = 15.0,
                    Tps = 29.4,
                    ToolSummary = I18n.GetToolLabel("edit_file")
                },
                new TurnItem
                {
                    TurnNo = 24,
                    StepIndex = 119,
                    Model = "Gemini 4 Carbon (High)",
                    ShortTag = "Carbon·H",
                    Tone = "carbon",
                    InTok = 2180,
                    CacheTok = 231400,
                    PromptTotal = 233580,
                    OutTok = 1420,
                    TotalUsed = 235000,
                    ThinkChars = 1890,
                    EstThinkTok = 520,
                    CacheHitPct = 99.1,
                    DurationSec = 14.5,
                    ToolExecSec = 0.9,
                    E2ESec = 15.4,
                    Tps = 26.8,
                    ToolSummary = I18n.GetToolLabel("run_command")
                }
            };

            var catBreakdowns = new Dictionary<string, List<BreakdownRowItem>>(StringComparer.OrdinalIgnoreCase);
            catBreakdowns["system"] = new List<BreakdownRowItem>
            {
                new BreakdownRowItem { Label = I18n.T("内置指令与原生工具底座", "Built-in System & Core Tools"), SubNote = I18n.T("宿主提示词与核心工具声明", "Host prompt and core tool schemas"), Tokens = 24800, SharePct = 50.1 },
                new BreakdownRowItem { Label = I18n.T("全局工程规则 (Rules)", "Global Rules"), SubNote = I18n.TF("{0} 份常驻规则文件", "{0} active rule files", 3), Tokens = 11200, SharePct = 22.6 },
                new BreakdownRowItem { Label = I18n.T("技能路由目录 (Skills)", "Skills Catalog"), SubNote = I18n.TF("{0} 项已挂载技能摘要", "{0} loaded skill summaries", 12), Tokens = 6800, SharePct = 13.7 },
                new BreakdownRowItem { Label = I18n.T("MCP 服务与工具索引", "MCP Servers & Schemas"), SubNote = I18n.TF("{0} 个已连接 MCP 服务", "{0} connected MCP servers", 2), Tokens = 6700, SharePct = 13.6 }
            };
            catBreakdowns["results"] = new List<BreakdownRowItem>
            {
                new BreakdownRowItem { Label = I18n.GetToolLabel("read_file"), SubNote = I18n.TF("累计触发 {0} 次 · 占总窗 {1:F1}%", "{0} calls · {1:F1}% of window", 34, 21.8), Tokens = 51200, SharePct = 44.8 },
                new BreakdownRowItem { Label = I18n.GetToolLabel("run_command"), SubNote = I18n.TF("累计触发 {0} 次 · 占总窗 {1:F1}%", "{0} calls · {1:F1}% of window", 19, 14.5), Tokens = 34100, SharePct = 29.9 },
                new BreakdownRowItem { Label = I18n.GetToolLabel("edit_file"), SubNote = I18n.TF("累计触发 {0} 次 · 占总窗 {1:F1}%", "{0} calls · {1:F1}% of window", 15, 7.9), Tokens = 18600, SharePct = 16.3 },
                new BreakdownRowItem { Label = I18n.GetToolLabel("grep_search"), SubNote = I18n.TF("累计触发 {0} 次 · 占总窗 {1:F1}%", "{0} calls · {1:F1}% of window", 9, 4.4), Tokens = 10300, SharePct = 9.0 }
            };
            catBreakdowns["assistant"] = new List<BreakdownRowItem>
            {
                new BreakdownRowItem { Label = I18n.T("内部思考链推演 (Thinking)", "Internal Reasoning (Thinking)"), SubNote = I18n.TF("占总输出 {0:F0}% · 保障推理深度", "{0:F0}% of output · reasoning depth", 39), Tokens = 24500, SharePct = 39.0 },
                new BreakdownRowItem { Label = I18n.T("可见正文与工具调用参数", "Visible Replies & Tool Args"), SubNote = I18n.TF("共 {0} 轮 · 单轮峰值 #{1} ({2})", "{0} turns · peak #{1} ({2})", 24, 96, "2.4k"), Tokens = 38300, SharePct = 61.0 }
            };
            catBreakdowns["user"] = new List<BreakdownRowItem>
            {
                new BreakdownRowItem { Label = I18n.T("用户提问与内联文本输入", "User Prompts & Inline Input"), SubNote = I18n.TF("共 {0} 次发送 · 平均每次 {1}", "{0} prompts · avg {1}", 14, "607"), Tokens = 8500, SharePct = 100.0 }
            };

            var hud = new HudSnapshot
            {
                Key = "antigravity-insiders:demo001a",
                Cid = "demo001a-4b9c-48d0-91a2-7f82c10e3a51",
                ShortId = "demo001a",
                Surface = surface,
                Title = convTitle,
                UpdatedAt = now.AddSeconds(-18),
                TotalSteps = 119,
                TotalTurns = 24,
                IsRunning = false,
                ActiveElapsedSec = 0,
                ActivePhaseLabel = I18n.T("思考中", "Thinking"),
                Compacted = true,
                CompactPreTokens = 294000,
                CompactSummaryTokens = 68000,
                TurnsSinceCompact = 8,
                Latest = turns[turns.Count - 1],
                RecentTurns = turns,
                SysTokens = 49500,
                SysRulesTokens = 11200,
                SysSkillsTokens = 6800,
                SysCoreTokens = 24800,
                SysMcpDeclTokens = 6700,
                SysMcpCoreTokens = 31500,
                SysSummaryTokens = 0,
                RulesFileCount = 3,
                SkillsEntryCount = 12,
                McpServerCount = 2,
                UserTokens = 8500,
                RespTokens = 62800,
                ResultsTokens = 114200,
                CacheWarnTurns = 1,
                SlowWaitTurns = 0,
                IqLabel = I18n.T("思考", "Think"),
                IqPill = "142",
                IqLevel = "ok",
                IqReason = I18n.TF(
                    "近 {0} 轮思考率 {1}% · 均思 {2} tok · 最新 {3}",
                    "Last {0} turns: {1}% thought · avg {2} tok · latest {3}",
                    4, 100, 588, I18n.TF("模型 {0:F1}s + 工具 {1:F1}s", "Model {0:F1}s + Tools {1:F1}s", 14.5, 0.9)),
                SubagentCount = 2,
                SubagentWorkingCount = 0,
                SubagentTurns = 11,
                SubagentTokens = 1482000,
                SubagentCachePct = 94.2,
                Subagents = new List<SubagentBriefItem>
                {
                    new SubagentBriefItem
                    {
                        Key = "antigravity-insiders:sub001",
                        Cid = "sub001",
                        ShortId = "sub001",
                        RoleTitle = I18n.T("DPI 排版审查", "DPI Layout Auditor"),
                        ModelBadge = "Argon·H",
                        Tone = "argon",
                        Turns = 6,
                        ContextUsed = 92400,
                        TotalTokens = 824000,
                        CacheHitPct = 95.1,
                        IsWorking = false,
                        ElapsedSec = 0,
                        LastAction = I18n.T("已完成汇报", "Completed report")
                    },
                    new SubagentBriefItem
                    {
                        Key = "antigravity-insiders:sub002",
                        Cid = "sub002",
                        ShortId = "sub002",
                        RoleTitle = I18n.T("边界用例测试", "Edge-Case Verifier"),
                        ModelBadge = "Flash·H",
                        Tone = "fast",
                        Turns = 5,
                        ContextUsed = 64200,
                        TotalTokens = 658000,
                        CacheHitPct = 93.0,
                        IsWorking = false,
                        ElapsedSec = 0,
                        LastAction = I18n.T("已完成汇报", "Completed report")
                    }
                },
                TopSources = new List<SourceBlock>
                {
                    new SourceBlock { Category = "results", Name = I18n.T("工具返回结果", "Tool Outputs"), Tokens = 114200, Count = 77 },
                    new SourceBlock { Category = "assistant", Name = I18n.T("助手思考与回复", "Assistant & Thinking"), Tokens = 62800, Count = 24 },
                    new SourceBlock { Category = "system", Name = I18n.T("系统与 MCP 底座", "System & MCP Base"), Tokens = 49500, Count = 17 },
                    new SourceBlock { Category = "user", Name = I18n.T("用户提问消息", "User Prompts"), Tokens = 8500, Count = 14 }
                },
                CategoryBreakdowns = catBreakdowns
            };

            var modelRanking = new List<KeyValuePair<string, ModelTokenStat>>
            {
                new KeyValuePair<string, ModelTokenStat>("Carbon (High) · Gemini 4", new ModelTokenStat
                {
                    InputTokens = 18600000,
                    CacheTokens = 168400000,
                    OutputTokens = 1420000,
                    Calls = 684,
                    CostUsd = 98.4
                }),
                new KeyValuePair<string, ModelTokenStat>("Argon (High) · Gemini 4", new ModelTokenStat
                {
                    InputTokens = 7400000,
                    CacheTokens = 82600000,
                    OutputTokens = 890000,
                    Calls = 412,
                    CostUsd = 34.2
                }),
                new KeyValuePair<string, ModelTokenStat>("Flash (High) · Gemini 3.8", new ModelTokenStat
                {
                    InputTokens = 2100000,
                    CacheTokens = 19800000,
                    OutputTokens = 410000,
                    Calls = 196,
                    CostUsd = 4.6
                })
            };

            var topTools = new List<KeyValuePair<string, int>>
            {
                new KeyValuePair<string, int>(I18n.GetToolLabel("read_file"), 842),
                new KeyValuePair<string, int>(I18n.GetToolLabel("edit_file"), 396),
                new KeyValuePair<string, int>(I18n.GetToolLabel("run_command"), 318)
            };

            var convRanking = new List<LedgerConvRankItem>
            {
                new LedgerConvRankItem
                {
                    ConvKey = hud.Key,
                    Title = convTitle,
                    Surface = surface,
                    IsWorking = false,
                    Calls = 486,
                    SubCount = 2,
                    SubTokens = 28400000,
                    InputTokens = 14200000,
                    CacheTokens = 127100000,
                    OutputTokens = 1300000,
                    CostUsd = 71.8,
                    LastTime = now.AddSeconds(-18)
                },
                new LedgerConvRankItem
                {
                    ConvKey = "antigravity:demo002b",
                    Title = I18n.T("构建零依赖 csc 自动化发版流水线", "Zero-Dependency csc Release Pipeline"),
                    Surface = I18n.FmtSurfaceLabel("antigravity"),
                    IsWorking = false,
                    Calls = 312,
                    SubCount = 1,
                    SubTokens = 11200000,
                    InputTokens = 8200000,
                    CacheTokens = 77400000,
                    OutputTokens = 800000,
                    CostUsd = 39.6,
                    LastTime = now.AddHours(-2)
                },
                new LedgerConvRankItem
                {
                    ConvKey = "antigravity-cli:demo003c",
                    Title = I18n.T("双语排版与多屏 DPI 渲染测试", "Bilingual Typography & Multi-DPI QA"),
                    Surface = "CLI",
                    IsWorking = false,
                    Calls = 184,
                    SubCount = 0,
                    SubTokens = 0,
                    InputTokens = 4100000,
                    CacheTokens = 39600000,
                    OutputTokens = 500000,
                    CostUsd = 14.2,
                    LastTime = now.AddHours(-5)
                }
            };

            var todaySum = new PeriodSummary
            {
                Label = I18n.T("今日实时", "Today"),
                InputTokens = 28100000,
                CacheTokens = 270800000,
                OutputTokens = 2720000,
                Turns = 1292,
                ConvCount = 18,
                ToolCalls = 1556,
                CostUsd = 137.2,
                ModelRanking = modelRanking,
                ToolRanking = topTools,
                ConvRanking = convRanking
            };

            var yestSum = new PeriodSummary
            {
                Label = I18n.T("昨日总计", "Yesterday"),
                InputTokens = 36400000,
                CacheTokens = 348200000,
                OutputTokens = 3150000,
                Turns = 1640,
                ConvCount = 24,
                ToolCalls = 1920,
                CostUsd = 178.5,
                ModelRanking = modelRanking,
                ToolRanking = topTools,
                ConvRanking = convRanking
            };

            var d7Sum = new PeriodSummary
            {
                Label = I18n.T("近 7 天", "Last 7 Days"),
                InputTokens = 162000000,
                CacheTokens = 1520000000,
                OutputTokens = 14800000,
                Turns = 6840,
                ConvCount = 78,
                ToolCalls = 8120,
                CostUsd = 742.0,
                ModelRanking = modelRanking,
                ToolRanking = topTools,
                ConvRanking = convRanking
            };

            var d30Sum = new PeriodSummary
            {
                Label = I18n.T("近 30 天", "Last 30 Days"),
                InputTokens = 412000000,
                CacheTokens = 3890000000,
                OutputTokens = 36200000,
                Turns = 18420,
                ConvCount = 186,
                ToolCalls = 21400,
                CostUsd = 1840.0,
                ModelRanking = modelRanking,
                ToolRanking = topTools,
                ConvRanking = convRanking
            };

            var trend = new List<DayBarPoint>();
            long[] sampleDays = new long[] { 125000000, 148000000, 264000000, 198000000, 312000000, 387750000, 301620000 };
            int[] sampleConvs = new int[] { 11, 14, 21, 16, 22, 24, 18 };
            for (int i = 6; i >= 0; i--)
            {
                DateTime dt = now.Date.AddDays(-i);
                int idx = 6 - i;
                string lbl = i == 0 ? I18n.T("今日", "Today") : (i == 1 ? I18n.T("昨日", "Yest") : dt.ToString("MM/dd"));
                trend.Add(new DayBarPoint
                {
                    DateKey = dt.ToString("yyyy-MM-dd"),
                    Label = lbl,
                    Tokens = sampleDays[idx],
                    Sessions = sampleConvs[idx],
                    IsToday = (i == 0)
                });
            }

            return new DashboardData
            {
                TodayDateStr = todayKey,
                ActiveWorkingCount = 0,
                ActiveHud = hud,
                RecentOptions = new List<ConvOptionItem>
                {
                    new ConvOptionItem { Key = hud.Key, Cid = hud.Cid, Title = convTitle, Surface = surface, LastTime = now.AddSeconds(-18), IsWorking = false },
                    new ConvOptionItem { Key = "antigravity:demo002b", Cid = "demo002b", Title = convRanking[1].Title, Surface = convRanking[1].Surface, LastTime = now.AddHours(-2), IsWorking = false },
                    new ConvOptionItem { Key = "antigravity-cli:demo003c", Cid = "demo003c", Title = convRanking[2].Title, Surface = "CLI", LastTime = now.AddHours(-5), IsWorking = false }
                },
                MemGuard = new MemGuardSnapshot
                {
                    AgProcCount = 5,
                    AgTotalMB = 1480.0,
                    AgMaxSingleMB = 640.0,
                    TempCacheCount = 12,
                    TempCacheMB = 18.4,
                    AutoGuardEnabled = autoMemGuard
                },
                Today = todaySum,
                Yesterday = yestSum,
                Days7 = d7Sum,
                Days30 = d30Sum,
                All = d30Sum,
                AllTimeTokens = 4338200000L,
                AllTimeUsd = 1840.0,
                AllTimeCachePct = 91.4,
                Rolling5hTokens = 184600000L,
                Rolling5hTurns = 412,
                Rolling5hCarbonTok = 124000000L,
                Rolling5hArgonTok = 48200000L,
                Rolling5hFastTok = 12400000L,
                Rolling5hStripText = I18n.TF(
                    "Carbon 218轮 · Argon 136轮 | 7天 {0}",
                    "Carbon 218t · Argon 136t | 7d {0}",
                    I18n.FmtBigTokens(d7Sum.TotalTokens)),
                Rolling5hHoverTip = I18n.TF(
                    "过去5小时共跑 412 轮 ({0}) · 过去7天累计 {1}",
                    "Last 5h: 412 turns ({0}) · Last 7d: {1}",
                    I18n.FmtBigTokens(184600000L), I18n.FmtBigTokens(d7Sum.TotalTokens)),
                Trend7d = trend,
                RefreshedAt = now
            };
        }

        public static ModelDrilldownData BuildDemoDrilldown(string modelName, PeriodTab period)
        {
            DateTime now = DateTime.Now;
            return new ModelDrilldownData
            {
                ModelName = string.IsNullOrEmpty(modelName) ? "Carbon (High) · Gemini 4" : modelName,
                PeriodLabel = I18n.T("今日", "Today"),
                DateRangeText = now.ToString("yyyy-MM-dd"),
                InputTokens = 18600000,
                CacheTokens = 168400000,
                OutputTokens = 1420000,
                CostUsd = 98.4,
                Turns = 684,
                ConvCount = 9,
                ToolCallCount = 512,
                LowCacheTurns = 4,
                TopTools = new List<KeyValuePair<string, int>>
                {
                    new KeyValuePair<string, int>(I18n.GetToolLabel("read_file"), 238),
                    new KeyValuePair<string, int>(I18n.GetToolLabel("edit_file"), 142),
                    new KeyValuePair<string, int>(I18n.GetToolLabel("run_command"), 96),
                    new KeyValuePair<string, int>(I18n.GetToolLabel("grep_search"), 36)
                },
                ConvRows = new List<ModelDrilldownConvRow>
                {
                    new ModelDrilldownConvRow
                    {
                        ConvKey = "antigravity-insiders:demo001a",
                        Title = I18n.T("重构 Win32 悬浮窗拖拽与磁吸停靠", "Refactor Win32 Floating Bar Drag & Snap"),
                        Surface = I18n.FmtSurfaceLabel("antigravity-insiders"),
                        FirstTime = now.AddHours(-3),
                        LastTime = now.AddMinutes(-1),
                        Calls = 382,
                        ToolCalls = 294,
                        LowCacheCalls = 2,
                        InputTokens = 11200000,
                        CacheTokens = 104600000,
                        OutputTokens = 880000,
                        CostUsd = 58.2
                    },
                    new ModelDrilldownConvRow
                    {
                        ConvKey = "antigravity:demo002b",
                        Title = I18n.T("构建零依赖 csc 自动化发版流水线", "Zero-Dependency csc Release Pipeline"),
                        Surface = I18n.FmtSurfaceLabel("antigravity"),
                        FirstTime = now.AddHours(-6),
                        LastTime = now.AddHours(-2),
                        Calls = 302,
                        ToolCalls = 218,
                        LowCacheCalls = 2,
                        InputTokens = 7400000,
                        CacheTokens = 63800000,
                        OutputTokens = 540000,
                        CostUsd = 40.2
                    }
                },
                TurnRows = new List<ModelDrilldownTurnRow>
                {
                    new ModelDrilldownTurnRow
                    {
                        ConvKey = "antigravity-insiders:demo001a",
                        ConvTitle = I18n.T("重构 Win32 悬浮窗拖拽与磁吸停靠", "Refactor Win32 Floating Bar Drag & Snap"),
                        TimeLocal = now.AddMinutes(-1),
                        InputTokens = 2180,
                        CacheTokens = 231400,
                        OutputTokens = 1420,
                        CostUsd = 0.08,
                        ToolsShort = I18n.GetToolLabel("run_command"),
                        ToolsFull = I18n.GetToolLabel("run_command"),
                        ToolCount = 1
                    }
                }
            };
        }
    }
}
