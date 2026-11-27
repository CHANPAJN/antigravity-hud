using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AntigravityHud
{
    internal sealed partial class TrayPopupForm
    {
        private sealed class QuickCmdDef
        {
            public string Slash;
            public string Icon;
            public bool Featured;
            public string TitleZh;
            public string TitleEn;
            public string SubZh;
            public string SubEn;
            public string TipZh;
            public string TipEn;
        }

        private static readonly QuickCmdDef[] OfficialSlashCommands = new[]
        {
            new QuickCmdDef
            {
                Slash = "/btw", Icon = "fork", Featured = true,
                TitleZh = "旁路分叉对话", TitleEn = "Side-Thread Question",
                SubZh = "带上下文提问，不污染主线进度", SubEn = "Ask with context without polluting main thread",
                TipZh = "点击复制 /btw — 临时插问旁路问题，不打断当前主任务上下文",
                TipEn = "Click to copy /btw — Ask a quick side question without cluttering the main thread"
            },
            new QuickCmdDef
            {
                Slash = "/compact", Icon = "compress", Featured = true,
                TitleZh = "折叠压缩当前对话", TitleEn = "Compact Conversation",
                SubZh = "总结历史上下文，释放可用空间", SubEn = "Summarize history and free context window",
                TipZh = "点击复制 /compact — 窗口上下文接近满载时一键压缩释放空间",
                TipEn = "Click to copy /compact — Summarize the conversation to free context capacity"
            },
            new QuickCmdDef
            {
                Slash = "/goal", Icon = "target", Featured = false,
                TitleZh = "持续完成", TitleEn = "Run to Goal",
                SubZh = "长任务自动推进", SubEn = "Thorough end-to-end run",
                TipZh = "点击复制 /goal — 适合长耗时任务，持续推进直到目标彻底达成",
                TipEn = "Click to copy /goal — Keep working thoroughly until the goal is fully achieved"
            },
            new QuickCmdDef
            {
                Slash = "/teamwork", Icon = "team", Featured = false,
                TitleZh = "团队协作", TitleEn = "Agent Team",
                SubZh = "多智能体分工", SubEn = "Parallel multi-agent",
                TipZh = "点击复制 /teamwork — 启动多智能体分工协作处理大型工程",
                TipEn = "Click to copy /teamwork — Coordinate a team of agents for larger projects"
            },
            new QuickCmdDef
            {
                Slash = "/boost", Icon = "spark", Featured = false,
                TitleZh = "深度分析", TitleEn = "Deep Boost",
                SubZh = "多视角严谨验证", SubEn = "Deep think & verify",
                TipZh = "点击复制 /boost — 启用深度思考、战略规划与多角度严谨验证",
                TipEn = "Click to copy /boost — Deep thinking, strategic planning, and rigorous verification"
            },
            new QuickCmdDef
            {
                Slash = "/plan", Icon = "list", Featured = false,
                TitleZh = "分步规划", TitleEn = "Step Plan",
                SubZh = "先定方案再动手", SubEn = "Plan before editing",
                TipZh = "点击复制 /plan — 复杂重构前先梳理完整步骤并等待确认",
                TipEn = "Click to copy /plan — Draft a careful step-by-step plan before execution"
            },
            new QuickCmdDef
            {
                Slash = "/grill-me", Icon = "chat", Featured = false,
                TitleZh = "追问对齐", TitleEn = "Align Plan",
                SubZh = "交互敲定细节", SubEn = "Interactive Q&A",
                TipZh = "点击复制 /grill-me — 通过交互式追问提前消除架构与需求分歧",
                TipEn = "Click to copy /grill-me — Resolve design decisions via an interactive interview"
            },
            new QuickCmdDef
            {
                Slash = "/browser", Icon = "globe", Featured = false,
                TitleZh = "网页交互", TitleEn = "Web Browser",
                SubZh = "检索与页面操作", SubEn = "Browse & interact",
                TipZh = "点击复制 /browser — 进行网页浏览、检索或交互式页面操作",
                TipEn = "Click to copy /browser — Browse the web or interact with live web apps"
            },
            new QuickCmdDef
            {
                Slash = "/schedule", Icon = "clock", Featured = false,
                TitleZh = "定时任务", TitleEn = "Schedule",
                SubZh = "倒计时与周期跑", SubEn = "Timer or cron job",
                TipZh = "点击复制 /schedule — 创建单次倒计时提醒或周期性自动任务",
                TipEn = "Click to copy /schedule — Set a one-shot timer or recurring background schedule"
            },
            new QuickCmdDef
            {
                Slash = "/learn", Icon = "bookmark", Featured = false,
                TitleZh = "记入规则", TitleEn = "Save Rule",
                SubZh = "沉淀长期偏好", SubEn = "Persist workflow",
                TipZh = "点击复制 /learn — 把刚排出的经验或使用习惯持久化写入规则",
                TipEn = "Click to copy /learn — Persist a correction or workflow setup for future sessions"
            }
        };

        private readonly List<KeyValuePair<Rectangle, QuickCmdDef>> _cmdCardHits = new List<KeyValuePair<Rectangle, QuickCmdDef>>();
        private string _copiedCmdSlash = "";
        private Timer _cmdAutoHideTimer;

        private void InitQuickCommandsState()
        {
            _cmdAutoHideTimer = new Timer { Interval = 440 };
            _cmdAutoHideTimer.Tick += (s, e) =>
            {
                _cmdAutoHideTimer.Stop();
                _copiedCmdSlash = "";
                if (Visible && _activeTab == MainTab.QuickCommands)
                {
                    Hide();
                }
            };
        }

        private void ResetQuickCommandsCopyState()
        {
            if (_cmdAutoHideTimer != null) _cmdAutoHideTimer.Stop();
            _copiedCmdSlash = "";
        }

        private void DisposeQuickCommandsState()
        {
            if (_cmdAutoHideTimer != null)
            {
                _cmdAutoHideTimer.Stop();
                _cmdAutoHideTimer.Dispose();
                _cmdAutoHideTimer = null;
            }
        }

        private bool HandleQuickCommandsMouseMove(Point pt, out string hoverTip)
        {
            hoverTip = "";
            for (int i = 0; i < _cmdCardHits.Count; i++)
            {
                if (_cmdCardHits[i].Key.Contains(pt))
                {
                    QuickCmdDef cmd = _cmdCardHits[i].Value;
                    hoverTip = I18n.T(cmd.TipZh, cmd.TipEn);
                    return true;
                }
            }
            return false;
        }

        private bool HandleQuickCommandsMouseClick(Point pt)
        {
            for (int i = 0; i < _cmdCardHits.Count; i++)
            {
                if (_cmdCardHits[i].Key.Contains(pt))
                {
                    CopySlashCommand(_cmdCardHits[i].Value);
                    return true;
                }
            }
            return false;
        }

        private void CopySlashCommand(QuickCmdDef cmd)
        {
            if (cmd == null || string.IsNullOrEmpty(cmd.Slash)) return;
            try
            {
                // Pure clipboard write only — zero keyboard injection, zero client intrusion
                Clipboard.SetDataObject(cmd.Slash, true, 4, 40);
            }
            catch { }

            _copiedCmdSlash = cmd.Slash;
            _hoverHint = I18n.TF("已复制 {0} 到剪贴板，切回输入框 Ctrl+V 即用", "Copied {0} — press Ctrl+V in Antigravity", cmd.Slash);
            Invalidate();

            if (_cmdAutoHideTimer != null)
            {
                _cmdAutoHideTimer.Stop();
                _cmdAutoHideTimer.Start();
            }
        }

        private void DrawQuickCommandsBody(
            Graphics g, int x, int y, int w, int availH, HudSnapshot hud, long budget,
            Font fCardTitle, Font fBodyBold, Font fBody, Font fSmall, Font fSmallBold)
        {
            _cmdCardHits.Clear();

            var rcCard = new Rectangle(x, y, w, availH);
            FillRoundCard(g, rcCard, S(10), ColCardBg, ColCardBorder);

            int pad = S(12);
            int ix = x + pad;
            int iw = w - pad * 2;
            int cy = y + S(11);

            // 1. Header row: vector slash badge + title + clipboard-only hint or copied feedback
            int headH = S(22);
            DrawCommandGlyph(g, "slash", ix + S(9), cy + headH / 2, ColAccent);
            DrawTxtL(g, I18n.T("官方快捷指令", "Official Slash Commands"), fCardTitle, ColTextMain,
                new Rectangle(ix + S(22), cy, S(180), headH));

            if (!string.IsNullOrEmpty(_copiedCmdSlash))
            {
                string okTxt = I18n.TF("已复制 {0} · Ctrl+V", "Copied {0} · Ctrl+V", _copiedCmdSlash);
                DrawRightAlignedPill(g, okTxt, fSmallBold, ColOkSoft, ColOkText, ix + iw, cy + headH / 2);
            }
            else
            {
                string hintTxt = I18n.T("点击复制 · Ctrl+V 即用", "Click to copy · Paste Ctrl+V");
                DrawTxtR(g, hintTxt, fSmall, ColTextSub, new Rectangle(ix + S(180), cy, iw - S(180), headH));
            }
            cy += headH + S(9);

            // Context fullness hint for /compact synergy
            double ctxPct = (hud != null && hud.Latest != null && budget > 0)
                ? (hud.Latest.TotalUsed * 100.0 / budget)
                : 0.0;
            bool warnCompact = ctxPct >= 75.0;

            // 2. Featured high-frequency cards (/btw, /compact)
            int featH = S(45);
            int featGap = S(7);
            for (int i = 0; i < OfficialSlashCommands.Length; i++)
            {
                QuickCmdDef cmd = OfficialSlashCommands[i];
                if (!cmd.Featured) continue;

                var rcFeat = new Rectangle(ix, cy, iw, featH);
                _cmdCardHits.Add(new KeyValuePair<Rectangle, QuickCmdDef>(rcFeat, cmd));

                bool hov = rcFeat.Contains(_mousePt);
                bool copied = string.Equals(_copiedCmdSlash, cmd.Slash, StringComparison.Ordinal);
                bool isCompactAlert = warnCompact && cmd.Slash == "/compact";

                Color bg;
                Color bd;
                if (copied)
                {
                    bg = ColOkSoft;
                    bd = ColOkBorder;
                }
                else if (isCompactAlert)
                {
                    bg = hov ? ColHoverFill : ColWarnSoft;
                    bd = hov ? ColWarn : ColWarnBorder;
                }
                else
                {
                    bg = hov ? ColHoverFill : (Pal.IsDark ? Pal.PinRowBg : Color.FromArgb(245, 248, 255));
                    bd = hov ? ColAccent : ColHoverBorder;
                }
                FillRoundCard(g, rcFeat, S(9), bg, bd);

                // Left vector icon box
                int icoBox = S(26);
                var rcIco = new Rectangle(rcFeat.Left + S(10), rcFeat.Top + (featH - icoBox) / 2, icoBox, icoBox);
                Color icoBoxBg = copied ? ColCardBg : (isCompactAlert ? ColCardBg : (Pal.IsDark ? ColCardBg : Color.White));
                Color icoCol = copied ? ColOkText : (isCompactAlert ? ColWarnText : ColAccent);
                FillRoundCard(g, rcIco, S(7), icoBoxBg, copied ? ColOkBorder : ColCardBorder);
                DrawCommandGlyph(g, cmd.Icon, rcIco.Left + icoBox / 2, rcIco.Top + icoBox / 2, icoCol);

                // Right slash copy pill
                string pillTxt = copied
                    ? I18n.T("已复制 ✓", "Copied")
                    : (cmd.Slash + (I18n.IsZh ? " · 复制" : " · Copy"));
                Color pillBg = copied ? ColOk : (Pal.IsDark ? ColCardBg : Color.White);
                Color pillFg = copied ? (Pal.IsDark ? ColWinBg : Color.White) : (isCompactAlert ? ColWarnText : ColAccent);
                int pillW = MeasureTxtW(g, pillTxt, fSmallBold) + S(14);
                var rcPill = new Rectangle(rcFeat.Right - S(10) - pillW, rcFeat.Top + (featH - S(22)) / 2, pillW, S(22));
                FillRoundCard(g, rcPill, S(11), pillBg, copied ? ColOk : (isCompactAlert ? ColWarnBorder : ColHoverBorder));
                DrawTxtC(g, pillTxt, fSmallBold, pillFg, rcPill);

                // Title & subtitle
                int tx = rcIco.Right + S(9);
                int tw = Math.Max(S(60), rcPill.Left - tx - S(8));
                string title = I18n.T(cmd.TitleZh, cmd.TitleEn);
                string sub = isCompactAlert
                    ? I18n.TF("当前上下文 {0:0}% · 建议折叠释放空间", "Context at {0:0}% · Recommended now", ctxPct)
                    : I18n.T(cmd.SubZh, cmd.SubEn);

                DrawTxtL(g, title, fBodyBold, ColTextMain, new Rectangle(tx, rcFeat.Top + S(6), tw, S(17)));
                DrawTxtL(g, sub, fSmall, isCompactAlert ? ColWarnText : ColTextSub, new Rectangle(tx, rcFeat.Top + S(23), tw, S(16)));

                cy += featH + featGap;
            }

            cy += S(4);

            // 3. Section label for 2x4 workflow command grid
            DrawTxtL(g, I18n.T("工作流与专项指令", "WORKFLOWS & TOOLS"), fSmallBold, ColTextSub,
                new Rectangle(ix + S(2), cy, iw, S(16)));
            cy += S(20);

            // 4. Clean 2-column x 4-row grid
            int colGap = S(8);
            int rowGap = S(7);
            int colW = (iw - colGap) / 2;
            int remainH = rcCard.Bottom - S(11) - cy;
            int rowH = Math.Max(S(38), Math.Min(S(44), (remainH - rowGap * 3) / 4));

            int gridIdx = 0;
            for (int i = 0; i < OfficialSlashCommands.Length; i++)
            {
                QuickCmdDef cmd = OfficialSlashCommands[i];
                if (cmd.Featured) continue;

                int row = gridIdx / 2;
                int col = gridIdx % 2;
                int gx = ix + col * (colW + colGap);
                int gy = cy + row * (rowH + rowGap);
                var rcItem = new Rectangle(gx, gy, colW, rowH);
                _cmdCardHits.Add(new KeyValuePair<Rectangle, QuickCmdDef>(rcItem, cmd));

                bool hov = rcItem.Contains(_mousePt);
                bool copied = string.Equals(_copiedCmdSlash, cmd.Slash, StringComparison.Ordinal);

                Color bg = copied ? ColOkSoft : (hov ? ColHoverFill : ColRowAltBg);
                Color bd = copied ? ColOkBorder : (hov ? ColAccentBorder : ColCardBorder);
                FillRoundCard(g, rcItem, S(8), bg, bd);

                // Vector icon
                int icx = rcItem.Left + S(16);
                int icy = rcItem.Top + rowH / 2;
                DrawCommandGlyph(g, cmd.Icon, icx, icy, copied ? ColOkText : (hov ? ColAccent : ColTextSub));

                // Right slash badge
                string tagTxt = copied ? I18n.T("已复制", "Copied") : cmd.Slash;
                Color tagFg = copied ? ColOkText : (hov ? ColAccent : ColTextSub);
                int tagW = MeasureTxtW(g, tagTxt, fSmall) + S(8);
                DrawTxtR(g, tagTxt, copied ? fSmallBold : fSmall, tagFg,
                    new Rectangle(rcItem.Right - S(9) - tagW, rcItem.Top, tagW, rowH));

                // Left title
                int lx = rcItem.Left + S(29);
                int lw = Math.Max(S(48), rcItem.Right - S(11) - tagW - lx);
                DrawTxtL(g, I18n.T(cmd.TitleZh, cmd.TitleEn), fBody, ColTextMain,
                    new Rectangle(lx, rcItem.Top, lw, rowH));

                gridIdx++;
            }
        }

        private void DrawCommandGlyph(Graphics g, string kind, int cx, int cy, Color col)
        {
            float r = S(5.2f);
            float lw = Math.Max(1.35f, 1.45f * _dpi);
            using (var pen = new Pen(col, lw) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (kind == "slash")
                {
                    // Rounded keycap containing a clean "/" slash glyph
                    var rcKey = new Rectangle((int)(cx - r * 1.15f), (int)(cy - r * 1.15f), (int)(r * 2.3f), (int)(r * 2.3f));
                    using (GraphicsPath gp = CreateRoundPath(rcKey, Math.Max(2, S(3))))
                    {
                        g.DrawPath(pen, gp);
                    }
                    g.DrawLine(pen, cx - r * 0.35f, cy + r * 0.62f, cx + r * 0.35f, cy - r * 0.62f);
                }
                else if (kind == "fork")
                {
                    // Branching fork line (/btw)
                    g.DrawLine(pen, cx - r * 0.85f, cy, cx + r * 0.85f, cy);
                    g.DrawLine(pen, cx - r * 0.1f, cy, cx + r * 0.75f, cy - r * 0.75f);
                    g.DrawLine(pen, cx + r * 0.35f, cy - r * 0.75f, cx + r * 0.75f, cy - r * 0.75f);
                    g.DrawLine(pen, cx + r * 0.75f, cy - r * 0.75f, cx + r * 0.75f, cy - r * 0.35f);
                }
                else if (kind == "compress")
                {
                    // Two arrows pointing inward (/compact)
                    g.DrawLine(pen, cx - r * 0.9f, cy, cx + r * 0.9f, cy);
                    g.DrawLine(pen, cx, cy - r * 0.95f, cx, cy - r * 0.25f);
                    g.DrawLine(pen, cx - r * 0.35f, cy - r * 0.55f, cx, cy - r * 0.22f);
                    g.DrawLine(pen, cx + r * 0.35f, cy - r * 0.55f, cx, cy - r * 0.22f);
                    g.DrawLine(pen, cx, cy + r * 0.95f, cx, cy + r * 0.25f);
                    g.DrawLine(pen, cx - r * 0.35f, cy + r * 0.55f, cx, cy + r * 0.22f);
                    g.DrawLine(pen, cx + r * 0.35f, cy + r * 0.55f, cx, cy + r * 0.22f);
                }
                else if (kind == "target")
                {
                    g.DrawEllipse(pen, cx - r, cy - r, r * 2f, r * 2f);
                    g.DrawEllipse(pen, cx - r * 0.45f, cy - r * 0.45f, r * 0.9f, r * 0.9f);
                }
                else if (kind == "team")
                {
                    g.DrawEllipse(pen, cx - r * 0.8f, cy - r * 0.75f, r * 0.8f, r * 0.8f);
                    g.DrawEllipse(pen, cx + r * 0.05f, cy - r * 0.75f, r * 0.8f, r * 0.8f);
                    g.DrawArc(pen, cx - r * 1.0f, cy + r * 0.1f, r * 1.2f, r * 1.1f, 200, 140);
                    g.DrawArc(pen, cx - r * 0.15f, cy + r * 0.1f, r * 1.2f, r * 1.1f, 200, 140);
                }
                else if (kind == "spark")
                {
                    // Clean upward chevron spark (/boost)
                    g.DrawLine(pen, cx - r * 0.75f, cy + r * 0.45f, cx, cy - r * 0.45f);
                    g.DrawLine(pen, cx, cy - r * 0.45f, cx + r * 0.75f, cy + r * 0.45f);
                    g.DrawLine(pen, cx - r * 0.5f, cy + r * 0.9f, cx, cy + r * 0.25f);
                    g.DrawLine(pen, cx, cy + r * 0.25f, cx + r * 0.5f, cy + r * 0.9f);
                }
                else if (kind == "list")
                {
                    // Checklist lines (/plan)
                    g.DrawLine(pen, cx - r * 0.85f, cy - r * 0.55f, cx + r * 0.85f, cy - r * 0.55f);
                    g.DrawLine(pen, cx - r * 0.85f, cy, cx + r * 0.85f, cy);
                    g.DrawLine(pen, cx - r * 0.85f, cy + r * 0.55f, cx + r * 0.35f, cy + r * 0.55f);
                }
                else if (kind == "chat")
                {
                    // Minimal rounded dialog box (/grill-me)
                    g.DrawRectangle(pen, cx - r * 0.85f, cy - r * 0.65f, r * 1.7f, r * 1.15f);
                    g.DrawLine(pen, cx - r * 0.35f, cy + r * 0.5f, cx - r * 0.55f, cy + r * 0.95f);
                    g.DrawLine(pen, cx - r * 0.55f, cy + r * 0.95f, cx + r * 0.1f, cy + r * 0.5f);
                }
                else if (kind == "globe")
                {
                    g.DrawEllipse(pen, cx - r, cy - r, r * 2f, r * 2f);
                    g.DrawLine(pen, cx - r, cy, cx + r, cy);
                    g.DrawEllipse(pen, cx - r * 0.48f, cy - r, r * 0.96f, r * 2f);
                }
                else if (kind == "clock")
                {
                    g.DrawEllipse(pen, cx - r, cy - r, r * 2f, r * 2f);
                    g.DrawLine(pen, cx, cy - r * 0.55f, cx, cy);
                    g.DrawLine(pen, cx, cy, cx + r * 0.48f, cy + r * 0.25f);
                }
                else if (kind == "bookmark")
                {
                    // Ribbon bookmark (/learn)
                    var pts = new[]
                    {
                        new PointF(cx - r * 0.62f, cy - r * 0.85f),
                        new PointF(cx + r * 0.62f, cy - r * 0.85f),
                        new PointF(cx + r * 0.62f, cy + r * 0.85f),
                        new PointF(cx, cy + r * 0.32f),
                        new PointF(cx - r * 0.62f, cy + r * 0.85f)
                    };
                    g.DrawPolygon(pen, pts);
                }
            }
        }
    }
}
