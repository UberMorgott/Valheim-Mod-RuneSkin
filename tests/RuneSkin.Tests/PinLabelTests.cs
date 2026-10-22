using RuneSkin.MapScreen;
using Xunit;

namespace RuneSkin.Tests
{
    // Map pin name labels: hung under their own pin, decluttered by priority.
    public class PinLabelTests
    {
        private static PinLabelMath.Box B(float x0, float y0, float x1, float y1, int prio, int order) =>
            new PinLabelMath.Box { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Priority = prio, Order = order };

        [Theory]
        [InlineData(100f, 40f, 2f, 0f, 10f, 100f, 28f)]   // text top 10 above the label origin
        [InlineData(-50f, -8f, 3f, 5f, -2f, -55f, -9f)]   // off-centre text rect
        public void HangPutsTextTopGapUnderPin(float pinX, float bottom, float gap, float tx, float ty, float ex, float ey)
        {
            PinLabelMath.Hang(pinX, bottom, gap, tx, ty, out var x, out var y);
            Assert.Equal(ex, x, 4);
            Assert.Equal(ey, y, 4);
            // The text top lands exactly gap below the pin bottom, centred on the pin: constant at every zoom.
            Assert.Equal(pinX, x + tx, 4);
            Assert.Equal(bottom - gap, y + ty, 4);
        }

        [Fact]
        public void DuplicateLabelHidesLowerPriority()
        {
            var boxes = new[] { B(0, 0, 50, 10, 1, 0), B(2, -3, 52, 7, 2, 1) }; // shared copy first, own pin second
            var shown = new bool[2];
            var hidden = PinLabelMath.Declutter(boxes, 2, new int[2], shown, 1f);
            Assert.Equal(1, hidden);
            Assert.True(shown[1]);
            Assert.False(shown[0]);
        }

        [Fact]
        public void SamePriorityKeepsEarlierPin()
        {
            var boxes = new[] { B(0, 0, 50, 10, 2, 0), B(10, 2, 60, 12, 2, 1), B(100, 0, 150, 10, 2, 2) };
            var shown = new bool[3];
            PinLabelMath.Declutter(boxes, 3, new int[3], shown, 1f);
            Assert.True(shown[0]);
            Assert.False(shown[1]);
            Assert.True(shown[2]); // apart: kept
        }

        [Fact]
        public void HiddenLabelClaimsNoSpace()
        {
            // 0 beats 1, 1 hidden; 2 overlaps only 1 -> stays.
            var boxes = new[] { B(0, 0, 50, 10, 2, 0), B(40, 0, 90, 10, 1, 1), B(60, 0, 110, 10, 1, 2) };
            var shown = new bool[3];
            PinLabelMath.Declutter(boxes, 3, new int[3], shown, 1f);
            Assert.Equal(new[] { true, false, true }, shown);
        }

        [Fact]
        public void WindArrowSitsInTheRecessCentre()
        {
            // Recess centre is low-left in the teardrop (tip top-right): below the middle in Unity's bottom-up space.
            Assert.Equal(0.475f, MinimapMath.WindRecessX, 3);
            Assert.Equal(0.399f, MinimapMath.WindRecessY, 3);
            Assert.True(MinimapMath.WindRecessY < 0.5f);
        }

        [Fact]
        public void HintsFitAtFullSizeWithEvenSpacing()
        {
            var n = MinimapMath.FitHints(new[] { 26f, 26f }, new[] { 100f, 100f }, 2, 400f, 6f, 16f, 0.7f, out var scale, out var spacing);
            Assert.Equal(2, n);
            Assert.Equal(1f, scale, 4);
            Assert.Equal((400f - 64f - 200f) / 3f, spacing, 3);
        }

        [Fact]
        public void HintsShrinkThenDrop()
        {
            // Shrink: 5 items need 5*132 + 6*16 = 756 > 700 at full size.
            var icons = new[] { 26f, 26f, 26f, 26f, 26f };
            var labels = new[] { 100f, 100f, 100f, 100f, 100f };
            var n = MinimapMath.FitHints(icons, labels, 5, 700f, 6f, 16f, 0.7f, out var scale, out var spacing);
            Assert.Equal(5, n);
            Assert.True(scale < 1f && scale >= 0.7f);
            Assert.Equal(16f, spacing, 3);
            // Too narrow even at minScale: the last item goes.
            n = MinimapMath.FitHints(icons, labels, 5, 450f, 6f, 16f, 0.7f, out scale, out spacing);
            Assert.True(n < 5 && n > 0);
            Assert.True(spacing >= 16f - 1e-3f);
            var used = n * 32f + n * 100f * scale + (n + 1) * spacing;
            Assert.Equal(450f, used, 2);
        }

        [Fact]
        public void TouchingIsNoCollision()
        {
            var boxes = new[] { B(0, 0, 50, 10, 2, 0), B(49.5f, 0, 99, 10, 2, 1) };
            var shown = new bool[2];
            Assert.Equal(0, PinLabelMath.Declutter(boxes, 2, new int[2], shown, 1f));
        }
    }
}
