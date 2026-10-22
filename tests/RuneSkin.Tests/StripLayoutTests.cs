using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    // HUD strip geometry (Track H, mockup hud-2-final): centred centre block, hotbar + ability zone centred in it,
    // wings filling the rest, live counts 0..N, cells that shrink to fit, config width / height.
    public class StripLayoutTests
    {
        // default-theme.json strip values at a 1920-unit strip.
        private static StripMath.Input Theme(float width = 1843f, int abilities = 2, int food = 3, int potions = 4) => new StripMath.Input
        {
            Width = width,
            Cell = 76f,
            CellH = 76f,
            Gap = 9f,
            ZoneGap = 26f,
            Pad = 12f,
            DurH = 10f,
            BarsH = 24f + 5f + 24f,
            MinBarsW = 600f,
            KnotW = 58f,
            WingOverlap = 24f,
            FoodCell = 72f,
            PotionCell = 68f,
            PotionH = 68f,
            TimerH = 18f,
            MinCell = 40f,
            Hotbar = 8,
            Abilities = abilities,
            Food = food,
            Potions = potions
        };

        [Fact]
        public void ZoneWidthCountsGapsBetweenCellsOnly()
        {
            Assert.Equal(0f, StripMath.Zone(0, 76f, 9f));
            Assert.Equal(76f, StripMath.Zone(1, 76f, 9f));
            Assert.Equal(8 * 76f + 7 * 9f, StripMath.Zone(8, 76f, 9f));
        }

        [Fact]
        public void CentreBlockIsCentredAndHoldsHotbarPlusAbilities()
        {
            var l = StripMath.Compute(Theme());
            var i = Theme();
            Assert.Equal(i.Width / 2f, l.CenterX + l.CenterW / 2f, 2);
            var top = l.HotbarW + i.ZoneGap + l.AbilityW;
            // the top row (hotbar + zone gap + abilities) is centred in the block's inner span
            Assert.Equal(l.CenterX + l.CenterW / 2f, l.HotbarX + top / 2f, 2);
            Assert.Equal(l.HotbarX + l.HotbarW + i.ZoneGap, l.AbilityX, 2);
            Assert.True(l.AbilityX + l.AbilityW <= l.CenterX + l.CenterW - i.Pad + 0.01f);
            Assert.Equal(l.BarsW, l.CenterW - 2f * i.Pad, 2);
            Assert.True(l.Fits);
        }

        [Fact]
        public void NoAbilitiesCollapsesTheZoneAndRecentresTheHotbar()
        {
            var l = StripMath.Compute(Theme(abilities: 0));
            Assert.Equal(0f, l.AbilityW);
            Assert.Equal(l.CenterX + l.CenterW / 2f, l.HotbarX + l.HotbarW / 2f, 2);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(4)]
        public void AbilityCountChangesTheCentreWidthOnlyPastTheBarMinimum(int n)
        {
            var l = StripMath.Compute(Theme(abilities: n));
            var i = Theme(abilities: n);
            var top = l.HotbarW + (n > 0 ? i.ZoneGap + StripMath.Zone(n, i.Cell, i.Gap) : 0f);
            Assert.Equal(System.Math.Max(top, i.MinBarsW) + 2f * i.Pad, l.CenterW, 2);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(3, 3)]
        [InlineData(4, 5)]
        [InlineData(5, 6)]
        public void WingCellsAreCentredInTheirWingAndInsideIt(int food, int potions)
        {
            var i = Theme(food: food, potions: potions);
            var l = StripMath.Compute(i);
            if (food > 0)
            {
                var w = (food - 1) * l.FoodStep + l.FoodCell;
                Assert.Equal(l.FoodCenterX, l.FoodX + w / 2f, 2);
                Assert.True(l.FoodX >= i.KnotW * StripMath.KnotReserve + i.Pad - 0.01f, $"food starts at {l.FoodX}");
                Assert.True(l.FoodX + w <= l.CenterX - i.Pad + 0.01f, $"food ends at {l.FoodX + w} centre {l.CenterX}");
            }
            if (potions > 0)
            {
                var w = (potions - 1) * l.PotionStep + l.PotionCell;
                Assert.Equal(l.PotionCenterX, l.PotionX + w / 2f, 2);
                Assert.True(l.PotionX >= l.CenterX + l.CenterW + i.Pad - 0.01f);
                Assert.True(l.PotionX + w <= i.Width - i.KnotW * StripMath.KnotReserve - i.Pad + 0.01f);
            }
        }

        [Fact]
        public void NarrowStripShrinksWingCellsInsteadOfOverflowing()
        {
            var i = Theme(width: 1300f, food: 4, potions: 6);
            var l = StripMath.Compute(i);
            Assert.True(l.PotionCell < i.PotionCell);
            Assert.True(l.PotionCell >= i.MinCell - 0.01f);
            Assert.Equal(l.PotionCenterX, l.PotionX + (5 * l.PotionStep + l.PotionCell) / 2f, 2); // centred even when tight
        }

        [Fact]
        public void WingsSitOnTheStripBottomContentsOnTheBarsLine()
        {
            var i = Theme();
            var l = StripMath.Compute(i);
            Assert.Equal(l.CenterX + i.WingOverlap, l.LeftW, 2);
            Assert.Equal(i.Width, l.RightX + l.RightW, 2);
            Assert.Equal(0f, l.WingY);
            Assert.True(l.WingH <= l.Height + 0.01f);
            Assert.Equal(l.BarsY, l.PotionY, 2);                // potion cells end on the bars' bottom line
            Assert.Equal(l.BarsY + i.TimerH, l.FoodY, 2);        // food bowls over their timer line
        }

        [Fact]
        public void VerticalStackBarsDurabilityCellsNoCaptions()
        {
            var i = Theme();
            var l = StripMath.Compute(i);
            Assert.Equal(i.Pad, l.BarsY, 2);
            Assert.Equal(l.BarsY + i.BarsH + i.DurH, l.CellsY, 2);
            Assert.Equal(l.CellsY + i.CellH + i.Pad, l.Height, 2);
        }

        [Fact]
        public void CompactStripFitsTwelvePercentOf1080()
        {
            // default theme: natural height, scaled to the 126 ref px default (Hud.StripHeight) = 11.7 % of 1080.
            var i = Theme();
            var l = StripMath.Compute(i);
            var k = StripMath.HeightScale(126f, l.Height);
            Assert.True(l.Height * k <= 0.12f * 1080f, $"{l.Height * k}");
        }

        [Fact]
        public void StatusRowWidthIsPerRowCells()
        {
            Assert.Equal(10 * 62f + 9 * 4f, StripMath.RowWidth(10, 62f, 4f), 2);
            Assert.Equal(62f, StripMath.RowWidth(0, 62f, 4f), 2);
        }
        // Wing slots: equal gaps incl. the margins for 1..max, fixed wing; too many -> min gaps, then smaller slots.
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void WingSlotsSpreadEvenlyWithEqualMargins(int n)
        {
            StripMath.WingSlots(n, 62f, 400f, 6f, 40f, out var cell, out var gap);
            Assert.Equal(62f, cell, 3);
            Assert.Equal(400f, (n + 1) * gap + n * cell, 2); // margins == gaps, the whole span used
        }

        [Fact]
        public void WingSlotsShrinkGapsThenSlotsToTheMinimum()
        {
            StripMath.WingSlots(5, 80f, 400f, 6f, 40f, out var cell, out var gap);
            Assert.Equal(6f, gap, 3);
            Assert.Equal((400f - 6f * 6f) / 5f, cell, 2);
            StripMath.WingSlots(5, 80f, 100f, 6f, 40f, out cell, out gap);
            Assert.Equal(40f, cell, 3); // floor: overflows centred rather than shrinking further
            StripMath.WingSlots(0, 80f, 400f, 6f, 40f, out cell, out gap);
            Assert.Equal(0f, gap);
        }
        [Fact]
        public void ConfigWidthAndHeight()
        {
            Assert.Equal(1843.2f, StripMath.WidthPx(1920f, 0.96f), 2);
            Assert.Equal(1920f, StripMath.WidthPx(1920f, 1.5f), 2);
            Assert.Equal(576f, StripMath.WidthPx(1920f, 0.1f), 2);
            Assert.Equal(1f, StripMath.HeightScale(290f, 290f), 3);
            Assert.Equal(1.5f, StripMath.HeightScale(435f, 290f), 3);
            Assert.Equal(1f, StripMath.HeightScale(0f, 290f), 3);
        }

        [Fact]
        public void CellXStepsByCellPlusGap()
        {
            Assert.Equal(100f, StripMath.CellX(100f, 0, 76f, 9f));
            Assert.Equal(100f + 3 * 85f, StripMath.CellX(100f, 3, 76f, 9f));
            Assert.Equal(40f, StripMath.FitCell(5, 76f, 10f, 240f), 3);
            Assert.Equal(76f, StripMath.FitCell(2, 76f, 10f, 1000f), 3);
        }
    }
}
