using RuneSkin.PreWorld;
using RuneSkin.Restyle;
using Xunit;

namespace RuneSkin.Tests
{
    // 0.20.3 track F: new-world inputs, hint row, tab row / separator bands. Numbers from the vanilla prefab
    // (SoftRef bundle 10ba9da1: newWorld/panel, StartGame/Panel) in panel units, y up.
    public class MenuRowTests
    {
        // An input text never takes the shrink-to-fit rule (that rule and the readable floor alternated the line between
        // shown and truncated away); every other truncating text still may.
        [Fact]
        public void InputTextsAreNeverShrunk()
        {
            Assert.False(LayoutMath.MayShrinkToFit(true));
            Assert.True(LayoutMath.MayShrinkToFit(false));
        }

        // Readable floor at GUI scale 0.77: 20.8-unit text, line 28.3 in the 25-unit vanilla text area of a 35-unit box.
        // The viewport grows 1.65 per side; asked again after that it is 0 (one write, then idle).
        [Fact]
        public void FieldViewportGrowsOnceToHoldTheLine()
        {
            var g = LayoutMath.FieldViewportGrow(25f, 35f, 28.3f);
            Assert.Equal(1.65f, g, 3);
            Assert.Equal(0f, LayoutMath.FieldViewportGrow(25f + 2f * g, 35f, 28.3f), 3);
            Assert.Equal(0f, LayoutMath.FieldViewportGrow(25f, 35f, 24.5f), 3); // fits: no write
        }

        [Fact]
        public void FieldViewportNeverGrowsPastItsBox()
        {
            Assert.Equal(5f, LayoutMath.FieldViewportGrow(25f, 35f, 60f), 3);
            Assert.Equal(0f, LayoutMath.FieldViewportGrow(35f, 35f, 60f), 3);
        }

        // The two writers of the old loop, replayed: the readable floor raises the size, the field never shrinks it and
        // grows its viewport. After the first pass every further pass writes nothing.
        [Fact]
        public void FloorAndFieldConvergeInOnePass()
        {
            float size = 18f, view = 25f;
            const float floor = 20.8f, box = 35f, lineFactor = 1.362f;
            float lastSize = -1f, lastView = -1f;
            for (var pass = 0; pass < 6; pass++)
            {
                if (size < floor) size = floor; // ReadableSweep
                if (LayoutMath.MayShrinkToFit(true)) size = 12f; // never for an input field
                view += 2f * LayoutMath.FieldViewportGrow(view, box, size * lineFactor);
                if (pass > 0)
                {
                    Assert.Equal(lastSize, size, 4);
                    Assert.Equal(lastView, view, 4);
                }
                lastSize = size;
                lastView = view;
            }
            Assert.True(view >= size * lineFactor - 0.01f);
        }

        // Hint 1.4 units into the name field (bottom 45.5) and 5 over the seed row (top 22.5): own row 8 under the field,
        // seed row 8 under the hint.
        [Fact]
        public void HintGetsItsOwnRow()
        {
            var (top, shift) = PreWorldMath.RowBelow(45.5f, 20.7f, 8f, 22.5f);
            Assert.Equal(37.5f, top, 3);
            Assert.Equal(13.7f, shift, 3);
            var (top2, shift2) = PreWorldMath.RowBelow(45.5f, 20.7f, 8f, 22.5f - shift);
            Assert.Equal(top, top2, 3);
            Assert.Equal(0f, shift2, 3);
        }

        [Fact]
        public void HintRowWithRoomMovesNothing()
        {
            var (_, shift) = PreWorldMath.RowBelow(45.5f, 20f, 8f, -10f);
            Assert.Equal(0f, shift, 3);
        }

        // Start game: 44-unit tabs centred at 300.9 (bottom 278.9) over the separator 282..286 over the title (visual
        // top 277): separator 15.1 down, title 18.1 down; laid out again, nothing moves.
        [Fact]
        public void HeaderBandSeparatesTabsSeparatorAndTitle()
        {
            var (d, b) = PreWorldMath.HeaderBand(278.9f, 286f, 4f, 277f, 8f);
            Assert.Equal(15.1f, d, 3);
            Assert.Equal(18.1f, b, 3);
            var (d2, b2) = PreWorldMath.HeaderBand(278.9f, 286f - d, 4f, 277f - b, 8f);
            Assert.Equal(0f, d2, 3);
            Assert.Equal(0f, b2, 3);
        }

        // Server filter tabs (bottom 247.5) over JoinPanel's separator 246..250 over the list (top 246).
        [Fact]
        public void HeaderBandServerListGivesUpItsTop()
        {
            var (d, b) = PreWorldMath.HeaderBand(247.5f, 250f, 4f, 246f, 8f);
            Assert.Equal(10.5f, d, 3);
            Assert.Equal(18.5f, b, 3);
        }
    }
}
