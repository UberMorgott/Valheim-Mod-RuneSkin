using System;
using System.Collections.Generic;

namespace RuneSkin.PlayerHud
{
    // Pure HUD layout math (no Unity types: unit-tested on net8). All sizes in reference pixels (1920x1080 at
    // GuiScale 1) unless named *Px (physical pixels). Spec: side notes hud-resource-states.md §2.2-§4,
    // ui-skin-engine-plan.md §8a.
    public static class HudMath
    {
        // Main bars have one fixed outer width (mockup hud-status-b, user call 2026-10-21: the max is shown as a
        // number, never as bar length). Window = outer width minus the frame caps, never below the readable floor.
        public static float FixedWindow(float outer, float caps, float minWindow)
        {
            if (!(outer > 0f) || float.IsInfinity(outer)) return minWindow;
            return Math.Max(minWindow, outer - caps);
        }

        // A max change pulses the value only for real steps: >= 1 unit (food decay ticks are debounced) and
        // >= stepShare of the larger max.
        public static bool IsMaxStep(float oldMax, float newMax, float stepShare)
        {
            var delta = Math.Abs(newMax - oldMax);
            return delta >= 1f && delta >= stepShare * Math.Max(oldMax, newMax);
        }

        public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

        // ---- pending-damage stripes (hud-resource-states §3) ------------------------------------------------------

        public struct Stripe
        {
            public int Key;      // effect key of the first contributor (colour), -1 = merged "other"
            public float Start;  // px from the bar's left edge
            public float Width;  // px
        }

        // Pending damage drawn as <= maxStripes stripes inside the current fill, right-aligned from the fill end,
        // in the given (fixed) order: the first entry sits next to the fill end. Total capped at current value; each
        // stripe at least minWidth px without growing the total (stripes that cannot get minWidth merge into the
        // last kept one). Entries past maxStripes merge into the last stripe (key -1 = "other").
        // pending: (key, amount) in units; barPx = px of the full max B. Returns the stripe count written to output.
        // Scratch (main thread only): no allocation per frame.
        private static float[] _amounts = new float[3], _widths = new float[3];
        private static int[] _keys = new int[3];

        public static int Stripes(IList<KeyValuePair<int, float>> pending, float current, float max, float barPx,
            float minWidth, int maxStripes, Stripe[] output)
        {
            if (pending == null || output == null || !(max > 0f) || !(current > 0f) || maxStripes <= 0) return 0;
            var count = 0;
            var slots = Math.Min(maxStripes, output.Length);
            if (slots <= 0) return 0;
            if (_amounts.Length < slots) { _amounts = new float[slots]; _keys = new int[slots]; _widths = new float[slots]; }
            var amounts = _amounts;
            var keys = _keys;
            for (var n = 0; n < pending.Count; n++)
            {
                var p = pending[n];
                if (!(p.Value > 0f) || float.IsInfinity(p.Value)) continue;
                if (count < slots)
                {
                    keys[count] = p.Key;
                    amounts[count++] = p.Value;
                }
                else
                {
                    amounts[slots - 1] += p.Value;
                    keys[slots - 1] = -1;
                }
            }
            if (count == 0) return 0;
            var fillPx = barPx * Math.Min(current, max) / max;
            var total = 0f;
            for (var i = 0; i < count; i++) total += amounts[i];
            var totalPx = Math.Min(fillPx, barPx * Math.Min(total, current) / max);
            if (!(totalPx > 0f)) return 0;
            // Widths proportional to amounts inside the capped total.
            var widths = _widths;
            for (var i = 0; i < count; i++) widths[i] = totalPx * amounts[i] / total;
            // Too narrow for every stripe at minWidth: keep as many as fit, the last kept one absorbs the rest.
            var fit = minWidth > 0f ? Math.Max(1, (int)Math.Floor(totalPx / minWidth)) : count;
            if (fit < count)
            {
                for (var i = fit; i < count; i++) widths[fit - 1] += widths[i];
                keys[fit - 1] = -1;
                count = fit;
            }
            // Raise narrow stripes to minWidth, taking the px from the widest ones (total unchanged).
            for (var guard = 0; guard < count * 2; guard++)
            {
                var narrow = -1;
                for (var i = 0; i < count; i++)
                    if (widths[i] < minWidth - 0.001f) { narrow = i; break; }
                if (narrow < 0) break;
                var widest = 0;
                for (var i = 1; i < count; i++)
                    if (widths[i] > widths[widest]) widest = i;
                var need = minWidth - widths[narrow];
                var spare = widths[widest] - minWidth;
                if (spare <= 0f) break;
                var take = Math.Min(need, spare);
                widths[widest] -= take;
                widths[narrow] += take;
            }
            var end = fillPx;
            for (var i = 0; i < count; i++)
            {
                output[i] = new Stripe { Key = keys[i], Start = end - widths[i], Width = widths[i] };
                end -= widths[i];
            }
            return count;
        }

        // ---- reserved segment (hud-resource-states §2.3) ---------------------------------------------------------

        // Locked segment at the bar end: [C/B, 1] of the bar. Returns its px start and width (width 0 = none).
        public static void Reserved(float available, float max, float barPx, out float start, out float width)
        {
            start = barPx;
            width = 0f;
            if (!(max > 0f) || !(available >= 0f) || available >= max) return;
            start = barPx * available / max;
            width = barPx - start;
        }

        // ---- flow layout (ui-skin-engine-plan §8a) -------------------------------------------------------------

        public struct Flow
        {
            public int Columns;
            public int Rows;
            public int Visible;     // entries drawn as cells
            public int Overflow;    // entries behind the "+N" cell (or behind the full-list action when no rows)
            public bool MoreCell;   // the last cell is the "+N" cell
        }

        // Rows that fit a height budget.
        public static int MaxRows(float budget, float cell, float gap) =>
            cell <= 0f || budget < cell ? 0 : (int)Math.Floor((budget + gap) / (cell + gap));

        public static int Columns(float width, float cell, float gap) =>
            cell <= 0f ? 1 : Math.Max(1, (int)Math.Floor((width + gap) / (cell + gap)));

        // Wrapping flow of `count` fixed cells in `width`, at most maxRows rows. Overflow reserves the last cell for
        // "+N" before counting N. maxRows 0 = nothing drawn, all entries overflow (reachable through the full list).
        public static Flow Wrap(int count, float cell, float gap, float width, int maxRows)
        {
            var f = new Flow { Columns = Columns(width, cell, gap) };
            if (count <= 0) return f;
            if (maxRows <= 0)
            {
                f.Overflow = count;
                return f;
            }
            var capacity = f.Columns * maxRows;
            if (count <= capacity)
            {
                f.Visible = count;
                f.Rows = (count + f.Columns - 1) / f.Columns;
                return f;
            }
            f.Visible = capacity - 1;
            f.Overflow = count - f.Visible;
            f.MoreCell = true;
            f.Rows = maxRows;
            return f;
        }

        // ---- scale and safe box (hud-resource-states §4) ---------------------------------------------------------

        // Physical px per reference px of the HUD cluster. guiFactor = min(W/1920, H/1080) x GuiScale (the canvas
        // scale, GuiScaler.cs:54-58). The cluster shrinks (only itself) to fit maxW/maxH of the safe area, but the
        // font floors win: numbers >= floorNumberPx, timers >= floorTimerPx physical px.
        public static float ClusterScale(float guiFactor, float hudScale, float clusterW, float clusterH,
            float safeWPx, float safeHPx, float maxWShare, float maxHShare,
            float numberFont, float floorNumberPx, float timerFont, float floorTimerPx)
        {
            var s = guiFactor * Clamp(hudScale, 0.75f, 1.5f);
            if (clusterW > 0f && clusterH > 0f)
                s = Math.Min(s, Math.Min(maxWShare * safeWPx / clusterW, maxHShare * safeHPx / clusterH));
            var floor = 0f;
            if (numberFont > 0f) floor = Math.Max(floor, floorNumberPx / numberFont);
            if (timerFont > 0f) floor = Math.Max(floor, floorTimerPx / timerFont);
            return Math.Max(s, floor);
        }

        // Horizontal span the HUD corners use: on screens wider than ultraRatio (21:9 and up) with the box on, a
        // centred 16:9 box of the safe height; else the safe area.
        public static void HudBox(float safeX, float safeW, float safeH, bool boxOn, float ultraRatio,
            out float x, out float w)
        {
            x = safeX;
            w = safeW;
            if (!boxOn || !(safeH > 0f) || safeW / safeH < ultraRatio) return;
            var box = safeH * 16f / 9f;
            if (box >= safeW) return;
            x = safeX + (safeW - box) / 2f;
            w = box;
        }

        // Whole seconds for status timers ("12", "1:05", "5m" past 10 min). Ceil so 0.4 s shows "1".
        public static string Timer(float seconds)
        {
            if (!(seconds > 0f) || float.IsInfinity(seconds)) return "";
            var s = (int)Math.Ceiling(seconds);
            if (s < 60) return s.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (s < 600) return (s / 60).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            return ((s + 59) / 60).ToString(System.Globalization.CultureInfo.InvariantCulture) + "m";
        }
    }
}
