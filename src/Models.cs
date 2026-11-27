using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AntigravityHud
{
    internal enum MainTab
    {
        WindowHud = 0,
        GlobalLedger = 1
    }

    internal enum ScaleMode
    {
        Auto = 0,
        K256 = 1,
        M1 = 2
    }

    internal enum PeriodTab
    {
        Today = 0,
        Yesterday = 1,
        Days7 = 2,
        Days30 = 3,
        All = 4
    }

    internal enum HudCard2Mode
    {
        Turns = 0,
        Subagents = 1,
        Breakdown = 2
    }

    internal enum TurnQuickFilter
    {
        All = 0,
        CacheWarn = 1,
        SlowWait = 2
    }

    internal enum LedgerRankMode
    {
        ByModel = 0,
        ByTask = 1
    }

    internal sealed class SubagentBriefItem
    {
        public string Key = "";
        public string Cid = "";
        public string ShortId = "";
        public string RoleTitle = "";
        public string ModelBadge = "";
        public string Tone = "argon";
        public bool IsWorking;
        public int ElapsedSec;
        public int Turns;
        public long ContextUsed;
        public long TotalTokens;
        public double CacheHitPct;
        public string LastAction = "";
    }

    internal sealed class BreakdownRowItem
    {
        public string Label = "";
        public string SubNote = "";
        public long Tokens;
        public double SharePct;
    }

    internal sealed class LedgerConvRankItem
    {
        public string ConvKey = "";
        public string Title = "";
        public string Surface = "";
        public bool IsWorking;
        public int Calls;
        public int SubCount;
        public long SubTokens;
        public long InputTokens;
        public long CacheTokens;
        public long OutputTokens;
        public double CostUsd;
        public DateTime LastTime = DateTime.MinValue;

        public long TotalTokens
        {
            get { return InputTokens + CacheTokens + OutputTokens; }
        }

        public double CacheHitPct
        {
            get
            {
                long p = InputTokens + CacheTokens;
                return p > 0 ? (CacheTokens * 100.0 / p) : 0.0;
            }
        }
    }

    internal sealed class SourceBlock
    {
        public string Category = "other";
        public string Name = "";
        public long Tokens;
        public int Count;
    }

    internal sealed class TurnItem
    {
        public int TurnNo;
        public int StepIndex;
        public string Model = "";
        public string ShortTag = "";
        public string Tone = "argon";
        public long InTok;
        public long CacheTok;
        public long PromptTotal;
        public long OutTok;
        public long TotalUsed;
        public int ThinkChars;
        public long EstThinkTok;
        public double CacheHitPct;
        public double? WaitSec;
        public double? DurationSec;
        public double? ToolExecSec;
        public double? E2ESec;
        public double? Tps;
        public double? E2ETps;
        public int GroupStepSeq = 1;
        public int GroupTotalSteps = 1;
        public long GroupOutTok;
        public double GroupTotalSec;
        public string ToolSummary = "";
        public bool ModelSwitchedCold;
        public bool IsCacheDropWarn;
        public bool IsPostCompact;
        public string SwitchedFromTag = "";
        public string ColdCacheReason = "";
    }

    internal sealed class HudSnapshot
    {
        public string Key = "";
        public string Cid = "";
        public string ShortId = "";
        public string Title = "";
        public string Surface = "主端";
        public bool IsSideQuestion;
        public bool IsSubagent;
        public string ParentConvKey = "";
        public string ParentTitle = "";
        public int TotalSteps;
        public int TotalTurns;
        public bool IsRunning;
        public int ActiveElapsedSec;
        public string ActivePhaseLabel = "思考中";
        public bool Compacted;
        public long CompactPreTokens;
        public long CompactSummaryTokens;
        public int TurnsSinceCompact = -1;
        public string LiveBannerLevel = "";
        public string LiveBannerText = "";
        public DateTime UpdatedAt = DateTime.MinValue;

        public TurnItem Latest;

        public long SysTokens;
        public long ToolsTokens;
        public long SysRulesTokens;
        public long SysSkillsTokens;
        public long SysCoreTokens;
        public long SysMcpDeclTokens;
        public long SysMcpCoreTokens;
        public long SysSummaryTokens;
        public int RulesFileCount;
        public int SkillsEntryCount;
        public int McpServerCount;
        public long UserTokens;
        public long RespTokens;
        public long ResultsTokens;

        public int SubagentCount;
        public int SubagentWorkingCount;
        public int SubagentTurns;
        public long SubagentTokens;
        public double SubagentCachePct;
        public List<SubagentBriefItem> Subagents = new List<SubagentBriefItem>();

        public List<SourceBlock> TopSources = new List<SourceBlock>();
        public SourceBlock OtherSources;
        public Dictionary<string, List<BreakdownRowItem>> CategoryBreakdowns =
            new Dictionary<string, List<BreakdownRowItem>>(StringComparer.OrdinalIgnoreCase);

        public int CacheWarnTurns;
        public int SlowWaitTurns;
        public string IqLevel = "idle"; // ok | warn | danger | idle
        public string IqLabel = "思考";
        public string IqPill = "--";
        public string IqReason = "";
        public List<TurnItem> RecentTurns = new List<TurnItem>();
    }

    internal sealed class HudCacheEntry
    {
        public long Length;
        public DateTime LastWriteUtc;
        public HudSnapshot Data;
    }

    internal sealed class ModelTokenStat
    {
        public long InputTokens;
        public long CacheTokens;
        public long OutputTokens;
        public int Calls;
        public double CostUsd;

        public long TotalTokens
        {
            get { return InputTokens + CacheTokens + OutputTokens; }
        }
    }

    internal sealed class DayBarPoint
    {
        public string DateKey = "";
        public string Label = "";
        public int Sessions;
        public long Tokens;
        public bool IsToday;
    }

    internal sealed class PeriodSummary
    {
        public string Label = "";
        public long InputTokens;
        public long CacheTokens;
        public long OutputTokens;
        public int Turns;
        public int ConvCount;
        public int ToolCalls;
        public double CostUsd;
        public List<KeyValuePair<string, ModelTokenStat>> ModelRanking = new List<KeyValuePair<string, ModelTokenStat>>();
        public List<KeyValuePair<string, int>> ToolRanking = new List<KeyValuePair<string, int>>();
        public List<LedgerConvRankItem> ConvRanking = new List<LedgerConvRankItem>();

        public long TotalTokens
        {
            get { return InputTokens + CacheTokens + OutputTokens; }
        }

        public double CacheHitRate
        {
            get
            {
                long p = InputTokens + CacheTokens;
                return p > 0 ? (CacheTokens * 100.0 / p) : 0.0;
            }
        }
    }

    internal sealed class LedgerTurnRecord
    {
        public string DateKey = "";
        public DateTime TimeLocal;
        public string Model = "";
        public long InputTokens;
        public long CacheTokens;
        public long OutputTokens;
        public double CostUsd;
        public string[] ToolNames;

        public long TotalTokens
        {
            get { return InputTokens + CacheTokens + OutputTokens; }
        }

        public double CacheHitPct
        {
            get
            {
                long p = InputTokens + CacheTokens;
                return p > 0 ? (CacheTokens * 100.0 / p) : 0.0;
            }
        }
    }

    internal sealed class ModelDrilldownConvRow
    {
        public string ConvKey = "";
        public string Title = "";
        public string Surface = "";
        public DateTime FirstTime = DateTime.MaxValue;
        public DateTime LastTime = DateTime.MinValue;
        public int Calls;
        public int ToolCalls;
        public int LowCacheCalls;
        public long InputTokens;
        public long CacheTokens;
        public long OutputTokens;
        public double CostUsd;

        public long TotalTokens
        {
            get { return InputTokens + CacheTokens + OutputTokens; }
        }

        public double CacheHitPct
        {
            get
            {
                long p = InputTokens + CacheTokens;
                return p > 0 ? (CacheTokens * 100.0 / p) : 0.0;
            }
        }
    }

    internal sealed class ModelDrilldownTurnRow
    {
        public string ConvKey = "";
        public string ConvTitle = "";
        public DateTime TimeLocal;
        public long InputTokens;
        public long CacheTokens;
        public long OutputTokens;
        public double CostUsd;
        public string ToolsShort = "—";
        public string ToolsFull = "无工具调用";
        public int ToolCount;

        public long TotalTokens
        {
            get { return InputTokens + CacheTokens + OutputTokens; }
        }

        public double CacheHitPct
        {
            get
            {
                long p = InputTokens + CacheTokens;
                return p > 0 ? (CacheTokens * 100.0 / p) : 0.0;
            }
        }
    }

    internal sealed class ModelDrilldownData
    {
        public string ModelName = "";
        public PeriodTab Period = PeriodTab.Today;
        public string PeriodLabel = "";
        public string DateRangeText = "";
        public long InputTokens;
        public long CacheTokens;
        public long OutputTokens;
        public double CostUsd;
        public int Turns;
        public int ConvCount;
        public int ToolCallCount;
        public int LowCacheTurns;
        public List<KeyValuePair<string, int>> TopTools = new List<KeyValuePair<string, int>>();
        public List<ModelDrilldownConvRow> ConvRows = new List<ModelDrilldownConvRow>();
        public List<ModelDrilldownTurnRow> TurnRows = new List<ModelDrilldownTurnRow>();

        public long TotalTokens
        {
            get { return InputTokens + CacheTokens + OutputTokens; }
        }

        public double CacheHitPct
        {
            get
            {
                long p = InputTokens + CacheTokens;
                return p > 0 ? (CacheTokens * 100.0 / p) : 0.0;
            }
        }

        public long AvgTokensPerTurn
        {
            get { return Turns > 0 ? (TotalTokens / Turns) : 0L; }
        }
    }

    internal sealed class ConvOptionItem
    {
        public string Key = "";
        public string Cid = "";
        public string Surface = "";
        public string Title = "";
        public DateTime LastTime = DateTime.MinValue;
        public bool IsWorking;
    }

    internal sealed class ConvTrackState
    {
        public string Key = "";
        public string Cid = "";
        public string Surface = "主端";
        public string TranscriptPath = "";
        public string AnnotPath = "";
        public long LastOffset;
        public DateTime LastWriteUtc = DateTime.MinValue;
        public string Title = "";
        public DateTime AnnotWriteUtc = DateTime.MinValue;
        public DateTime LastUserViewUtc = DateTime.MinValue;
        public DateTime LastUserInputUtc = DateTime.MinValue;
        public bool IsWorking;
        public bool LatestIsToolExec;
        public DateTime LastStepLocal = DateTime.MinValue;
        public int MaxStepIndex;
        public bool IsSideQuestion;
        public bool IsSubagent;
        public string ParentCid = "";
        public string AssignedRole = "";
        public string LastActionSummary = "";
        public string LastShortModel = "";
        public string LastModelTone = "argon";
        public long LatestContextUsed;
        public Queue<string> PendingSubRoles = new Queue<string>();
        public List<string> SpawnedSubCids = new List<string>();
        public Dictionary<string, string> SpawnedSubRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> SideQuestionCids = new List<string>();
        public Dictionary<string, string> SideQuestionTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, ModelTokenStat>> ByDateModel =
            new Dictionary<string, Dictionary<string, ModelTokenStat>>(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, Dictionary<string, int>>> ByDateModelTools =
            new Dictionary<string, Dictionary<string, Dictionary<string, int>>>(StringComparer.Ordinal);
        public List<LedgerTurnRecord> LedgerTurns = new List<LedgerTurnRecord>();
    }

    internal sealed class MemGuardSnapshot
    {
        public int AgProcCount;
        public double AgTotalMB;
        public double AgMaxSingleMB;
        public int TempCacheCount;
        public double TempCacheMB;
        public bool AutoGuardEnabled;
        public double LastFreedMB;
        public double TotalFreedMB;
        public DateTime LastActionAt = DateTime.MinValue;
        public bool IsMemHigh
        {
            get { return AgTotalMB >= LocalMemoryGuard.OverflowWarnMB; }
        }
    }

    internal sealed class DashboardData
    {
        public string TodayDateStr = "";
        public int ActiveWorkingCount;
        public List<ConvOptionItem> RecentOptions = new List<ConvOptionItem>();
        public HudSnapshot ActiveHud;
        public MemGuardSnapshot MemGuard = new MemGuardSnapshot();
        public PeriodSummary Today = new PeriodSummary();
        public PeriodSummary Yesterday = new PeriodSummary();
        public PeriodSummary Days7 = new PeriodSummary();
        public PeriodSummary Days30 = new PeriodSummary();
        public PeriodSummary All = new PeriodSummary();
        public long AllTimeTokens;
        public double AllTimeUsd;
        public double AllTimeCachePct;
        public long Rolling5hTokens;
        public int Rolling5hTurns;
        public long Rolling5hCarbonTok;
        public long Rolling5hArgonTok;
        public long Rolling5hFastTok;
        public long Rolling5hOtherTok;
        public string Rolling5hStripText = "";
        public string Rolling5hHoverTip = "";
        public List<DayBarPoint> Trend7d = new List<DayBarPoint>();
        public DateTime RefreshedAt = DateTime.Now;
    }

    internal static class JsonFastHelper
    {
        public static string ExtractString(string line, string fieldName, int startSearchIdx)
        {
            if (string.IsNullOrEmpty(line)) return "";
            string needle = "\"" + fieldName + "\"";
            int idx = line.IndexOf(needle, startSearchIdx, StringComparison.Ordinal);
            if (idx < 0) return "";
            int p = idx + needle.Length;
            while (p < line.Length && (line[p] == ' ' || line[p] == '\t' || line[p] == ':')) p++;
            if (p >= line.Length || line[p] != '"') return "";
            p++;

            var sb = new StringBuilder();
            while (p < line.Length)
            {
                char c = line[p++];
                if (c == '"')
                    break;
                if (c == '\\' && p < line.Length)
                {
                    char esc = line[p++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (p + 4 <= line.Length)
                            {
                                int code;
                                if (int.TryParse(line.Substring(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                {
                                    // Filter out surrogate pairs so GDI+ never renders tofu boxes
                                    if (code < 0xD800 || code > 0xDFFF)
                                        sb.Append((char)code);
                                }
                                p += 4;
                            }
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    if (!char.IsSurrogate(c))
                        sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public static long ExtractLong(string line, string fieldName)
        {
            if (string.IsNullOrEmpty(line)) return 0;
            string needle = "\"" + fieldName + "\"";
            int idx = line.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return 0;
            int p = idx + needle.Length;
            while (p < line.Length && (line[p] == ' ' || line[p] == '\t' || line[p] == ':')) p++;
            long val = 0;
            bool any = false;
            while (p < line.Length && line[p] >= '0' && line[p] <= '9')
            {
                val = val * 10 + (line[p] - '0');
                any = true;
                p++;
            }
            return any ? val : 0;
        }

        public static double ExtractDouble(string line, string fieldName)
        {
            if (string.IsNullOrEmpty(line)) return 0.0;
            string needle = "\"" + fieldName + "\"";
            int idx = line.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return 0.0;
            int p = idx + needle.Length;
            while (p < line.Length && (line[p] == ' ' || line[p] == '\t' || line[p] == ':')) p++;
            int start = p;
            while (p < line.Length && ((line[p] >= '0' && line[p] <= '9') || line[p] == '.' || line[p] == '-' || line[p] == '+' || line[p] == 'e' || line[p] == 'E'))
            {
                p++;
            }
            if (p <= start) return 0.0;
            double v;
            if (double.TryParse(line.Substring(start, p - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return 0.0;
        }

        public static long EstTextTokens(string txt)
        {
            if (string.IsNullOrEmpty(txt)) return 0;
            int cjk = 0;
            for (int i = 0; i < txt.Length; i++)
            {
                int code = txt[i];
                if (code >= 0x3400 && code <= 0x9FFF) cjk++;
            }
            double est = cjk * 0.68 + (txt.Length - cjk) / 3.8;
            return Math.Max(1L, (long)Math.Ceiling(est));
        }

        public static long SumTruncatedBytes(string txt)
        {
            if (string.IsNullOrEmpty(txt)) return 0;
            int pos = 0;
            long sum = 0;
            while (pos < txt.Length)
            {
                int idx = txt.IndexOf("<truncated ", pos, StringComparison.Ordinal);
                if (idx < 0) break;
                int p = idx + 11;
                long num = 0;
                bool any = false;
                while (p < txt.Length && txt[p] >= '0' && txt[p] <= '9')
                {
                    num = num * 10 + (txt[p] - '0');
                    any = true;
                    p++;
                }
                if (any && p + 6 <= txt.Length && string.CompareOrdinal(txt, p, " bytes>", 0, 7) == 0)
                {
                    sum += num;
                    pos = p + 7;
                }
                else
                {
                    pos = idx + 11;
                }
            }
            return sum;
        }

        public static long EstTextTokensWithTrunc(string txt)
        {
            if (string.IsNullOrEmpty(txt)) return 0;
            long baseTok = EstTextTokens(txt);
            long truncBytes = SumTruncatedBytes(txt);
            if (truncBytes > 0)
            {
                baseTok += (long)Math.Round(truncBytes / 3.7);
            }
            return baseTok;
        }

        public static long EstToolCallsTokensFromLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return 0;
            int tcKey = line.IndexOf("\"tool_calls\"", StringComparison.Ordinal);
            if (tcKey < 0) return 0;
            int arrStart = line.IndexOf('[', tcKey + 12);
            if (arrStart < 0) return 0;

            bool inStr = false;
            bool esc = false;
            int bracketDepth = 0;
            int arrEnd = -1;
            int effectiveChars = 0;
            int cjkChars = 0;

            for (int i = arrStart; i < line.Length; i++)
            {
                char ch = line[i];
                if (inStr)
                {
                    if (esc)
                    {
                        esc = false;
                        // Unescape double-escaped sequences inside stringified args so backslashes don't inflate length
                        if (ch != '\\' && ch != '"') effectiveChars++;
                        continue;
                    }
                    if (ch == '\\')
                    {
                        esc = true;
                        continue;
                    }
                    if (ch == '"')
                    {
                        inStr = false;
                        continue;
                    }
                    if (ch >= 0x3400 && ch <= 0x9FFF) cjkChars++;
                    else effectiveChars++;
                    continue;
                }

                if (ch == '[') { bracketDepth++; continue; }
                if (ch == ']')
                {
                    bracketDepth--;
                    if (bracketDepth <= 0)
                    {
                        arrEnd = i;
                        break;
                    }
                    continue;
                }
                if (ch == '"')
                {
                    inStr = true;
                    continue;
                }
                effectiveChars++;
            }

            string slice = arrEnd > arrStart
                ? line.Substring(arrStart, arrEnd - arrStart + 1)
                : line.Substring(arrStart);
            if (slice.Length <= 2) return 0;

            double tok = cjkChars * 0.68 + effectiveChars / 3.9;
            long truncBytes = SumTruncatedBytes(slice);
            if (truncBytes > 0)
            {
                tok += truncBytes / 3.7;
            }
            return Math.Max(8L, (long)Math.Round(tok));
        }

        public static string StripSurrogates(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var sb = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (!char.IsSurrogate(c) && c != '\uFE0F' && c != '\u200D' && c != '\uFFFD')
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }

}
