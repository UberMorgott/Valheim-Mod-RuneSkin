using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    // Mockup hud-4M3 geometry (user approved 2026-10-22): centre column = bar block + hotbar block, medallions on the
    // block sides, wings = one bottom slot row each, resource row split, shield perimeter, centre gauges.
    public class StripLayoutTests
    {
        private static StripMath.Input In(int abilities = 2, int food = 4, int potions = 4, float width = 1860f) => new StripMath.Input
        {
            Width = width,
            Cell = 78f,
            CellH = 92f,
            Gap = 11f,
            ZoneGap = 30f,
            BlockPadX = 26f,
            BlockPadT = 18f,
            BlockPadB = 8f,
            DurH = 14f,
            BarsH = 128f,
            MinCenterW = 760f,
            WingH = 130f,
            KnotW = 72f,
            WingTuck = 14f,
            MedW = 218f,
            MedH = 142f,
            MedY = 172f,
            MedTuck = 14f,
            FoodCell = 84f,
            PotionCell = 76f,
            PotionH = 88f,
            TimerH = 24f,
            MinCell = 52f,
            SlotPad = 8f,
            Hotbar = 8,
            Abilities = abilities,
            Food = food,
            Potions = potions
        };

        [Fact]
        public void HotbarBlockSitsOnTheBarBlock()
        {
            var i = In();
            var l = StripMath.Compute(i);
            Assert.Equal(i.BarsH, l.BlockY);
            Assert.Equal(l.BlockY + l.BlockH, l.Height);
            Assert.Equal(i.BlockPadB + i.DurH + i.CellH + i.BlockPadT, l.BlockH);
            Assert.True(l.CellsY >= l.BlockY && l.CellsY + i.CellH <= l.Height);
            // bar block and hotbar block share the centre column, centred in the strip
            Assert.Equal(l.CenterX, l.BarsX);
            Assert.Equal(l.CenterW, l.BarsW);
            Assert.Equal(i.Width, 2f * l.CenterX + l.CenterW, 3);
        }

        [Fact]
        public void BlockClosesItsWidthAroundTheAbilityCount()
        {
            var two = StripMath.Compute(In(2));
            var one = StripMath.Compute(In(1));
            Assert.True(two.CenterW > one.CenterW);
            Assert.Equal(78f + 11f, two.CenterW - one.CenterW, 3);
            // cells inside the block's side frames
            Assert.True(two.HotbarX >= two.CenterX + 26f - 0.01f);
            Assert.True(two.AbilityX + two.AbilityW <= two.CenterX + two.CenterW - 26f + 0.01f);
            // divider in the zone gap
            Assert.True(two.DividerX > two.HotbarX + two.HotbarW && two.DividerX < two.AbilityX);
        }

        // User 2026-10-24: the hotbar-end medallions are removed from the target panel; the hotbar cells must not move
        // (column alignment). No cell / block / wing coordinate depends on the medallion geometry.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void HotbarCells_IndependentOfTheMedallions(bool centre)
        {
            var with = In();
            if (centre) Centre(ref with);
            var without = with;
            without.MedW = without.MedH = without.MedY = without.MedTuck = without.MedRW = without.MedRTuck = 0f;
            var a = StripMath.Compute(with);
            var b = StripMath.Compute(without);
            for (var n = 0; n < with.Hotbar; n++)
                Assert.Equal(StripMath.CellX(a.HotbarX, n, with.Cell, with.Gap), StripMath.CellX(b.HotbarX, n, without.Cell, without.Gap), 4);
            Assert.Equal(a.CellsY, b.CellsY, 4);
            Assert.Equal(a.AbilityX, b.AbilityX, 4);
            Assert.Equal((a.CenterX, a.CenterW, a.BarsX, a.BarsW, a.LeftW, a.RightX), (b.CenterX, b.CenterW, b.BarsX, b.BarsW, b.LeftW, b.RightX));
            Assert.Equal((a.FoodX, a.FoodStep, a.PotionX, a.PotionStep, a.FoodShown, a.PotionShown), (b.FoodX, b.FoodStep, b.PotionX, b.PotionStep, b.FoodShown, b.PotionShown));
        }

        [Fact]
        public void MedallionsTouchTheBlockSides()
        {
            var i = In();
            var l = StripMath.Compute(i);
            Assert.Equal(l.CenterX, l.MedLX + i.MedW - i.MedTuck, 3);
            Assert.Equal(l.CenterX + l.CenterW - i.MedTuck, l.MedRX, 3);
            Assert.Equal(i.MedY, l.MedY + i.MedH / 2f, 3);
        }

        [Fact]
        public void WingsAreOneBottomRowTuckedUnderTheBars()
        {
            var i = In();
            var l = StripMath.Compute(i);
            Assert.Equal(l.CenterX + i.WingTuck, l.LeftW, 3);
            Assert.Equal(l.CenterX + l.CenterW - i.WingTuck, l.RightX, 3);
            Assert.Equal(i.Width, l.RightX + l.RightW, 3);
            // food spread evenly between the end-cap and the bar block: equal gaps incl. the margins
            var span = l.CenterX - i.KnotW - i.SlotPad;
            var gap = l.FoodStep - l.FoodCell;
            Assert.Equal(gap, l.FoodX - i.KnotW, 2);
            Assert.Equal(gap, i.KnotW + span - (l.FoodX + 3 * l.FoodStep + l.FoodCell), 2);
            // slots vertically centred in the row
            Assert.Equal((i.WingH - l.FoodCell) / 2f, l.FoodY, 3);
            Assert.Equal((i.WingH - l.PotionH) / 2f, l.PotionY, 3);
            Assert.True(l.Fits);
        }

        [Fact]
        public void FivePotionsShrinkGapsThenCells()
        {
            var l = StripMath.Compute(In(potions: 5, width: 1500f));
            Assert.True(l.PotionStep - l.PotionCell >= 11f - 0.01f);
            Assert.True(l.PotionCell <= 76f);
            Assert.True(l.PotionCell >= 52f);
        }

        [Fact]
        public void WingSlotsSpaceEvenlyThenShrink()
        {
            StripMath.WingSlots(4, 84f, 500f, 11f, 52f, out var cell, out var gap);
            Assert.Equal(84f, cell);
            Assert.Equal((500f - 4 * 84f) / 5f, gap, 3);
            StripMath.WingSlots(5, 84f, 400f, 11f, 52f, out cell, out gap);
            Assert.Equal(11f, gap);
            Assert.Equal((400f - 6 * 11f) / 5f, cell, 3);
            StripMath.WingSlots(0, 84f, 400f, 11f, 52f, out cell, out gap);
            Assert.Equal(0f, gap);
        }

        // User call 2026-10-23: the strip spans ~96 % of the screen, its hotbar cells are the inventory grid cells (one
        // slot token), everything else scales with that cell. 16:9 screens: GUI canvas scale = height / 1080.
        [Theory]
        [InlineData(1280, 720)]
        [InlineData(1366, 768)]
        [InlineData(1920, 1080)]
        [InlineData(2560, 1440)]
        public void HotbarCellIsTheInventorySlotAtEveryResolution(int w, int h)
        {
            var canvas = System.Math.Min(w / 1920f, h / 1080f);
            var slot = StripMath.SlotPx(70f, 1f, canvas);
            var unit = StripMath.UnitPx(slot, 78f);
            var widthPx = StripMath.WidthPx(w, 0.96f);
            var l = StripMath.Compute(In(width: widthPx / unit));
            Assert.Equal(70f * canvas, 78f * unit, 3); // hotbar cell px = inventory grid cell px (InventoryMath: GridCell x k)
            Assert.Equal(0.96f * w, (l.RightX + l.RightW) * unit, 1);
            Assert.True(l.Fits);
            Assert.Equal(84f, l.FoodCell); // wide wings: nothing shrinks
            // height follows the slot: same share of the screen at every 16:9 size, well under the old 30 % band
            var share = l.Height * unit / h;
            Assert.InRange(share, 0.15f, 0.3f);
        }

        // Shipped wing art cuts (theme size.hud.strip7.wing*.src / cut / slot, art px of strip7_wing_l / _r).
        internal static readonly StripMath.WingCut CutL = new StripMath.WingCut { W = 1032f, H = 264f, Tip = 654f, Seg = 366f, Outer = 153f, SlotTip = 810f, SlotSeg = 534f, InnerRight = true };
        internal static readonly StripMath.WingCut CutR = new StripMath.WingCut { W = 1096f, H = 264f, Tip = 355f, Seg = 622f, Outer = 910f, SlotTip = 236f, SlotSeg = 503f };

        private static void Centre(ref StripMath.Input i)
        {
            i.CentreWings = true;
            i.CutL = CutL;
            i.CutR = CutR;
            i.WingH = 95f;
            i.JoinL = 76f;
            i.JoinR = 78f;
            i.EndPadL = 85f;
            i.EndPadR = 60.5f;
            i.BarsExtra = 57.7f;
            i.FoodSize = 75f;
            i.PotionSize = 77.1f;
            i.WingMax = 4;
        }

        // Centre wings (user 2026-10-26, mockup bottom-hud-center): each wing starts at a fixed join from the bar block
        // and grows outward with its count; the inner end never moves, the outer end moves out by one segment per slot.
        [Theory]
        [InlineData(3000f)]
        [InlineData(2600f)]
        [InlineData(3400f)]
        public void CentreWings_GrowOutwardFromTheBarBlock(float width)
        {
            float? lastL = null, lastR = null;
            for (var n = 0; n <= 4; n++)
            {
                var i = In(food: n, potions: n, width: width);
                Centre(ref i);
                var l = StripMath.Compute(i);
                Assert.Equal(n, l.FoodShown);
                Assert.Equal(n, l.PotionShown);
                Assert.Equal(l.BarsX - 76f, l.WingL.Inner, 2);
                Assert.Equal(l.BarsX + l.BarsW + 78f, l.WingR.Inner, 2);
                if (n == 0)
                {
                    Assert.Equal(0f, l.WingL.Right - l.WingL.X);
                    Assert.Equal(0f, l.WingR.Right - l.WingR.X);
                    continue;
                }
                // inner end fixed at the block, the wing grows away from it
                Assert.Equal(l.WingL.Inner, l.WingL.Right, 2);
                Assert.Equal(l.WingR.Inner, l.WingR.X, 2);
                if (lastL is float pl) Assert.Equal(l.WingL.SegW, pl - l.WingL.X, 2);
                if (lastR is float pr) Assert.Equal(l.WingR.SegW, l.WingR.Right - pr, 2);
                lastL = l.WingL.X;
                lastR = l.WingR.Right;
                // pieces abut: tip | segments | outer, no gap, no overlap
                Assert.Equal(l.WingL.Tip.X, n > 1 ? l.WingL.Seg(0).Right : l.WingL.Outer.Right, 2);
                Assert.Equal(l.WingL.X, l.WingL.Outer.X, 2);
                Assert.Equal(l.WingR.Tip.Right, n > 1 ? l.WingR.Seg(0).X : l.WingR.Outer.X, 2);
                Assert.Equal(l.WingR.Right, l.WingR.Outer.Right, 2);
                // slots inside their wing, reading order left -> right, no overlap; the last food / first potion innermost
                for (var k = 0; k < n; k++)
                {
                    var fx = l.FoodLeft(k);
                    var px = l.PotionLeft(k);
                    Assert.True(fx >= l.WingL.X && fx + l.FoodCell <= l.WingL.Right, $"food {k}/{n} {fx} in {l.WingL.X}..{l.WingL.Right}");
                    Assert.True(px >= l.WingR.X && px + l.PotionCell <= l.WingR.Right, $"potion {k}/{n} {px} in {l.WingR.X}..{l.WingR.Right}");
                    if (k > 0)
                    {
                        Assert.True(fx >= l.FoodLeft(k - 1) + l.FoodCell, $"food {k} overlaps");
                        Assert.True(px >= l.PotionLeft(k - 1) + l.PotionCell, $"potion {k} overlaps");
                    }
                }
                Assert.True(l.FoodLeft(n - 1) + l.FoodCell <= l.WingL.Inner && l.FoodLeft(n - 1) + l.FoodCell > l.WingL.Inner - l.WingL.Tip.W);
                Assert.True(l.PotionLeft(0) >= l.WingR.Inner && l.PotionLeft(0) < l.WingR.Inner + l.WingR.Tip.W);
                Assert.Equal(l.FoodLeft(0), l.FoodX, 2);
                Assert.Equal(l.PotionLeft(0), l.PotionX, 2);
                // symmetric about the strip centre within the art's own asymmetry (a few units)
                var mid = width / 2f;
                Assert.InRange((mid - l.WingL.Inner) - (l.WingR.Inner - mid), -10f, 10f);
            }
        }

        // Slots sit on the art's slot areas: the tip's slot centre and each segment's (Codex review: no averaged pitch).
        [Fact]
        public void CentreWings_SlotsOnTheArtSlotAreas()
        {
            var r = StripMath.Wing(CutL, 3, 500f, 95f);
            var s = 95f / 264f;
            Assert.Equal(500f - (1032f - 810f) * s, r.SlotCenter(0), 3);
            Assert.Equal(500f - ((1032f - 654f) + (654f - 534f)) * s, r.SlotCenter(1), 3);
            Assert.Equal(r.SlotCenter(1) - (654f - 366f) * s, r.SlotCenter(2), 3);
            var q = StripMath.Wing(CutR, 2, 100f, 95f);
            Assert.Equal(100f + 236f * s, q.SlotCenter(0), 3);
            Assert.Equal(100f + 503f * s, q.SlotCenter(1), 3);
            Assert.Equal(100f + (355f + 267f + 186f) * s, q.Right, 3);
            // source columns of the pre-cut pieces (cuts.json strip7_wing_*_tip / _seg / _outer)
            Assert.Equal((654f, 378f, 366f, 288f, 0f, 153f), (r.TipSrc, r.TipSrcW, r.SegSrc, r.SegSrcW, r.OuterSrc, r.OuterSrcW));
            Assert.Equal((0f, 355f, 355f, 267f, 910f, 186f), (q.TipSrc, q.TipSrcW, q.SegSrc, q.SegSrcW, q.OuterSrc, q.OuterSrcW));
        }

        // Counts clip to WingMax and to the strip (end medallion inside): a narrow strip shows fewer slots, never off-screen.
        [Fact]
        public void CentreWings_ClipToTheStrip()
        {
            var i = In(food: 9, potions: 9, width: 3400f);
            Centre(ref i);
            var l = StripMath.Compute(i);
            Assert.Equal(4, l.FoodShown);
            Assert.Equal(4, l.PotionShown);
            Assert.False(l.Fits);
            i = In(food: 4, potions: 4, width: 1300f);
            Centre(ref i);
            l = StripMath.Compute(i);
            Assert.True(l.FoodShown < 4 && l.PotionShown < 4, $"{l.FoodShown} / {l.PotionShown}");
            Assert.True(l.FoodShown == 0 || l.WingL.X - 85f >= 0f);
            Assert.True(l.PotionShown == 0 || l.WingR.Right + 60.5f <= 1300f);
        }

        // 16:9 / 21:9 / 16:10 at the default GUI scale: food 0..3 and potions 0..4 fit the default strip (no clipping); a larger GUI scale grows the slot and the strip and may clip the outer slots (CentreWings_ClipToTheStrip).
        [Theory]
        [InlineData(1920, 1080, 1f)]
        [InlineData(1280, 720, 1f)]
        [InlineData(2560, 1080, 1f)]
        [InlineData(3440, 1440, 1f)]
        [InlineData(1920, 1200, 1f)]
        [InlineData(2560, 1440, 1f)]
        public void CentreWings_FitEveryAspect(int w, int h, float gui)
        {
            var canvas = System.Math.Min(w / 1920f, h / 1080f) * gui;
            var unit = StripMath.UnitPx(StripMath.SlotPx(70f, 1f, canvas), 85f);
            var box = w / (float)h >= 2.2f ? System.Math.Min(w, h * 16f / 9f) : w;
            for (var f = 0; f <= 3; f++)
                for (var p = 0; p <= 4; p++)
                {
                    // the shipped centre column: 8 hotbar cells + the guardian power cell (theme size.hud.strip.*)
                    var i = In(abilities: 1, food: f, potions: p, width: StripMath.WidthPx(box, 1f) / unit);
                    i.Cell = i.CellH = 85f;
                    i.Gap = i.ZoneGap = 7.8f;
                    i.BlockPadX = 67.4f;
                    i.AbilityCell = 90.8f;
                    i.MinCenterW = 760f;
                    Centre(ref i);
                    var l = StripMath.Compute(i);
                    Assert.True(l.Fits, $"{w}x{h} gui {gui}: food {f} -> {l.FoodShown}, potions {p} -> {l.PotionShown} (width {i.Width:0})");
                }
        }
        [Fact]
        public void SkinScaleMovesBothTogether()
        {
            Assert.Equal(70f * 1.25f, StripMath.SlotPx(70f, 1.25f, 1f), 3);
            Assert.Equal(35f, StripMath.SlotPx(70f, 0.1f, 1f), 3); // InventoryMath's 0.5..2 clamp
            Assert.Equal(1f, StripMath.UnitPx(0f, 78f));
        }

        [Fact]
        public void FontFloorRaisesSmallTextOnly()
        {
            Assert.Equal(26f, StripMath.FontFloor(26f, 14f, 0.8f), 3);
            Assert.Equal(14f / 0.5f, StripMath.FontFloor(24f, 14f, 0.5f), 3);
        }

        [Fact]
        public void StatusRowsEndBeforeTheMedallion()
        {
            Assert.Equal(10, StripMath.StatusPerRow(10, 42f, 16f, 1000f));
            Assert.Equal(6, StripMath.StatusPerRow(10, 42f, 16f, 6 * 42f + 5 * 16f + 1f));
            Assert.Equal(1, StripMath.StatusPerRow(10, 42f, 16f, 10f));
            var l = StripMath.Compute(In(width: 2200f));
            var inset = 72f + 4f;
            var n = StripMath.StatusPerRow(10, 42f, 16f, l.MedLX - inset - 16f);
            Assert.True(inset + StripMath.RowWidth(n, 42f, 16f) <= l.MedLX - 16f + 0.01f);
        }

        [Fact]
        public void ResourceRowSplitsAmongPresentEntries()
        {
            var x = new float[3];
            var w = new float[3];
            // early game: stamina alone, full width
            Assert.Equal(1f, StripMath.RowSplit(700f, 18f, new[] { 1f, 0f, 0f }, 3, x, w));
            Assert.Equal(700f, w[0], 3);
            Assert.Equal(0f, w[1]);
            // eitr eaten: halves with one gap
            StripMath.RowSplit(700f, 18f, new[] { 1f, 1f, 0f }, 3, x, w);
            Assert.Equal((700f - 18f) / 2f, w[0], 3);
            Assert.Equal(w[0], w[1], 3);
            Assert.Equal(w[0] + 18f, x[1], 3);
            // three entries
            StripMath.RowSplit(700f, 18f, new[] { 1f, 1f, 1f }, 3, x, w);
            Assert.Equal((700f - 36f) / 3f, w[2], 3);
            Assert.Equal(700f, x[2] + w[2], 3);
            // sliding in: stamina shrinks monotonically while eitr grows
            var prev = 701f;
            for (var t = 0f; t <= 1f; t += 0.1f)
            {
                StripMath.RowSplit(700f, 18f, new[] { 1f, t, 0f }, 3, x, w);
                Assert.True(w[0] <= prev + 0.001f);
                Assert.True(x[1] + w[1] <= 700f + 0.01f);
                prev = w[0];
            }
            // nothing present: empty row
            Assert.Equal(0f, StripMath.RowSplit(700f, 18f, new[] { 0f, 0f, 0f }, 3, x, w));
        }

        [Fact]
        public void ShieldRunsFromTheLeftCap()
        {
            Assert.Equal(0f, StripMath.ShieldRun(0f, 150f, out var l0, out var r0));
            Assert.Equal(0f, l0);
            Assert.Equal(0.5f, StripMath.ShieldRun(75f, 150f, out var l1, out var r1), 3);
            Assert.Equal(1f, l1);
            Assert.Equal(0f, r1);
            Assert.Equal(1f, StripMath.ShieldRun(300f, 150f, out _, out var r2));
            Assert.Equal(1f, r2);
            Assert.Equal(0f, r0);
        }

        [Fact]
        public void GaugeFadesInOnChangeAndOutAfterIdle()
        {
            var g = new GaugeState();
            Assert.True(g.Step(0.3f, false, 0f, 0.1f, 1.5f, 0.25f));
            Assert.True(g.Alpha > 0f);
            for (var t = 0.1f; t < 1f; t += 0.1f) g.Step(0.3f, false, t, 0.1f, 1.5f, 0.25f);
            Assert.Equal(1f, g.Alpha);
            // idle past the delay: hides and fades out
            Assert.False(g.Step(0.3f, false, 2f, 0.1f, 1.5f, 0.25f));
            for (var t = 2.1f; t < 3f; t += 0.1f) g.Step(0.3f, false, t, 0.1f, 1.5f, 0.25f);
            Assert.Equal(0f, g.Alpha);
            // held (full rage / staggered) stays regardless of idle
            Assert.True(g.Step(1f, true, 10f, 0.1f, 1.5f, 0.25f));
            Assert.True(g.Step(1f, true, 20f, 0.1f, 1.5f, 0.25f));
            // zero never shows
            var z = new GaugeState();
            Assert.False(z.Step(0f, false, 0f, 0.1f, 1.5f, 0.25f));
        }
    }
}
