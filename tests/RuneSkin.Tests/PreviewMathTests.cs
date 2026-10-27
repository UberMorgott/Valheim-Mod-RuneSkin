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
        public void CameraFitsHeightOnTallViewsAndWidthOnNarrowOnes()
        {
            var tan = Math.Tan(15.0 * Math.PI / 180.0);
            // Portrait 0.66: height bound (2 m body, 1.2 m wide: 1.2 / 0.66 = 1.82 < 2).
            Assert.Equal((float)(1.0 / tan), PreviewMath.Distance(2f, 1.2f, 0.66f, 30f, 0f), 3);
            // Very narrow 0.4: width bound.
            Assert.Equal((float)(0.6 / (tan * 0.4)), PreviewMath.Distance(2f, 1.2f, 0.4f, 30f, 0f), 3);
            // Padding pushes the camera back proportionally.
            Assert.Equal(PreviewMath.Distance(2f, 1.2f, 0.66f, 30f, 0f) * 1.1f, PreviewMath.Distance(2f, 1.2f, 0.66f, 30f, 0.1f), 3);
            // Bad aspect falls back to square.
            Assert.Equal(PreviewMath.Distance(2f, 1.2f, 1f, 30f, 0f), PreviewMath.Distance(2f, 1.2f, 0f, 30f, 0f), 3);
        }

        [Fact]
        public void FigureShareKeepsTheBodyHeightInsideALargerStage()
        {
            var tan = Math.Tan(15.0 * Math.PI / 180.0);
            // Stage twice the figure height: the body takes half the view height (camera twice as far).
            var share = PreviewMath.Share(150f, 300f);
            Assert.Equal(0.5f, share, 4);
            Assert.Equal((float)(2.0 / tan), PreviewMath.Distance(2f, 2.2f, 1.5f, 30f, 0f, share), 3);
            // Projected body height / view height == share (what lands on screen = the figure box).
            var d = PreviewMath.Distance(2f, 2.2f, 1.5f, 30f, 0f, share);
            Assert.Equal(share, (float)(2.0 / (2.0 * d * tan)), 3);
            // The span still fits a narrow stage: width bound wins, the body only gets smaller, never cut.
            var narrow = PreviewMath.Distance(2f, 2.2f, 0.4f, 30f, 0f, share);
            Assert.True(2.2 <= 2.0 * narrow * tan * 0.4 + 1e-3);
            // Figure taller than the stage / no figure: whole stage.
            // Tiny share: camera far back (CharacterPreview moves its far plane with the distance).
            Assert.True(PreviewMath.Distance(2f, 2.2f, 1f, 30f, 0.06f, PreviewMath.Share(10f, 300f)) > 30f);
            Assert.Equal(1f, PreviewMath.Share(400f, 300f));
            Assert.Equal(1f, PreviewMath.Share(0f, 300f));
            Assert.Equal(1f, PreviewMath.Share(100f, 0f));
            Assert.Equal(PreviewMath.Distance(2f, 1.2f, 0.66f, 30f, 0f), PreviewMath.Distance(2f, 1.2f, 0.66f, 30f, 0f, 7f), 3);
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
