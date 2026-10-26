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
            public bool CentreWings;     // target panel (user 2026-10-26): wings against the bar block, grown outward
            public WingCut CutL, CutR;   // wing art slices in that mode (sprite px)
            public float JoinL, JoinR;   // bar block side -> wing inner end (the wing-tip diamond + a short rail between)
            public float EndPadL, EndPadR; // how far the end medallion reaches past the wing's outer end
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
            public int FoodShown, PotionShown, FoodMax, PotionMax; // slots drawn / that fit inside the strip
            public WingRun WingL, WingR;                 // centre-wings mode: the composed wing pieces

            // Left edge of food / potion cell i (reading order left -> right). Centre wings: the art's own slot
            // centres (the last food at the left wing's inner end, the first potion at the right wing's); else a
            // uniform row from FoodX / PotionX.
            public float FoodLeft(int i) => WingL.Slots > 0 ? WingL.SlotCenter(WingL.Slots - 1 - i) - FoodCell / 2f : FoodX + i * FoodStep;
            public float PotionLeft(int i) => WingR.Slots > 0 ? WingR.SlotCenter(i) - PotionCell / 2f : PotionX + i * PotionStep;
        }

        // Wing art slices (centre-wings mode), sprite px of the wing sprite (W x H): the tip piece (inner pointed end +
        // the slot nearest the bar block), one segment (separator + one slot area, repeated per extra slot) and the outer
        // end (under the end medallion). Cuts sit on the separators' outer edges, so tip -> segment and segment ->
        // segment joins continue the art. Slot* = centre x of the tip's / segment's slot area. InnerRight: the inner end
        // is the sprite's right edge (left wing); else its left edge (right wing).
        public struct WingCut
        {
            public float W, H, Tip, Seg, Outer, SlotTip, SlotSeg;
            public bool InnerRight;
        }

        // A composed wing in strip units: tip at the inner end, Segs segments outward, outer piece last. Source rects
        // (art px, x0 / width) of the pre-cut pieces. Slot k (0 = nearest the bar block): k = 0 on the tip's own slot
        // area, k >= 1 on segment k - 1's (Slot1 + (k - 1) * Step; Step < 0 for the left wing). X / Right = the extent.
        public struct WingRun
        {
            public int Slots, Segs;
            public Box Tip, Outer;
            public float SegX, SegW;      // left edge of the first (innermost) segment, width of one
            public float SegDX;           // offset from one segment to the next outward (-SegW left wing, +SegW right)
            public float TipSrc, TipSrcW, SegSrc, SegSrcW, OuterSrc, OuterSrcW;
            public float Slot0, Slot1, Step, X, Right, Inner;
            public float SlotCenter(int k) => k <= 0 ? Slot0 : Slot1 + (k - 1) * Step;
            public Box Seg(int j) => new Box(SegX + j * SegDX, Tip.Y, SegW, Tip.H);
        }

        // Pure: the wing for n slots with its inner end at innerX, drawn h units high (the art's own aspect).
        public static WingRun Wing(in WingCut c, int n, float innerX, float h)
        {
            var r = new WingRun { Inner = innerX, X = innerX, Right = innerX };
            if (n <= 0 || c.W <= 0f || c.H <= 0f) return r;
            var s = h / c.H;
            float tipLen, segLen, outLen, slotTip, inSeg;
            if (c.InnerRight)
            {
                r.TipSrc = c.Tip; r.TipSrcW = c.W - c.Tip;
                r.SegSrc = c.Seg; r.SegSrcW = c.Tip - c.Seg;
                r.OuterSrc = 0f; r.OuterSrcW = c.Outer;
                slotTip = c.W - c.SlotTip;
                inSeg = c.Tip - c.SlotSeg;
            }
            else
            {
                r.TipSrc = 0f; r.TipSrcW = c.Tip;
                r.SegSrc = c.Tip; r.SegSrcW = c.Seg - c.Tip;
                r.OuterSrc = c.Outer; r.OuterSrcW = c.W - c.Outer;
                slotTip = c.SlotTip;
                inSeg = c.SlotSeg - c.Tip;
            }
            tipLen = r.TipSrcW; segLen = r.SegSrcW; outLen = r.OuterSrcW;
            var dir = c.InnerRight ? -1f : 1f;
            // outward interval [a, b] (sprite px from the inner end) -> box in strip units
            Box Out(float a, float b) => dir < 0f ? new Box(innerX - b * s, 0f, (b - a) * s, h) : new Box(innerX + a * s, 0f, (b - a) * s, h);
            r.Slots = n;
            r.Segs = n - 1;
            r.Tip = Out(0f, tipLen);
            var first = Out(tipLen, tipLen + segLen);
            r.SegX = first.X;
            r.SegW = segLen * s;
            r.SegDX = dir * segLen * s;
            var outA = tipLen + (n - 1) * segLen;
            r.Outer = Out(outA, outA + outLen);
            var total = (outA + outLen) * s;
            r.X = dir < 0f ? innerX - total : innerX;
            r.Right = dir < 0f ? innerX : innerX + total;
            // every slot on its own art slot area (Codex review: no averaged pitch): the tip's, then one per segment
            r.Slot0 = innerX + dir * slotTip * s;
            r.Slot1 = innerX + dir * (tipLen + inSeg) * s;
            r.Step = dir * segLen * s;
            return r;
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
            if (i.CentreWings) CentreLayout(i, ref o);
            return o;
        }

        // Target panel, centre wings (user 2026-10-26, mockups bottom-hud-center / -v2): each wing sits against the bar
        // block (wing-tip diamond + a short rail between) and grows outward with its slot count; the end medallion caps
        // the outer end. Slots keep reading order left -> right: the last food sits at the inner end of the left wing,
        // the first potion at the inner end of the right wing. A count is clipped to what fits inside the strip (end
        // medallion included) and to WingMax.
        private static void CentreLayout(in Input i, ref Layout o)
        {
            var fc = i.FoodSize > 0f ? i.FoodSize : i.Cell;
            var pc = i.PotionSize > 0f ? i.PotionSize : i.Cell;
            var innerL = o.BarsX - i.JoinL;
            var innerR = o.BarsX + o.BarsW + i.JoinR;
            var fmax = MaxSlots(i.CutL, innerL, i.WingH, i.WingMax, i.EndPadL, float.MaxValue);
            var pmax = MaxSlots(i.CutR, innerR, i.WingH, i.WingMax, float.MinValue, i.Width - i.EndPadR);
            o.FoodMax = fmax;
            o.PotionMax = pmax;
            o.FoodShown = Math.Min(Math.Max(0, i.Food), fmax);
            o.PotionShown = Math.Min(Math.Max(0, i.Potions), pmax);
            o.WingL = Wing(i.CutL, o.FoodShown, innerL, i.WingH);
            o.WingR = Wing(i.CutR, o.PotionShown, innerR, i.WingH);
            o.FoodCell = fc;
            o.FoodStep = Math.Abs(o.WingL.Step);
            o.PotionCell = o.PotionH = pc;
            o.PotionStep = Math.Abs(o.WingR.Step);
            o.FoodX = o.FoodShown > 0 ? o.FoodLeft(0) : innerL;      // leftmost food = the outermost slot
            o.PotionX = o.PotionShown > 0 ? o.PotionLeft(0) : innerR;
            o.LeftW = o.WingL.Right;
            o.RightX = o.WingR.X;
            o.RightW = o.WingR.Right - o.WingR.X;
            var inner = i.WingInnerY > 0f ? i.WingInnerY : i.WingH / 2f;
            o.FoodY = inner - fc / 2f - i.FoodLift;
            o.PotionY = inner - pc / 2f;
            o.Fits = o.FoodShown == i.Food && o.PotionShown == i.Potions;
        }

        // Slots that fit with the wing inside [lo, hi] (strip units; the end medallion's overhang already taken off).
        private static int MaxSlots(in WingCut c, float inner, float h, int cap, float lo, float hi)
        {
            var limit = cap > 0 ? cap : 16;
            var n = 0;
            while (n < limit)
            {
                var r = Wing(c, n + 1, inner, h);
                if (r.X < lo || r.Right > hi) break;
                n++;
            }
            return n;
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
            public Box EndL, EndR, WingL, WingR, CapL, CapR, TipL, TipR, Knot2L, Knot2R;
            public float Bottom;
        }

        public static Frame TargetFrame(in Layout l, Func<string, float, float> t7)
        {
            var f = new Frame();
            // Centre wings (user 2026-10-26, mockup bottom-hud-center-v2): each wing's pointed tip (its own diamond gem and
            // rail stubs) sits right against the bar block's inner diamond - one connector, no separate wing-tip diamond or
            // rail run; the end medallion past the outer end (wing.x / wing.r.right = how far it reaches beyond it). No
            // slots on a side = no wing or medallion there (the block keeps its own tip art).
            float wingH = t7("wing.h", 95f);
            var wl = l.WingL;
            var wr = l.WingR;
            bool onL = wl.Slots > 0, onR = wr.Slots > 0;
            float endW = t7("end.w", 132.5f), endH = t7("end.h", 115.3f), endY = t7("end.y", -10f);
            float endRW = t7("end.r.w", endW), endRH = t7("end.r.h", endH), endRY = t7("end.r.y", endY);
            float endPadL = t7("wing.x", 85f), endPadR = t7("wing.r.right", 60.5f);
            f.EndL = onL ? new Box(wl.X - endPadL, endY, endW, endH) : new Box(wl.Inner, endY, 0f, endH);
            f.EndR = onR ? new Box(wr.Right + endPadR - endRW, endRY, endRW, endRH) : new Box(wr.Inner, endRY, 0f, endRH);
            f.WingL = new Box(wl.X, 0f, wl.Right - wl.X, wingH);
            f.WingR = new Box(wr.X, 0f, wr.Right - wr.X, wingH);
            float k2dx = t7("knot2.dx", -21.6f), k2y = t7("knot2.y", 47.5f), k2w = t7("knot2.w", 30.5f), k2h = t7("knot2.h", 32.6f);
            f.Knot2L = new Box(l.BarsX + k2dx - k2w / 2f, k2y - k2h / 2f, k2w, k2h);
            f.Knot2R = new Box(l.BarsX + l.BarsW - k2dx - k2w / 2f, k2y - k2h / 2f, k2w, k2h);
            // Rail under the joint (user crops 0.20.0: separate tip caps of another height left a hard step and see-through
            // gaps): one tiled rail from INSIDE the wing tip (rail.stub.wing units under its chevron, whose notches are
            // see-through) under the inner diamond to rail.under units inside the bar block, at the rail's native height
            // on the target's rows (rail.y / rail.h; runs 20261024-134656 / -143631: stretched rows drifted). No wing: it
            // starts under the block's own inner diamond (no rail into the scene).
            float railY = t7("rail.y", 15.8f), railH = t7("rail.h", 60.6f);
            float railRY = t7("rail.r.y", railY), railRH = t7("rail.r.h", railH);
            var inWing = t7("rail.stub.wing", 30f);
            var under = t7("rail.under", 24f);
            var capLX = onL ? Math.Min(wl.Inner - inWing, f.Knot2L.X) : f.Knot2L.X;
            f.CapL = new Box(capLX, railY, Math.Max(0f, l.BarsX + under - capLX), railH);
            var capRR = onR ? Math.Max(wr.Inner + inWing, f.Knot2R.Right) : f.Knot2R.Right;
            var capRX = l.BarsX + l.BarsW - under;
            f.CapR = new Box(capRX, railRY, Math.Max(0f, capRR - capRX), railRH);            // block-tip caps: exact crops of the target at the bar block tips (target 428..450 / 1178..1200 x 148..194: the
            // rail's glint and the bead under the inner diamond, which the plain rail lacks - joint C 10.87 -> 8.03 dE76
            // predicted) on their target boxes, over the rail extensions and under the inner diamond and the block tip
            float tipY = t7("tipcap.y", 13f), tipW = t7("tipcap.w", 0f), tipH = t7("tipcap.h", 66.3f);
            f.TipL = new Box(l.BarsX + t7("tipcap.dx", -14.4f), tipY, tipW, tipH);
            f.TipR = new Box(l.BarsX + l.BarsW + t7("tipcap.r.dx", -17.3f), tipY, tipW, tipH);
            f.Bottom = Math.Min(0f, Math.Min(Math.Min(endY, endRY), Math.Min(f.CapL.Y, f.CapR.Y)));
            return f;
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
