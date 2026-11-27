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
    internal sealed class LocalTelemetryEngine
    {
        private readonly string _geminiRoot;
        private readonly string _extApiLedgerPath;
        public readonly LocalMemoryGuard MemGuard;
        private readonly Dictionary<string, ConvTrackState> _states = new Dictionary<string, ConvTrackState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HudCacheEntry> _hudCache = new Dictionary<string, HudCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _stepFinishUtcMap = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private long _extApiLedgerOffset;
        private int _extApiLedgerLineSeq;
        private DateTime _extApiLedgerMtimeUtc = DateTime.MinValue;
        private readonly Dictionary<string, ExternalApiRecord> _extApiRecords = new Dictionary<string, ExternalApiRecord>(StringComparer.Ordinal);


        private DateTime _lastDirScanUtc = DateTime.MinValue;
        private DateTime _lastBrainDirMtimeUtc = DateTime.MinValue;
        private string _currentDayKey = "";


        private static readonly Regex RxUuid = new Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled);
        private static readonly Regex RxPbTitle = new Regex("title\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Compiled);
        private static readonly Regex RxPbViewTime = new Regex("last_user_view_time\\s*:\\s*\\{\\s*seconds\\s*:\\s*(\\d+)(?:\\s+nanos\\s*:\\s*(\\d+))?", RegexOptions.Compiled);
        private static readonly Regex RxPbSideId = new Regex("side_question_cascade_ids\\s*:\\s*\"([0-9a-fA-F-]{36})\"", RegexOptions.Compiled);
        private static readonly Regex RxPbSideTitle = new Regex("side_question_titles\\s*:\\s*\\{\\s*key\\s*:\\s*\"([0-9a-fA-F-]{36})\"\\s+value\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Compiled);
        private static readonly Regex RxSubagentUuid = new Regex("(?:\"conversationId\"\\s*:\\s*\"|conversation://|sender=)([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})", RegexOptions.Compiled);
        private static readonly Regex RxSenderUuid = new Regex("\\bsender=([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})", RegexOptions.Compiled);
        private static readonly Regex RxCreatedConvId = new Regex("\"conversationId\"\\s*:\\s*\"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\"", RegexOptions.Compiled);
        private static readonly Regex RxInvokeRole = new Regex("\"Role\"\\s*:\\s*\"(?:\\\\+\")?([^\"\\\\]{1,36})", RegexOptions.Compiled);

        private DateTime _baseCfgScanUtc = DateTime.MinValue;
        private long _cachedRulesEstTok = 4200;
        private long _cachedSkillsEstTok = 3600;
        private long _cachedCoreSysEstTok = 9800;
        private long _cachedMcpDeclEstTok = 5400;
        private long _cachedMcpCoreEstTok = 15200;
        private int _cachedRulesCount = 3;
        private int _cachedSkillsCount = 38;
        private int _cachedMcpServerCount = 4;

        private void GetBaseConfigWeights(out long rulesEst, out long skillsEst, out long mcpCoreEst)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if ((nowUtc - _baseCfgScanUtc).TotalSeconds < 30.0)
            {
                rulesEst = _cachedRulesEstTok;
                skillsEst = _cachedSkillsEstTok;
                mcpCoreEst = _cachedMcpCoreEstTok;
                return;
            }
            _baseCfgScanUtc = nowUtc;

            long rTok = 0;
            int rCnt = 0;
            try
            {
                string rulesDir = Path.Combine(_geminiRoot, "config", "rules");
                if (Directory.Exists(rulesDir))
                {
                    foreach (string rf in Directory.GetFiles(rulesDir, "*.md"))
                    {
                        rCnt++;
                        try { rTok += JsonFastHelper.EstTextTokens(File.ReadAllText(rf, Encoding.UTF8)); } catch { }
                    }
                }
            }
            catch { }

            long sTok = 0;
            int sCnt = 0;
            try
            {
                foreach (string skRoot in new[]
                {
                    Path.Combine(_geminiRoot, "config", "skills"),
                    Path.Combine(_geminiRoot, "antigravity-insiders", "builtin", "skills"),
                    Path.Combine(_geminiRoot, "antigravity", "builtin", "skills")
                })
                {
                    if (!Directory.Exists(skRoot)) continue;
                    foreach (string sd in Directory.GetDirectories(skRoot))
                    {
                        string smd = Path.Combine(sd, "SKILL.md");
                        if (!File.Exists(smd)) continue;
                        sCnt++;
                        sTok += 95;
                    }
                }
                string plugRoot = Path.Combine(_geminiRoot, "config", "plugins");
                if (Directory.Exists(plugRoot))
                {
                    foreach (string pd in Directory.GetDirectories(plugRoot))
                    {
                        string pSkills = Path.Combine(pd, "skills");
                        if (Directory.Exists(pSkills))
                        {
                            int nPlugSk = Directory.GetDirectories(pSkills).Length;
                            sCnt += nPlugSk;
                            sTok += nPlugSk * 95L;
                        }
                    }
                }
            }
            catch { }

            long coreSysTok = 9800; // Core system identity, safety, built-in tool declarations
            long mcpDeclTok = 600;
            int mcpSrvCnt = 0;
            try
            {
                foreach (string mcpRoot in new[]
                {
                    Path.Combine(_geminiRoot, "antigravity-insiders", "mcp"),
                    Path.Combine(_geminiRoot, "antigravity", "mcp")
                })
                {
                    if (!Directory.Exists(mcpRoot)) continue;
                    foreach (string srvDir in Directory.GetDirectories(mcpRoot))
                    {
                        mcpSrvCnt++;
                        mcpDeclTok += 90; // Server header
                        try
                        {
                            string[] toolJsons = Directory.GetFiles(srvDir, "*.json");
                            // Lazy tools list only names (~8 tok each); eager tools include full schema
                            mcpDeclTok += toolJsons.Length * 14L;
                        }
                        catch { }
                    }
                    break;
                }
            }
            catch { }

            _cachedRulesCount = Math.Max(1, rCnt);
            _cachedSkillsCount = Math.Max(1, sCnt);
            _cachedMcpServerCount = mcpSrvCnt;
            _cachedRulesEstTok = Math.Max(800L, rTok);
            _cachedSkillsEstTok = Math.Max(600L, sTok);
            _cachedCoreSysEstTok = coreSysTok;
            _cachedMcpDeclEstTok = Math.Max(400L, mcpDeclTok);
            _cachedMcpCoreEstTok = _cachedCoreSysEstTok + _cachedMcpDeclEstTok;

            rulesEst = _cachedRulesEstTok;
            skillsEst = _cachedSkillsEstTok;
            mcpCoreEst = _cachedMcpCoreEstTok;
        }

        public static bool IsInternalNoiseTool(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return true;
            return string.Equals(rawName, "update_focus", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(rawName, "update_steps", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(rawName, "task_boundary", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetToolChineseLabel(string rawName)
        {
            return I18n.GetToolLabel(rawName);
        }

        private static string BuildCleanActionSummary(string line)
        {
            string[] rawTools = ExtractRawToolNames(line);
            if (rawTools == null || rawTools.Length == 0) return "";

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>(4);
            for (int i = 0; i < rawTools.Length; i++)
            {
                string rn = rawTools[i];
                if (string.IsNullOrEmpty(rn)) continue;
                if (rn == "update_steps" || rn == "update_focus" || rn == "task_boundary") continue;

                string label = GetToolChineseLabel(rn);
                if (label.IndexOf('_') >= 0) label = I18n.T("扩展工具", "Ext Tool");

                int c;
                if (counts.TryGetValue(label, out c))
                {
                    counts[label] = c + 1;
                }
                else
                {
                    counts[label] = 1;
                    order.Add(label);
                }
            }

            if (order.Count == 0) return I18n.T("状态同步", "Status Sync");

            var parts = new List<string>(3);
            int take = Math.Min(2, order.Count);
            for (int i = 0; i < take; i++)
            {
                string k = order[i];
                int cnt = counts[k];
                parts.Add(cnt > 1 ? (k + " ×" + cnt) : k);
            }
            string res = string.Join(" · ", parts.ToArray());
            if (order.Count > 2) res += " +" + (order.Count - 2);
            return res;
        }

        public LocalTelemetryEngine(string userProfile, bool autoMemoryGuard = false)
        {
            _geminiRoot = Path.Combine(userProfile, ".gemini");
            _extApiLedgerPath = Path.Combine(_geminiRoot, "data", "paid_api_ledger.jsonl");
            MemGuard = new LocalMemoryGuard(userProfile, autoMemoryGuard);
        }

        public void InvalidateLanguageCache()
        {
            _hudCache.Clear();
        }

        private void RefreshExternalApiLedger(bool forceFull)
        {
            try
            {
                if (!File.Exists(_extApiLedgerPath))
                {
                    if (_extApiRecords.Count > 0)
                    {
                        _extApiRecords.Clear();
                        _extApiLedgerOffset = 0;
                        _extApiLedgerLineSeq = 0;
                        _extApiLedgerMtimeUtc = DateTime.MinValue;
                    }
                    return;
                }

                var fi = new FileInfo(_extApiLedgerPath);
                if (forceFull || fi.Length < _extApiLedgerOffset)
                {
                    _extApiLedgerOffset = 0;
                    _extApiLedgerLineSeq = 0;
                    _extApiRecords.Clear();
                }

                if (fi.Length > _extApiLedgerOffset || fi.LastWriteTimeUtc != _extApiLedgerMtimeUtc)
                {
                    using (var fs = new FileStream(_extApiLedgerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fs.Seek(_extApiLedgerOffset, SeekOrigin.Begin);
                        using (var sr = new StreamReader(fs, Encoding.UTF8, false, 4096, true))
                        {
                            string ln;
                            while ((ln = sr.ReadLine()) != null)
                            {
                                _extApiLedgerLineSeq++;
                                if (string.IsNullOrWhiteSpace(ln) || ln.IndexOf("cost_usd", StringComparison.Ordinal) < 0) continue;
                                double usd = JsonFastHelper.ExtractDouble(ln, "cost_usd");
                                if (usd <= 0.0) continue;

                                string opId = JsonFastHelper.ExtractString(ln, "op_id", 0);
                                string recKey = !string.IsNullOrEmpty(opId) ? opId : ("line_" + _extApiLedgerLineSeq);
                                string dtKey = JsonFastHelper.ExtractString(ln, "date", 0);
                                string ts = JsonFastHelper.ExtractString(ln, "ts", 0);
                                if (string.IsNullOrEmpty(dtKey) && ts.Length >= 10) dtKey = ts.Substring(0, 10);
                                if (string.IsNullOrEmpty(dtKey)) dtKey = DateTime.Now.ToString("yyyy-MM-dd");

                                _extApiRecords[recKey] = new ExternalApiRecord
                                {
                                    RecordKey = recKey,
                                    DateKey = dtKey,
                                    Ts = ts,
                                    Cid = JsonFastHelper.ExtractString(ln, "cid", 0),
                                    Model = JsonFastHelper.ExtractString(ln, "model", 0),
                                    Kind = JsonFastHelper.ExtractString(ln, "kind", 0),
                                    CostUsd = usd
                                };
                            }
                        }
                        _extApiLedgerOffset = fs.Position;
                    }
                    _extApiLedgerMtimeUtc = fi.LastWriteTimeUtc;
                }
            }
            catch { }
        }

        private ExternalApiSnapshot BuildExternalApiSnapshot(string todayKey, string monthPrefix, string activeCid)
        {
            var snap = new ExternalApiSnapshot();
            if (_extApiRecords.Count == 0) return snap;

            snap.HasLedger = true;
            string latestSortKey = "";
            foreach (var kv in _extApiRecords)
            {
                ExternalApiRecord r = kv.Value;
                snap.TotalUsd += r.CostUsd;
                if (r.DateKey == todayKey)
                {
                    snap.TodayUsd += r.CostUsd;
                    snap.TodayCalls++;
                }
                if (!string.IsNullOrEmpty(r.DateKey) && r.DateKey.StartsWith(monthPrefix, StringComparison.Ordinal))
                {
                    snap.MonthUsd += r.CostUsd;
                    snap.MonthCalls++;
                }
                if (!string.IsNullOrEmpty(activeCid) && !string.IsNullOrEmpty(r.Cid) &&
                    string.Equals(r.Cid, activeCid, StringComparison.OrdinalIgnoreCase))
                {
                    snap.ConvUsd += r.CostUsd;
                    snap.ConvCalls++;
                }

                string sk = !string.IsNullOrEmpty(r.Ts) ? r.Ts : (r.DateKey + "_" + r.RecordKey);
                if (string.CompareOrdinal(sk, latestSortKey) >= 0)
                {
                    latestSortKey = sk;
                    snap.LatestModel = string.IsNullOrEmpty(r.Model) ? "api" : r.Model;
                    snap.LatestKind = r.Kind ?? "";
                    snap.LatestDate = r.DateKey ?? "";
                    snap.LatestUsd = r.CostUsd;
                }
            }

            string kindPart = string.IsNullOrEmpty(snap.LatestKind) ? "" : ("/" + snap.LatestKind);
            string latestPart = string.IsNullOrEmpty(snap.LatestModel)
                ? ""
                : I18n.TF(" · 最近 {0}{1} ${2:F2}", " · Last {0}{1} ${2:F2}", snap.LatestModel, kindPart, snap.LatestUsd);
            snap.HoverDetail = I18n.TF(
                "本地外部 API 离线账本 (今日 {0} 笔 · 本月 {1} 笔{2}) · 0 密钥读取",
                "Local external API ledger ({0} today · {1} this month{2}) · zero key access",
                snap.TodayCalls, snap.MonthCalls, latestPart);
            return snap;
        }

        public DashboardData Query(string selectedConvKey, bool forceFullScan, string activeWindowTitle = "")
        {
            DateTime now = DateTime.Now;
            string todayKey = now.ToString("yyyy-MM-dd");
            if (_currentDayKey != todayKey)
            {
                _currentDayKey = todayKey;
                _lastDirScanUtc = DateTime.MinValue;
            }

            DateTime nowUtc = DateTime.UtcNow;
            string mainBrainDir = Path.Combine(_geminiRoot, "antigravity", "brain");
            DateTime curBrainMtime = Directory.Exists(mainBrainDir) ? Directory.GetLastWriteTimeUtc(mainBrainDir) : DateTime.MinValue;
            bool brainAdded = curBrainMtime != _lastBrainDirMtimeUtc;

            if (forceFullScan || brainAdded || (nowUtc - _lastDirScanUtc).TotalSeconds >= 15.0 || _states.Count == 0)
            {
                _lastDirScanUtc = nowUtc;
                _lastBrainDirMtimeUtc = curBrainMtime;
                DiscoverSurfaces();
            }

            foreach (var kv in _states.ToArray())
            {
                UpdateConversationIncremental(kv.Value);
            }

            RefreshExternalApiLedger(forceFullScan);
            DashboardData res = BuildDashboardData(now, todayKey, selectedConvKey, activeWindowTitle);
            string activeCid = (res.ActiveHud != null && !string.IsNullOrEmpty(res.ActiveHud.Cid)) ? res.ActiveHud.Cid : "";
            res.ExternalApi = BuildExternalApiSnapshot(todayKey, now.ToString("yyyy-MM-"), activeCid);
            res.MemGuard = MemGuard.GetSnapshot(forceFullScan);
            return res;
        }

        private void DiscoverSurfaces()
        {
            var surfaces = new[]
            {
                new { Dir = "antigravity", Label = "antigravity" },
                new { Dir = "antigravity-insiders", Label = "antigravity-insiders" },
                new { Dir = "antigravity-cli", Label = "antigravity-cli" }
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DateTime cutoffUtc = DateTime.UtcNow.AddDays(-35);

            foreach (var sf in surfaces)
            {
                string root = Path.Combine(_geminiRoot, sf.Dir);
                string bDir = Path.Combine(root, "brain");
                string aDir = Path.Combine(root, "annotations");
                if (!Directory.Exists(bDir)) continue;

                try
                {
                    foreach (string dir in Directory.EnumerateDirectories(bDir))
                    {
                        string cid = Path.GetFileName(dir);
                        if (string.IsNullOrEmpty(cid) || !RxUuid.IsMatch(cid)) continue;

                        string tp = Path.Combine(dir, ".system_generated", "logs", "transcript.jsonl");
                        var fi = new FileInfo(tp);
                        if (!fi.Exists) continue;

                        string stateKey = sf.Dir + ":" + cid;
                        seen.Add(stateKey);

                        if (!_states.ContainsKey(stateKey))
                        {
                            if (fi.LastWriteTimeUtc < cutoffUtc)
                            {
                                _states[stateKey] = new ConvTrackState
                                {
                                    Key = stateKey,
                                    Cid = cid,
                                    Surface = sf.Label,
                                    TranscriptPath = tp,
                                    AnnotPath = Path.Combine(aDir, cid + ".pbtxt"),
                                    LastOffset = fi.Length,
                                    LastWriteUtc = fi.LastWriteTimeUtc,
                                    LastStepLocal = fi.LastWriteTime
                                };
                            }
                            else
                            {
                                _states[stateKey] = new ConvTrackState
                                {
                                    Key = stateKey,
                                    Cid = cid,
                                    Surface = sf.Label,
                                    TranscriptPath = tp,
                                    AnnotPath = Path.Combine(aDir, cid + ".pbtxt")
                                };
                            }
                        }
                    }
                }
                catch { }
            }

            foreach (string oldKey in _states.Keys.ToArray())
            {
                if (!seen.Contains(oldKey))
                    _states.Remove(oldKey);
            }
        }

        private void UpdateConversationIncremental(ConvTrackState st)
        {
            try
            {
                UpdateAnnotationTitle(st);

                var fi = new FileInfo(st.TranscriptPath);
                if (!fi.Exists) return;

                if (fi.Length < st.LastOffset)
                {
                    st.ByDateModel.Clear();
                    st.ByDateModelTools.Clear();
                    st.LedgerTurns.Clear();
                    st.LastOffset = 0;
                    st.MaxStepIndex = 0;
                }

                if (fi.Length == st.LastOffset && fi.LastWriteTimeUtc == st.LastWriteUtc)
                {
                    if (st.IsWorking && (DateTime.Now - st.LastStepLocal).TotalSeconds > 120.0)
                        st.IsWorking = false;
                    return;
                }

                using (var fs = new FileStream(st.TranscriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    fs.Seek(st.LastOffset, SeekOrigin.Begin);
                    long rem = fs.Length - st.LastOffset;
                    if (rem <= 0) return;

                    byte[] buf = new byte[rem];
                    int read = 0;
                    while (read < rem)
                    {
                        int n = fs.Read(buf, read, (int)(rem - read));
                        if (n <= 0) break;
                        read += n;
                    }

                    int lastNl = -1;
                    for (int i = read - 1; i >= 0; i--)
                    {
                        if (buf[i] == (byte)'\n')
                        {
                            lastNl = i;
                            break;
                        }
                    }

                    if (lastNl < 0) return;

                    int consumeBytes = lastNl + 1;
                    st.LastOffset += consumeBytes;
                    st.LastWriteUtc = fi.LastWriteTimeUtc;

                    string textChunk = Encoding.UTF8.GetString(buf, 0, consumeBytes);
                    string[] lines = textChunk.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string rawLine in lines)
                    {
                        ParseIncrementalLedgerLine(st, rawLine.Trim());
                    }
                }

                if (st.IsWorking && (DateTime.Now - st.LastStepLocal).TotalSeconds > 120.0)
                    st.IsWorking = false;
            }
            catch { }
        }

        private void ParseIncrementalLedgerLine(ConvTrackState st, string line)
        {
            if (string.IsNullOrEmpty(line) || line[0] != '{') return;

            int sIdx = (int)JsonFastHelper.ExtractLong(line, "step_index");
            if (sIdx > st.MaxStepIndex) st.MaxStepIndex = sIdx;

            string stepType = JsonFastHelper.ExtractString(line, "type", 0);
            if (string.IsNullOrEmpty(stepType)) return;

            DateTime stepLocal = st.LastStepLocal;
            string createdStr = JsonFastHelper.ExtractString(line, "created_at", 0);
            if (!string.IsNullOrEmpty(createdStr))
            {
                DateTime parsed;
                if (DateTime.TryParse(createdStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                {
                    stepLocal = parsed.ToLocalTime();
                    st.LastStepLocal = stepLocal;
                }
            }

            if (stepType == "SYSTEM_MESSAGE" && sIdx == 0)
            {
                Match mSend = RxSenderUuid.Match(line);
                if (mSend.Success)
                {
                    string pCid = mSend.Groups[1].Value;
                    if (!string.IsNullOrEmpty(pCid) && !string.Equals(pCid, st.Cid, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!st.IsSideQuestion)
                            st.IsSubagent = true;
                        if (string.IsNullOrEmpty(st.ParentCid))
                            st.ParentCid = pCid;
                        string surfPrefix = st.Key.Contains(":") ? st.Key.Substring(0, st.Key.IndexOf(':') + 1) : "";
                        ConvTrackState parentSt;
                        if (_states.TryGetValue(surfPrefix + pCid, out parentSt) && parentSt != null)
                        {
                            if (!parentSt.SpawnedSubCids.Contains(st.Cid))
                                parentSt.SpawnedSubCids.Add(st.Cid);
                            string rName;
                            if (string.IsNullOrEmpty(st.AssignedRole) && parentSt.SpawnedSubRoles.TryGetValue(st.Cid, out rName))
                                st.AssignedRole = rName;
                        }
                    }
                }
                if (string.IsNullOrEmpty(st.Title))
                {
                    string sysContent = JsonFastHelper.ExtractString(line, "content", 0);
                    if (!string.IsNullOrEmpty(sysContent))
                    {
                        int cIdx = sysContent.IndexOf("content=", StringComparison.Ordinal);
                        string body = cIdx >= 0 ? sysContent.Substring(cIdx + 8) : sysContent;
                        string clean = ExtractCleanUserPreview(body, 22);
                        if (!string.IsNullOrEmpty(clean))
                            st.Title = clean;
                    }
                }
                return;
            }

            if (stepType == "GENERIC")
            {
                if (line.IndexOf("Created the following subagent", StringComparison.Ordinal) >= 0 &&
                    line.IndexOf("conversationId", StringComparison.Ordinal) >= 0)
                {
                    string surfPrefix = st.Key.Contains(":") ? st.Key.Substring(0, st.Key.IndexOf(':') + 1) : "";
                    foreach (Match mc in RxCreatedConvId.Matches(line))
                    {
                        string scid = mc.Groups[1].Value;
                        if (string.IsNullOrEmpty(scid) || string.Equals(scid, st.Cid, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!st.SpawnedSubCids.Contains(scid))
                            st.SpawnedSubCids.Add(scid);
                        string roleName = st.PendingSubRoles.Count > 0 ? st.PendingSubRoles.Dequeue() : "";
                        if (!string.IsNullOrEmpty(roleName))
                            st.SpawnedSubRoles[scid] = roleName;

                        ConvTrackState subSt;
                        if (_states.TryGetValue(surfPrefix + scid, out subSt) && subSt != null)
                        {
                            if (!subSt.IsSideQuestion) subSt.IsSubagent = true;
                            subSt.ParentCid = st.Cid;
                            if (!string.IsNullOrEmpty(roleName))
                                subSt.AssignedRole = roleName;
                        }
                    }
                }
                return;
            }

            if (stepType == "USER_INPUT")
            {
                if (sIdx == 0)
                    st.IsSubagent = false;
                st.IsWorking = true;
                st.LatestIsToolExec = false;
                if (stepLocal > DateTime.MinValue)
                    st.LastUserInputUtc = stepLocal.ToUniversalTime();
                if (string.IsNullOrEmpty(st.Title))
                {
                    string content = JsonFastHelper.ExtractString(line, "content", 0);
                    string extracted = ExtractCleanUserPreview(content, 22);
                    if (!string.IsNullOrEmpty(extracted))
                        st.Title = extracted;
                }
                return;
            }

            if (stepType == "PLANNER_RESPONSE")
            {
                string status = JsonFastHelper.ExtractString(line, "status", 0);
                bool hasToolCalls = line.IndexOf("\"tool_calls\":[{", StringComparison.Ordinal) >= 0;
                if (hasToolCalls)
                {
                    st.IsWorking = true;
                    st.LatestIsToolExec = true;
                    string actSum = BuildCleanActionSummary(line);
                    if (!string.IsNullOrEmpty(actSum))
                        st.LastActionSummary = actSum;
                    if (line.IndexOf("\"invoke_subagent\"", StringComparison.Ordinal) >= 0)
                    {
                        foreach (string rVal in ExtractInvokeSubagentRoles(line))
                        {
                            st.PendingSubRoles.Enqueue(rVal);
                        }
                    }
                }
                else if (status == "DONE" || status == "ERROR")
                {
                    st.IsWorking = false;
                    st.LatestIsToolExec = false;
                }

                long inTok = JsonFastHelper.ExtractLong(line, "input_tokens");
                long cacheTok = JsonFastHelper.ExtractLong(line, "cache_read_tokens");
                long outTok = JsonFastHelper.ExtractLong(line, "output_tokens");
                if (inTok + cacheTok + outTok > 0)
                    st.LatestContextUsed = inTok + cacheTok + outTok;

                if ((inTok > 0 || cacheTok > 0 || outTok > 0) && stepLocal > DateTime.MinValue)
                {
                    string dKey = stepLocal.ToString("yyyy-MM-dd");
                    string rawModel = JsonFastHelper.ExtractString(line, "model", 0);
                    if (string.IsNullOrEmpty(rawModel)) rawModel = "unknown";
                    string prettyMod = PrettyModelName(rawModel);
                    string sBadge, sTone;
                    ShortModelBadge(prettyMod, out sBadge, out sTone);
                    st.LastShortModel = sBadge;
                    st.LastModelTone = sTone;
                    double cost = EstimateStepUsd(rawModel, inTok, cacheTok, outTok);

                    Dictionary<string, ModelTokenStat> dayMap;
                    if (!st.ByDateModel.TryGetValue(dKey, out dayMap))
                    {
                        dayMap = new Dictionary<string, ModelTokenStat>(StringComparer.OrdinalIgnoreCase);
                        st.ByDateModel[dKey] = dayMap;
                    }

                    ModelTokenStat ms;
                    if (!dayMap.TryGetValue(prettyMod, out ms))
                    {
                        ms = new ModelTokenStat();
                        dayMap[prettyMod] = ms;
                    }
                    ms.InputTokens += inTok;
                    ms.CacheTokens += cacheTok;
                    ms.OutputTokens += outTok;
                    ms.Calls += 1;
                    ms.CostUsd += cost;

                    string[] rawTools = hasToolCalls ? ExtractRawToolNames(line) : null;
                    string[] cleanTools = null;
                    if (rawTools != null && rawTools.Length > 0)
                    {
                        var kept = new List<string>(rawTools.Length);
                        for (int ti = 0; ti < rawTools.Length; ti++)
                        {
                            if (!IsInternalNoiseTool(rawTools[ti]))
                                kept.Add(rawTools[ti]);
                        }
                        if (kept.Count > 0) cleanTools = kept.ToArray();
                    }

                    if (cleanTools != null && cleanTools.Length > 0)
                    {
                        Dictionary<string, Dictionary<string, int>> dayToolMap;
                        if (!st.ByDateModelTools.TryGetValue(dKey, out dayToolMap))
                        {
                            dayToolMap = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
                            st.ByDateModelTools[dKey] = dayToolMap;
                        }
                        Dictionary<string, int> modToolMap;
                        if (!dayToolMap.TryGetValue(prettyMod, out modToolMap))
                        {
                            modToolMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                            dayToolMap[prettyMod] = modToolMap;
                        }
                        for (int ti = 0; ti < cleanTools.Length; ti++)
                        {
                            string tn = cleanTools[ti];
                            int prevCnt;
                            modToolMap.TryGetValue(tn, out prevCnt);
                            modToolMap[tn] = prevCnt + 1;
                        }
                    }

                    st.LedgerTurns.Add(new LedgerTurnRecord
                    {
                        DateKey = dKey,
                        TimeLocal = stepLocal,
                        Model = prettyMod,
                        InputTokens = inTok,
                        CacheTokens = cacheTok,
                        OutputTokens = outTok,
                        CostUsd = cost,
                        ToolNames = cleanTools
                    });
                }
            }
        }

        private static string[] ExtractRawToolNames(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            int tcKey = line.IndexOf("\"tool_calls\"", StringComparison.Ordinal);
            if (tcKey < 0) return null;

            int arrStart = line.IndexOf('[', tcKey + 12);
            if (arrStart < 0) return null;

            var names = new List<string>(4);
            bool inStr = false;
            bool esc = false;
            int bracketDepth = 0;
            int braceDepth = 0;

            for (int i = arrStart; i < line.Length && names.Count < 16; i++)
            {
                char ch = line[i];
                if (inStr)
                {
                    if (esc) { esc = false; continue; }
                    if (ch == '\\') { esc = true; continue; }
                    if (ch == '"') inStr = false;
                    continue;
                }

                if (ch == '[') { bracketDepth++; continue; }
                if (ch == ']')
                {
                    bracketDepth--;
                    if (bracketDepth <= 0) break;
                    continue;
                }
                if (ch == '{') { braceDepth++; continue; }
                if (ch == '}')
                {
                    if (braceDepth > 0) braceDepth--;
                    continue;
                }

                if (ch == '"')
                {
                    if (bracketDepth == 1 && braceDepth == 1 &&
                        i + 6 < line.Length &&
                        line[i + 1] == 'n' && line[i + 2] == 'a' && line[i + 3] == 'm' && line[i + 4] == 'e' && line[i + 5] == '"')
                    {
                        int p = i + 6;
                        while (p < line.Length && (line[p] == ' ' || line[p] == '\t')) p++;
                        if (p < line.Length && line[p] == ':')
                        {
                            p++;
                            while (p < line.Length && (line[p] == ' ' || line[p] == '\t')) p++;
                            if (p < line.Length && line[p] == '"')
                            {
                                p++;
                                var sb = new StringBuilder(24);
                                bool vEsc = false;
                                while (p < line.Length)
                                {
                                    char vc = line[p++];
                                    if (vEsc) { sb.Append(vc); vEsc = false; continue; }
                                    if (vc == '\\') { vEsc = true; continue; }
                                    if (vc == '"') break;
                                    sb.Append(vc);
                                }
                                string tn = sb.ToString().Trim();
                                if (!string.IsNullOrEmpty(tn))
                                    names.Add(tn);
                                i = p - 1;
                                continue;
                            }
                        }
                    }
                    inStr = true;
                }
            }

            return names.Count > 0 ? names.ToArray() : null;
        }

        private void UpdateAnnotationTitle(ConvTrackState st)
        {
            if (string.IsNullOrEmpty(st.AnnotPath) || !File.Exists(st.AnnotPath)) return;
            try
            {
                var fi = new FileInfo(st.AnnotPath);
                if (fi.LastWriteTimeUtc == st.AnnotWriteUtc) return;

                st.AnnotWriteUtc = fi.LastWriteTimeUtc;
                string raw;
                using (var fs = new FileStream(st.AnnotPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    raw = sr.ReadToEnd();
                }

                var m = RxPbTitle.Match(raw);
                if (m.Success)
                {
                    string decoded = JsonFastHelper.StripSurrogates(DecodePbtxtOctal(m.Groups[1].Value)).Trim();
                    if (!string.IsNullOrEmpty(decoded))
                        st.Title = decoded;
                }

                var mv = RxPbViewTime.Match(raw);
                if (mv.Success)
                {
                    long secs;
                    if (long.TryParse(mv.Groups[1].Value, out secs) && secs > 0)
                    {
                        long nanos = 0;
                        if (mv.Groups[2].Success) long.TryParse(mv.Groups[2].Value, out nanos);
                        DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                        st.LastUserViewUtc = epoch.AddSeconds(secs).AddTicks(nanos / 100L);
                    }
                }
                else if (fi.LastWriteTimeUtc > st.LastUserViewUtc)
                {
                    st.LastUserViewUtc = fi.LastWriteTimeUtc;
                }

                st.SideQuestionCids.Clear();
                foreach (Match ms in RxPbSideId.Matches(raw))
                {
                    string scid = ms.Groups[1].Value;
                    if (!string.IsNullOrEmpty(scid) && !st.SideQuestionCids.Contains(scid))
                        st.SideQuestionCids.Add(scid);
                }

                st.SideQuestionTitles.Clear();
                foreach (Match mt in RxPbSideTitle.Matches(raw))
                {
                    string scid = mt.Groups[1].Value;
                    string stitle = JsonFastHelper.StripSurrogates(DecodePbtxtOctal(mt.Groups[2].Value)).Trim();
                    if (!string.IsNullOrEmpty(scid) && !string.IsNullOrEmpty(stitle))
                        st.SideQuestionTitles[scid] = stitle;
                }

                if (st.SideQuestionCids.Count > 0)
                {
                    string surfPrefix = st.Key.Contains(":") ? st.Key.Substring(0, st.Key.IndexOf(':') + 1) : "";
                    foreach (string scid in st.SideQuestionCids)
                    {
                        ConvTrackState childSt;
                        if (_states.TryGetValue(surfPrefix + scid, out childSt) && childSt != null)
                        {
                            childSt.IsSideQuestion = true;
                            childSt.ParentCid = st.Cid;
                            string mappedTitle;
                            if (st.SideQuestionTitles.TryGetValue(scid, out mappedTitle) && !string.IsNullOrEmpty(mappedTitle))
                            {
                                childSt.Title = mappedTitle;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private HudSnapshot GetOrParseHud(ConvTrackState st)
        {
            if (st == null || string.IsNullOrEmpty(st.TranscriptPath)) return null;
            try
            {
                var fi = new FileInfo(st.TranscriptPath);
                if (!fi.Exists) return null;

                HudCacheEntry hit;
                if (_hudCache.TryGetValue(st.Key, out hit) &&
                    hit.Length == fi.Length &&
                    hit.LastWriteUtc == fi.LastWriteTimeUtc)
                {
                    hit.Data.Title = string.IsNullOrEmpty(st.Title) ? hit.Data.Title : st.Title;
                    hit.Data.IsRunning = st.IsWorking;
                    hit.Data.ActivePhaseLabel = st.LatestIsToolExec ? I18n.T("执行中", "Running") : I18n.T("思考中", "Thinking");
                    hit.Data.ActiveElapsedSec = st.IsWorking && st.LastStepLocal > DateTime.MinValue
                        ? Math.Max(1, (int)Math.Round((DateTime.Now - st.LastStepLocal).TotalSeconds))
                        : 0;
                    RefreshHudSubagentsFromMemory(st, hit.Data, null);
                    return hit.Data;
                }

                string rawText;
                using (var fs = new FileStream(st.TranscriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    rawText = sr.ReadToEnd();
                }

                string[] rawLines = rawText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                long baseSystemTools = 23000;
                long userTokensSum = 0;
                long respThinkTokensSum = 0;
                long toolResultsTokensSum = 0;
                bool compacted = false;
                bool resetAfterCheckpoint = false;

                var sourceBuckets = new Dictionary<string, SourceBlock>(StringComparer.Ordinal);
                Action<string, string, long> addSource = (cat, lbl, tok) =>
                {
                    long n = Math.Max(0L, tok);
                    if (n <= 0) return;
                    string cleanLbl = JsonFastHelper.StripSurrogates(lbl);
                    string k = cat + ":" + cleanLbl;
                    SourceBlock sb;
                    if (sourceBuckets.TryGetValue(k, out sb))
                    {
                        sb.Tokens += n;
                        sb.Count += 1;
                    }
                    else
                    {
                        sourceBuckets[k] = new SourceBlock
                        {
                            Category = cat,
                            Name = cleanLbl,
                            Tokens = n,
                            Count = 1
                        };
                    }
                };

                var turns = new List<TurnItem>();
                long prevPromptTotal = 0;
                double prevCacheHitPct = -1.0;
                long interUserEst = 0;
                long pendingSummaryFromInput = 0;
                long activeSummaryPart = 0;
                bool interHasGeneric = false;
                bool justCompactedFlag = false;
                var pendingToolLabels = new List<string>();
                DateTime prevStepTimeUtc = DateTime.MinValue;
                DateTime lastTriggerUtc = DateTime.MinValue;
                int groupStartTurnIdx = 0;
                string prevShortTag = "";
                long compactPreTok = 0;
                long compactSumTok = 0;
                int turnsSinceCompact = -1;
                string fallbackTitle = "";
                int totalSteps = 0;

                Action finalizeTurnGroup = () =>
                {
                    int gCount = turns.Count - groupStartTurnIdx;
                    if (gCount > 0)
                    {
                        long gOut = 0;
                        double gSec = 0.0;
                        for (int gi = 0; gi < gCount; gi++)
                        {
                            TurnItem gt = turns[groupStartTurnIdx + gi];
                            gOut += gt.OutTok;
                            gSec += (gt.E2ESec ?? (gt.DurationSec ?? 0.0));
                        }
                        gSec = Math.Round(gSec, 1);
                        for (int gi = 0; gi < gCount; gi++)
                        {
                            TurnItem gt = turns[groupStartTurnIdx + gi];
                            gt.GroupStepSeq = gi + 1;
                            gt.GroupTotalSteps = gCount;
                            gt.GroupOutTok = gOut;
                            gt.GroupTotalSec = gSec;
                        }
                    }
                    groupStartTurnIdx = turns.Count;
                };

                for (int i = 0; i < rawLines.Length; i++)
                {
                    string ln = rawLines[i].Trim();
                    if (string.IsNullOrEmpty(ln) || ln[0] != '{') continue;
                    totalSteps++;

                    string sType = JsonFastHelper.ExtractString(ln, "type", 0);
                    DateTime curTimeUtc = DateTime.MinValue;
                    string createdStr = JsonFastHelper.ExtractString(ln, "created_at", 0);
                    if (!string.IsNullOrEmpty(createdStr))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(createdStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt))
                            curTimeUtc = dt;
                    }

                    if (sType == "CHECKPOINT")
                    {
                        finalizeTurnGroup();
                        compacted = true;
                        resetAfterCheckpoint = true;
                        justCompactedFlag = true;
                        long cpIn = JsonFastHelper.ExtractLong(ln, "input_tokens");
                        long cpCache = JsonFastHelper.ExtractLong(ln, "cache_read_tokens");
                        long cpOut = JsonFastHelper.ExtractLong(ln, "output_tokens");
                        if (cpIn + cpCache > 0) compactPreTok = cpIn + cpCache;
                        else if (prevPromptTotal > 0) compactPreTok = prevPromptTotal;
                        if (cpOut > 0) compactSumTok = cpOut;
                        else
                        {
                            string cpTxt = JsonFastHelper.ExtractString(ln, "content", 0);
                            if (!string.IsNullOrEmpty(cpTxt))
                                compactSumTok = JsonFastHelper.EstTextTokensWithTrunc(cpTxt);
                        }
                        turnsSinceCompact = 0;
                        interUserEst = 0;
                        interHasGeneric = false;
                        pendingToolLabels.Clear();
                        if (curTimeUtc > DateTime.MinValue)
                        {
                            prevStepTimeUtc = curTimeUtc;
                            lastTriggerUtc = curTimeUtc;
                        }
                        continue;
                    }

                    if (sType == "USER_INPUT")
                    {
                        finalizeTurnGroup();
                        string content = JsonFastHelper.ExtractString(ln, "content", 0);
                        int csStart = content != null ? content.IndexOf("<CONTEXT_SUMMARY>", StringComparison.Ordinal) : -1;
                        int csEnd = (csStart >= 0 && content != null) ? content.IndexOf("</CONTEXT_SUMMARY>", csStart, StringComparison.Ordinal) : -1;
                        if (csStart >= 0 && csEnd > csStart)
                        {
                            string sumSegment = content.Substring(csStart, (csEnd + 18) - csStart);
                            string restUser = content.Substring(0, csStart) + content.Substring(csEnd + 18);
                            long sumEst = JsonFastHelper.EstTextTokens(sumSegment);
                            if (sumEst > 200)
                            {
                                compacted = true;
                                pendingSummaryFromInput = sumEst;
                                if (compactSumTok <= 0) compactSumTok = sumEst;
                            }
                            interUserEst += JsonFastHelper.EstTextTokens(restUser);
                        }
                        else
                        {
                            interUserEst += JsonFastHelper.EstTextTokens(content);
                        }
                        if (string.IsNullOrEmpty(fallbackTitle))
                            fallbackTitle = ExtractCleanUserPreview(content, 24);
                        if (curTimeUtc > DateTime.MinValue)
                        {
                            prevStepTimeUtc = curTimeUtc;
                            lastTriggerUtc = curTimeUtc;
                        }
                        continue;
                    }

                    if (sType == "GENERIC")
                    {
                        interHasGeneric = true;
                        if (curTimeUtc > DateTime.MinValue)
                        {
                            prevStepTimeUtc = curTimeUtc;
                            lastTriggerUtc = curTimeUtc;
                        }
                        continue;
                    }

                    if (sType != "PLANNER_RESPONSE")
                    {
                        if (curTimeUtc > DateTime.MinValue)
                        {
                            prevStepTimeUtc = curTimeUtc;
                            lastTriggerUtc = curTimeUtc;
                        }
                        continue;
                    }

                    long inTok = JsonFastHelper.ExtractLong(ln, "input_tokens");
                    long cacheTok = JsonFastHelper.ExtractLong(ln, "cache_read_tokens");
                    long outTok = JsonFastHelper.ExtractLong(ln, "output_tokens");
                    long promptTotal = inTok + cacheTok;
                    long totalUsed = promptTotal + outTok;

                    string rawModel = JsonFastHelper.ExtractString(ln, "model", 0);
                    string lowRawM = (rawModel ?? "").ToLowerInvariant();
                    bool turnExpectsThink = lowRawM.Contains("carbon") || lowRawM.Contains("argon") || lowRawM.Contains("neon") ||
                                            lowRawM.Contains("hydrogen") || lowRawM.Contains("orchestrator") ||
                                            lowRawM.Contains("opus") || lowRawM.Contains("sonnet") ||
                                            (lowRawM.Contains("flash") && lowRawM.Contains("high"));

                    string thinkingTxt = JsonFastHelper.ExtractString(ln, "thinking", 0);
                    int thinkChars = string.IsNullOrEmpty(thinkingTxt) ? 0 : thinkingTxt.Trim().Length;
                    long summaryThinkTok = thinkChars > 0
                        ? Math.Min(outTok > 0 ? outTok : long.MaxValue, JsonFastHelper.EstTextTokensWithTrunc(thinkingTxt))
                        : 0;

                    long estThinkTok = summaryThinkTok;
                    if (outTok > 0 && (turnExpectsThink || thinkChars > 0))
                    {
                        string contentTxt = JsonFastHelper.ExtractString(ln, "content", 0);
                        long contentTok = string.IsNullOrEmpty(contentTxt) ? 0 : JsonFastHelper.EstTextTokensWithTrunc(contentTxt);
                        long toolCallsTok = JsonFastHelper.EstToolCallsTokensFromLine(ln);
                        long visibleOutTok = contentTok + toolCallsTok;
                        long residualThinkTok = outTok > visibleOutTok ? (outTok - visibleOutTok) : 0;
                        estThinkTok = Math.Min(outTok, Math.Max(summaryThinkTok, residualThinkTok));
                    }

                    if (turns.Count == 0 || resetAfterCheckpoint || (prevPromptTotal > 0 && (promptTotal - prevPromptTotal) < -5000))
                    {
                        if (turns.Count > 0)
                        {
                            compacted = true;
                            justCompactedFlag = true;
                        }
                        resetAfterCheckpoint = false;
                        sourceBuckets.Clear();
                        userTokensSum = 0;
                        respThinkTokensSum = 0;
                        toolResultsTokensSum = 0;

                        long cfgRulesEst, cfgSkillsEst, cfgMcpEst;
                        GetBaseConfigWeights(out cfgRulesEst, out cfgSkillsEst, out cfgMcpEst);
                        long cfgBaseSum = cfgRulesEst + cfgSkillsEst + cfgMcpEst;

                        long uInit = Math.Min(promptTotal, interUserEst);
                        long sysAndSummary = Math.Max(4000L, promptTotal - uInit);
                        long summaryPart = 0;
                        if (pendingSummaryFromInput > 0 && sysAndSummary > pendingSummaryFromInput + 6000)
                        {
                            summaryPart = pendingSummaryFromInput;
                            pendingSummaryFromInput = 0;
                        }
                        else if (compacted && compactSumTok > 0 && sysAndSummary > compactSumTok + 8000)
                        {
                            summaryPart = compactSumTok;
                        }
                        else if (compacted && sysAndSummary > cfgBaseSum + 2000)
                        {
                            summaryPart = sysAndSummary - cfgBaseSum;
                        }
                        activeSummaryPart = Math.Max(0L, summaryPart);
                        long pureBasePart = Math.Max(2000L, sysAndSummary - activeSummaryPart);
                        baseSystemTools = sysAndSummary;

                        long rShare = (long)Math.Round(pureBasePart * ((double)cfgRulesEst / Math.Max(1L, cfgBaseSum)));
                        long sShare = (long)Math.Round(pureBasePart * ((double)cfgSkillsEst / Math.Max(1L, cfgBaseSum)));
                        long mShare = Math.Max(500L, pureBasePart - rShare - sShare);

                        addSource("system", I18n.T("用户全局规则 (Rules)", "Global Rules"), rShare);
                        addSource("system", I18n.T("技能目录索引 (Skills)", "Skills Index"), sShare);
                        addSource("system", I18n.T("系统与 MCP 底座", "System & MCP Base"), mShare);
                        if (activeSummaryPart > 0)
                            addSource("system", I18n.T("历史对话折叠摘要", "Compacted Summary"), activeSummaryPart);

                        userTokensSum += uInit;
                        if (uInit > 0) addSource("user", I18n.T("用户提问消息", "User Prompts"), uInit);
                    }
                    else
                    {
                        long deltaPrompt = promptTotal - prevPromptTotal;
                        if (deltaPrompt > 0)
                        {
                            long prevOut = turns[turns.Count - 1].OutTok;
                            long newExternal = Math.Max(0L, deltaPrompt - prevOut);
                            long uPart = Math.Min(newExternal, interUserEst);
                            long toolPart = Math.Max(0L, newExternal - uPart);

                            userTokensSum += uPart;
                            toolResultsTokensSum += toolPart;

                            if (uPart > 0) addSource("user", I18n.T("用户提问消息", "User Prompts"), uPart);
                            if (toolPart > 0)
                            {
                                if (pendingToolLabels.Count > 0)
                                {
                                    long perTool = Math.Max(1L, toolPart / pendingToolLabels.Count);
                                    for (int ti = 0; ti < pendingToolLabels.Count; ti++)
                                        addSource("results", pendingToolLabels[ti], perTool);
                                }
                                else if (interHasGeneric)
                                {
                                    addSource("results", I18n.T("工具返回结果", "Tool Outputs"), toolPart);
                                }
                                else
                                {
                                    addSource("system", I18n.T("系统注入动态上下文", "Dynamic Context"), toolPart);
                                }
                            }
                        }
                    }

                    respThinkTokensSum += outTok;
                    if (outTok > 0)
                        addSource("response", I18n.T("助手思考与回复", "Assistant & Thinking"), outTok);

                    pendingToolLabels = ExtractToolCallLabels(ln);
                    string stepToolSum = BuildCleanActionSummary(ln);
                    interUserEst = 0;
                    interHasGeneric = false;

                    int curStepIdx = (int)JsonFastHelper.ExtractLong(ln, "step_index");
                    string stepCacheKey = st.Key + ":" + curStepIdx;
                    double? waitSec = null;
                    double? durationSec = null;
                    double? toolExecSec = null;
                    double? e2eSec = null;
                    double? tps = null;
                    double? e2eTps = null;
                    if (curTimeUtc > DateTime.MinValue)
                    {
                        // WaitSec only applies at the first step after USER_INPUT (request dispatch overhead)
                        if (turns.Count == groupStartTurnIdx && lastTriggerUtc > DateTime.MinValue && curTimeUtc >= lastTriggerUtc)
                        {
                            double wSec = Math.Round((curTimeUtc - lastTriggerUtc).TotalSeconds, 1);
                            if (wSec >= 0.0 && wSec <= 120.0)
                                waitSec = wSec;
                        }

                        DateTime endUtc = DateTime.MinValue;
                        DateTime nextTurnOrBoundaryUtc = DateTime.MinValue;
                        bool sawToolGeneric = false;

                        // Look ahead: first step after PLANNER_RESPONSE marks end of model streaming;
                        // subsequent PLANNER_RESPONSE (before next USER_INPUT) marks end of tool execution.
                        for (int nxt = i + 1; nxt < rawLines.Length; nxt++)
                        {
                            string nLn = rawLines[nxt].Trim();
                            if (string.IsNullOrEmpty(nLn) || nLn[0] != '{') continue;
                            string nType = JsonFastHelper.ExtractString(nLn, "type", 0);
                            if (nType == "USER_INPUT" || nType == "CHECKPOINT")
                            {
                                if (endUtc == DateTime.MinValue)
                                    _stepFinishUtcMap.TryGetValue(stepCacheKey, out endUtc);
                                break;
                            }

                            string nCreated = JsonFastHelper.ExtractString(nLn, "created_at", 0);
                            DateTime nDt = DateTime.MinValue;
                            if (!string.IsNullOrEmpty(nCreated))
                            {
                                DateTime.TryParse(nCreated, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out nDt);
                            }

                            if (endUtc == DateTime.MinValue)
                            {
                                if (nDt > DateTime.MinValue) endUtc = nDt;
                                if (nType == "GENERIC") sawToolGeneric = true;
                                if (nType == "PLANNER_RESPONSE") break;
                            }
                            else if (nType == "PLANNER_RESPONSE")
                            {
                                if (nDt > DateTime.MinValue) nextTurnOrBoundaryUtc = nDt;
                                break;
                            }
                        }

                        if (endUtc == DateTime.MinValue && i == rawLines.Length - 1 && fi.LastWriteTimeUtc > curTimeUtc)
                        {
                            double tailSec = (fi.LastWriteTimeUtc - curTimeUtc).TotalSeconds;
                            if (tailSec >= 0.4 && tailSec <= 240.0)
                            {
                                endUtc = fi.LastWriteTimeUtc;
                                _stepFinishUtcMap[stepCacheKey] = endUtc;
                            }
                        }

                        if (endUtc > curTimeUtc)
                        {
                            double sec = Math.Round((endUtc - curTimeUtc).TotalSeconds, 1);
                            if (sec >= 0.4 && sec <= 300.0)
                            {
                                durationSec = sec;
                                if (sawToolGeneric && nextTurnOrBoundaryUtc > endUtc)
                                {
                                    double tSec = Math.Round((nextTurnOrBoundaryUtc - endUtc).TotalSeconds, 1);
                                    if (tSec >= 0.1 && tSec <= 600.0)
                                        toolExecSec = tSec;
                                }
                                double totalE2E = Math.Round(sec + (waitSec ?? 0.0) + (toolExecSec ?? 0.0), 1);
                                e2eSec = totalE2E;
                                if (outTok > 0)
                                {
                                    double rawTps = Math.Round(outTok / sec, 1);
                                    if (rawTps <= 900.0)
                                        tps = rawTps;
                                    if (totalE2E >= 0.5)
                                    {
                                        double rawE2ETps = Math.Round(outTok / totalE2E, 1);
                                        if (rawE2ETps <= 900.0)
                                            e2eTps = rawE2ETps;
                                    }
                                }
                            }
                        }
                        prevStepTimeUtc = curTimeUtc;
                        lastTriggerUtc = endUtc > curTimeUtc ? endUtc : curTimeUtc;
                    }

                    double cacheHitPct = promptTotal > 0 ? Math.Round((cacheTok * 100.0) / promptTotal, 1) : 0.0;
                    string prettyMod = PrettyModelName(rawModel);
                    string shortTag, tone;
                    ShortModelBadge(prettyMod, out shortTag, out tone);

                    bool isPostCompactTurn = justCompactedFlag && turns.Count >= 1;
                    justCompactedFlag = false;

                    bool switchedCold = !isPostCompactTurn &&
                                        !string.IsNullOrEmpty(prevShortTag) &&
                                        !string.Equals(prevShortTag, shortTag, StringComparison.OrdinalIgnoreCase) &&
                                        inTok >= 16000 && cacheHitPct < 25.0;
                    bool isCacheDrop = !isPostCompactTurn &&
                                       !switchedCold &&
                                       turns.Count >= 1 &&
                                       prevCacheHitPct >= 65.0 &&
                                       (prevCacheHitPct - cacheHitPct) >= 22.0 &&
                                       inTok >= 18000;
                    string coldReason = "";
                    if (isPostCompactTurn)
                    {
                        coldReason = I18n.TF("上下文折叠后重建缓存 · 载入 {0}", "Cache rebuilt after compaction · {0}",
                            TrayPopupForm.FmtCompact(inTok));
                    }
                    else if (switchedCold)
                    {
                        coldReason = I18n.TF("换模重热 ({0}→{1}) · 载入 {2}", "Model warm-up ({0}->{1}) · {2}",
                            prevShortTag, shortTag, TrayPopupForm.FmtCompact(inTok));
                    }
                    else if (isCacheDrop)
                    {
                        coldReason = I18n.TF("同模掉缓存 ({0:F0}%→{1:F0}%) · 新增 {2}", "Cache drop ({0:F0}%->{1:F0}%) · +{2}",
                            prevCacheHitPct, cacheHitPct, TrayPopupForm.FmtCompact(inTok));
                    }
                    else if (turns.Count >= 1 && inTok >= 30000 && cacheHitPct < 10.0)
                    {
                        isCacheDrop = true;
                        coldReason = I18n.TF("缓存重热 · 全量载入 {0}", "Cache warm-up · {0}",
                            TrayPopupForm.FmtCompact(inTok));
                    }

                    turns.Add(new TurnItem
                    {
                        TurnNo = turns.Count + 1,
                        StepIndex = curStepIdx,
                        Model = prettyMod,
                        ShortTag = shortTag,
                        Tone = tone,
                        InTok = inTok,
                        CacheTok = cacheTok,
                        PromptTotal = promptTotal,
                        OutTok = outTok,
                        TotalUsed = totalUsed,
                        ThinkChars = thinkChars,
                        EstThinkTok = estThinkTok,
                        CacheHitPct = cacheHitPct,
                        WaitSec = waitSec,
                        DurationSec = durationSec,
                        ToolExecSec = toolExecSec,
                        E2ESec = e2eSec,
                        Tps = tps,
                        E2ETps = e2eTps,
                        ToolSummary = stepToolSum,
                        ModelSwitchedCold = switchedCold,
                        IsCacheDropWarn = isCacheDrop,
                        IsPostCompact = isPostCompactTurn,
                        SwitchedFromTag = switchedCold ? prevShortTag : "",
                        ColdCacheReason = coldReason
                    });

                    if (turnsSinceCompact >= 0)
                        turnsSinceCompact++;
                    prevShortTag = shortTag;
                    prevPromptTotal = promptTotal;
                    prevCacheHitPct = cacheHitPct;
                }

                finalizeTurnGroup();

                TurnItem latest = turns.Count > 0 ? turns[turns.Count - 1] : null;
                long actualUsed = latest != null ? latest.TotalUsed : 0;
                long rawCompSum = baseSystemTools + userTokensSum + respThinkTokensSum + toolResultsTokensSum;
                double scale = (actualUsed > 0 && rawCompSum > 0) ? ((double)actualUsed / rawCompSum) : 1.0;

                long cfgR, cfgS, cfgM;
                GetBaseConfigWeights(out cfgR, out cfgS, out cfgM);
                long cfgTotal = Math.Max(1L, cfgR + cfgS + cfgM);
                long scaledSysTotal = (long)Math.Round(baseSystemTools * scale);
                long scaledSummary = Math.Min(scaledSysTotal, (long)Math.Round(activeSummaryPart * scale));
                long scaledPureBase = Math.Max(0L, scaledSysTotal - scaledSummary);
                long scaledRules = (long)Math.Round(scaledPureBase * ((double)cfgR / cfgTotal));
                long scaledSkills = (long)Math.Round(scaledPureBase * ((double)cfgS / cfgTotal));
                long scaledMcpCore = Math.Max(0L, scaledPureBase - scaledRules - scaledSkills);
                double mcpDeclFrac = _cachedMcpCoreEstTok > 0
                    ? ((double)_cachedMcpDeclEstTok / _cachedMcpCoreEstTok)
                    : 0.22;
                long scaledMcpDecl = (long)Math.Round(scaledMcpCore * mcpDeclFrac);
                long scaledCoreSys = Math.Max(0L, scaledMcpCore - scaledMcpDecl);

                long sysFixed = scaledRules + scaledSkills + scaledSummary;
                long toolsFixed = scaledMcpCore;
                long userScaled = (long)Math.Round(userTokensSum * scale);
                long respScaled = (long)Math.Round(respThinkTokensSum * scale);
                long resultsScaled = Math.Max(0L, actualUsed - sysFixed - toolsFixed - userScaled - respScaled);

                var extraSubIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (rawText.IndexOf("invoke_subagent", StringComparison.Ordinal) >= 0 ||
                    rawText.IndexOf("conversation://", StringComparison.Ordinal) >= 0)
                {
                    foreach (Match mSub in RxSubagentUuid.Matches(rawText))
                    {
                        string scid = mSub.Groups[1].Value;
                        if (string.IsNullOrEmpty(scid)) continue;
                        if (string.Equals(scid, st.Cid, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!string.IsNullOrEmpty(st.ParentCid) && string.Equals(scid, st.ParentCid, StringComparison.OrdinalIgnoreCase)) continue;
                        if (st.SideQuestionCids.Contains(scid)) continue;
                        extraSubIds.Add(scid);
                    }
                }

                List<SourceBlock> sortedSources = sourceBuckets.Values
                    .Select(b => new SourceBlock
                    {
                        Category = b.Category,
                        Name = b.Name,
                        Tokens = (long)Math.Round(b.Tokens * scale),
                        Count = b.Count
                    })
                    .Where(b => b.Tokens > 0)
                    .OrderByDescending(b => b.Tokens)
                    .ToList();

                List<SourceBlock> top5 = sortedSources.Take(5).ToList();
                SourceBlock otherBlock = null;
                if (sortedSources.Count > 5)
                {
                    int restCnt = sortedSources.Count - 5;
                    long restTok = sortedSources.Skip(5).Sum(x => x.Tokens);
                    if (restTok > 0)
                    {
                        otherBlock = new SourceBlock
                        {
                            Category = "other",
                            Name = I18n.TF("其余 {0} 项", "{0} more items", restCnt),
                            Tokens = restTok,
                            Count = restCnt
                        };
                    }
                }

                // Build native per-category diagnostic breakdown lists (for click-to-inspect)
                var catBreakdowns = new Dictionary<string, List<BreakdownRowItem>>(StringComparer.OrdinalIgnoreCase);
                long denomAll = Math.Max(1L, actualUsed);

                // 1) System & Fold Summary Breakdown
                var sysBreak = new List<BreakdownRowItem>();
                long sysCombined = Math.Max(1L, sysFixed + toolsFixed);
                if (scaledSummary > 0)
                {
                    sysBreak.Add(new BreakdownRowItem
                    {
                        Label = I18n.T("历史对话折叠摘要", "Compacted Summary"),
                        SubNote = compactPreTok > 0 ? (I18n.T("折叠前 ", "Before ") + TrayPopupForm.FmtCompact(compactPreTok)) : I18n.T("自动压缩保留记忆", "Retained summary memory"),
                        Tokens = scaledSummary,
                        SharePct = scaledSummary * 100.0 / sysCombined
                    });
                }
                sysBreak.Add(new BreakdownRowItem
                {
                    Label = I18n.T("内置指令与原生工具底座", "Built-in System & Core Tools"),
                    SubNote = I18n.T("宿主提示词与核心工具声明", "Host prompt & core tool schemas"),
                    Tokens = scaledCoreSys,
                    SharePct = scaledCoreSys * 100.0 / sysCombined
                });
                sysBreak.Add(new BreakdownRowItem
                {
                    Label = I18n.T("全局工程规则 (Rules)", "Global Rules"),
                    SubNote = I18n.TF("{0} 份常驻规则文件", "{0} active rule files", _cachedRulesCount),
                    Tokens = scaledRules,
                    SharePct = scaledRules * 100.0 / sysCombined
                });
                sysBreak.Add(new BreakdownRowItem
                {
                    Label = I18n.T("技能路由目录 (Skills)", "Skills Catalog"),
                    SubNote = I18n.TF("{0} 项已挂载技能摘要", "{0} mounted skill summaries", _cachedSkillsCount),
                    Tokens = scaledSkills,
                    SharePct = scaledSkills * 100.0 / sysCombined
                });
                sysBreak.Add(new BreakdownRowItem
                {
                    Label = I18n.T("MCP 服务与工具索引", "MCP Servers & Tools"),
                    SubNote = I18n.TF("{0} 个已连接 MCP 服务", "{0} connected MCP servers", _cachedMcpServerCount),
                    Tokens = scaledMcpDecl,
                    SharePct = scaledMcpDecl * 100.0 / sysCombined
                });
                catBreakdowns["system"] = sysBreak.Where(x => x.Tokens > 0).OrderByDescending(x => x.Tokens).ToList();

                // 2) Tool Results & File Read Breakdown
                var resSources = sortedSources.Where(s => s.Category == "results").ToList();
                var resBreak = new List<BreakdownRowItem>();
                long resDenom = Math.Max(1L, resultsScaled > 0 ? resultsScaled : resSources.Sum(x => x.Tokens));
                double resScaleAdj = (resultsScaled > 0 && resSources.Sum(x => x.Tokens) > 0)
                    ? ((double)resultsScaled / resSources.Sum(x => x.Tokens))
                    : 1.0;
                foreach (SourceBlock rs in resSources.Take(6))
                {
                    long adjT = (long)Math.Round(rs.Tokens * resScaleAdj);
                    resBreak.Add(new BreakdownRowItem
                    {
                        Label = rs.Name,
                        SubNote = I18n.TF("累计触发 {0} 次 · 占总窗 {1:F1}%", "{0} calls · {1:F1}% of window", rs.Count, adjT * 100.0 / denomAll),
                        Tokens = adjT,
                        SharePct = Math.Min(100.0, adjT * 100.0 / resDenom)
                    });
                }
                if (resSources.Count > 6)
                {
                    int rRestCnt = resSources.Count - 6;
                    long rRestTok = (long)Math.Round(resSources.Skip(6).Sum(x => x.Tokens) * resScaleAdj);
                    if (rRestTok > 0)
                    {
                        resBreak.Add(new BreakdownRowItem
                        {
                            Label = I18n.TF("其余 {0} 类工具返回", "{0} other tool outputs", rRestCnt),
                            SubNote = I18n.T("长尾工具输出合计", "Remaining long-tail tool outputs"),
                            Tokens = rRestTok,
                            SharePct = Math.Min(100.0, rRestTok * 100.0 / resDenom)
                        });
                    }
                }
                catBreakdowns["results"] = resBreak;

                // 3) Model Thinking & Reply Breakdown
                long sumThinkRaw = 0;
                long sumOutRaw = 0;
                TurnItem peakOutTurn = null;
                for (int ti = 0; ti < turns.Count; ti++)
                {
                    sumThinkRaw += turns[ti].EstThinkTok;
                    sumOutRaw += turns[ti].OutTok;
                    if (peakOutTurn == null || turns[ti].OutTok > peakOutTurn.OutTok)
                        peakOutTurn = turns[ti];
                }
                double thinkRatio = sumOutRaw > 0 ? Math.Min(0.92, Math.Max(0.0, (double)sumThinkRaw / sumOutRaw)) : 0.0;
                long thinkScaled = (long)Math.Round(respScaled * thinkRatio);
                long replyScaled = Math.Max(0L, respScaled - thinkScaled);
                long respDenom = Math.Max(1L, respScaled);
                var respBreak = new List<BreakdownRowItem>();
                respBreak.Add(new BreakdownRowItem
                {
                    Label = I18n.T("内部思考链推演 (Thinking)", "Internal Reasoning (Thinking)"),
                    SubNote = I18n.TF("占总输出 {0:F0}% · 保障推理深度", "{0:F0}% of output · reasoning depth", thinkRatio * 100.0),
                    Tokens = thinkScaled,
                    SharePct = thinkScaled * 100.0 / respDenom
                });
                respBreak.Add(new BreakdownRowItem
                {
                    Label = I18n.T("可见正文与工具调用参数", "Visible Replies & Tool Args"),
                    SubNote = peakOutTurn != null ? I18n.TF("共 {0} 轮 · 单轮峰值 #{1} ({2})", "{0} turns · peak #{1} ({2})", turns.Count, peakOutTurn.TurnNo, TrayPopupForm.FmtCompact(peakOutTurn.OutTok)) : I18n.T("历史回复累计", "Cumulative assistant output"),
                    Tokens = replyScaled,
                    SharePct = replyScaled * 100.0 / respDenom
                });
                catBreakdowns["response"] = respBreak.OrderByDescending(x => x.Tokens).ToList();

                // 4) User Input Breakdown
                SourceBlock usrBlk = sortedSources.FirstOrDefault(s => s.Category == "user");
                int uMsgCnt = usrBlk != null ? Math.Max(1, usrBlk.Count) : 1;
                var userBreak = new List<BreakdownRowItem>
                {
                    new BreakdownRowItem
                    {
                        Label = I18n.T("用户提问与内联文本输入", "User Prompts & Inline Input"),
                        SubNote = I18n.TF("共 {0} 次发送 · 平均每次 {1}", "{0} prompts · avg {1}", uMsgCnt, TrayPopupForm.FmtCompact(userScaled / uMsgCnt)),
                        Tokens = userScaled,
                        SharePct = userScaled > 0 ? 100.0 : 0.0
                    }
                };
                catBreakdowns["user"] = userBreak;

                // Thinking IQ Health Diagnosis (strictly evaluated on latest 6 turns; up to 80 stored for scroll & filter)
                List<TurnItem> recent6 = turns.Skip(Math.Max(0, turns.Count - 6)).ToList();
                List<TurnItem> recentSlice = turns.Skip(Math.Max(0, turns.Count - 80)).ToList();
                int cacheWarnCnt = 0;
                int slowWaitCnt = 0;
                for (int ri = 0; ri < recentSlice.Count; ri++)
                {
                    TurnItem rt = recentSlice[ri];
                    if (rt.IsCacheDropWarn || rt.ModelSwitchedCold || (rt.TurnNo > 1 && rt.CacheHitPct < 35.0))
                        cacheWarnCnt++;
                    if (rt.WaitSec.HasValue && rt.WaitSec.Value >= 6.0)
                        slowWaitCnt++;
                }

                string iqLevel = "idle";
                string iqLabel = I18n.T("思考", "Think");
                string iqPill = "--";
                string iqReason = I18n.T("等待当前窗口完成首轮模型生成", "Waiting for first turn in this session");

                if (recent6.Count > 0)
                {
                    TurnItem lastT = recent6[recent6.Count - 1];
                    string lowM = (lastT.Model ?? "").ToLowerInvariant();
                    bool expectsThink = lowM.Contains("carbon") || lowM.Contains("argon") || lowM.Contains("neon") ||
                                        lowM.Contains("hydrogen") || lowM.Contains("orchestrator") ||
                                        lowM.Contains("opus") || lowM.Contains("sonnet") ||
                                        (lowM.Contains("flash") && lowM.Contains("high"));

                    int zeroStreak = 0;
                    for (int k = recent6.Count - 1; k >= 0; k--)
                    {
                        if (recent6[k].ThinkChars == 0 && recent6[k].EstThinkTok < 20 && recent6[k].OutTok > 180)
                            zeroStreak++;
                        else
                            break;
                    }

                    if (!expectsThink && lastT.EstThinkTok == 0)
                    {
                        iqLevel = "idle";
                        iqLabel = I18n.T("轻量", "Fast");
                        iqPill = I18n.T("高速", "Lite");
                        iqReason = I18n.TF("当前档位 ({0}) 属高速轻量模型，无需强制思考链", "Fast lightweight model ({0}), reasoning trace optional", lastT.Model);
                    }
                    else if (zeroStreak >= 2)
                    {
                        iqLevel = "danger";
                        iqLabel = I18n.T("无思考", "NoThink");
                        iqPill = I18n.TF("连{0}轮", "{0}x", zeroStreak);
                        iqReason = I18n.TF("连续 {0} 轮长输出未检测到思考链文本，建议留意或新开空白窗口", "{0} consecutive long turns without thinking trace", zeroStreak);
                    }
                    else
                    {
                        List<TurnItem> withThink = recent6.Where(t => t.EstThinkTok > 0).ToList();
                        int thinkRate = (int)Math.Round(withThink.Count * 100.0 / recent6.Count);
                        long avgThink = withThink.Count > 0 ? (long)Math.Round(withThink.Average(t => (double)t.EstThinkTok)) : 0;

                        iqLevel = "ok";
                        iqLabel = I18n.T("思考", "Think");
                        long pillNum = lastT.EstThinkTok > 0 ? lastT.EstThinkTok : avgThink;
                        iqPill = pillNum >= 1000 ? string.Format("{0:F1}k", pillNum / 1000.0) : pillNum.ToString();

                        string timingSummary;
                        if (lastT.DurationSec.HasValue && lastT.ToolExecSec.HasValue && lastT.ToolExecSec.Value >= 0.5)
                        {
                            timingSummary = I18n.TF("模型 {0:F1}s + 工具 {1:F1}s", "Model {0:F1}s + Tools {1:F1}s", lastT.DurationSec.Value, lastT.ToolExecSec.Value);
                        }
                        else if (lastT.DurationSec.HasValue && lastT.GroupTotalSteps > 1 && lastT.GroupTotalSec > lastT.DurationSec.Value + 0.5)
                        {
                            timingSummary = I18n.TF("本步 {0:F1}s (本问{1}步共 {2:F0}s)", "Step {0:F1}s ({1} steps, {2:F0}s total)", lastT.DurationSec.Value, lastT.GroupTotalSteps, lastT.GroupTotalSec);
                        }
                        else if (lastT.DurationSec.HasValue)
                        {
                            timingSummary = I18n.TF("模型 {0:F1}s", "Model {0:F1}s", lastT.DurationSec.Value);
                        }
                        else
                        {
                            timingSummary = "—";
                        }

                        iqReason = I18n.TF("近 {0} 轮思考率 {1}% · 均思 {2} tok · 最新 {3}", "Last {0} turns: {1}% thought · avg {2} tok · {3}",
                            recent6.Count, thinkRate, avgThink, timingSummary);
                    }
                }

                recentSlice.Reverse();

                int activeElapsedSec = st.IsWorking && st.LastStepLocal > DateTime.MinValue
                    ? Math.Max(1, (int)Math.Round((DateTime.Now - st.LastStepLocal).TotalSeconds))
                    : 0;

                string liveBannerLv = "";
                string liveBannerTxt = "";
                if (st.IsWorking && activeElapsedSec >= 18 && actualUsed >= 380000 && pendingToolLabels.Count == 0)
                {
                    liveBannerLv = "compact";
                    liveBannerTxt = I18n.TF("高负载运行 ({0}s) · 处于对话折叠或全量重热阶段", "Heavy load ({0}s) · compacting or warming cache", activeElapsedSec);
                }
                else if (latest != null && latest.ModelSwitchedCold)
                {
                    liveBannerLv = "cold_switch";
                    liveBannerTxt = I18n.TF("换模重热 ({0} → {1}) · 全量载入 {2}", "Model warm-up ({0} -> {1}) · {2}",
                        latest.SwitchedFromTag, latest.ShortTag, TrayPopupForm.FmtCompact(latest.InTok));
                }
                else if (latest != null && latest.IsCacheDropWarn)
                {
                    liveBannerLv = "cold_switch";
                    liveBannerTxt = latest.ColdCacheReason;
                }
                else if (compacted && turnsSinceCompact >= 0 && turnsSinceCompact <= 2 && compactPreTok > 0)
                {
                    int savedPct = (compactSumTok > 0 && compactPreTok > compactSumTok)
                        ? (int)Math.Round((1.0 - (double)compactSumTok / compactPreTok) * 100.0)
                        : 0;
                    liveBannerLv = "compact_done";
                    liveBannerTxt = I18n.TF("对话已折叠 · {0} → ~{1}{2}", "Compacted · {0} -> ~{1}{2}",
                        TrayPopupForm.FmtCompact(compactPreTok),
                        compactSumTok > 0 ? TrayPopupForm.FmtCompact(compactSumTok) : I18n.T("精简", "Trimmed"),
                        savedPct >= 10 ? (" (-" + savedPct + "%)") : "");
                }

                var hud = new HudSnapshot
                {
                    Key = st.Key,
                    Cid = st.Cid,
                    ShortId = st.Cid.Length >= 8 ? st.Cid.Substring(0, 8) : st.Cid,
                    Title = !string.IsNullOrEmpty(st.Title) ? st.Title : (!string.IsNullOrEmpty(fallbackTitle) ? fallbackTitle : (I18n.T("会话 ", "Chat ") + st.Cid.Substring(0, 8))),
                    Surface = I18n.FmtSurfaceLabel(st.Surface),
                    IsSideQuestion = st.IsSideQuestion,
                    TotalSteps = totalSteps,
                    TotalTurns = turns.Count,
                    IsRunning = st.IsWorking,
                    ActiveElapsedSec = activeElapsedSec,
                    ActivePhaseLabel = pendingToolLabels.Count > 0 ? I18n.T("执行中", "Running") : I18n.T("思考中", "Thinking"),
                    Compacted = compacted,
                    CompactPreTokens = compactPreTok,
                    CompactSummaryTokens = compactSumTok,
                    TurnsSinceCompact = turnsSinceCompact,
                    LiveBannerLevel = liveBannerLv,
                    LiveBannerText = liveBannerTxt,
                    UpdatedAt = st.LastStepLocal > DateTime.MinValue ? st.LastStepLocal : fi.LastWriteTime,
                    Latest = latest,
                    SysTokens = sysFixed,
                    ToolsTokens = toolsFixed,
                    SysRulesTokens = scaledRules,
                    SysSkillsTokens = scaledSkills,
                    SysCoreTokens = scaledCoreSys,
                    SysMcpDeclTokens = scaledMcpDecl,
                    SysMcpCoreTokens = scaledMcpCore,
                    SysSummaryTokens = scaledSummary,
                    RulesFileCount = _cachedRulesCount,
                    SkillsEntryCount = _cachedSkillsCount,
                    McpServerCount = _cachedMcpServerCount,
                    UserTokens = userScaled,
                    RespTokens = respScaled,
                    ResultsTokens = resultsScaled,
                    TopSources = top5,
                    OtherSources = otherBlock,
                    CategoryBreakdowns = catBreakdowns,
                    CacheWarnTurns = cacheWarnCnt,
                    SlowWaitTurns = slowWaitCnt,
                    IqLevel = iqLevel,
                    IqLabel = iqLabel,
                    IqPill = iqPill,
                    IqReason = iqReason,
                    RecentTurns = recentSlice
                };

                RefreshHudSubagentsFromMemory(st, hud, extraSubIds);

                if (_hudCache.Count >= 16)
                    _hudCache.Clear();

                _hudCache[st.Key] = new HudCacheEntry
                {
                    Length = fi.Length,
                    LastWriteUtc = fi.LastWriteTimeUtc,
                    Data = hud
                };
                return hud;
            }
            catch
            {
                return null;
            }
        }

        private void RefreshHudSubagentsFromMemory(ConvTrackState st, HudSnapshot hud, HashSet<string> extraScannedIds)
        {
            if (st == null || hud == null) return;

            string surfPrefix = st.Key.Contains(":") ? st.Key.Substring(0, st.Key.IndexOf(':') + 1) : "";

            // Populate parent link if viewing a subagent or side-question
            if (!string.IsNullOrEmpty(st.ParentCid) && !string.Equals(st.ParentCid, st.Cid, StringComparison.OrdinalIgnoreCase))
            {
                hud.IsSubagent = !st.IsSideQuestion;
                string pKey = surfPrefix + st.ParentCid;
                ConvTrackState pSt;
                if (_states.TryGetValue(pKey, out pSt) && pSt != null)
                {
                    if (pSt.IsSideQuestion && !string.IsNullOrEmpty(pSt.ParentCid))
                    {
                        ConvTrackState topSt;
                        if (_states.TryGetValue(surfPrefix + pSt.ParentCid, out topSt) && topSt != null)
                            pSt = topSt;
                    }
                    hud.ParentConvKey = pSt.Key;
                    hud.ParentTitle = !string.IsNullOrEmpty(pSt.Title) ? pSt.Title : (I18n.T("主窗口 ", "Main ") + pSt.Cid.Substring(0, 8));
                }
                else
                {
                    hud.ParentConvKey = pKey;
                    hud.ParentTitle = I18n.T("主窗口 ", "Main ") + (st.ParentCid.Length >= 8 ? st.ParentCid.Substring(0, 8) : st.ParentCid);
                }
                if (!string.IsNullOrEmpty(st.AssignedRole))
                    hud.Title = st.AssignedRole;
            }

            var subIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < st.SpawnedSubCids.Count; i++)
                subIds.Add(st.SpawnedSubCids[i]);

            var validParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { st.Cid };
            for (int i = 0; i < st.SideQuestionCids.Count; i++)
                validParents.Add(st.SideQuestionCids[i]);

            foreach (var cand in _states.Values)
            {
                if (cand == null || cand.IsSideQuestion || !cand.IsSubagent) continue;
                if (!string.IsNullOrEmpty(cand.ParentCid) &&
                    validParents.Contains(cand.ParentCid) &&
                    !string.Equals(cand.Cid, st.Cid, StringComparison.OrdinalIgnoreCase))
                {
                    subIds.Add(cand.Cid);
                }
            }

            int subCnt = 0;
            int subWorking = 0;
            int subTurns = 0;
            long subPromptTok = 0;
            long subCacheTok = 0;
            long subOutTok = 0;
            var briefs = new List<SubagentBriefItem>();

            foreach (string scid in subIds)
            {
                if (string.IsNullOrEmpty(scid) || string.Equals(scid, st.Cid, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(st.ParentCid) && string.Equals(scid, st.ParentCid, StringComparison.OrdinalIgnoreCase)) continue;
                if (st.SideQuestionCids.Contains(scid)) continue;

                ConvTrackState subSt;
                if (!_states.TryGetValue(surfPrefix + scid, out subSt) || subSt == null) continue;
                if (subSt.IsSideQuestion || !subSt.IsSubagent) continue;
                if ((subSt.LedgerTurns == null || subSt.LedgerTurns.Count == 0) && !subSt.IsWorking) continue;

                if (string.IsNullOrEmpty(subSt.ParentCid))
                    subSt.ParentCid = st.Cid;

                string roleTitle = subSt.AssignedRole;
                if (string.IsNullOrEmpty(roleTitle))
                    st.SpawnedSubRoles.TryGetValue(scid, out roleTitle);
                if (!string.IsNullOrEmpty(roleTitle) && string.IsNullOrEmpty(subSt.AssignedRole))
                    subSt.AssignedRole = roleTitle;
                if (string.IsNullOrEmpty(roleTitle))
                    roleTitle = !string.IsNullOrEmpty(subSt.Title) ? subSt.Title : (I18n.T("子代理 ", "Subagent ") + scid.Substring(0, 6));

                long sPrompt = 0, sCache = 0, sOut = 0;
                int sTurns = subSt.LedgerTurns != null ? subSt.LedgerTurns.Count : 0;
                for (int si = 0; si < sTurns; si++)
                {
                    LedgerTurnRecord ltr = subSt.LedgerTurns[si];
                    sPrompt += (ltr.InputTokens + ltr.CacheTokens);
                    sCache += ltr.CacheTokens;
                    sOut += ltr.OutputTokens;
                }

                subCnt++;
                if (subSt.IsWorking) subWorking++;
                subTurns += sTurns;
                subPromptTok += sPrompt;
                subCacheTok += sCache;
                subOutTok += sOut;

                int elap = subSt.IsWorking && subSt.LastStepLocal > DateTime.MinValue
                    ? Math.Max(1, (int)Math.Round((DateTime.Now - subSt.LastStepLocal).TotalSeconds))
                    : 0;
                double cPct = sPrompt > 0 ? Math.Round(sCache * 100.0 / sPrompt, 1) : 0.0;
                string actTxt = !string.IsNullOrEmpty(subSt.LastActionSummary)
                    ? subSt.LastActionSummary
                    : (subSt.IsWorking ? I18n.T("正在推演任务", "Working...") : I18n.T("已完成汇报", "Completed"));

                briefs.Add(new SubagentBriefItem
                {
                    Key = subSt.Key,
                    Cid = scid,
                    ShortId = scid.Length >= 6 ? scid.Substring(0, 6) : scid,
                    RoleTitle = roleTitle,
                    ModelBadge = !string.IsNullOrEmpty(subSt.LastShortModel) ? subSt.LastShortModel : I18n.T("同模", "Same"),
                    Tone = !string.IsNullOrEmpty(subSt.LastModelTone) ? subSt.LastModelTone : "argon",
                    IsWorking = subSt.IsWorking,
                    ElapsedSec = elap,
                    Turns = sTurns,
                    ContextUsed = subSt.LatestContextUsed,
                    TotalTokens = sPrompt + sOut,
                    CacheHitPct = cPct,
                    LastAction = actTxt
                });
            }

            briefs = briefs
                .OrderByDescending(b => b.IsWorking ? 1 : 0)
                .ThenByDescending(b => b.TotalTokens)
                .ToList();

            hud.SubagentCount = subCnt;
            hud.SubagentWorkingCount = subWorking;
            hud.SubagentTurns = subTurns;
            hud.SubagentTokens = subPromptTok + subOutTok;
            hud.SubagentCachePct = subPromptTok > 0 ? Math.Round(subCacheTok * 100.0 / subPromptTok, 1) : 0.0;
            hud.Subagents = briefs;
        }

        private static List<string> ExtractInvokeSubagentRoles(string line)
        {
            var roles = new List<string>();
            int tcPos = line.IndexOf("\"tool_calls\"", StringComparison.Ordinal);
            if (tcPos < 0) return roles;
            int searchPos = tcPos;
            while (searchPos < line.Length && roles.Count < 8)
            {
                int nameKey = line.IndexOf("\"name\"", searchPos, StringComparison.Ordinal);
                if (nameKey < 0) break;
                int nextNameKey = line.IndexOf("\"name\"", nameKey + 6, StringComparison.Ordinal);
                int sliceEnd = nextNameKey > 0 ? nextNameKey : line.Length;
                string sub = line.Substring(nameKey, sliceEnd - nameKey);
                string rawName = JsonFastHelper.ExtractString(sub, "name", 0);
                if (rawName == "invoke_subagent")
                {
                    string role = CleanArgValue(JsonFastHelper.ExtractString(sub, "Role", 0));
                    if (!string.IsNullOrEmpty(role))
                        roles.Add(role);
                }
                if (nextNameKey < 0) break;
                searchPos = nextNameKey;
            }
            return roles;
        }

        private static List<string> ExtractToolCallLabels(string line)
        {
            var list = new List<string>();
            int tcPos = line.IndexOf("\"tool_calls\"", StringComparison.Ordinal);
            if (tcPos < 0) return list;

            int searchPos = tcPos;
            while (searchPos < line.Length && list.Count < 8)
            {
                int nameKey = line.IndexOf("\"name\"", searchPos, StringComparison.Ordinal);
                if (nameKey < 0) break;

                int nextNameKey = line.IndexOf("\"name\"", nameKey + 6, StringComparison.Ordinal);
                int sliceEnd = nextNameKey > 0 ? nextNameKey : line.Length;
                string sub = line.Substring(nameKey, sliceEnd - nameKey);

                string rawName = JsonFastHelper.ExtractString(sub, "name", 0);
                if (!string.IsNullOrEmpty(rawName) && !IsInternalNoiseTool(rawName))
                {
                    string verb = I18n.GetToolLabel(rawName);
                    if (string.IsNullOrEmpty(verb))
                        verb = rawName;

                    string hint = "";
                    if (rawName == "read_file" || rawName == "write_file" || rawName == "edit_file" || rawName == "view_file")
                    {
                        string pVal = CleanArgValue(JsonFastHelper.ExtractString(sub, "Path", 0));
                        if (string.IsNullOrEmpty(pVal))
                            pVal = CleanArgValue(JsonFastHelper.ExtractString(sub, "path", 0));
                        if (!string.IsNullOrEmpty(pVal))
                        {
                            try { hint = Path.GetFileName(pVal.Replace('/', '\\')); }
                            catch { hint = ShortenText(pVal, 14); }
                        }
                    }
                    else if (rawName == "run_command")
                    {
                        string ts = CleanArgValue(JsonFastHelper.ExtractString(sub, "toolSummary", 0));
                        hint = !string.IsNullOrEmpty(ts) ? ShortenText(ts, 12) : I18n.T("命令执行", "Shell Cmd");
                    }
                    else if (rawName == "invoke_subagent")
                    {
                        string role = CleanArgValue(JsonFastHelper.ExtractString(sub, "Role", 0));
                        hint = !string.IsNullOrEmpty(role) ? ShortenText(role, 12) : "";
                    }
                    else if (rawName == "search_web")
                    {
                        string q = CleanArgValue(JsonFastHelper.ExtractString(sub, "query", 0));
                        hint = !string.IsNullOrEmpty(q) ? ShortenText(q, 12) : "";
                    }

                    list.Add(!string.IsNullOrEmpty(hint) ? (verb + " · " + hint) : verb);
                }

                if (nextNameKey < 0) break;
                searchPos = nextNameKey;
            }
            return list;
        }

        private static string CleanArgValue(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            string s = val.Trim();
            while (s.Length >= 2 && ((s[0] == '"' && s[s.Length - 1] == '"') || (s[0] == '\'' && s[s.Length - 1] == '\'')))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            s = s.Replace("\\\\", "\\").Replace("\\\"", "\"");
            return JsonFastHelper.StripSurrogates(s).Trim('"', '\'', ' ');
        }

        private DashboardData BuildDashboardData(DateTime now, string todayKey, string selectedConvKey, string activeWindowTitle)
        {
            var data = new DashboardData
            {
                TodayDateStr = todayKey,
                RefreshedAt = now
            };

            string yesterdayKey = now.Date.AddDays(-1).ToString("yyyy-MM-dd");
            DateTime d7Start = now.Date.AddDays(-6);
            DateTime d30Start = now.Date.AddDays(-29);

            var modelsToday = new Dictionary<string, ModelTokenStat>(StringComparer.OrdinalIgnoreCase);
            var modelsYesterday = new Dictionary<string, ModelTokenStat>(StringComparer.OrdinalIgnoreCase);
            var models7d = new Dictionary<string, ModelTokenStat>(StringComparer.OrdinalIgnoreCase);
            var models30d = new Dictionary<string, ModelTokenStat>(StringComparer.OrdinalIgnoreCase);
            var modelsAll = new Dictionary<string, ModelTokenStat>(StringComparer.OrdinalIgnoreCase);
            var toolsToday = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var toolsYesterday = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var tools7d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var tools30d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var toolsAll = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var convsToday = new Dictionary<string, LedgerConvRankItem>(StringComparer.OrdinalIgnoreCase);
            var convsYesterday = new Dictionary<string, LedgerConvRankItem>(StringComparer.OrdinalIgnoreCase);
            var convs7d = new Dictionary<string, LedgerConvRankItem>(StringComparer.OrdinalIgnoreCase);
            var convs30d = new Dictionary<string, LedgerConvRankItem>(StringComparer.OrdinalIgnoreCase);
            var convsAll = new Dictionary<string, LedgerConvRankItem>(StringComparer.OrdinalIgnoreCase);
            var countedSubToday = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var countedSubYest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var countedSub7d = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var countedSub30d = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var countedSubAll = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var liveDayTotals = new Dictionary<string, KeyValuePair<long, double>>(StringComparer.Ordinal);
            var liveDaySessions = new Dictionary<string, int>(StringComparer.Ordinal);

            Action<Dictionary<string, LedgerConvRankItem>, HashSet<string>, ConvTrackState, ModelTokenStat> accConv =
                (cMap, subSeen, curSt, ms) =>
                {
                    ConvTrackState ownerSt = curSt;
                    bool rolledFromSub = curSt.IsSubagent && !curSt.IsSideQuestion;
                    string surfPrefix = curSt.Key.Contains(":") ? curSt.Key.Substring(0, curSt.Key.IndexOf(':') + 1) : "";
                    for (int hop = 0; hop < 3; hop++)
                    {
                        if (string.IsNullOrEmpty(ownerSt.ParentCid) || string.Equals(ownerSt.ParentCid, ownerSt.Cid, StringComparison.OrdinalIgnoreCase))
                            break;
                        ConvTrackState pSt;
                        if (_states.TryGetValue(surfPrefix + ownerSt.ParentCid, out pSt) && pSt != null)
                        {
                            if (ownerSt.IsSubagent && !ownerSt.IsSideQuestion)
                                rolledFromSub = true;
                            ownerSt = pSt;
                        }
                        else
                        {
                            break;
                        }
                    }

                    LedgerConvRankItem row;
                    if (!cMap.TryGetValue(ownerSt.Key, out row))
                    {
                        string dispTitle = !string.IsNullOrEmpty(ownerSt.Title)
                            ? ownerSt.Title
                            : (!string.IsNullOrEmpty(ownerSt.AssignedRole) ? ownerSt.AssignedRole : (I18n.T("会话 ", "Chat ") + ownerSt.Cid.Substring(0, 8)));
                        row = new LedgerConvRankItem
                        {
                            ConvKey = ownerSt.Key,
                            Title = dispTitle,
                            Surface = ownerSt.Surface,
                            IsWorking = ownerSt.IsWorking,
                            LastTime = ownerSt.LastStepLocal
                        };
                        cMap[ownerSt.Key] = row;
                    }
                    if (curSt.IsWorking) row.IsWorking = true;
                    if (curSt.LastStepLocal > row.LastTime) row.LastTime = curSt.LastStepLocal;
                    row.InputTokens += ms.InputTokens;
                    row.CacheTokens += ms.CacheTokens;
                    row.OutputTokens += ms.OutputTokens;
                    row.Calls += ms.Calls;
                    row.CostUsd += ms.CostUsd;
                    if (rolledFromSub)
                    {
                        row.SubTokens += ms.TotalTokens;
                        string pairKey = ownerSt.Key + "->" + curSt.Key;
                        if (subSeen.Add(pairKey))
                            row.SubCount++;
                    }
                };

            long allPrompt = 0;
            long allCache = 0;
            DateTime cutoff5h = now.AddHours(-5.0);
            long r5hCarbon = 0, r5hArgon = 0, r5hFast = 0, r5hOther = 0;
            int c5hCarbon = 0, c5hArgon = 0, c5hFast = 0, c5hOther = 0;

            var orderedStates = _states.Values
                .Where(s => s.LastStepLocal > DateTime.MinValue || s.LastUserViewUtc > DateTime.MinValue)
                .OrderByDescending(s => GetUserFocusUtc(s))
                .ToList();

            foreach (var st in _states.Values)
            {
                if (st.IsWorking) data.ActiveWorkingCount++;

                if (st.LedgerTurns != null && st.LedgerTurns.Count > 0)
                {
                    for (int li = st.LedgerTurns.Count - 1; li >= 0; li--)
                    {
                        LedgerTurnRecord ltr = st.LedgerTurns[li];
                        if (ltr.TimeLocal < cutoff5h) break;
                        long tot = ltr.TotalTokens;
                        data.Rolling5hTokens += tot;
                        data.Rolling5hTurns++;
                        string mLow = (ltr.Model ?? "").ToLowerInvariant();
                        if (mLow.Contains("carbon") || mLow.Contains("neon")) { r5hCarbon += tot; c5hCarbon++; }
                        else if (mLow.Contains("argon")) { r5hArgon += tot; c5hArgon++; }
                        else if (mLow.Contains("flash") || mLow.Contains("hydrogen")) { r5hFast += tot; c5hFast++; }
                        else { r5hOther += tot; c5hOther++; }
                    }
                }

                bool hitToday = false, hitYesterday = false, hit7d = false, hit30d = false, hitAll = false;
                foreach (var dayKv in st.ByDateModel)
                {
                    string dKey = dayKv.Key;
                    DateTime dDate;
                    bool validDate = DateTime.TryParseExact(dKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dDate);

                    bool isToday = (dKey == todayKey);
                    bool isYesterday = (dKey == yesterdayKey);
                    bool is7d = validDate && (dDate >= d7Start && dDate <= now.Date);
                    bool is30d = validDate && (dDate >= d30Start && dDate <= now.Date);

                    int sCnt;
                    liveDaySessions.TryGetValue(dKey, out sCnt);
                    liveDaySessions[dKey] = sCnt + 1;

                    foreach (var mKv in dayKv.Value)
                    {
                        string mName = mKv.Key;
                        ModelTokenStat ms = mKv.Value;

                        allPrompt += (ms.InputTokens + ms.CacheTokens);
                        allCache += ms.CacheTokens;

                        KeyValuePair<long, double> curDay;
                        liveDayTotals.TryGetValue(dKey, out curDay);
                        liveDayTotals[dKey] = new KeyValuePair<long, double>(curDay.Key + ms.TotalTokens, curDay.Value + ms.CostUsd);

                        hitAll = true;
                        Accumulate(data.All, modelsAll, mName, ms);
                        accConv(convsAll, countedSubAll, st, ms);
                        if (isToday) { hitToday = true; Accumulate(data.Today, modelsToday, mName, ms); accConv(convsToday, countedSubToday, st, ms); }
                        if (isYesterday) { hitYesterday = true; Accumulate(data.Yesterday, modelsYesterday, mName, ms); accConv(convsYesterday, countedSubYest, st, ms); }
                        if (is7d) { hit7d = true; Accumulate(data.Days7, models7d, mName, ms); accConv(convs7d, countedSub7d, st, ms); }
                        if (is30d) { hit30d = true; Accumulate(data.Days30, models30d, mName, ms); accConv(convs30d, countedSub30d, st, ms); }
                    }
                }

                foreach (var dayToolKv in st.ByDateModelTools)
                {
                    string dKey = dayToolKv.Key;
                    DateTime dDate;
                    bool validDate = DateTime.TryParseExact(dKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dDate);

                    bool isToday = (dKey == todayKey);
                    bool isYesterday = (dKey == yesterdayKey);
                    bool is7d = validDate && (dDate >= d7Start && dDate <= now.Date);
                    bool is30d = validDate && (dDate >= d30Start && dDate <= now.Date);

                    foreach (var modTools in dayToolKv.Value.Values)
                    {
                        AccumulateTools(data.All, toolsAll, modTools);
                        if (isToday) AccumulateTools(data.Today, toolsToday, modTools);
                        if (isYesterday) AccumulateTools(data.Yesterday, toolsYesterday, modTools);
                        if (is7d) AccumulateTools(data.Days7, tools7d, modTools);
                        if (is30d) AccumulateTools(data.Days30, tools30d, modTools);
                    }
                }

                if (hitToday) data.Today.ConvCount++;
                if (hitYesterday) data.Yesterday.ConvCount++;
                if (hit7d) data.Days7.ConvCount++;
                if (hit30d) data.Days30.ConvCount++;
                if (hitAll) data.All.ConvCount++;
            }

            data.Today.Label = I18n.T("今日实时", "Today");
            data.Today.ModelRanking = modelsToday.OrderByDescending(x => x.Value.TotalTokens).ToList();
            data.Today.ToolRanking = toolsToday.OrderByDescending(x => x.Value).ToList();
            data.Today.ConvRanking = convsToday.Values.OrderByDescending(x => x.TotalTokens).Take(30).ToList();

            data.Yesterday.Label = I18n.T("昨日总计", "Yesterday");
            data.Yesterday.ModelRanking = modelsYesterday.OrderByDescending(x => x.Value.TotalTokens).ToList();
            data.Yesterday.ToolRanking = toolsYesterday.OrderByDescending(x => x.Value).ToList();
            data.Yesterday.ConvRanking = convsYesterday.Values.OrderByDescending(x => x.TotalTokens).Take(30).ToList();

            data.Days7.Label = I18n.T("近 7 天", "Last 7 Days");
            data.Days7.ModelRanking = models7d.OrderByDescending(x => x.Value.TotalTokens).ToList();
            data.Days7.ToolRanking = tools7d.OrderByDescending(x => x.Value).ToList();
            data.Days7.ConvRanking = convs7d.Values.OrderByDescending(x => x.TotalTokens).Take(30).ToList();

            data.Days30.Label = I18n.T("近 30 天", "Last 30 Days");
            data.Days30.ModelRanking = models30d.OrderByDescending(x => x.Value.TotalTokens).ToList();
            data.Days30.ToolRanking = tools30d.OrderByDescending(x => x.Value).ToList();
            data.Days30.ConvRanking = convs30d.Values.OrderByDescending(x => x.TotalTokens).Take(30).ToList();

            data.All.Label = I18n.T("全部日志", "All Logs");
            data.All.ModelRanking = modelsAll.OrderByDescending(x => x.Value.TotalTokens).ToList();
            data.All.ToolRanking = toolsAll.OrderByDescending(x => x.Value).ToList();
            data.All.ConvRanking = convsAll.Values.OrderByDescending(x => x.TotalTokens).Take(30).ToList();

            long grandTok = 0;
            double grandUsd = 0.0;
            foreach (var kv in liveDayTotals)
            {
                grandTok += kv.Value.Key;
                grandUsd += kv.Value.Value;
            }
            data.AllTimeTokens = grandTok;
            data.AllTimeUsd = grandUsd;
            data.AllTimeCachePct = allPrompt > 0 ? Math.Round(allCache * 100.0 / allPrompt, 1) : 92.0;

            data.Rolling5hCarbonTok = r5hCarbon;
            data.Rolling5hArgonTok = r5hArgon;
            data.Rolling5hFastTok = r5hFast;
            data.Rolling5hOtherTok = r5hOther;

            var poolParts = new List<string>(4);
            var tipParts = new List<string>(4);
            if (r5hCarbon > 0) { poolParts.Add(I18n.TF("Carbon {0}轮", "Carbon {0}t", c5hCarbon)); tipParts.Add(I18n.TF("Carbon {0}轮 ({1})", "Carbon {0}t ({1})", c5hCarbon, I18n.FmtBigTokens(r5hCarbon))); }
            if (r5hArgon > 0) { poolParts.Add(I18n.TF("Argon {0}轮", "Argon {0}t", c5hArgon)); tipParts.Add(I18n.TF("Argon {0}轮 ({1})", "Argon {0}t ({1})", c5hArgon, I18n.FmtBigTokens(r5hArgon))); }
            if (r5hFast > 0) { poolParts.Add(I18n.TF("Flash {0}轮", "Flash {0}t", c5hFast)); tipParts.Add(I18n.TF("Flash/Hydro {0}轮 ({1})", "Flash/Hydro {0}t ({1})", c5hFast, I18n.FmtBigTokens(r5hFast))); }
            if (r5hOther > 0) { poolParts.Add(I18n.TF("其他 {0}轮", "Other {0}t", c5hOther)); tipParts.Add(I18n.TF("Claude/GPT {0}轮 ({1})", "Claude/GPT {0}t ({1})", c5hOther, I18n.FmtBigTokens(r5hOther))); }

            string p5Text = poolParts.Count > 0 ? string.Join(" · ", poolParts.ToArray()) : I18n.T("暂无对话", "No recent turns");
            data.Rolling5hStripText = I18n.TF("{0} | 7天 {1}", "{0} | 7d {1}", p5Text, I18n.FmtBigTokens(data.Days7.TotalTokens));
            data.Rolling5hHoverTip = I18n.TF("过去5小时共跑 {0} 轮 ({1}): {2} · 过去7天累计 {3}",
                "Last 5h: {0} turns ({1}): {2} · Last 7d: {3}",
                data.Rolling5hTurns,
                I18n.FmtBigTokens(data.Rolling5hTokens),
                tipParts.Count > 0 ? string.Join(", ", tipParts.ToArray()) : I18n.T("无记录", "None"),
                I18n.FmtBigTokens(data.Days7.TotalTokens));

            for (int i = 6; i >= 0; i--)
            {
                DateTime dt = now.Date.AddDays(-i);
                string dk = dt.ToString("yyyy-MM-dd");
                KeyValuePair<long, double> liveVal;
                liveDayTotals.TryGetValue(dk, out liveVal);
                int sessCnt;
                liveDaySessions.TryGetValue(dk, out sessCnt);

                data.Trend7d.Add(new DayBarPoint
                {
                    DateKey = dk,
                    Label = i == 0 ? I18n.T("今日", "Today") : (i == 1 ? I18n.T("昨日", "Yest") : dt.ToString("MM/dd")),
                    Sessions = sessCnt,
                    Tokens = liveVal.Key,
                    IsToday = (i == 0)
                });
            }

            foreach (var st in orderedStates.Where(s => !s.IsSubagent && !s.IsSideQuestion).Take(10))
            {
                string t = !string.IsNullOrEmpty(st.Title) ? st.Title : (I18n.T("会话 ", "Chat ") + st.Cid.Substring(0, 8));
                data.RecentOptions.Add(new ConvOptionItem
                {
                    Key = st.Key,
                    Cid = st.Cid,
                    Surface = I18n.FmtSurfaceLabel(st.Surface),
                    Title = t,
                    LastTime = st.LastStepLocal,
                    IsWorking = st.IsWorking
                });
            }

            ConvTrackState targetState = null;
            if (!string.IsNullOrEmpty(selectedConvKey))
            {
                if (!_states.TryGetValue(selectedConvKey, out targetState))
                {
                    string cleanPart = selectedConvKey.Contains(":")
                        ? selectedConvKey.Substring(selectedConvKey.IndexOf(':') + 1)
                        : selectedConvKey;
                    targetState = _states.Values.FirstOrDefault(s =>
                        s.Cid.StartsWith(cleanPart, StringComparison.OrdinalIgnoreCase) ||
                        s.Key.IndexOf(selectedConvKey, StringComparison.OrdinalIgnoreCase) >= 0);
                }
            }
            if (targetState == null)
            {
                targetState = MatchStateByWindowTitle(activeWindowTitle, orderedStates);
            }
            if (targetState == null && orderedStates.Count > 0)
            {
                targetState = orderedStates[0];
            }

            // Ensure the auto-matched active window sits at index 0 of RecentOptions so UI header matches 100%
            if (targetState != null && string.IsNullOrEmpty(selectedConvKey))
            {
                int existIdx = data.RecentOptions.FindIndex(o => o.Key == targetState.Key);
                if (existIdx > 0)
                {
                    ConvOptionItem item = data.RecentOptions[existIdx];
                    data.RecentOptions.RemoveAt(existIdx);
                    data.RecentOptions.Insert(0, item);
                }
                else if (existIdx < 0)
                {
                    data.RecentOptions.Insert(0, new ConvOptionItem
                    {
                        Key = targetState.Key,
                        Cid = targetState.Cid,
                        Surface = targetState.Surface,
                        Title = !string.IsNullOrEmpty(targetState.Title) ? targetState.Title : (I18n.T("会话 ", "Chat ") + targetState.Cid.Substring(0, 8)),
                        LastTime = targetState.LastStepLocal,
                        IsWorking = targetState.IsWorking
                    });
                }
            }
            if (targetState != null)
            {
                data.ActiveHud = GetOrParseHud(targetState);
                if (data.ActiveHud != null && string.IsNullOrEmpty(stTitleSafe(targetState)) && !string.IsNullOrEmpty(data.ActiveHud.Title))
                {
                    targetState.Title = data.ActiveHud.Title;
                    foreach (var opt in data.RecentOptions)
                    {
                        if (opt.Key == targetState.Key) opt.Title = data.ActiveHud.Title;
                    }
                }
            }

            string activeCidForPaid = targetState != null ? targetState.Cid : "";

            return data;
        }

        private ConvTrackState ResolveActiveSideQuestionIfNewer(ConvTrackState matched)
        {
            if (matched == null || matched.SideQuestionCids == null || matched.SideQuestionCids.Count == 0)
                return matched;

            string surfPrefix = matched.Key.Contains(":") ? matched.Key.Substring(0, matched.Key.IndexOf(':') + 1) : "";
            ConvTrackState newestChild = null;
            DateTime bestWrite = matched.LastWriteUtc;

            foreach (string scid in matched.SideQuestionCids)
            {
                ConvTrackState child;
                if (!_states.TryGetValue(surfPrefix + scid, out child) || child == null) continue;
                child.IsSideQuestion = true;
                child.ParentCid = matched.Cid;
                string mappedTitle;
                if (matched.SideQuestionTitles.TryGetValue(scid, out mappedTitle) && !string.IsNullOrEmpty(mappedTitle))
                    child.Title = mappedTitle;

                if (child.IsWorking || child.LastWriteUtc > bestWrite)
                {
                    if (newestChild == null || (child.IsWorking && !newestChild.IsWorking) || child.LastWriteUtc > newestChild.LastWriteUtc)
                    {
                        newestChild = child;
                        if (child.LastWriteUtc > bestWrite) bestWrite = child.LastWriteUtc;
                    }
                }
            }
            return newestChild ?? matched;
        }

        private ConvTrackState MatchStateByWindowTitle(string winTitleRaw, List<ConvTrackState> orderedStates)
        {
            if (string.IsNullOrEmpty(winTitleRaw)) return null;
            string wt = JsonFastHelper.StripSurrogates(winTitleRaw).Trim();
            if (string.IsNullOrEmpty(wt)) return null;

            // Antigravity IDE window title typically ends with " - Antigravity IDE" (uses 'antigravity' surface)
            if (wt.IndexOf("Antigravity IDE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ConvTrackState ideLatest = orderedStates.FirstOrDefault(s => s.Surface == "antigravity");
                if (ideLatest != null)
                    return ResolveActiveSideQuestionIfNewer(ideLatest);
            }

            // 1. Exact title or "<ConvTitle> - <Workspace>" prefix match (longest title wins if prefix overlaps)
            ConvTrackState bestExact = _states.Values
                .Where(s => !string.IsNullOrEmpty(s.Title) &&
                            (wt.Equals(s.Title, StringComparison.Ordinal) ||
                             wt.StartsWith(s.Title + " - ", StringComparison.Ordinal)))
                .OrderByDescending(s => s.Title.Length)
                .ThenByDescending(s => GetUserFocusUtc(s))
                .FirstOrDefault();

            if (bestExact != null)
                return ResolveActiveSideQuestionIfNewer(bestExact);

            // 2. Fallback for truncated window titles: strip workspace suffix after the last " - "
            int sep = wt.LastIndexOf(" - ", StringComparison.Ordinal);
            string head = (sep > 0 ? wt.Substring(0, sep) : wt).Trim();
            head = head.TrimEnd('.', '…', ' ');
            if (head.Length >= 3 &&
                !head.Equals("Antigravity", StringComparison.OrdinalIgnoreCase) &&
                !head.StartsWith("Outside of", StringComparison.OrdinalIgnoreCase))
            {
                ConvTrackState bestPrefix = _states.Values
                    .Where(s => !string.IsNullOrEmpty(s.Title) &&
                                (s.Title.StartsWith(head, StringComparison.Ordinal) ||
                                 head.StartsWith(s.Title, StringComparison.Ordinal)))
                    .OrderByDescending(s => GetUserFocusUtc(s))
                    .FirstOrDefault();
                if (bestPrefix != null)
                    return ResolveActiveSideQuestionIfNewer(bestPrefix);
            }

            return orderedStates.Count > 0 ? ResolveActiveSideQuestionIfNewer(orderedStates[0]) : null;
        }

        private static DateTime GetUserFocusUtc(ConvTrackState st)
        {
            if (st == null) return DateTime.MinValue;
            DateTime best = st.LastUserViewUtc;
            if (st.LastUserInputUtc > best) best = st.LastUserInputUtc;
            if (best == DateTime.MinValue && st.LastStepLocal > DateTime.MinValue)
                best = st.LastStepLocal.ToUniversalTime();
            return best;
        }

        private static string stTitleSafe(ConvTrackState st)
        {
            return st != null ? st.Title : "";
        }

        private static void Accumulate(PeriodSummary sum, Dictionary<string, ModelTokenStat> map, string mName, ModelTokenStat add)
        {
            sum.InputTokens += add.InputTokens;
            sum.CacheTokens += add.CacheTokens;
            sum.OutputTokens += add.OutputTokens;
            sum.Turns += add.Calls;
            sum.CostUsd += add.CostUsd;

            ModelTokenStat cur;
            if (!map.TryGetValue(mName, out cur))
            {
                cur = new ModelTokenStat();
                map[mName] = cur;
            }
            cur.InputTokens += add.InputTokens;
            cur.CacheTokens += add.CacheTokens;
            cur.OutputTokens += add.OutputTokens;
            cur.Calls += add.Calls;
            cur.CostUsd += add.CostUsd;
        }

        private static void AccumulateTools(PeriodSummary sum, Dictionary<string, int> map, Dictionary<string, int> add)
        {
            if (add == null || add.Count == 0) return;
            foreach (var kv in add)
            {
                if (IsInternalNoiseTool(kv.Key)) continue;
                string labelKey = GetToolChineseLabel(kv.Key);
                sum.ToolCalls += kv.Value;
                int prev;
                map.TryGetValue(labelKey, out prev);
                map[labelKey] = prev + kv.Value;
            }
        }

        public ModelDrilldownData QueryModelDrilldown(string modelName, PeriodTab period)
        {
            DateTime now = DateTime.Now;
            string todayKey = now.ToString("yyyy-MM-dd");
            string yesterdayKey = now.Date.AddDays(-1).ToString("yyyy-MM-dd");
            DateTime d7Start = now.Date.AddDays(-6);
            DateTime d30Start = now.Date.AddDays(-29);

            var res = new ModelDrilldownData
            {
                ModelName = modelName ?? "",
                Period = period,
                PeriodLabel = period == PeriodTab.Today ? I18n.T("今日", "Today")
                    : (period == PeriodTab.Yesterday ? I18n.T("昨日", "Yesterday")
                    : (period == PeriodTab.Days7 ? I18n.T("近7天", "7 Days")
                    : (period == PeriodTab.Days30 ? I18n.T("近30天", "30 Days") : I18n.T("全部", "All")))),
                DateRangeText = period == PeriodTab.Today ? now.ToString("MM-dd")
                    : (period == PeriodTab.Yesterday ? now.Date.AddDays(-1).ToString("MM-dd")
                    : (period == PeriodTab.Days7 ? (d7Start.ToString("MM-dd") + " ~ " + now.ToString("MM-dd"))
                    : (period == PeriodTab.Days30 ? (d30Start.ToString("MM-dd") + " ~ " + now.ToString("MM-dd")) : I18n.T("近35天日志", "Last 35d logs"))))
            };

            if (string.IsNullOrEmpty(modelName)) return res;

            var toolMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var st in _states.Values)
            {
                if (st.LedgerTurns == null || st.LedgerTurns.Count == 0) continue;

                ModelDrilldownConvRow convRow = null;
                string title = !string.IsNullOrEmpty(st.Title)
                    ? st.Title
                    : (I18n.T("会话 ", "Chat ") + (st.Cid.Length >= 8 ? st.Cid.Substring(0, 8) : st.Cid));

                for (int i = 0; i < st.LedgerTurns.Count; i++)
                {
                    LedgerTurnRecord tr = st.LedgerTurns[i];
                    if (!string.Equals(tr.Model, modelName, StringComparison.OrdinalIgnoreCase)) continue;

                    bool match = false;
                    if (period == PeriodTab.All)
                    {
                        match = true;
                    }
                    else if (period == PeriodTab.Today)
                    {
                        match = (tr.DateKey == todayKey);
                    }
                    else if (period == PeriodTab.Yesterday)
                    {
                        match = (tr.DateKey == yesterdayKey);
                    }
                    else if (period == PeriodTab.Days7)
                    {
                        DateTime dDate;
                        match = DateTime.TryParseExact(tr.DateKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dDate)
                                && dDate >= d7Start && dDate <= now.Date;
                    }
                    else
                    {
                        DateTime dDate;
                        match = DateTime.TryParseExact(tr.DateKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dDate)
                                && dDate >= d30Start && dDate <= now.Date;
                    }

                    if (!match) continue;

                    if (convRow == null)
                    {
                        convRow = new ModelDrilldownConvRow
                        {
                            ConvKey = st.Key,
                            Title = title,
                            Surface = st.Surface
                        };
                    }

                    convRow.Calls += 1;
                    convRow.InputTokens += tr.InputTokens;
                    convRow.CacheTokens += tr.CacheTokens;
                    convRow.OutputTokens += tr.OutputTokens;
                    convRow.CostUsd += tr.CostUsd;
                    if (tr.TimeLocal < convRow.FirstTime) convRow.FirstTime = tr.TimeLocal;
                    if (tr.TimeLocal > convRow.LastTime) convRow.LastTime = tr.TimeLocal;

                    bool isLowCache = tr.CacheHitPct < 50.0;
                    if (isLowCache)
                    {
                        convRow.LowCacheCalls += 1;
                        res.LowCacheTurns += 1;
                    }

                    res.Turns += 1;
                    res.InputTokens += tr.InputTokens;
                    res.CacheTokens += tr.CacheTokens;
                    res.OutputTokens += tr.OutputTokens;
                    res.CostUsd += tr.CostUsd;

                    int tc = 0;
                    string toolsShort = "—";
                    string toolsFull = I18n.T("无工具调用", "No tool calls");
                    if (tr.ToolNames != null && tr.ToolNames.Length > 0)
                    {
                        var cnList = new List<string>(tr.ToolNames.Length);
                        for (int ti = 0; ti < tr.ToolNames.Length; ti++)
                        {
                            string tn = tr.ToolNames[ti];
                            if (IsInternalNoiseTool(tn)) continue;
                            string cnLabel = GetToolChineseLabel(tn);
                            cnList.Add(cnLabel);
                            int prev;
                            toolMap.TryGetValue(cnLabel, out prev);
                            toolMap[cnLabel] = prev + 1;
                        }
                        tc = cnList.Count;
                        convRow.ToolCalls += tc;
                        res.ToolCallCount += tc;
                        if (tc > 0)
                        {
                            toolsShort = tc == 1 ? cnList[0] : (cnList[0] + " +" + (tc - 1));
                            toolsFull = string.Join("、", cnList.ToArray());
                        }
                    }

                    res.TurnRows.Add(new ModelDrilldownTurnRow
                    {
                        ConvKey = st.Key,
                        ConvTitle = title,
                        TimeLocal = tr.TimeLocal,
                        InputTokens = tr.InputTokens,
                        CacheTokens = tr.CacheTokens,
                        OutputTokens = tr.OutputTokens,
                        CostUsd = tr.CostUsd,
                        ToolsShort = toolsShort,
                        ToolsFull = toolsFull,
                        ToolCount = tc
                    });
                }

                if (convRow != null)
                {
                    res.ConvRows.Add(convRow);
                }
            }

            res.ConvCount = res.ConvRows.Count;
            res.ConvRows.Sort((a, b) => b.TotalTokens.CompareTo(a.TotalTokens));
            res.TurnRows.Sort((a, b) => b.TimeLocal.CompareTo(a.TimeLocal));
            res.TopTools = toolMap.OrderByDescending(x => x.Value).ToList();
            return res;
        }

        private static double EstimateStepUsd(string rawModel, long inTok, long cacheTok, long outTok)
        {
            string r = (rawModel ?? "").ToLowerInvariant();
            double pIn, pCache, pOut;
            if (r.Contains("neon"))
            {
                pIn = 3.00; pCache = 0.15; pOut = 15.00;
            }
            else if (r.Contains("carbon") || r.Contains("argon") || r.Contains("orchestrator") || r.Contains("pro"))
            {
                pIn = 2.00; pCache = 0.3125; pOut = 12.00;
            }
            else if (r.Contains("flash-lite"))
            {
                pIn = 0.15; pCache = 0.04; pOut = 0.60;
            }
            else
            {
                pIn = 0.50; pCache = 0.10; pOut = 3.00;
            }
            return (inTok * pIn + cacheTok * pCache + outTok * pOut) / 1000000.0;
        }

        private static string FormatLevelLabel(string rawLv)
        {
            if (string.IsNullOrEmpty(rawLv)) return "";
            string low = rawLv.ToLowerInvariant();
            if (low == "xhigh") return "XHigh";
            if (low == "high") return "High";
            if (low == "medium") return "Medium";
            if (low == "low") return "Low";
            return char.ToUpperInvariant(low[0]) + low.Substring(1);
        }

        public static string PrettyModelName(string raw)
        {
            string s = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(s)) return I18n.T("待命 · 尚未生成", "Idle · No turns yet");
            string low = s.ToLowerInvariant();

            if (low.Contains("orchestrator"))
            {
                if (low.Contains("neon")) return "Neon Orchestrator · Gemini 4";
                if (low.Contains("argon")) return "Argon Orchestrator · Gemini 4";
                return "Carbon Orchestrator · Gemini 4";
            }

            Match mGem = Regex.Match(low, @"^gemini-([0-9.]+)-([a-z0-9-]+?)(?:-(xhigh|high|medium|low))?$");
            if (mGem.Success)
            {
                string ver = mGem.Groups[1].Value;
                string codeRaw = mGem.Groups[2].Value;
                string lv = FormatLevelLabel(mGem.Groups[3].Value);
                string code;
                if (codeRaw == "flash-lite") code = "Flash-Lite";
                else code = char.ToUpperInvariant(codeRaw[0]) + codeRaw.Substring(1);
                return string.IsNullOrEmpty(lv)
                    ? string.Format("{0} · Gemini {1}", code, ver)
                    : string.Format("{0} ({1}) · Gemini {2}", code, lv, ver);
            }

            Match mCl = Regex.Match(low, @"^claude-(opus|sonnet|haiku)(?:-([0-9]+)-([0-9]+))?(?:-(xhigh|high|medium|low))?$");
            if (mCl.Success)
            {
                string fam = char.ToUpperInvariant(mCl.Groups[1].Value[0]) + mCl.Groups[1].Value.Substring(1);
                string ver = (!string.IsNullOrEmpty(mCl.Groups[2].Value) && !string.IsNullOrEmpty(mCl.Groups[3].Value))
                    ? (" " + mCl.Groups[2].Value + "." + mCl.Groups[3].Value)
                    : "";
                string lv = FormatLevelLabel(mCl.Groups[4].Value);
                return string.IsNullOrEmpty(lv)
                    ? string.Format("{0}{1} · Claude", fam, ver)
                    : string.Format("{0}{1} ({2}) · Claude", fam, ver, lv);
            }

            if (low.StartsWith("gpt-oss-"))
            {
                Match mGpt = Regex.Match(low, @"^gpt-oss-([0-9a-z]+)(?:-(xhigh|high|medium|low))?$");
                if (mGpt.Success)
                {
                    string sz = mGpt.Groups[1].Value.ToUpperInvariant();
                    string lv = FormatLevelLabel(mGpt.Groups[2].Value);
                    return string.IsNullOrEmpty(lv)
                        ? string.Format("GPT-OSS {0}", sz)
                        : string.Format("GPT-OSS {0} ({1})", sz, lv);
                }
            }

            if (low.Contains("flash-lite")) return "Flash-Lite · Gemini";
            if (low.Contains("flash")) return "Flash · Gemini 3.8";
            return s;
        }

        public static void ShortModelBadge(string prettyMod, out string tag, out string tone)
        {
            string s = (prettyMod ?? "").Trim();
            if (string.IsNullOrEmpty(s) || s.StartsWith(I18n.T("待命", "Idle")))
            {
                tag = I18n.T("待命", "Idle");
                tone = "muted";
                return;
            }

            string lv = "";
            Match mLv = Regex.Match(s, @"(?:\(|-|\s)(xhigh|high|medium|low)(?:\)|$|\s)", RegexOptions.IgnoreCase);
            if (mLv.Success)
            {
                string g = mLv.Groups[1].Value.ToLowerInvariant();
                if (g == "xhigh") lv = "·XH";
                else if (g == "high") lv = "·H";
                else if (g == "medium") lv = "·M";
                else if (g == "low") lv = "·L";
            }

            if (s.IndexOf("Orchestrator", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (s.IndexOf("Neon", StringComparison.OrdinalIgnoreCase) >= 0) { tag = "Neon·Or"; tone = "neon"; return; }
                if (s.IndexOf("Argon", StringComparison.OrdinalIgnoreCase) >= 0) { tag = "Argon·Or"; tone = "argon"; return; }
                tag = "Orch"; tone = "carbon"; return;
            }
            if (s.IndexOf("Neon", StringComparison.OrdinalIgnoreCase) >= 0) { tag = "Neon" + lv; tone = "neon"; return; }
            if (s.IndexOf("Carbon", StringComparison.OrdinalIgnoreCase) >= 0) { tag = "Carbon" + lv; tone = "carbon"; return; }
            if (s.IndexOf("Argon", StringComparison.OrdinalIgnoreCase) >= 0) { tag = "Argon" + lv; tone = "argon"; return; }
            if (s.IndexOf("Hydrogen", StringComparison.OrdinalIgnoreCase) >= 0) { tag = "Hydro" + lv; tone = "fast"; return; }
            if (s.IndexOf("Flash-Lite", StringComparison.OrdinalIgnoreCase) >= 0) { tag = "FLite" + lv; tone = "fast"; return; }
            if (s.IndexOf("Flash", StringComparison.OrdinalIgnoreCase) >= 0) { tag = "Flash" + lv; tone = "fast"; return; }
            if (s.IndexOf("Opus", StringComparison.OrdinalIgnoreCase) >= 0) { tag = "Opus" + lv; tone = "carbon"; return; }
            if (s.IndexOf("Sonnet", StringComparison.OrdinalIgnoreCase) >= 0) { tag = "Sonnet" + lv; tone = "argon"; return; }
            if (s.IndexOf("GPT-OSS", StringComparison.OrdinalIgnoreCase) >= 0) { tag = "GPT120" + lv; tone = "argon"; return; }

            // 通用兜底：自动剥离 gemini-X- / claude- 前缀，确保未知新代号永远显示在最前面
            string cleaned = Regex.Replace(s, @"^(?:gemini-[0-9.]+-|claude-)", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"(?:\s*\((?:xhigh|high|medium|low)\)|-(?:xhigh|high|medium|low))$", "", RegexOptions.IgnoreCase).Trim();
            int sepIdx = cleaned.IndexOfAny(new[] { ' ', '·', '(' });
            if (sepIdx > 0) cleaned = cleaned.Substring(0, sepIdx);
            if (cleaned.Length > 0) cleaned = char.ToUpperInvariant(cleaned[0]) + cleaned.Substring(1);
            if (cleaned.Length > 7) cleaned = cleaned.Substring(0, 7);
            tag = (string.IsNullOrEmpty(cleaned) ? "Model" : cleaned) + lv;
            tone = "argon";
        }

        private static string ExtractCleanUserPreview(string content, int maxLen)
        {
            if (string.IsNullOrEmpty(content)) return "";
            string s = content;
            int uStart = s.IndexOf("<USER_REQUEST>", StringComparison.Ordinal);
            int uEnd = s.IndexOf("</USER_REQUEST>", StringComparison.Ordinal);
            if (uStart >= 0 && uEnd > uStart)
            {
                s = s.Substring(uStart + 14, uEnd - (uStart + 14));
            }
            s = Regex.Replace(JsonFastHelper.StripSurrogates(s), "\\s+", " ").Trim();
            if (s.StartsWith("[Attachment:", StringComparison.OrdinalIgnoreCase))
                return I18n.T("图片多模态消息", "Image / Multimodal Input");
            return ShortenText(s, maxLen);
        }

        private static string ShortenText(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string c = Regex.Replace(s, "\\s+", " ").Trim();
            if (c.Length <= maxLen) return c;
            return c.Substring(0, maxLen) + "…";
        }

        private static string DecodePbtxtOctal(string escaped)
        {
            var bytes = new List<byte>();
            for (int i = 0; i < escaped.Length; i++)
            {
                char c = escaped[i];
                if (c == '\\' && i + 1 < escaped.Length)
                {
                    char next = escaped[i + 1];
                    if (next >= '0' && next <= '7')
                    {
                        int octVal = 0;
                        int count = 0;
                        while (count < 3 && (i + 1 + count) < escaped.Length)
                        {
                            char d = escaped[i + 1 + count];
                            if (d >= '0' && d <= '7') { octVal = (octVal * 8) + (d - '0'); count++; }
                            else break;
                        }
                        bytes.Add((byte)(octVal & 0xFF));
                        i += count;
                    }
                    else
                    {
                        if (next == 'n') bytes.Add((byte)'\n');
                        else if (next == 't') bytes.Add((byte)'\t');
                        else if (next == '"') bytes.Add((byte)'"');
                        else if (next == '\\') bytes.Add((byte)'\\');
                        else bytes.AddRange(Encoding.UTF8.GetBytes(new[] { next }));
                        i++;
                    }
                }
                else
                {
                    bytes.AddRange(Encoding.UTF8.GetBytes(new[] { c }));
                }
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }

}
