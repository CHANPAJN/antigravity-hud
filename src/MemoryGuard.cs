using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace AntigravityHud
{
    internal sealed class LocalMemoryGuard
    {
        public const double OverflowWarnMB = 2800.0;
        private readonly string _agRoot;
        private bool _autoGuardEnabled;
        private double _lastFreedMB;
        private double _totalFreedMB;
        private DateTime _lastActionAt = DateTime.MinValue;
        private DateTime _lastSampleUtc = DateTime.MinValue;
        private DateTime _lastAutoTrimUtc = DateTime.MinValue;
        private MemGuardSnapshot _cached = new MemGuardSnapshot();

        public LocalMemoryGuard(string userProfile, bool autoGuardInitial = false)
        {
            _agRoot = Path.Combine(userProfile, ".gemini", "antigravity");
            _autoGuardEnabled = autoGuardInitial;
            _cached.AutoGuardEnabled = autoGuardInitial;
        }

        public bool AutoGuardEnabled
        {
            get { return _autoGuardEnabled; }
        }

        public void SetAutoGuardEnabled(bool enabled)
        {
            _autoGuardEnabled = enabled;
            _cached.AutoGuardEnabled = enabled;
        }

        public bool ToggleAutoGuard()
        {
            _autoGuardEnabled = !_autoGuardEnabled;
            _cached.AutoGuardEnabled = _autoGuardEnabled;
            return _autoGuardEnabled;
        }

        public MemGuardSnapshot GetSnapshot(bool force)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (!force && _lastSampleUtc != DateTime.MinValue && (nowUtc - _lastSampleUtc).TotalSeconds < 18.0)
            {
                _cached.AutoGuardEnabled = _autoGuardEnabled;
                _cached.LastFreedMB = _lastFreedMB;
                _cached.TotalFreedMB = _totalFreedMB;
                _cached.LastActionAt = _lastActionAt;
                return _cached;
            }

            _lastSampleUtc = nowUtc;
            var snap = SampleNowInternal();

            // Opt-in only: only trims when the user explicitly enabled AutoGuard
            if (_autoGuardEnabled && snap.AgTotalMB >= OverflowWarnMB && (nowUtc - _lastAutoTrimUtc).TotalSeconds >= 120.0)
            {
                _lastAutoTrimUtc = nowUtc;
                CleanAndTrimNow(true);
                snap = SampleNowInternal();
            }

            _cached = snap;
            return snap;
        }

        private static bool IsAntigravityClientProc(string pName)
        {
            if (string.IsNullOrEmpty(pName)) return false;
            return string.Equals(pName, "Antigravity", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(pName, "Antigravity - Insiders", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(pName, "Antigravity IDE", StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerable<string> EnumerateCleanableTempDirs()
        {
            string gemDir = Path.GetDirectoryName(_agRoot) ?? _agRoot;
            return new[]
            {
                Path.Combine(_agRoot, "brain", "tempmediaStorage"),
                Path.Combine(_agRoot, "crashes"),
                Path.Combine(gemDir, "antigravity-insiders", "brain", "tempmediaStorage"),
                Path.Combine(gemDir, "antigravity-insiders", "crashes")
            };
        }

        private MemGuardSnapshot SampleNowInternal()
        {
            var snap = new MemGuardSnapshot
            {
                AutoGuardEnabled = _autoGuardEnabled,
                LastFreedMB = _lastFreedMB,
                TotalFreedMB = _totalFreedMB,
                LastActionAt = _lastActionAt
            };

            // 1. Read-only Antigravity WorkingSet sampling (zero process termination)
            double totalBytes = 0;
            double maxSingleBytes = 0;
            int agCount = 0;

            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    using (p)
                    {
                        string pName = "";
                        try { pName = p.ProcessName ?? ""; } catch { continue; }

                        if (IsAntigravityClientProc(pName))
                        {
                            agCount++;
                            try
                            {
                                long ws = p.WorkingSet64;
                                totalBytes += ws;
                                if (ws > maxSingleBytes) maxSingleBytes = ws;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            snap.AgProcCount = agCount;
            snap.AgTotalMB = Math.Round(totalBytes / (1024.0 * 1024.0), 1);
            snap.AgMaxSingleMB = Math.Round(maxSingleBytes / (1024.0 * 1024.0), 1);

            // 2. Count cleanable Antigravity temp media & crash dump files
            int tempCnt = 0;
            long tempBytes = 0;
            foreach (string sub in EnumerateCleanableTempDirs())
            {
                if (!Directory.Exists(sub)) continue;
                try
                {
                    foreach (string fp in Directory.GetFiles(sub))
                    {
                        try
                        {
                            var fi = new FileInfo(fp);
                            tempCnt++;
                            tempBytes += fi.Length;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            snap.TempCacheCount = tempCnt;
            snap.TempCacheMB = Math.Round(tempBytes / (1024.0 * 1024.0), 1);
            return snap;
        }

        public double CleanAndTrimNow(bool isAutoTrigger)
        {
            double freed = 0.0;

            // 1. Clean only Antigravity tempmediaStorage & crash logs (never touches conversations or user files)
            DateTime nowUtc = DateTime.UtcNow;
            foreach (string sub in EnumerateCleanableTempDirs())
            {
                if (!Directory.Exists(sub)) continue;
                try
                {
                    foreach (string fp in Directory.GetFiles(sub))
                    {
                        try
                        {
                            var fi = new FileInfo(fp);
                            if (isAutoTrigger && (nowUtc - fi.LastWriteTimeUtc).TotalMinutes < 10.0) continue;
                            double mb = fi.Length / (1024.0 * 1024.0);
                            fi.Attributes = FileAttributes.Normal;
                            fi.Delete();
                            freed += mb;
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // 2. Trim Antigravity / Insiders / IDE working sets via standard Win32 EmptyWorkingSet (zero process kills)
            double beforeWsBytes = 0;
            string[] agNames = new[] { "Antigravity", "Antigravity - Insiders", "Antigravity IDE" };
            try
            {
                foreach (string targetNm in agNames)
                {
                    Process[] agProcs = Process.GetProcessesByName(targetNm);
                    foreach (Process p in agProcs)
                    {
                        using (p)
                        {
                            try { beforeWsBytes += p.WorkingSet64; } catch { }
                            try { NativeMethods.EmptyWorkingSet(p.Handle); } catch { }
                        }
                    }
                }
            }
            catch { }

            Thread.Sleep(80);

            double afterWsBytes = 0;
            try
            {
                foreach (string targetNm in agNames)
                {
                    Process[] agProcsAfter = Process.GetProcessesByName(targetNm);
                    foreach (Process p in agProcsAfter)
                    {
                        using (p)
                        {
                            try { afterWsBytes += p.WorkingSet64; } catch { }
                        }
                    }
                }
            }
            catch { }

            double trimmedMB = Math.Max(0.0, (beforeWsBytes - afterWsBytes) / (1024.0 * 1024.0));
            freed += trimmedMB;

            _lastFreedMB = Math.Round(freed, 1);
            _totalFreedMB = Math.Round(_totalFreedMB + _lastFreedMB, 1);
            _lastActionAt = DateTime.Now;
            _lastSampleUtc = DateTime.UtcNow;
            _cached = SampleNowInternal();
            return _lastFreedMB;
        }
    }
}
