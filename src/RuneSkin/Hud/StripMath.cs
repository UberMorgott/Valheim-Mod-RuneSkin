using System;

namespace RuneSkin.PlayerHud
{
    // Pure geometry of the HUD strip, mockup hud-4M3 (user approved 2026-10-22). Centre column: the bar block at the
    // bottom (HP row on top, the resource row under it; a missing resource row only shortens the block's frame, nothing
    // above moves) and the hotbar block on it (8 hotbar cells, a braided divider, 1..2 ability cells). The two triquetra
    // medallions sit against the hotbar block's sides. Wings left (food 0..N) and right (potions 0..N) are one slot row
    // each at the bottom, from the knotwork end-cap at the outer edge to the bar block (tucked under it). All values in
    // strip units (mockup 1080p px), y up from the strip bottom.
    public static class StripMath
    {
        public struct Input
        {
            public float Width;          // strip outer width
            public float Cell;           // hotbar / ability cell width
            public float CellH;          // hotbar / ability cell height
            public float Gap;            // gap between cells of one zone
            public float ZoneGap;        // hotbar -> ability zone (the divider sits in it)
            public float BlockPadX;      // hotbar block: side frame + inner margin
            public float BlockPadT, BlockPadB; // hotbar block: top rail + margin, bottom margin (under the durability line)
            public float DurH;           // durability line + gap under the hotbar cells
            public float BarsH;          // bar block, full height (two rows); fixed so the hotbar never moves
            public float MinCenterW;     // narrowest centre column
            public float WingH;          // wing row height (rails included)
            public float KnotW;          // wing end-cap width (no slots over it)
            public float WingTuck;       // how far a wing reaches under the bar block
            public float MedW, MedH;     // medallion box
            public float MedY;           // medallion centre above the strip bottom
            public float MedTuck;        // how far a medallion's join reaches under the hotbar block
            public float FoodCell, PotionCell, PotionH, TimerH;
            public float MinCell;        // wing slots never scale below this
            public float SlotPad;        // margin inside a wing at its block end
            public int Hotbar, Abilities, Food, Potions;
        }

        public struct Layout
        {
            public float Height;                         // whole strip (bar block + hotbar block)
            public float CenterX, CenterW;               // centre column (bar block and hotbar block share it)
            public float BarsX, BarsY, BarsW, BarsH;     // bar block (full height)
            public float BlockY, BlockH;                 // hotbar block on top of the bar block
            public float HotbarX, AbilityX, CellsY;      // cell row: left edges, bottom of the cells
            public float HotbarW, AbilityW;
            public float DividerX;                       // centre x of the hotbar | ability divider (no abilities: unused)
            public float MedLX, MedRX, MedY;             // medallion boxes: left edges, bottom
            public float WingH;
            public float LeftW, RightX, RightW;          // left wing [0, LeftW], right wing [RightX, RightX + RightW]
            public float FoodX, FoodCell, FoodStep, FoodY;
            public float PotionX, PotionCell, PotionH, PotionStep, PotionY;
            public bool Fits;                            // false: centre + minimal wings exceed the width
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
            // The block closes its width around the live ability count (no set skill: one cell, no empty socket).
            var topW = o.HotbarW + (i.Abilities > 0 ? i.ZoneGap + o.AbilityW : 0f);
            o.CenterW = Math.Max(topW + 2f * i.BlockPadX, i.MinCenterW);
            o.CenterX = (i.Width - o.CenterW) / 2f;
            o.BarsX = o.CenterX;
            o.BarsW = o.CenterW;
            o.BarsY = 0f;
            o.BarsH = i.BarsH;
            o.BlockY = i.BarsH;
            o.BlockH = i.BlockPadB + i.DurH + i.CellH + i.BlockPadT;
            o.Height = o.BlockY + o.BlockH;
            o.CellsY = o.BlockY + i.BlockPadB + i.DurH;
            o.HotbarX = o.CenterX + (o.CenterW - topW) / 2f;
            o.AbilityX = o.HotbarX + o.HotbarW + (i.Abilities > 0 ? i.ZoneGap : 0f);
            o.DividerX = o.HotbarX + o.HotbarW + i.ZoneGap / 2f;
            // Medallions against the block sides, their joins tucked under the block frame.
            o.MedY = i.MedY - i.MedH / 2f;
            o.MedLX = o.CenterX - i.MedW + i.MedTuck;
            o.MedRX = o.CenterX + o.CenterW - i.MedTuck;

            // Wings: one slot row each at the bottom, end-cap at the outer edge, tucked under the bar block.
            o.WingH = i.WingH;
            o.LeftW = Math.Max(0f, o.CenterX + i.WingTuck);
            o.RightX = o.CenterX + o.CenterW - i.WingTuck;
            o.RightW = Math.Max(0f, i.Width - o.RightX);
            var span = Math.Max(0f, o.CenterX - i.KnotW - i.SlotPad);
            // Wing slots (user rules 2026-10-22): the live count spread evenly over the row - equal gaps incl. the margins
            // to the row ends; when they do not fit the gaps shrink to the minimum, then all slots scale down together.
            WingSlots(i.Food, i.FoodCell, span, i.Gap, i.MinCell, out var foodCell, out var fg);
            WingSlots(i.Potions, i.PotionCell, span, i.Gap, i.MinCell, out var potCell, out var pg);
            o.FoodCell = foodCell;
            o.PotionCell = potCell;
            o.PotionH = i.PotionCell > 0f ? i.PotionH * o.PotionCell / i.PotionCell : i.PotionH;
            o.FoodStep = o.FoodCell + fg;
            o.PotionStep = o.PotionCell + pg;
            var foodW = Zone(i.Food, o.FoodCell, fg);
            var potW = Zone(i.Potions, o.PotionCell, pg);
            o.FoodX = i.KnotW + (span - foodW) / 2f;
            o.PotionX = o.CenterX + o.CenterW + i.SlotPad + (span - potW) / 2f;
            o.FoodY = (i.WingH - o.FoodCell) / 2f; // the timer sits on the ring's lower arc (mockup), inside the box
            o.PotionY = (i.WingH - o.PotionH) / 2f;
            o.Fits = o.CenterX >= i.KnotW + i.SlotPad && (i.Food == 0 || o.FoodCell >= i.FoodCell * 0.6f) && (i.Potions == 0 || o.PotionCell >= i.PotionCell * 0.6f);
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

        // Resource row (under the HP row): the present entries share the width evenly; weights animate 0..1 when an entry
        // appears / goes (eitr food eaten / expired) so the split slides instead of jumping. Writes x / width of entry i
        // into xs / ws (row-local, gaps between visible entries); returns the summed weight (0 = empty row).
        public static float RowSplit(float rowW, float gap, float[] weights, int n, float[] xs, float[] ws)
        {
            var total = 0f;
            for (var k = 0; k < n; k++) total += Math.Max(0f, Math.Min(1f, weights[k]));
            if (total <= 0f)
            {
                for (var k = 0; k < n; k++) { xs[k] = 0f; ws[k] = 0f; }
                return 0f;
            }
            // Gaps fade in with the entries next to them: (total - 1) gaps' worth for total weight.
            var gaps = Math.Max(0f, total - 1f) * gap;
            var unit = Math.Max(0f, rowW - gaps) / total;
            var x = 0f;
            var before = 0f; // summed weight of the entries left of k
            for (var k = 0; k < n; k++)
            {
                var w = Math.Max(0f, Math.Min(1f, weights[k]));
                x += gap * Math.Min(1f, before) * w; // the gap in front of k fades with k and with what precedes it
                xs[k] = x;
                ws[k] = unit * w;
                x += ws[k];
                before += w;
            }
            return total;
        }

        // Shield perimeter (mockup hud-4M3): the glow starts on the left end cap and runs along the top and bottom edges
        // together; full at shield >= max (right cap lit). Returns the lit share of the straight part 0..1 and the caps.
        public static float ShieldRun(float shield, float max, out float leftCap, out float rightCap)
        {
            leftCap = rightCap = 0f;
            if (!(max > 0f) || !(shield > 0f)) return 0f;
            var f = Math.Min(1f, shield / max);
            leftCap = Math.Min(1f, f * 20f); // fades in over the first 5 %
            rightCap = f >= 0.995f ? 1f : 0f;
            return f;
        }

        // Shared slot size (user call 2026-10-23): the strip hotbar cell IS an inventory grid cell. Both come from the
        // theme token size.slot (canvas units) x the skin scale (InventoryMath clamps it to 0.5..2) x the GUI canvas scale.
        public static float SlotPx(float slotUnits, float skinScale, float canvasScale) =>
            Math.Max(0f, slotUnits) * Math.Max(0.5f, Math.Min(2f, skinScale)) * Math.Max(0f, canvasScale);

        // Strip scale (screen px per strip unit): the hotbar cell's design width (size.hud.strip.cell) maps onto the
        // slot; every other strip piece keeps its mockup proportion to that cell.
        public static float UnitPx(float slotPx, float cellUnits) => cellUnits > 0f && slotPx > 0f ? slotPx / cellUnits : 1f;

        // Strip outer width in screen px: share of the (safe / 16:9 box) width.
        public static float WidthPx(float boxW, float share) => Math.Max(0f, boxW) * Math.Max(0.3f, Math.Min(1f, share));

        // Font size (strip units) that renders at least floorPx screen px at unitPx px per unit.
        public static float FontFloor(float size, float floorPx, float unitPx) => unitPx > 0f ? Math.Max(size, floorPx / unitPx) : size;

        // Status groups above the wings: buffs on one side, debuffs on the other, `perRow` cells a row, rows growing
        // upward. Width of a full row (the group's Arrange width).
        public static float RowWidth(int perRow, float cell, float gap) => Zone(Math.Max(1, perRow), cell, gap);

        // Cells per status row that fit into avail (the row must end before the medallion), at least 1, at most want.
        public static int StatusPerRow(int want, float cell, float gap, float avail)
        {
            var fit = cell > 0f ? (int)Math.Floor((avail + Math.Max(0f, gap)) / (cell + Math.Max(0f, gap))) : want;
            return Math.Max(1, Math.Min(want, fit));
        }
    }
}
