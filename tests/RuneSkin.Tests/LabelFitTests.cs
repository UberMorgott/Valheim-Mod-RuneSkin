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

        // A 40 px arrow button keeps room for its glyph; wide plates keep the full cap inset.
        [Theory]
        [InlineData(26f, 40f, 0.12f, 4.8f)]
        [InlineData(26f, 400f, 0.12f, 26f)]
        [InlineData(26f, -10f, 0.12f, 0f)]
        public void InsetScalesOnSmallButtons(float inset, float width, float share, float want) => Assert.Equal(want, LabelFit.Inset(inset, width, share), 3);
    }
}
