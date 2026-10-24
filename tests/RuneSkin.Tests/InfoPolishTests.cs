using System;
using RuneSkin.CraftingScreen;
using RuneSkin.InfoScreens;
using RuneSkin.InventoryScreen;
using Xunit;

namespace RuneSkin.Tests
{
    // M3 polish: info tab row, skill sort order, VNEI grid fit, RecipeChains height cap, repair-free button row.
    public class InfoPolishTests
    {
        [Theory]
        [InlineData(false, false, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, true, false)]
        public void HeaderTabsOnlyWithoutDockedStrip(bool skills, bool trophies, bool shown)
        {
            // One tab strip on screen (RuneSkin 0.20.1 defect): skills / trophies carry the InfoTabs mirror.
            Assert.Equal(shown, InfoMath.HeaderTabsShown(skills, trophies));
        }

        [Fact]
        public void TabRowCentredAndInside()
        {
            var row = new Box(100f, 50f, 700f, 70f);
            var cells = InfoMath.TabRow(row, 5, 1.3f, 10f);
            Assert.Equal(5, cells.Length);
            Assert.All(cells, c => Assert.True(c.Inside(row), $"{c} outside {row}"));
            Assert.Equal(91f, cells[0].W, 3);
            var left = cells[0].X - row.X;
            var right = row.Right - cells[4].Right;
            Assert.Equal(left, right, 3);
        }

        [Fact]
        public void TabRowShrinksToFitManyTabs()
        {
            var row = new Box(0f, 0f, 400f, 70f);
            var cells = InfoMath.TabRow(row, 8, 1.3f, 10f);
            Assert.All(cells, c => Assert.True(c.Inside(row)));
            Assert.True(cells[1].X >= cells[0].Right + 9.99f);
            Assert.Empty(InfoMath.TabRow(row, 0, 1.3f, 10f));
        }

        [Fact]
        public void SkillOrderByLevelThenName()
        {
            var levels = new[] { 5f, 20f, 5f, 1f };
            var names = new[] { "Swords", "Axes", "Bows", "Clubs" };
            Assert.Equal(new[] { 1, 2, 0, 3 }, InfoMath.SkillOrder(levels, names, 1, StringComparer.Ordinal));
            Assert.Equal(new[] { 1, 2, 3, 0 }, InfoMath.SkillOrder(levels, names, 2, StringComparer.Ordinal));
            Assert.Equal(new[] { 0, 1, 2, 3 }, InfoMath.SkillOrder(levels, names, 0, StringComparer.Ordinal));
        }

        [Fact]
        public void VneiGridFitsTheBody()
        {
            var (x, y) = CraftingMath.VneiCells(560f, 600f, 1f);
            Assert.Equal(11, x); // 11*50+10 = 560
            Assert.Equal(10, y); // 10*50+100 = 600
            Assert.True(x * 50f + 10f <= 560f && y * 50f + 100f <= 600f);
            var (sx, sy) = CraftingMath.VneiCells(560f, 600f, 2f);
            Assert.Equal((5, 4), (sx, sy));
            Assert.Equal((1, 1), CraftingMath.VneiCells(10f, 10f, 1f));
            Assert.Equal((1, 1), CraftingMath.VneiCells(500f, 500f, 0f));
        }

        [Fact]
        public void ChainBoxCapped()
        {
            Assert.Equal(150f, CraftingMath.ChainHeight(400f, 700f, 300f, 150f, 72f));
            Assert.Equal(100f, CraftingMath.ChainHeight(100f, 700f, 300f, 150f, 72f));
            // Little room above: the cap never goes under the minimum.
            Assert.Equal(72f, CraftingMath.ChainHeight(400f, 700f, 680f, 150f, 72f));
        }

        [Fact]
        public void CraftButtonTakesTheRowWithoutRepair()
        {
            var i = new CraftingInput { Panel = new Box(1312f, 32f, 576f, 818f), Tabs = 3, Categories = 9, Requirements = 4, Search = true, Repair = false };
            var o = CraftingMath.Compute(i, new CraftingTokens());
            Assert.True(o.Repair.IsEmpty);
            Assert.Equal(o.ButtonRow.X, o.Craft.X, 3);
            Assert.Equal(o.ButtonRow.W, o.Craft.W, 3);
        }
    }
}
