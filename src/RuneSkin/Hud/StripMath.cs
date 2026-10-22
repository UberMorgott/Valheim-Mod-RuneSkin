using System;

namespace RuneSkin.PlayerHud
{
    // Pure geometry of the HUD strip (mockups hud-2-final -> hud-3, user calls 2026-10-22): one compact strip along the
    // bottom, nearly the full width, no captions. Centre block = hotbar (8 bowl cells) + ability zone (0..4) on top, the
    // HP / shield bar and the stamina | eitr halves under them. Wings left (food 0..N) and right (potions 0..N) sit behind
    // the centre block, BOTTOM-aligned with it (their contents end on the bars' bottom line), knotwork ends at the outer
    // edges. All values in strip units (reference px of the strip root, y up from the strip bottom).
    public static class StripMath
    {
        // The knot ornament reaches KnotReserve of its width into the wing span.
        public const float KnotReserve = 0.6f;

        public struct Input
        {
            public float Width;          // strip outer width
            public float Cell;           // hotbar / ability cell width
            public float CellH;          // hotbar / ability cell height (compact strip: landscape cells)
            public float Gap;            // gap between cells of one zone
            public float ZoneGap;        // hotbar -> ability zone
            public float Pad;            // inner padding of every panel
            public float DurH;           // durability line + gap under the hotbar cells
            public float BarsH;          // the whole bar block (HP row + stamina | eitr row)
            public float MinBarsW;       // narrowest bar block
            public float KnotW;          // wing end ornament width
            public float WingOverlap;    // how far a wing reaches under the centre block
            public float FoodCell, PotionCell, PotionH, TimerH;
            public float MinCell;        // wing slots never scale below this
            public int Hotbar, Abilities, Food, Potions;
        }

        public struct Layout
        {
            public float Height;                         // whole strip (= centre block)
            public float CenterX, CenterW;
            public float HotbarX, AbilityX, CellsY;     // cell row: left edges, bottom of the cells
            public float HotbarW, AbilityW;
            public float BarsX, BarsY, BarsW;           // bar block
            public float WingY, WingH;                  // both wings
            public float LeftW, RightX, RightW;         // left wing [0, LeftW], right wing [RightX, RightX + RightW]
            public float FoodX, FoodCell, FoodStep, FoodY;
            public float PotionX, PotionCell, PotionH, PotionStep, PotionY;
            public float FoodCenterX, PotionCenterX;    // wing content centres
            public bool Fits;                           // false: centre + minimal wings exceed the width
        }

        public static float Zone(int n, float cell, float gap) => n > 0 ? n * cell + (n - 1) * Math.Max(0f, gap) : 0f;

        // Left edge of cell i in a zone starting at x.
        public static float CellX(float x, int i, float cell, float gap) => x + i * (cell + gap);

        // Cell side that fits n cells into avail (never larger than the wanted side, never below 0).
        public static float FitCell(int n, float cell, float gap, float avail)
        {
            if (n <= 0) return cell;
            var fit = (avail - (n - 1) * gap) / n;
            return Math.Max(0f, Math.Min(cell, fit));
        }

        public static Layout Compute(in Input i)
        {
            var o = new Layout();
            o.HotbarW = Zone(i.Hotbar, i.Cell, i.Gap);
            o.AbilityW = Zone(i.Abilities, i.Cell, i.Gap);
            var topW = o.HotbarW + (i.Abilities > 0 ? i.ZoneGap + o.AbilityW : 0f);
            var inner = Math.Max(topW, i.MinBarsW);
            o.CenterW = inner + 2f * i.Pad;
            o.CenterX = (i.Width - o.CenterW) / 2f;
            // Bottom-up: pad, bars, durability line, cells, pad.
            o.BarsX = o.CenterX + i.Pad;
            o.BarsW = inner;
            o.BarsY = i.Pad;
            o.CellsY = o.BarsY + i.BarsH + i.DurH;
            o.Height = o.CellsY + i.CellH + i.Pad;
            o.HotbarX = o.CenterX + i.Pad + (inner - topW) / 2f;
            o.AbilityX = o.HotbarX + o.HotbarW + (i.Abilities > 0 ? i.ZoneGap : 0f);

            // Wings on the strip bottom: food bowls with their timer line under them, potion cells; contents bottom-aligned
            // on the bars' bottom line (pad).
            var foodH = i.FoodCell + i.TimerH;
            o.WingY = 0f;
            o.WingH = Math.Min(o.Height, i.Pad + Math.Max(foodH, i.PotionH) + i.Pad);
            o.LeftW = Math.Max(0f, o.CenterX + i.WingOverlap);
            o.RightX = o.CenterX + o.CenterW - i.WingOverlap;
            o.RightW = Math.Max(0f, i.Width - o.RightX);

            // Usable span of a wing: from past the knot to the centre block's edge.
            var knot = i.KnotW * KnotReserve;
            var span = Math.Max(0f, o.CenterX - knot - 2f * i.Pad);
            // Wing slots (user rules 2026-10-22): wing fixed, the live count spread evenly over it - equal gaps incl. the
            // margins to the wing ends; when they do not fit the gaps shrink to the minimum, then all slots scale down
            // together to the min-size token.
            WingSlots(i.Food, i.FoodCell, span, i.Gap, i.MinCell, out var foodCell, out var fg);
            WingSlots(i.Potions, i.PotionCell, span, i.Gap, i.MinCell, out var potCell, out var pg);
            o.FoodCell = foodCell;
            o.PotionCell = potCell;
            o.PotionH = i.PotionCell > 0f ? i.PotionH * o.PotionCell / i.PotionCell : i.PotionH;
            o.FoodStep = o.FoodCell + fg;
            o.PotionStep = o.PotionCell + pg;
            var foodW = Zone(i.Food, o.FoodCell, fg);
            var potW = Zone(i.Potions, o.PotionCell, pg);
            o.FoodCenterX = knot + i.Pad + span / 2f;
            o.PotionCenterX = o.CenterX + o.CenterW + i.Pad + span / 2f;
            o.FoodX = o.FoodCenterX - foodW / 2f;
            o.PotionX = o.PotionCenterX - potW / 2f;
            o.FoodY = o.BarsY + i.TimerH;
            o.PotionY = o.BarsY;
            o.Fits = o.CenterX >= knot + 2f * i.Pad && (i.Food == 0 || o.FoodCell >= i.FoodCell * 0.6f) && (i.Potions == 0 || o.PotionCell >= i.PotionCell * 0.6f);
            return o;
        }

        // n slots over span, space-evenly (n + 1 equal gaps: margins = gaps). Too narrow: gaps shrink to minGap, then the
        // slots scale down together, never below minCell (then they overflow, centred). n = 0: nothing.
        public static void WingSlots(int n, float wantCell, float span, float minGap, float minCell, out float cell, out float gap)
        {
            cell = wantCell;
            gap = 0f;
            if (n <= 0) return;
            var even = (span - n * wantCell) / (n + 1);
            if (even >= minGap)
            {
                gap = even;
                return;
            }
            gap = Math.Max(0f, minGap);
            cell = Math.Max(Math.Min(wantCell, minCell), Math.Min(wantCell, (span - (n + 1) * gap) / n));
        }

        // Strip scale (strip units -> reference px): the configured height over the natural one, clamped.
        public static float HeightScale(float wantH, float naturalH) =>
            naturalH > 0f && wantH > 0f ? Math.Max(0.4f, Math.Min(2.5f, wantH / naturalH)) : 1f;

        // Strip outer width in screen px: share of the (safe / 16:9 box) width.
        public static float WidthPx(float boxW, float share) => Math.Max(0f, boxW) * Math.Max(0.3f, Math.Min(1f, share));

        // Status groups above the strip (user call 2026-10-22): buffs on one side, debuffs on the other, `perRow` cells a
        // row, rows growing upward. Width of a full row (the group's Arrange width).
        public static float RowWidth(int perRow, float cell, float gap) => Zone(Math.Max(1, perRow), cell, gap);
    }
}
