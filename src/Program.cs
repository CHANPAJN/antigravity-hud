using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AntigravityHud
{
    internal static class Program
    {
        private static Mutex _mutex;
        private static bool _ownsMutex;

        public static void ReleaseSingleInstanceMutex()
        {
            try
            {
                if (_mutex != null)
                {
                    if (_ownsMutex)
                    {
                        try { _mutex.ReleaseMutex(); } catch { }
                        _ownsMutex = false;
                    }
                    _mutex.Close();
                    _mutex = null;
                }
            }
            catch { }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            NativeMethods.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Offscreen self-test / preview render mode (zero UI intrusion, zero focus steal)
            if (args != null && args.Length >= 2 && args[0] == "--render-preview")
            {
                RunOffscreenRenderPreview(args);
                return;
            }

            if (args != null && args.Length >= 1 && args[0] == "--test-config")
            {
                RunConfigSelfTest();
                return;
            }

            bool createdNew;
            _mutex = new Mutex(true, @"Local\AntigravityHUD_SingleInstance_Mutex", out createdNew);
            if (!createdNew) return;
            _ownsMutex = true;

            try
            {
                Application.Run(new TaskbarAppContext());
            }
            finally
            {
                ReleaseSingleInstanceMutex();
            }
        }

        private static void RunConfigSelfTest()
        {
            var cfg = new UserConfig
            {
                AnchorMode = "custom",
                CustomX = 1280,
                CustomY = 24,
                HorizontalAlign = "right",
                MonitorDeviceName = @"\\.\DISPLAY1",
                PositionLocked = false,
                Language = "en",
                AutoMemoryGuard = false,
                ScaleMode = "auto",
                BarStyle = "minimal"
            };
            ConfigStore.Save(cfg);
            UserConfig loaded = ConfigStore.Load();
            if (loaded.AnchorMode != "custom" || loaded.CustomX != 1280 || loaded.CustomY != 24 ||
                loaded.HorizontalAlign != "right" || loaded.Language != "en" || loaded.AutoMemoryGuard ||
                loaded.BarStyle != "minimal")
            {
                Environment.Exit(2);
            }
            // Restore clean defaults after self-test
            ConfigStore.Save(new UserConfig());
        }

        private static void RunOffscreenRenderPreview(string[] args)
        {
            string outPng = args[1];
            try
            {
                string mode = args.Length >= 3 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : "hud";
                bool useDemo = false;
                string langArg = "auto";
                string reqConv = "";

                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] == "--demo")
                    {
                        useDemo = true;
                    }
                    else if (args[i] == "--lang" && i + 1 < args.Length)
                    {
                        langArg = args[++i];
                    }
                    else if (args[i] == "--conv" && i + 1 < args.Length)
                    {
                        reqConv = args[++i];
                    }
                }

                I18n.SetLanguage(langArg);
                float dpi = NativeMethods.GetSystemDpiScale();
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var cfg = new UserConfig { Language = langArg, AnchorMode = mode == "hud_custom_pos" ? "custom" : "taskbar-auto" };

                LocalTelemetryEngine eng = useDemo ? null : new LocalTelemetryEngine(profile, false);
                DashboardData d = useDemo
                    ? DemoDataFactory.BuildDemoDashboard(false)
                    : eng.Query("", true);

                if (mode == "menu")
                {
                    var mCfg = new UserConfig { Language = langArg, AnchorMode = "custom", BarStyle = "compact", PositionLocked = false };
                    using (var menuFrm = new ModernContextMenuForm(dpi, mCfg, null, null, null, null, null, null, null, () => false))
                    {
                        menuFrm.RenderOffscreenToPng(outPng);
                    }
                    return;
                }

                if (mode == "bar")
                {
                    using (var bar = new TaskbarCapsuleBarForm(dpi, cfg, null, null, null))
                    {
                        bar.UpdateTelemetry(d, ScaleMode.Auto);
                        int bw;
                        using (Bitmap bmp = bar.BuildBarBitmap(out bw))
                        {
                            bmp.Save(outPng, ImageFormat.Png);
                        }
                    }
                    return;
                }

                if (!useDemo && !string.IsNullOrEmpty(reqConv))
                {
                    ConvOptionItem matchOpt = d.RecentOptions.FirstOrDefault(o =>
                        o.Cid.StartsWith(reqConv, StringComparison.OrdinalIgnoreCase) ||
                        o.Key.IndexOf(reqConv, StringComparison.OrdinalIgnoreCase) >= 0);
                    string selKey = matchOpt != null ? matchOpt.Key : reqConv;
                    d = eng.Query(selKey, false);
                }

                MainTab tab = (mode.StartsWith("ledger", StringComparison.Ordinal) || mode == "drill" || mode == "drill_turns")
                    ? MainTab.GlobalLedger
                    : MainTab.WindowHud;

                Func<string, PeriodTab, ModelDrilldownData> drillCb = useDemo
                    ? (Func<string, PeriodTab, ModelDrilldownData>)DemoDataFactory.BuildDemoDrilldown
                    : (mn, per) => eng.QueryModelDrilldown(mn, per);

                using (var frm = new TrayPopupForm(dpi, null, null, null, null, null, null, null, drillCb, null, null, () => cfg))
                {
                    frm.SetMainTab(tab);
                    frm.UpdateData(d, "");
                    if (mode == "hud_subagents")
                    {
                        frm.SetPreviewMode(HudCard2Mode.Subagents, "", "", LedgerRankMode.ByModel, TurnQuickFilter.All);
                    }
                    else if (mode == "hud_sys")
                    {
                        frm.SetPreviewMode(HudCard2Mode.Breakdown, "system", I18n.T("系统提示与摘要", "System & Summary"), LedgerRankMode.ByModel, TurnQuickFilter.All);
                    }
                    else if (mode == "hud_res")
                    {
                        frm.SetPreviewMode(HudCard2Mode.Breakdown, "results", I18n.T("工具返回与读取输出", "Tool Outputs & File Reads"), LedgerRankMode.ByModel, TurnQuickFilter.All);
                    }
                    else if (mode == "hud_slow")
                    {
                        frm.SetPreviewMode(HudCard2Mode.Turns, "", "", LedgerRankMode.ByModel, TurnQuickFilter.SlowWait);
                    }
                    else if (mode == "ledger_tasks")
                    {
                        frm.SetLedgerPeriod(PeriodTab.Days7);
                        frm.SetPreviewMode(HudCard2Mode.Turns, "", "", LedgerRankMode.ByTask, TurnQuickFilter.All);
                    }
                    else if (mode == "ledger_yesterday") frm.SetLedgerPeriod(PeriodTab.Yesterday);
                    else if (mode == "ledger_7d") frm.SetLedgerPeriod(PeriodTab.Days7);
                    else if (mode == "ledger_30d") frm.SetLedgerPeriod(PeriodTab.Days30);
                    else if (mode == "ledger_all") frm.SetLedgerPeriod(PeriodTab.All);
                    else if (mode == "drill" || mode == "drill_turns")
                    {
                        frm.SetLedgerPeriod(PeriodTab.Today);
                        string topMod = d.Today.ModelRanking.Count > 0 ? d.Today.ModelRanking[0].Key : "Carbon (High) · Gemini 4";
                        frm.EnterModelDrilldown(topMod, mode == "drill_turns" ? 1 : 0);
                    }
                    frm.RenderOffscreenToPng(outPng, mode == "hud_hover");
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(outPng + ".err", ex.ToString(), Encoding.UTF8);
                Environment.Exit(1);
            }
        }
    }
}
