using RuneSkin.Restyle;
using Xunit;

namespace RuneSkin.Tests
{
    // Confirm dialogs as one box sized to content (PanelMath.FitBox), the modal sort band (ModalOrder) and text kept
    // inside a framed box (InsetMargins: user 2026-10-23 quit dialog + map filter hint).
    public class DialogTests
    {
        [Fact]
        public void FitBoxPadsTheContentUnion()
        {
            // question text 180 x 30 above two buttons spanning 220 x 46.
            Assert.True(PanelMath.FitBox(-110f, -60f, 110f, 40f, 111f, 44f, 320f, 150f, out var x0, out var y0, out var x1, out var y1));
            Assert.Equal(-221f, x0);
            Assert.Equal(221f, x1);
            Assert.Equal(-104f, y0);
            Assert.Equal(84f, y1);
        }

        [Fact]
        public void FitBoxGrowsSymmetricallyToTheMinimum()
        {
            Assert.True(PanelMath.FitBox(-20f, -10f, 20f, 10f, 10f, 10f, 320f, 150f, out var x0, out var y0, out var x1, out var y1));
            Assert.Equal(320f, x1 - x0, 3);
            Assert.Equal(150f, y1 - y0, 3);
            Assert.Equal(0f, (x0 + x1) * 0.5f, 3); // centred on the content
            Assert.Equal(0f, (y0 + y1) * 0.5f, 3);
        }

        [Theory]
        [InlineData(1f, 0f, 0f, 1f)] // inverted x: empty union
        [InlineData(float.NaN, 0f, 1f, 1f)]
        public void FitBoxRefusesAnEmptyUnion(float a, float b, float c, float d) =>
            Assert.False(PanelMath.FitBox(a, b, c, d, 10f, 10f, 0f, 0f, out _, out _, out _, out _));

        [Fact]
        public void ModalBandSitsBetweenTheBaseAndTheTopOverlay()
        {
            const int band = 10; // TopOverlay.Band
            var modal = PanelMath.ModalOrder(3, band);
            Assert.True(modal > 3);
            Assert.True(modal < 3 + band);
            Assert.True(PanelMath.ModalOrder(0, 1) >= 1);
        }

        [Fact]
        public void InsetMarginsKeepTheTextInsideTheInnerRect()
        {
            // map hint: text rect 680 wide spanning the box with 10 px vanilla margins; caps 103 + 8 margin.
            var (l, t, r, b) = PanelMath.InsetMargins(-340f, -40f, 340f, 40f, -345f, -45f, 345f, 45f, 111f, 30f, 1f, 10f, 0f, 10f, 0f);
            Assert.Equal(-345f + 111f, -340f + l, 3);
            Assert.Equal(345f - 111f, 340f - r, 3);
            Assert.Equal(-45f + 30f, -40f + b, 3);
            Assert.Equal(45f - 30f, 40f - t, 3);
        }

        [Fact]
        public void InsetMarginsNeverLowerVanillaMargins()
        {
            var (l, t, r, b) = PanelMath.InsetMargins(0f, 0f, 100f, 50f, -500f, -500f, 600f, 600f, 10f, 10f, 1f, 7f, 3f, 5f, 2f);
            Assert.Equal((7f, 3f, 5f, 2f), (l, t, r, b));
        }

        [Fact]
        public void InsetMarginsAreInTextUnits()
        {
            // text scaled x2 inside the box: 20 box units = 10 text units.
            var (l, _, _, _) = PanelMath.InsetMargins(0f, 0f, 200f, 50f, 0f, 0f, 200f, 50f, 20f, 0f, 2f, 0f, 0f, 0f, 0f);
            Assert.Equal(10f, l, 3);
        }
    }
}
