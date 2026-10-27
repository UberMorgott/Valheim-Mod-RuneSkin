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
        public void ManyRequirementsGrowRowsThenShrink()
        {
            // Full-size rows while the detail has height above the icon + description minimum (3 here) ...
            var o = CraftingMath.Compute(Input(10), new CraftingTokens());
            Assert.Equal(3, o.RequirementRows);
            Assert.Equal(68f, o.RequirementCells[0].W, 2);
            Assert.True(o.Icon.H >= 112f - 0.01f && o.Description.H >= 48f - 0.01f);
            // ... a short window has no such room: the cells shrink to fit two rows (floor ReqMinW)
            var shortWin = CraftingMath.Compute(Input(10, h: 640f), new CraftingTokens());
            Assert.Equal(2, shortWin.RequirementRows);
            Assert.True(shortWin.RequirementCells[0].W < 68f && shortWin.RequirementCells[0].W >= 52f);
            // Past the floor the rows grow instead (every requirement stays visible, no paging).
            var many = CraftingMath.Compute(Input(14), new CraftingTokens());
            Assert.Equal(14, many.RequirementCells.Length);
            Assert.True(many.RequirementRows >= 3);
            AllInside(many, Input(14).Panel);
        }

        [Fact]
        public void SubCategoryRowSitsUnderTheCategoriesAndTheBodyGivesWay()
        {
            var without = CraftingMath.Compute(Input(4), new CraftingTokens());
            Assert.True(without.SubCategoryRow.IsEmpty);
            Assert.Empty(without.SubCategories.Cells);

            var i = Input(4);
            i.SubCategories = 8; // All + swords, axes, clubs, spears, knives, polearms, shields
            var o = CraftingMath.Compute(i, new CraftingTokens());
            AllInside(o, i.Panel);
            Assert.False(o.SubCategoryRow.IsEmpty);
            Assert.True(o.SubCategoryRow.Inside(i.Panel));
            Assert.True(o.SubCategoryRow.Y >= o.CategoryRow.Bottom);
            Assert.True(o.Search.Y >= o.SubCategoryRow.Bottom);
            Assert.Equal(44f, o.SubCategoryRow.H); // same as the category cells (approved mockup)
            Assert.Equal(8, o.SubCategories.Visible);
            Assert.False(o.SubCategories.Scroll);
            // Left-aligned like the categories, same square cells and pitch.
            Assert.Equal(o.CategoryRow.X, o.SubCategories.Cells[0].X);
            Assert.Equal(o.Categories.Cells[0].W, o.SubCategories.Cells[0].W);
            Assert.Equal(o.Categories.Cells[1].X, o.SubCategories.Cells[1].X);
            for (var a = 0; a < 8; a++)
                for (var b = a + 1; b < 8; b++)
                    Assert.False(o.SubCategories.Cells[a].Overlaps(o.SubCategories.Cells[b]));
            // The row's height comes off the body only.
            Assert.Equal(without.List.H - (44f + 8f), o.List.H, 2);
            Assert.Equal(without.Header, o.Header);
        }

        [Fact]
        public void SubCategoryRowNeedsTheCategoryRow()
        {
            var i = Input(4, cats: 0);
            i.SubCategories = 3;
            var o = CraftingMath.Compute(i, new CraftingTokens());
            Assert.True(o.SubCategoryRow.IsEmpty);
        }

        [Fact]
        public void SubCategoryRowScrollsWhenItDoesNotFit()
        {
            var i = Input(4);
            i.SubCategories = 30;
            var o = CraftingMath.Compute(i, new CraftingTokens());
            Assert.True(o.SubCategories.Scroll);
            foreach (var c in o.SubCategories.Cells)
                if (!c.IsEmpty) Assert.True(c.Inside(o.SubCategoryRow));
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

        [Theory]
        [InlineData(316f, 64f)]
        [InlineData(244f, 64f)]
        [InlineData(180f, 48f)]
        public void ActionRowPartsInsideCraftRowNoOverlap(float w, float h)
        {
            var craft = new Box(1290f, 780f, w, h);
            var r = CraftingMath.ActionRow(craft, true, 8f);
            foreach (var b in new[] { r.Minus, r.Field, r.Plus, r.Button, r.Bar, r.Cancel })
            {
                Assert.False(b.IsEmpty);
                Assert.True(b.Inside(craft), $"{b} outside {craft}");
            }
            Assert.False(r.Minus.Overlaps(r.Field));
            Assert.False(r.Field.Overlaps(r.Plus));
            Assert.False(r.Plus.Overlaps(r.Button));
            Assert.False(r.Bar.Overlaps(r.Cancel));
            Assert.True(r.Plus.Right - craft.X <= w * 0.4f + 0.01f, "stepper wider than its share");
            Assert.True(r.Button.W >= w * 0.55f);
            Assert.True(r.Bar.W > r.Cancel.W);
        }

        [Theory]
        [InlineData(316f, 64f)]
        [InlineData(180f, 48f)]
        public void ActionRowStepGapFollowsInnerShare(float w, float h)
        {
            var craft = new Box(0f, 0f, w, h);
            var r = CraftingMath.ActionRow(craft, true, 8f, innerShare: 0.8f);
            var tight = CraftingMath.ActionRow(craft, true, 8f);
            Assert.True(r.Field.X - r.Minus.Right > tight.Field.X - tight.Minus.Right);
            Assert.True(r.Plus.Right - craft.X <= w * 0.4f + 0.01f, "stepper wider than its share");
            Assert.False(r.Plus.Overlaps(r.Button));
        }

        // Anvil box from the button alone (mockup idle: pad 0.18 h, side 0.58 h) and the label span after it never
        // reaches back over it, whatever the label width.
        [Theory]
        [InlineData(178f, 62f)]
        [InlineData(140f, 40f)]
        [InlineData(320f, 64f)]
        public void LeadGlyphFixedByButtonAndLabelStartsAfterIt(float w, float h)
        {
            var b = new Box(500f, 800f, w, h);
            var g = CraftingMath.LeadGlyph(b, 0.18f, 0.58f);
            Assert.Equal(b.X + h * 0.18f, g.X, 3);
            Assert.Equal(h * 0.58f, g.W, 3);
            Assert.Equal(b.Y + (h - g.H) / 2f, g.Y, 3);
            Assert.True(g.Inside(b));
            var start = RuneSkin.Restyle.LabelFit.LeadStart(g.Right, 0.16f * h, b.X);
            Assert.True(start >= g.Right + 0.16f * h - 0.001f);
        }

        [Fact]
        public void ActionRowWithoutStepperButtonTakesTheRow()
        {
            var craft = new Box(10f, 20f, 316f, 64f);
            var r = CraftingMath.ActionRow(craft, false, 8f);
            Assert.Equal(craft.X, r.Button.X);
            Assert.Equal(craft.W, r.Button.W);
            Assert.True(r.Minus.IsEmpty && r.Plus.IsEmpty && r.Field.IsEmpty);
        }

        [Theory]
        [InlineData("7", 1, 20, 7)]
        [InlineData(" 12 ", 1, 20, 12)]
        [InlineData("250", 1, 20, 20)]
        [InlineData("0", 3, 20, 1)]
        [InlineData("", 3, 20, 3)]
        [InlineData("x5", 4, 20, 4)]
        [InlineData("-3", 4, 20, 4)]
        [InlineData("9", 1, 0, 1)]
        public void ParseAmountClampsToOneAndMax(string text, int fallback, int max, int want) =>
            Assert.Equal(want, CraftingMath.ParseAmount(text, fallback, max));

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(17, 17)]
        [InlineData(99, 99)]
        [InlineData(150, 99)]
        public void LargestOkFindsTheLimit(int limit, int want) =>
            Assert.Equal(want, CraftingMath.LargestOk(99, n => n <= limit));

        // Live 0.25.11 at 1280x720: header 40 x 0.8 = 32-unit slot, root canvas scale ~0.56 -> star ~18 px, digit box
        // ~8 px: no line fitted and the star showed no level. The star grows until its digit meets the readable floor.
        [Theory]
        [InlineData(32f, 0.5625f)]
        [InlineData(32f, 1f)]
        [InlineData(64f, 1f)]
        public void LevelDigitNeverBelowReadableFloor(float slotH, float scale)
        {
            var floor = 14f / scale;
            var slot = new Box(500f, 40f, CraftingMath.LevelSide(slotH, floor), slotH);
            var star = CraftingMath.LevelBadge(slot, floor, out var size);
            Assert.True(size >= floor - 0.001f, $"digit {size} under floor {floor}");
            Assert.True(star.H >= slotH - 0.001f);
            Assert.Equal(star.W, star.H, 3);
            Assert.Equal(slot.X + slot.W / 2f, star.X + star.W / 2f, 3);
            Assert.Equal(slot.Y + slot.H / 2f, star.Y + star.H / 2f, 3);
            Assert.Equal(slot.W, star.W, 3); // the reserved aux width is exactly the star
        }

        [Fact]
        public void LevelStarKeepsSlotSizeWhenDigitAlreadyReadable()
        {
            var star = CraftingMath.LevelBadge(new Box(0f, 0f, 64f, 64f), 14f, out var size);
            Assert.Equal(64f, star.W, 3);
            Assert.Equal(64f * CraftingMath.LevelFontShare, size, 3);
        }
    }
}
