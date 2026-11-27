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
    internal sealed class TreemapCellLayout
    {
        public SourceBlock Item;
        public RectangleF Rect;
        public string HoverDetail = "";
    }

    internal static class SquarifyEngine
    {
        private sealed class AreaNode
        {
            public SourceBlock Block;
            public float Area;
        }

        public static List<TreemapCellLayout> Compute(List<SourceBlock> blocks, RectangleF bounds, long totalSum)
        {
            var result = new List<TreemapCellLayout>();
            if (blocks == null || blocks.Count == 0 || bounds.Width <= 4 || bounds.Height <= 4)
                return result;

            List<SourceBlock> valid = blocks.Where(b => b != null && b.Tokens > 0).OrderByDescending(b => b.Tokens).ToList();
            long sum = valid.Sum(x => x.Tokens);
            if (sum <= 0) return result;

            double minWeight = sum * 0.16;
            List<double> weights = valid.Select(b => Math.Max(minWeight, (double)b.Tokens)).ToList();
            double wSum = weights.Sum();
            double scale = (bounds.Width * bounds.Height) / Math.Max(1.0, wSum);
            var entries = new List<AreaNode>(valid.Count);
            for (int i = 0; i < valid.Count; i++)
            {
                entries.Add(new AreaNode { Block = valid[i], Area = (float)(weights[i] * scale) });
            }

            RectangleF rem = bounds;
            var row = new List<AreaNode>();

            Action flush = () =>
            {
                float s = row.Sum(r => r.Area);
                if (s <= 0) { row.Clear(); return; }

                if (rem.Width >= rem.Height)
                {
                    float t = s / Math.Max(1f, rem.Height);
                    float y = rem.Y;
                    foreach (var r in row)
                    {
                        float h = r.Area / Math.Max(1f, t);
                        AddCell(result, r.Block, new RectangleF(rem.X, y, t, h), totalSum);
                        y += h;
                    }
                    rem = new RectangleF(rem.X + t, rem.Y, Math.Max(0f, rem.Width - t), rem.Height);
                }
                else
                {
                    float t = s / Math.Max(1f, rem.Width);
                    float x = rem.X;
                    foreach (var r in row)
                    {
                        float w = r.Area / Math.Max(1f, t);
                        AddCell(result, r.Block, new RectangleF(x, rem.Y, w, t), totalSum);
                        x += w;
                    }
                    rem = new RectangleF(rem.X, rem.Y + t, rem.Width, Math.Max(0f, rem.Height - t));
                }
                row.Clear();
            };

            foreach (var entry in entries)
            {
                if (row.Count > 0)
                {
                    float side = Math.Max(1f, Math.Min(rem.Width, rem.Height));
                    var candidate = new List<AreaNode>(row) { entry };
                    if (WorstAspect(candidate, side) > WorstAspect(row, side))
                    {
                        flush();
                    }
                }
                row.Add(entry);
            }
            if (row.Count > 0) flush();

            return result;
        }

        private static void AddCell(List<TreemapCellLayout> outList, SourceBlock blk, RectangleF rawRect, long totalSum)
        {
            double pct = totalSum > 0 ? (blk.Tokens * 100.0 / totalSum) : 0.0;
            string mult = (blk.Category != "other" && blk.Count > 1) ? (" x" + blk.Count) : "";
            outList.Add(new TreemapCellLayout
            {
                Item = blk,
                Rect = rawRect,
                HoverDetail = string.Format("{0}{1} : {2} ({3:F1}%)", blk.Name, mult, TrayPopupForm.FmtCompact(blk.Tokens), pct)
            });
        }

        private static float WorstAspect(List<AreaNode> nodes, float side)
        {
            float s = nodes.Sum(n => n.Area);
            if (s <= 0 || side <= 0) return float.MaxValue;
            float t = s / side;
            float maxR = 0f;
            foreach (var n in nodes)
            {
                float l = n.Area / Math.Max(0.01f, t);
                float ratio = l > t ? (l / Math.Max(0.01f, t)) : (t / Math.Max(0.01f, l));
                if (ratio > maxR) maxR = ratio;
            }
            return maxR;
        }
    }

}
