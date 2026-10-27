using System;
using RuneSkin.InventoryScreen;
using Xunit;

namespace RuneSkin.Tests
{
    // Live character preview math (CharacterPreview): theme rect placement, texture size, camera fit, drag yaw, layer.
    public class PreviewMathTests
    {
        private static readonly Box Figure = new Box(100f, 50f, 200f, 300f);

        [Fact]
        public void FullThemeRectIsTheFigureBoxInset()
        {
            var b = PreviewMath.Place(Figure, 0f, 0f, 1f, 1f, 8f);
            Assert.Equal(108f, b.X, 3);
            Assert.Equal(58f, b.Y, 3);
            Assert.Equal(184f, b.W, 3);
            Assert.Equal(284f, b.H, 3);
        }

        [Fact]
        public void ThemeRectMovesThePreviewInsideTheHost()
        {
            var b = PreviewMath.Place(Figure, 0.5f, 0.25f, 0.5f, 0.5f, 0f);
            Assert.Equal(200f, b.X, 3);
            Assert.Equal(125f, b.Y, 3);
            Assert.Equal(100f, b.W, 3);
            Assert.Equal(150f, b.H, 3);
        }

        [Fact]
        public void ThemeRectIsClampedToTheHost()
        {
            var b = PreviewMath.Place(Figure, 0.8f, -1f, 0.5f, 3f, 0f);
            Assert.Equal(260f, b.X, 3);
            Assert.Equal(50f, b.Y, 3);
            Assert.Equal(40f, b.W, 3); // w cut at the right edge
            Assert.Equal(300f, b.H, 3);
        }

        [Fact]
        public void EmptyHostOrOversizedInsetHidesThePreview()
        {
            Assert.True(PreviewMath.Place(default, 0f, 0f, 1f, 1f, 0f).IsEmpty);
            Assert.True(PreviewMath.Place(new Box(0f, 0f, 10f, 10f), 0f, 0f, 1f, 1f, 6f).IsEmpty);
        }

        [Fact]
        public void TextureFollowsPhysicalPixelsEvenAndClamped()
        {
            Assert.Equal((368, 568), PreviewMath.TextureSize(184f, 284f, 2f));
            Assert.Equal((64, 64), PreviewMath.TextureSize(10f, 10f, 1f));
            var big = PreviewMath.TextureSize(3000f, 1500f, 1f);
            Assert.Equal(2048, big.W);
            Assert.Equal(1024, big.H); // aspect kept when the long side is clamped
            Assert.Equal((0, 0), PreviewMath.TextureSize(0f, 100f, 1f));
            Assert.Equal((186, 284), PreviewMath.TextureSize(184.6f, 283.7f, 1f)); // 185 rounds up to even
        }

        [Fact]
        public void ResizeOnlyBeyondSlack()
        {
            Assert.True(PreviewMath.NeedsResize(0, 0, 100, 100));
            Assert.False(PreviewMath.NeedsResize(100, 200, 102, 198));
            Assert.True(PreviewMath.NeedsResize(100, 200, 104, 200));
        }

        [Fact]
        public void FitHeightFillsTheViewHeightWithTheMargins()
        {
            var tan = Math.Tan(15.0 * Math.PI / 180.0);
            var spans = new[] { (0f, 1.85f), (0f, 2.05f), (-0.02f, 2.4f) };
            foreach (var aspect in new[] { 0.5f, 0.66f, 1f, 1.8f })
                foreach (var (y0, y1) in spans)
                {
                    var (d, cy) = PreviewMath.FitHeight(y0, y1, aspect, 30f, 0.06f, 0.03f, 0.5f);
                    var viewH = 2.0 * d * tan;
                    var lo = cy - viewH / 2.0;
                    // feet 3% above the lower edge, head 6% under the upper edge: the figure takes 91% of the height
                    Assert.Equal(0.03, (y0 - lo) / viewH, 3);
                    Assert.Equal(0.94, (y1 - lo) / viewH, 3);
                }
        }

        [Fact]
        public void FitHeightKeepsNearExtremesInsideThePerspectiveView()
        {
            var tan = Math.Tan(15.0 * Math.PI / 180.0);
            const float near = 0.3f;
            var (d, cy) = PreviewMath.FitHeight(0f, 2f, 0.8f, 30f, 0.06f, 0.03f, 0.5f, near);
            // a foot / helmet point `near` in front of the figure plane projects exactly onto the margins
            double Project(double y, double depth) => 0.5 + (y - cy) / (2.0 * depth * tan);
            Assert.Equal(0.03, Project(0.0, d - near), 3);
            Assert.Equal(0.94, Project(2.0, d - near), 3);
            // points on or behind the plane land inside the margins
            Assert.True(Project(0.0, d) > 0.03 && Project(2.0, d) < 0.94);
            Assert.Equal(PreviewMath.FitHeight(0f, 2f, 0.8f, 30f, 0.06f, 0.03f, 0.5f), PreviewMath.FitHeight(0f, 2f, 0.8f, 30f, 0.06f, 0.03f, 0.5f, -1f));
        }

        [Fact]
        public void FitHeightNeverShrinksForWideSpansOnlyForTheCoreWidth()
        {
            var tan = Math.Tan(15.0 * Math.PI / 180.0);
            // A portrait view: the height fit stands whatever a weapon spans sideways (no width input but the core).
            var (d, _) = PreviewMath.FitHeight(0f, 2f, 0.5f, 30f, 0f, 0f, 0.6f);
            Assert.Equal((float)(1.0 / tan), d, 3);
            // A very narrow view: the core width bounds, feet still anchored at the bottom margin.
            var (dn, cyn) = PreviewMath.FitHeight(0f, 2f, 0.2f, 30f, 0.06f, 0.03f, 0.6f);
            Assert.Equal((float)(0.3 / (tan * 0.2)), dn, 3);
            var viewH = 2.0 * dn * tan;
            Assert.Equal(0.03, (0.0 - (cyn - viewH / 2.0)) / viewH, 3);
            // Bad aspect falls back to square; a degenerate span frames MinSpan.
            Assert.Equal(PreviewMath.FitHeight(0f, 2f, 1f, 30f, 0f, 0f, 0f), PreviewMath.FitHeight(0f, 2f, 0f, 30f, 0f, 0f, 0f));
            Assert.Equal(PreviewMath.FitHeight(1f, 1f + PreviewMath.MinSpan, 1f, 30f, 0f, 0f, 0f), PreviewMath.FitHeight(1f, 1f, 1f, 30f, 0f, 0f, 0f));
            Assert.Equal(PreviewMath.FitHeight(0f, PreviewMath.MinSpan, 1f, 30f, 0f, 0f, 0f), PreviewMath.FitHeight(0f, float.NaN, 1f, 30f, 0f, 0f, 0f));
        }

        [Fact]
        public void SmoothSnapsFirstEasesLaterAndIgnoresJitter()
        {
            Assert.Equal(1.9f, PreviewMath.Smooth(float.NaN, 1.9f, 0.016f, 10f, 0.01f));
            Assert.Equal(1.9f, PreviewMath.Smooth(1.9f, 1.905f, 0.016f, 10f, 0.01f)); // inside the deadband
            var v = PreviewMath.Smooth(1.9f, 2.3f, 0.016f, 10f, 0.01f);
            Assert.True(v > 1.9f && v < 2.3f);
            // frame-rate independent: two 8 ms steps == one 16 ms step
            var two = PreviewMath.Smooth(PreviewMath.Smooth(1.9f, 2.3f, 0.008f, 10f, 0f), 2.3f, 0.008f, 10f, 0f);
            Assert.Equal(PreviewMath.Smooth(1.9f, 2.3f, 0.016f, 10f, 0f), two, 4);
            // converges within a second at rate 10
            var c = 1.9f;
            for (var i = 0; i < 60; i++) c = PreviewMath.Smooth(c, 2.3f, 1f / 60f, 10f, 0.01f);
            Assert.True(Math.Abs(c - 2.3f) <= 0.01f);
            Assert.Equal(2.3f, PreviewMath.Smooth(1.9f, 2.3f, 0f, 10f, 0.01f));
        }

        [Fact]
        public void DragTurnsAndWraps()
        {
            Assert.Equal(350f, PreviewMath.Yaw(0f, 20f, 0.5f), 3);
            Assert.Equal(10f, PreviewMath.Yaw(350f, -40f, 0.5f), 3);
            Assert.Equal(0f, PreviewMath.Yaw(0f, 720f, 0.5f), 3);
        }

        [Fact]
        public void PicksTheHighestUnnamedLayer()
        {
            string Names(int i) => i == 31 || i == 30 || i < 8 ? "used" + i : i == 29 ? "" : null;
            Assert.Equal(29, PreviewMath.PickLayer(Names));
            Assert.Equal(-1, PreviewMath.PickLayer(i => "x"));
            Assert.Equal(-1, PreviewMath.PickLayer(null));
            Assert.Equal(31, PreviewMath.PickLayer(i => i == 31 ? "" : "x"));
        }
    }
}
