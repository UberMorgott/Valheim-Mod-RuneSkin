using RuneSkin.CraftingScreen;
using RuneSkin.InventoryScreen;
using Xunit;

namespace RuneSkin.Tests
{
    // CraftingMath: the M3 crafting column (mockup-1 / layout A right column) inside the M2 crafting box.
    public class CraftingLayoutTests
    {
        private static CraftingInput Input(int reqs, int tabs = 3, int cats = 9, float w = 576f, float h = 818f) => new CraftingInput
        {
            Panel = new Box(1312f, 32f, w, h),
            Tabs = tabs,
            Categories = cats,
            Requirements = reqs,
            Search = true,
            Repair = true,
        };

        private static void AllInside(CraftingLayout o, Box panel)
        {
            foreach (var b in new[] { o.Header, o.TabRow, o.CategoryRow, o.Search, o.List, o.Detail, o.ButtonRow, o.Craft, o.Repair, o.Requirements, o.Description, o.Icon })
                if (!b.IsEmpty) Assert.True(b.Inside(panel), $"{b} outside {panel}");
            foreach (var c in o.RequirementCells) Assert.True(c.Inside(o.Detail), $"req {c} outside detail {o.Detail}");
        }

        [Fact]
        public void FourRequirementsOneRowEverythingInside()
        {
            var i = Input(4);
            var o = CraftingMath.Compute(i, new CraftingTokens());
            Assert.Equal(1, o.RequirementRows);
            Assert.Equal(4, o.RequirementColumns);
            AllInside(o, i.Panel);
            Assert.False(o.List.Overlaps(o.Detail));
            Assert.False(o.Detail.Overlaps(o.ButtonRow));
            Assert.True(o.Description.H >= 48f);
            Assert.True(o.Requirements.Y >= o.Description.Bottom);
        }

        [Fact]
        public void MoreThanFourRequirementsWrapInsteadOfPaging()
        {
            var i = Input(7);
            var o = CraftingMath.Compute(i, new CraftingTokens());
            Assert.Equal(7, o.RequirementCells.Length);
            Assert.Equal(2, o.RequirementRows);
            AllInside(o, i.Panel);
            for (var a = 0; a < 7; a++)
                for (var b = a + 1; b < 7; b++)
                    Assert.False(o.RequirementCells[a].Overlaps(o.RequirementCells[b]));
        }

        [Fact]
        public void ManyRequirementsShrinkToTwoRows()
        {
            var o = CraftingMath.Compute(Input(10), new CraftingTokens());
            Assert.Equal(2, o.RequirementRows);
            Assert.True(o.RequirementCells[0].W < 68f && o.RequirementCells[0].W >= 52f);
            // Past the floor the rows grow instead (every requirement stays visible, no paging).
            var many = CraftingMath.Compute(Input(14), new CraftingTokens());
            Assert.Equal(14, many.RequirementCells.Length);
            Assert.True(many.RequirementRows >= 3);
            AllInside(many, Input(14).Panel);
        }

        [Fact]
        public void TabsScrollWhenTheyDoNotFit()
        {
            var o = CraftingMath.Compute(Input(4, tabs: 9), new CraftingTokens());
            Assert.True(o.Tabs.Scroll);
            Assert.True(o.Tabs.Visible < 9);
            Assert.False(o.Tabs.Prev.IsEmpty);
            var shown = 0;
            foreach (var c in o.Tabs.Cells) if (!c.IsEmpty) { shown++; Assert.True(c.Inside(o.TabRow)); Assert.False(c.Overlaps(o.Tabs.Prev)); Assert.False(c.Overlaps(o.Tabs.Next)); }
            Assert.Equal(o.Tabs.Visible, shown);
        }

        [Fact]
        public void ThreeTabsCentredNoScroll()
        {
            var o = CraftingMath.Compute(Input(4), new CraftingTokens());
            Assert.False(o.Tabs.Scroll);
            Assert.Equal(3, o.Tabs.Visible);
            var left = o.Tabs.Cells[0].X - o.TabRow.X;
            var right = o.TabRow.Right - o.Tabs.Cells[2].Right;
            Assert.InRange(left - right, -0.5f, 0.5f);
        }

        [Fact]
        public void CategoryOffsetClampsAndNoCategoriesNoRow()
        {
            var i = Input(4, cats: 30);
            i.CategoryOffset = 100;
            var o = CraftingMath.Compute(i, new CraftingTokens());
            Assert.True(o.Categories.Scroll);
            Assert.Equal(30 - o.Categories.Visible, o.Categories.First);
            var none = CraftingMath.Compute(Input(4, cats: 0), new CraftingTokens());
            Assert.True(none.CategoryRow.IsEmpty);
            Assert.True(none.List.H > o.List.H);
        }

        [Fact]
        public void ZeroRequirementsNoGridAndSmallPanelStaysInside()
        {
            var i = Input(0, w: 456f, h: 600f);
            var o = CraftingMath.Compute(i, new CraftingTokens());
            Assert.Equal(0, o.RequirementRows);
            Assert.Empty(o.RequirementCells);
            AllInside(o, i.Panel);
        }
    }
}
