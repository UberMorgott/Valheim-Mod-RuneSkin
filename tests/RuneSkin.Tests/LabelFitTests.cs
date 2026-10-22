using RuneSkin.Restyle;
using Xunit;

namespace RuneSkin.Tests
{
    public class LabelFitTests
    {
        // Margin puts the text edge exactly inset from the button edge: label edge + margin == button edge + inset.
        [Theory]
        [InlineData(26f, 0f, 26f)]
        [InlineData(26f, 10f, 16f)]
        [InlineData(26f, 40f, -14f)]
        [InlineData(26f, -8f, 34f)]
        [InlineData(-5f, 0f, 0f)]
        public void MarginKeepsTheInset(float inset, float gap, float want) => Assert.Equal(want, LabelFit.Margin(inset, gap), 3);

        [Theory]
        [InlineData(200f, 100f, 20f, 40f)]
        [InlineData(100f, 200f, 20f, 10f)]
        [InlineData(100f, 0f, 20f, float.MaxValue)]
        public void OneLineSizeScalesWithWidth(float area, float w, float size, float want) => Assert.Equal(want, LabelFit.OneLineSize(area, w, size), 3);

        // Requirement socket name box 64 x 26.7, line 1.2 per unit, max 13 / min 10 (0.10.2): short names stay at
        // max, "Одуванчик" shrinks on one line, "Кожаные обрывки" wraps to two lines, one long word / no two-line fit shrinks below min (never clipped).
        [Theory]
        [InlineData(2.2f, 0f, 13f, 1)]
        [InlineData(5.3f, 0f, 12.0755f, 1)]
        [InlineData(8.4f, 4.0f, 11.125f, 2)]
        [InlineData(8f, 0f, 8f, 1)]
        [InlineData(20f, 12f, 5.3333f, 2)]
        public void NameSizeShrinksThenWraps(float oneLineW, float twoLineW, float want, int wantLines)
        {
            var size = LabelFit.NameSize(13f, 10f, 64f, 26.7f, 1.2f, oneLineW, twoLineW, out var lines);
            Assert.Equal(want, size, 2);
            Assert.Equal(wantLines, lines);
        }

        // Measured two-line block taller than 2 x one line: the size follows it (run 20261022-170700).
        [Fact]
        public void NameSizeUsesTheMeasuredTwoLineHeight()
        {
            var size = LabelFit.NameSize(13f, 10f, 64f, 26.7f, 1.2f, 8.4f, 4.0f, out var lines, 2.5f);
            Assert.Equal(2, lines);
            Assert.Equal(10.68f, size, 2);
        }

        [Fact]
        public void BestSplitBalancesTheLines()
        {
            float W(int i, int j) => j - i;
            Assert.Equal(7, LabelFit.BestSplit("Кожаные обрывки", W, out var widest));
            Assert.Equal(7f, widest, 3);
            Assert.Equal(-1, LabelFit.BestSplit("Одуванчик", W, out _));
            Assert.Equal(5, LabelFit.BestSplit("Шкура из оленя", W, out var w2));
            Assert.Equal(8f, w2, 3);
        }

        // A 40 px arrow button keeps room for its glyph; wide plates keep the full cap inset.
        [Theory]
        [InlineData(26f, 40f, 0.12f, 4.8f)]
        [InlineData(26f, 400f, 0.12f, 26f)]
        [InlineData(26f, -10f, 0.12f, 0f)]
        public void InsetScalesOnSmallButtons(float inset, float width, float share, float want) => Assert.Equal(want, LabelFit.Inset(inset, width, share), 3);
    }
}
