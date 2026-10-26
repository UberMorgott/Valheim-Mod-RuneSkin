using System;

namespace RuneSkin.PlayerHud
{
    // Pure geometry of the bottom HUD panel, mockup bottom-hud-center-v2 (user approved 2026-10-26; its bottom row,
    // 3 foods / 4 potions, is the single source). Every frame piece is a full-height column slice of that row (art px =
    // mockup 1080p px; sprites authored 2x): the centre = left end (bar block end + inner diamond) | the 8-cell hotbar run
    // | one ability slice per ability cell | right end; each wing = inner cap (strap + nub against the diamond) | one
    // segment per slot (the row's own segments, innermost first) | round triquetra end medallion. Pieces butt edge to
    // edge exactly as in the row, so food 3 / potions 4 IS the mockup. Cells, bars and texts are overlays on the art's
    // measured wells / troughs / sockets. Strip units = art px x Unit (the hotbar cell = the inventory slot), y up from
    // the strip bottom (the row's lowest opaque pixel).
    public static class StripMath
    {
        // Art geometry in art px (mockup 1080p px of the bottom row), from the theme (size.hud.panel.*).
        public struct WingArt
        {
            public float InnerW;   // inner cap: strap + nub, butts the centre's end
            public float SegW;     // one segment = one slot pitch, cut centred on its slot (slot centre = SegW / 2)
            public float EndW;     // round end medallion cap
            public float SlotY;    // slot centre above the strip bottom
            public int Arts;       // distinct segment slices of the row (innermost first; food 3, potions 4)
            public float EndJoint; // plate margin inside the end piece before its strap, from the inner edge (art px): the
                                   // mirrored potion end carries the food wing's socket margin (food 0, potion 10.5)
        }

        public struct Input
        {
            public float Width;          // strip outer width (units)
            public float Unit;           // strip units per art px
            public float CenterDX;       // centre offset from the strip middle (offline comparisons only)
            public float H;              // row height (art px): every piece is a full-height column slice
            public float EndLW, HotbarW, AbilityW, EndRW; // centre slices (art px)
            public float Cell, CellH;    // hotbar cell (units: the inventory slot)
            public float AbilityCell;    // ability cell side (units)
            public float HotbarC0;       // centre of hotbar cell 0 from the hotbar slice's left edge (art px)
            public float HotbarPitch;    // hotbar cell pitch (art px)
            public float AbilityC;       // centre of the ability cell in its slice (art px)
            public float CellsCY;        // hotbar / ability cell centre above the strip bottom (art px)
            public float BarsL, BarsR;   // bar box insets from the centre's outer edges (art px)
            public float BarsB, BarsT;   // bar box bottom / top above the strip bottom (art px): resource row bottom, HP top
            public float WingTop;        // top of the wings above the strip bottom (art px): status rows start above it
            public WingArt FoodArt, PotionArt;
            public float FoodCell, PotionCell; // wing slot sides (units)
            public float FoodLift;       // food disc centre above its cell centre (the timer band under the disc)
            public int WingMax;          // slots per wing at most (0 = no cap)
            public int Hotbar, Abilities, Food, Potions;
        }

        // A composed wing in strip units: inner cap against the centre, Slots segments outward, the end cap last.
        public struct WingRun
        {
            public int Slots;
            public float Dir;            // -1 left wing (grows left), +1 right wing
            public Box Inner, End;
            public float Seg0X, SegW;    // left edge of the innermost segment, segment width
            public float SlotY;          // slot centre y
            public float X, Right;       // drawn extent (end cap included); X = Right = the centre's edge without slots
            public Box Seg(int k) => new Box(Seg0X + Dir * k * SegW, Inner.Y, SegW, Inner.H);
            public float SlotCenter(int k) => Seg0X + Dir * k * SegW + SegW / 2f;
        }

        public struct Layout
        {
            public float Height, Unit;                   // whole panel (row height)
            public float CenterX, CenterW;               // centre (both ends, hotbar run, ability slices)
            public Box EndL, Hotbar, EndR;               // centre slices
            public float AbilityX0, AbilitySliceW;       // first ability slice's left edge, slice width (one per cell)
            public float BarsX, BarsY, BarsW, BarsH;     // bar box: HP row at its top, the resource row at its bottom
            public float HotbarX, AbilityX, CellsY;      // cell row: left edges, bottom of the cells
            public float Gap;                            // gap between hotbar cells (pitch - cell)
            public float WingH;                          // top of the wings
            public float StatusL, StatusR;               // status rows end / start here (the centre's sides)
            public float DrawnLeft, DrawnRight;          // drawn extent (end medallion to end medallion)
            public WingRun WingL, WingR;
            public float FoodX, FoodCell, FoodY;
            public float PotionX, PotionCell, PotionH, PotionY;
            public bool Fits;                            // false: a wing count was clipped to the strip
            public int FoodShown, PotionShown, FoodMax, PotionMax; // slots drawn / that fit inside the strip
            public float JointL, JointR;                 // outermost cell edge -> its end medallion's strap (units; 0 = no slots)

            // Left edge of food / potion cell i (reading order left -> right): the last food sits in the left wing's
            // innermost segment, the first potion in the right wing's.
            public float FoodLeft(int i) => WingL.SlotCenter(FoodShown - 1 - i) - FoodCell / 2f;
            public float PotionLeft(int i) => WingR.SlotCenter(i) - PotionCell / 2f;
            public float AbilityLeft(int i, float cell) => AbilityX0 + i * AbilitySliceW + AbilityX - cell / 2f;
        }

        public struct Box
        {
            public float X, Y, W, H;
            public Box(float x, float y, float w, float h) { X = x; Y = y; W = Math.Max(0f, w); H = Math.Max(0f, h); }
            public float Right => X + W;
            public float CenterX => X + W / 2f;
        }

        public static float Zone(int n, float cell, float gap) => n > 0 ? n * cell + (n - 1) * Math.Max(0f, gap) : 0f;

        // Left edge of cell i in a zone starting at x.
        public static float CellX(float x, int i, float cell, float gap) => x + i * (cell + gap);

        // Segment art for slot k of n (k = 0 innermost) from `arts` distinct row slices: the innermost and the outermost
        // keep their own slice (the outermost meets the end medallion as in the row), the ones between take the row's
        // middle slices in order and repeat them past the row's count.
        public static int SegArt(int k, int n, int arts)
        {
            if (arts <= 1 || k <= 0) return 0;
            if (k >= n - 1) return arts - 1;
            return arts == 2 ? 1 : 1 + (k - 1) % (arts - 2);
        }

        // Pure: the wing for n slots, inner cap against the centre edge at innerX.
        public static WingRun Wing(in WingArt a, int n, float innerX, float dir, float unit, float h)
        {
            var r = new WingRun { Dir = dir, X = innerX, Right = innerX, SlotY = a.SlotY * unit };
            if (n <= 0) return r;
            r.Slots = n;
            float inW = a.InnerW * unit, segW = a.SegW * unit, endW = a.EndW * unit;
            r.SegW = segW;
            if (dir < 0f)
            {
                r.Inner = new Box(innerX - inW, 0f, inW, h);
                r.Seg0X = r.Inner.X - segW;
                var endX = r.Inner.X - n * segW - endW;
                r.End = new Box(endX, 0f, endW, h);
                r.X = endX;
                r.Right = innerX;
            }
            else
            {
                r.Inner = new Box(innerX, 0f, inW, h);
                r.Seg0X = r.Inner.Right;
                r.End = new Box(r.Inner.Right + n * segW, 0f, endW, h);
                r.X = innerX;
                r.Right = r.End.Right;
            }
            return r;
        }

        // Slots that fit with the wing (end medallion included) inside [0, width]: n = 0..cap.
        public static int MaxSlots(in WingArt a, float innerX, float dir, float unit, float width, int cap)
        {
            var limit = cap > 0 ? cap : 16;
            var room = dir < 0f ? innerX : width - innerX;
            room -= (a.InnerW + a.EndW) * unit;
            if (room < 0f || a.SegW <= 0f) return 0;
            return Math.Min(limit, (int)Math.Floor(room / (a.SegW * unit) + 1e-4f));
        }

        public static Layout Compute(in Input i)
        {
            var o = new Layout();
            var u = i.Unit > 0f ? i.Unit : 1f;
            o.Unit = u;
            o.Height = i.H * u;
            var abilities = Math.Max(0, i.Abilities);
            o.AbilitySliceW = i.AbilityW * u;
            o.CenterW = (i.EndLW + i.HotbarW + i.EndRW) * u + abilities * o.AbilitySliceW;
            o.CenterX = (i.Width - o.CenterW) / 2f + i.CenterDX;
            o.EndL = new Box(o.CenterX, 0f, i.EndLW * u, o.Height);
            o.Hotbar = new Box(o.EndL.Right, 0f, i.HotbarW * u, o.Height);
            o.AbilityX0 = o.Hotbar.Right;
            o.EndR = new Box(o.AbilityX0 + abilities * o.AbilitySliceW, 0f, i.EndRW * u, o.Height);
            // cells: the hotbar cell is the inventory slot; the gap fills the art's pitch
            o.Gap = i.HotbarPitch * u - i.Cell;
            o.HotbarX = o.Hotbar.X + i.HotbarC0 * u - i.Cell / 2f;
            o.AbilityX = i.AbilityC * u;
            o.CellsY = i.CellsCY * u - i.CellH / 2f;
            // bar box between the art's troughs
            o.BarsX = o.CenterX + i.BarsL * u;
            o.BarsW = Math.Max(0f, o.CenterW - (i.BarsL + i.BarsR) * u);
            o.BarsY = i.BarsB * u;
            o.BarsH = Math.Max(0f, (i.BarsT - i.BarsB) * u);
            o.WingH = i.WingTop * u;
            o.StatusL = o.CenterX;
            o.StatusR = o.CenterX + o.CenterW;
            // wings: counts clipped to the strip and WingMax
            var innerL = o.CenterX;
            var innerR = o.CenterX + o.CenterW;
            o.FoodMax = MaxSlots(i.FoodArt, innerL, -1f, u, i.Width, i.WingMax);
            o.PotionMax = MaxSlots(i.PotionArt, innerR, 1f, u, i.Width, i.WingMax);
            o.FoodShown = Math.Min(Math.Max(0, i.Food), o.FoodMax);
            o.PotionShown = Math.Min(Math.Max(0, i.Potions), o.PotionMax);
            o.WingL = Wing(i.FoodArt, o.FoodShown, innerL, -1f, u, o.Height);
            o.WingR = Wing(i.PotionArt, o.PotionShown, innerR, 1f, u, o.Height);
            o.FoodCell = i.FoodCell;
            o.PotionCell = o.PotionH = i.PotionCell;
            o.FoodY = o.WingL.SlotY - i.FoodCell / 2f - i.FoodLift;
            o.PotionY = o.WingR.SlotY - i.PotionCell / 2f;
            o.FoodX = o.FoodShown > 0 ? o.FoodLeft(0) : innerL;
            o.PotionX = o.PotionShown > 0 ? o.PotionLeft(0) : innerR;
            o.DrawnLeft = Math.Min(o.CenterX, o.WingL.X);
            o.DrawnRight = Math.Max(o.CenterX + o.CenterW, o.WingR.Right);
            // the ends mirror each other (user 2026-10-26): strap as far from the outermost food cell as from the last potion cell
            o.JointL = o.FoodShown > 0 ? o.FoodLeft(0) - (o.WingL.End.Right - i.FoodArt.EndJoint * u) : 0f;
            o.JointR = o.PotionShown > 0 ? o.WingR.End.X + i.PotionArt.EndJoint * u - (o.PotionLeft(o.PotionShown - 1) + o.PotionCell) : 0f;
            o.Fits = o.FoodShown == Math.Max(0, i.Food) && o.PotionShown == Math.Max(0, i.Potions);
            return o;
        }

        // Wing slot count (user 2026-10-26): every slot of the current capacity, empty ones included; never fewer than the
        // filled ones (a capacity that just shrank under eaten foods keeps them visible until they expire).
        public const int VanillaFoodSlots = 3; // Player.m_maxFoods (Player.cs:330)

        public static int WingSlots(int capacity, int filled) => Math.Max(Math.Max(0, capacity), Math.Max(0, filled));

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
