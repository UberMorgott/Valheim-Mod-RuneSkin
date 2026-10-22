using RuneSkin.MapScreen;
using RuneSkin.WorldHud;
using Xunit;

namespace RuneSkin.Tests
{
    // M4 polish: taller enemy bars, large map zoom slider, legend row fit.
    public class M4PolishTests
    {
        [Theory]
        [InlineData(9f, 15f, 15f / 9f)]
        [InlineData(20f, 15f, 1f)]  // already taller: never shrinks
        [InlineData(0f, 15f, 1f)]   // degenerate rect
        [InlineData(9f, 0f, 1f)]    // no target token
        public void BarGrowthNeverShrinks(float current, float target, float expected)
        {
            Assert.Equal(expected, EnemyHudMath.BarGrowth(current, target), 4);
        }

        [Fact]
        public void ZoomSliderEnds()
        {
            Assert.Equal(0f, MinimapMath.ZoomToSlider(1f, 0.01f, 1f), 4);
            Assert.Equal(1f, MinimapMath.ZoomToSlider(0.01f, 0.01f, 1f), 4);
            Assert.Equal(0.5f, MinimapMath.ZoomToSlider(0.1f, 0.01f, 1f), 4); // geometric middle
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.3f)]
        [InlineData(0.77f)]
        [InlineData(1f)]
        public void ZoomSliderRoundTrips(float v)
        {
            var z = MinimapMath.SliderToZoom(v, 0.01f, 1f);
            Assert.Equal(v, MinimapMath.ZoomToSlider(z, 0.01f, 1f), 4);
        }

        [Fact]
        public void ZoomSliderClampsAndSurvivesBadRange()
        {
            Assert.Equal(0f, MinimapMath.ZoomToSlider(5f, 0.01f, 1f), 4);
            Assert.Equal(1f, MinimapMath.ZoomToSlider(0.001f, 0.01f, 1f), 4);
            Assert.Equal(0f, MinimapMath.ZoomToSlider(0.5f, 0f, 1f), 4);
            Assert.Equal(1f, MinimapMath.SliderToZoom(0.5f, 1f, 1f), 4);
        }

        [Theory]
        [InlineData(400f, 7, 40f, 28f, 40f)]   // room: preferred
        [InlineData(300f, 10, 40f, 28f, 30f)]  // shrunk to fit
        [InlineData(100f, 10, 40f, 28f, 28f)]  // floor wins
        [InlineData(100f, 0, 40f, 28f, 40f)]   // no rows
        public void LegendRowHeight(float available, int count, float preferred, float floor, float expected)
        {
            Assert.Equal(expected, MinimapMath.RowHeight(available, count, preferred, floor), 4);
        }
    }
}
