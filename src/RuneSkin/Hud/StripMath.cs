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
            public float WingInnerY;     // centre of the wing plate's inner area above the strip bottom (0 = WingH / 2)
            public float FoodLift;       // food disc centre above its cell centre (the timer band under the disc)
            public float BarsExtra;      // bar block reaches this far past the hotbar block on each side (target panel)
            public float ConnW;          // connector knot over the wing | bar block joint (slots stop before its half)
            public bool GrowFromEnds;    // target panel: fixed cells + fixed gap from the outer medallion inwards
            public float WingGap;        // gap between wing slots in that mode (0 = Gap)
            public float PotionStart, PotionGap; // right wing in that mode: margin from the right end / gap (0 = as food)
            public float CenterDX;       // centre column offset from the strip middle (0; offline comparisons only)
            public float MedRW, MedRTuck; // right medallion width / tuck when its art differs from the left (0 = as left)
            public float FoodSize, PotionSize; // wing slot sides in that mode (target: round 52 px / square 53 px; 0 = Cell)
            public float CellsDX;        // cell row offset inside the hotbar block (target: the left pad is narrower)
            public float AbilityCell;    // ability cell side (target: 63 px vs the 59 px hotbar cell; 0 = Cell)
            public int WingMax;          // slots per wing at most (user 2026-10-23: 0..4; 0 = no cap)
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
            public int FoodShown, PotionShown, FoodMax, PotionMax; // slots drawn / that fit before the connector knot
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
            o.AbilityW = Zone(i.Abilities, i.AbilityCell > 0f ? i.AbilityCell : i.Cell, i.Gap);
            // The block closes its width around the live ability count (no set skill: one cell, no empty socket).
            var topW = o.HotbarW + (i.Abilities > 0 ? i.ZoneGap + o.AbilityW : 0f);
            o.CenterW = Math.Max(topW + 2f * i.BlockPadX, i.MinCenterW);
            o.CenterX = (i.Width - o.CenterW) / 2f + i.CenterDX;
            o.BarsX = o.CenterX - Math.Max(0f, i.BarsExtra);
            o.BarsW = o.CenterW + 2f * Math.Max(0f, i.BarsExtra);
            o.BarsY = 0f;
            o.BarsH = i.BarsH;
            o.BlockY = i.BarsH;
            o.BlockH = i.BlockPadB + i.DurH + i.CellH + i.BlockPadT;
            o.Height = o.BlockY + o.BlockH;
            o.CellsY = o.BlockY + i.BlockPadB + i.DurH;
            o.HotbarX = o.CenterX + (o.CenterW - topW) / 2f + i.CellsDX;
            o.AbilityX = o.HotbarX + o.HotbarW + (i.Abilities > 0 ? i.ZoneGap : 0f);
            o.DividerX = o.HotbarX + o.HotbarW + i.ZoneGap / 2f;
            // Medallions against the block sides, their joins tucked under the block frame.
            o.MedY = i.MedY - i.MedH / 2f;
            o.MedLX = o.CenterX - i.MedW + i.MedTuck;
            o.MedRX = o.CenterX + o.CenterW - (i.MedRW > 0f ? i.MedRTuck : i.MedTuck);

            // Wings: one slot row each at the bottom, end-cap at the outer edge, tucked under the bar block.
            o.WingH = i.WingH;
            o.LeftW = Math.Max(0f, o.BarsX + i.WingTuck);
            o.RightX = o.BarsX + o.BarsW - i.WingTuck;
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
            // Sockets centred on the plate's inner area (user 2026-10-23: they sat high): the food DISC centre (the cell's
            // timer band hangs under it) and the potion cell centre on WingInnerY.
            var inner = i.WingInnerY > 0f ? i.WingInnerY : i.WingH / 2f;
            o.FoodY = inner - o.FoodCell / 2f - i.FoodLift;
            o.PotionY = inner - o.PotionH / 2f;
            o.Fits = o.CenterX >= i.KnotW + i.SlotPad && (i.Food == 0 || o.FoodCell >= i.FoodCell * 0.6f) && (i.Potions == 0 || o.PotionCell >= i.PotionCell * 0.6f);
            o.FoodShown = i.Food;
            o.PotionShown = i.Potions;
            if (i.GrowFromEnds) GrowLayout(i, ref o);
            return o;
        }

        // Target panel (user 2026-10-23): the wing frame is fixed; food slots start at the left medallion and grow
        // rightward, potions start at the right medallion and grow leftward, fixed square cells and a fixed gap; a slot
        // never reaches the connector knot over the wing | bar block joint (the count is clipped to what fits).
        private static void GrowLayout(in Input i, ref Layout o)
        {
            var fc = i.FoodSize > 0f ? i.FoodSize : i.Cell;
            var pc = i.PotionSize > 0f ? i.PotionSize : i.Cell;
            var g = i.WingGap > 0f ? i.WingGap : Math.Max(0f, i.Gap);
            var start = i.KnotW + i.SlotPad;
            var stop = o.BarsX - i.ConnW / 2f;
            var pitch = fc + g;
            var max = pitch > 0f ? Math.Max(0, (int)Math.Floor((stop - start + g) / pitch)) : 0;
            if (i.WingMax > 0) max = Math.Min(max, i.WingMax);
            o.FoodMax = max;
            o.FoodShown = Math.Min(i.Food, max);
            o.FoodCell = fc;
            o.FoodStep = pitch;
            o.FoodX = start;
            // right wing mirrored: the last potion sits against the right medallion, earlier ones to its left
            var ps = i.PotionStart > 0f ? i.PotionStart : start;
            var pg = i.PotionGap > 0f ? i.PotionGap : g;
            var pmax = pc + pg > 0f ? Math.Max(0, (int)Math.Floor((i.Width - ps - (o.BarsX + o.BarsW + i.ConnW / 2f) + pg) / (pc + pg))) : 0;
            if (i.WingMax > 0) pmax = Math.Min(pmax, i.WingMax);
            o.PotionMax = pmax;
            o.PotionShown = Math.Min(i.Potions, pmax);
            o.PotionCell = o.PotionH = pc;
            o.PotionStep = pc + pg;
            o.PotionX = i.Width - ps - o.PotionShown * pc - Math.Max(0, o.PotionShown - 1) * pg;
            var inner = i.WingInnerY > 0f ? i.WingInnerY : i.WingH / 2f;
            o.FoodY = inner - fc / 2f - i.FoodLift;
            o.PotionY = inner - pc / 2f;
            o.Fits = o.FoodShown == i.Food && o.PotionShown == i.Potions;
        }
        public struct Box
        {
            public float X, Y, W, H;
            public Box(float x, float y, float w, float h) { X = x; Y = y; W = Math.Max(0f, w); H = Math.Max(0f, h); }
            public float Right => X + W;
            public float CenterX => X + W / 2f;
        }

        // Target panel frame pieces (strip-target/spec.md) in strip units, from the layout and the size.hud.strip7.*
        // tokens (t7(name, fallback)). The small diamonds sit ON a continuous rail (run + stub), no gap.
        // Bottom = the lowest piece edge (end medallion tips hang below the wing): the strip is placed so it is on screen.
        public struct Frame
        {
            public Box EndL, EndR, WingL, WingR, KnotL, KnotR, RailL, RailR, StubL, StubR, CapL, CapR, TipL, TipR, Knot2L, Knot2R;
            public float Bottom;
        }

        public static Frame TargetFrame(in Layout l, float width, Func<string, float, float> t7)
        {
            var f = new Frame();
            var w = width;
            float endW = t7("end.w", 132.5f), endH = t7("end.h", 115.3f), endY = t7("end.y", -10f);
            f.EndL = new Box(0f, endY, endW, endH);
            float endRW = t7("end.r.w", endW), endRH = t7("end.r.h", endH), endRY = t7("end.r.y", endY);
            f.EndR = new Box(w - endRW, endRY, endRW, endRH);
            float wingH = t7("wing.h", 95f), wingX = t7("wing.x", 85f), wingW = t7("wing.w", 371.7f), wingRW = t7("wing.r.w", 334.2f), wingRR = t7("wing.r.right", 111f);
            f.WingL = new Box(wingX, 0f, wingW, wingH);
            f.WingR = new Box(w - wingRR - wingRW, 0f, wingRW, wingH);
            float knotX = t7("knot.x", 448f), knotW = t7("knot.w", 40.3f), knotY = t7("knot.y", 26f), knotH = t7("knot.h", 43.2f), knotRR = t7("knot.r.right", 436.5f);
            f.KnotL = new Box(knotX, knotY, knotW, knotH);
            float knotRW = t7("knot.r.w", knotW), knotRH = t7("knot.r.h", knotH), knotRY = t7("knot.r.y", knotY);
            f.KnotR = new Box(w - knotRR - knotRW, knotRY, knotRW, knotRH);
            // Rail runs at the target's own scale (the piece stretched over the target's run: rail.x / rail.r.right), and
            // a short stub of the same tileable rail under the whole wing-tip diamond up to the run's start: the
            // diamond's art ends short of its box, so without it a gap showed between diamond and rail (run
            // 20261023-175632; the run itself must not move or stretch: its wire pattern is pinned to the target).
            // Rail rows (jointdiff / stripsim, runs 20261024-134656 vs -143631): the rail art is a crop of the target at its
            // own scale (2 sprite px per unit), so its wire rows only land on the target's at that scale. Fitting its
            // opaque rows onto the wing stub band (0.20.1 RailBand, x 1.21) moved the outer bright rows 2.8 target px out
            // (157.4 -> 154.6, 184.6 -> 187.5) and every joint got worse (A 9.43 -> 13.54 dE76). The box is the sprite at
            // native height on the target's rows (rail.y / rail.h; the left run uses the right rail's exact crop, whose
            // band is the target's full 37 px - the left tile's cut lost ~5 px of its dark edge rows).
            float railX = t7("rail.x", 486f), railRR = t7("rail.r.right", 474f), tuck = t7("rail.tuck", 6f);
            float railY = t7("rail.y", 15.8f), railH = t7("rail.h", 60.6f);
            float railRY = t7("rail.r.y", railY), railRH = t7("rail.r.h", railH);
            f.RailL = new Box(railX, railY, l.BarsX + t7("rail.tuck.l", tuck) - railX, railH);
            var rx = l.BarsX + l.BarsW - tuck;
            f.RailR = new Box(rx, railRY, w - railRR - rx, railRH);
            // Stubs: the same tileable rail from INSIDE the wing tip (rail.stub.wing under the tip's chevron, whose notches
            // are see-through - user crop 0.20.0: scene showing under the left tip) to the run. Tile phase: a stub / cap
            // abuts its run (no overlap) and starts its tiling at that joint (StubL / CapR are drawn mirrored, HudStrip),
            // so the wire pattern meets the run's first / last column instead of an arbitrary column (seam next to the
            // diamonds and the bar block tips).
            var inWing = t7("rail.stub.wing", 30f);
            var stubL = Math.Min(f.KnotL.X, f.WingL.Right - inWing);
            f.StubL = new Box(stubL, railY, railX - stubL, railH);
            var stubR = f.RailR.Right;
            f.StubR = new Box(stubR, f.RailR.Y, Math.Max(f.KnotR.Right, f.WingR.X + inWing) - stubR, f.RailR.H);
            // rail extensions under the bar block's pointed ends: the same rail (its own y / height, tiled) runs on from the
            // rail end to rail.under units INSIDE the block, so the block tip and the inner diamond are drawn over a
            // continuous rail (user crops 0.20.0: separate tip caps of another height left a hard rectangular step and
            // see-through gaps next to the tip)
            var under = t7("rail.under", 24f);
            var capLX = f.RailL.Right;
            f.CapL = new Box(capLX, f.RailL.Y, Math.Max(0f, l.BarsX + under - capLX), f.RailL.H);
            var capRR = f.RailR.X;
            var capRX = l.BarsX + l.BarsW - under;
            f.CapR = new Box(capRX, f.RailR.Y, Math.Max(0f, capRR - capRX), f.RailR.H);
            // block-tip caps: exact crops of the target at the bar block tips (target 428..450 / 1178..1200 x 148..194: the
            // rail's glint and the bead under the inner diamond, which the plain rail lacks - joint C 10.87 -> 8.03 dE76
            // predicted) on their target boxes, over the rail extensions and under the inner diamond and the block tip
            float tipY = t7("tipcap.y", 13f), tipW = t7("tipcap.w", 0f), tipH = t7("tipcap.h", 66.3f);
            f.TipL = new Box(l.BarsX + t7("tipcap.dx", -14.4f), tipY, tipW, tipH);
            f.TipR = new Box(l.BarsX + l.BarsW + t7("tipcap.r.dx", -17.3f), tipY, tipW, tipH);
            float k2dx = t7("knot2.dx", -21.6f), k2y = t7("knot2.y", 47.5f), k2w = t7("knot2.w", 30.5f), k2h = t7("knot2.h", 32.6f);
            f.Knot2L = new Box(l.BarsX + k2dx - k2w / 2f, k2y - k2h / 2f, k2w, k2h);
            f.Knot2R = new Box(l.BarsX + l.BarsW - k2dx - k2w / 2f, k2y - k2h / 2f, k2w, k2h);
            f.Bottom = Math.Min(0f, Math.Min(Math.Min(endY, endRY), Math.Min(f.RailL.Y, f.RailR.Y)));
            return f;
        }

        // A wing slot [x, x + w] covers a connector diamond when it spans the diamond's centre (the slot hides it).
        public static bool Covers(float x, float w, in Box knot) => w > 0f && x <= knot.CenterX && x + w >= knot.CenterX;

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

        // Chat window lift (its parent's units): the drawn bottom at home below the strip top (+ gap) -> raised by the
        // difference; already above -> 0 (never lowered).
        public static float ChatLift(float homeBottom, float clearTop) => Math.Max(0f, clearTop - homeBottom);

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
