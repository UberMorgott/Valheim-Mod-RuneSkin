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

        [Fact]
        public void CompactStripIsAtMostTwelvePercent()
        {
            var natural = StripMath.Compute(In()).Height;
            var k = StripMath.HeightScale(130f, natural);
            Assert.True(natural * k <= 0.12f * 1080f + 0.5f);
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
