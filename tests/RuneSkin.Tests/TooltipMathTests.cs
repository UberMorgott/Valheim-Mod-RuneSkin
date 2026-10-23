using RuneSkin.Screens;
using Xunit;

namespace RuneSkin.Tests
{
    // Tooltip placement above the HUD strip (TooltipPlacer): bounds, side choice, flip, cap, scroll + indicator math.
    public class TooltipMathTests
    {
        // 1920x1080 safe area, strip top at 200 px, margin 8 -> usable 8..1912 x 208..1072.
        private static readonly TipRect Bounds = TooltipMath.Bounds(new TipRect(0f, 0f, 1920f, 1080f), 200f, 8f);

        private static TipRect Cell(float x, float y) => TipRect.Sized(x, y, 64f, 64f);

        [Fact]
        public void BoundsFloorIsTheStripTop()
        {
            Assert.Equal(208f, Bounds.YMin, 3);
            Assert.Equal(1072f, Bounds.YMax, 3);
            Assert.Equal(8f, Bounds.XMin, 3);
            var noStrip = TooltipMath.Bounds(new TipRect(0f, 0f, 1920f, 1080f), 0f, 8f);
            Assert.Equal(8f, noStrip.YMin, 3);
        }

        [Fact]
        public void HangsDownRightOfTheCellWhenItFits()
        {
            var cell = Cell(500f, 700f);
            var r = TooltipMath.Place(cell, 300f, 200f, Bounds, 10f);
            Assert.Equal(574f, r.XMin, 3);
            Assert.Equal(764f, r.YMax, 3); // top aligned with the cell top
            Assert.False(r.Overlaps(cell));
        }

        [Fact]
        public void GoesLeftWhenTheRightSideIsFull()
        {
            var cell = Cell(1800f, 700f);
            var r = TooltipMath.Place(cell, 300f, 200f, Bounds, 10f);
            Assert.Equal(1800f - 10f - 300f, r.XMin, 3);
            Assert.False(r.Overlaps(cell));
        }

        [Fact]
        public void BottomRowFlipsUpAndNeverCrossesTheStrip()
        {
            var cell = Cell(500f, 220f); // bottom inventory row just above the strip
            var r = TooltipMath.Place(cell, 300f, 400f, Bounds, 10f);
            Assert.True(r.YMin >= Bounds.YMin - 0.001f);
            Assert.Equal(220f, r.YMin, 3); // grows upward from the cell bottom
            Assert.False(r.Overlaps(cell));
        }

        [Fact]
        public void CellInsideTheStripStillPutsTheTooltipAboveIt()
        {
            var cell = Cell(900f, 60f); // strip hotbar cell (below the floor)
            var r = TooltipMath.Place(cell, 300f, 400f, Bounds, 10f);
            Assert.Equal(Bounds.YMin, r.YMin, 3);
        }

        [Fact]
        public void TopRowIsClampedUnderTheCeiling()
        {
            var cell = Cell(500f, 1040f);
            var r = TooltipMath.Place(cell, 300f, 200f, Bounds, 10f);
            Assert.True(r.YMax <= Bounds.YMax + 0.001f);
            Assert.True(r.YMin >= Bounds.YMin - 0.001f);
        }

        [Fact]
        public void CappedTooltipFillsTheBoundsExactly()
        {
            var h = TooltipMath.MaxHeight(Bounds);
            Assert.Equal(864f, h, 3);
            var r = TooltipMath.Place(Cell(500f, 500f), 300f, h, Bounds, 10f);
            Assert.Equal(Bounds.YMin, r.YMin, 3);
            Assert.Equal(Bounds.YMax, r.YMax, 3);
        }

        [Fact]
        public void TooWideForEitherSideIsClampedInside()
        {
            var cell = Cell(900f, 500f);
            var r = TooltipMath.Place(cell, 1500f, 200f, Bounds, 10f);
            Assert.True(r.XMin >= Bounds.XMin - 0.001f);
            Assert.True(r.XMax <= Bounds.XMax + 0.001f);
        }

        [Fact]
        public void SweepNeverCoversTheHoveredCell()
        {
            // A column of cells bottom -> top: every placement keeps the hovered cell free (its pointer events stay).
            for (var y = 210f; y < 1000f; y += 70f)
            {
                var cell = Cell(700f, y);
                var r = TooltipMath.Place(cell, 320f, 500f, Bounds, 10f);
                Assert.False(r.Overlaps(cell), $"cell at y {y} covered");
                Assert.True(r.YMin >= Bounds.YMin - 0.001f, $"cell at y {y} under the strip");
            }
        }

        [Fact]
        public void OverflowNeedsMoreThanTolerance()
        {
            Assert.False(TooltipMath.Overflows(400.3f, 400f));
            Assert.True(TooltipMath.Overflows(401f, 400f));
        }

        [Fact]
        public void WheelStepsAndClamps()
        {
            // content 1000, clip 400 -> range 600; step 60 px = 0.1 normalized; wheel down (< 0) moves toward 0.
            Assert.Equal(0.9f, TooltipMath.Scrolled(1f, -1f, 60f, 1000f, 400f), 4);
            Assert.Equal(1f, TooltipMath.Scrolled(0.95f, 0.1f, 60f, 1000f, 400f), 4);
            Assert.Equal(0f, TooltipMath.Scrolled(0.05f, -3f, 60f, 1000f, 400f), 4);
            Assert.Equal(0.5f, TooltipMath.Scrolled(0.5f, -1f, 60f, 300f, 400f), 4); // no overflow: unchanged
        }

        [Fact]
        public void HandleSizeAndPosition()
        {
            TooltipMath.Handle(400f, 1000f, 1f, 380f, 12f, out var h, out var y);
            Assert.Equal(152f, h, 3);
            Assert.Equal(228f, y, 3); // top
            TooltipMath.Handle(400f, 1000f, 0f, 380f, 12f, out _, out y);
            Assert.Equal(0f, y, 3); // bottom
            TooltipMath.Handle(10f, 100000f, 0.5f, 380f, 12f, out h, out _);
            Assert.Equal(12f, h, 3); // min size
        }

        [Fact]
        public void FadesShowWhereContentIsHidden()
        {
            TooltipMath.Fades(true, 1f, out var top, out var bottom);
            Assert.False(top);
            Assert.True(bottom);
            TooltipMath.Fades(true, 0f, out top, out bottom);
            Assert.True(top);
            Assert.False(bottom);
            TooltipMath.Fades(false, 0.5f, out top, out bottom);
            Assert.False(top || bottom);
        }
    }
}
