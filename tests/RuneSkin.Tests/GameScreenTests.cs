using RuneSkin.API;
using RuneSkin.GameScreens;
using Xunit;

namespace RuneSkin.Tests
{
    public class GameScreenTests
    {
        // Tip banner: the tip box plus both pads, never narrower than the banner minimum, never wider than the cap.
        [Theory]
        [InlineData(300f, 20f, 520f, 1400f, 520f)]
        [InlineData(800f, 20f, 520f, 1400f, 840f)]
        [InlineData(2000f, 20f, 520f, 1400f, 1400f)]
        [InlineData(-5f, 20f, 0f, 0f, 40f)]
        public void BannerWidthClamps(float tip, float pad, float min, float max, float want) =>
            Assert.Equal(want, GameScreenMath.BannerWidth(tip, pad, min, max), 3);

        [Fact]
        public void BannerHeightStacksTitleAndPads() => Assert.Equal(160f, GameScreenMath.BannerHeight(100f, 24f, 18f), 3);

        // Bands sit clear of the text: their inner edge is exactly gap away from the text box.
        [Fact]
        public void BandOffsetClearsText()
        {
            const float text = 300f, band = 22f, gap = 10f;
            var off = GameScreenMath.BandOffset(text, band, gap);
            Assert.Equal(text * 0.5f + gap, off - band * 0.5f, 3);
        }

        [Theory]
        [InlineData(0f, 40f, 0f)]
        [InlineData(1f, 40f, -40f)]
        [InlineData(10f, 40f, -40f)]
        [InlineData(float.NaN, 40f, 0f)]
        public void SpinAngleTurnsClockwiseAndWraps(float t, float speed, float want) =>
            Assert.Equal(want, GameScreenMath.SpinAngle(t, speed), 3);

        [Fact]
        public void SpinAngleStaysInOneTurn()
        {
            for (var t = 0f; t < 100f; t += 0.37f)
            {
                var a = GameScreenMath.SpinAngle(t, 47f);
                Assert.InRange(a, -360f, 0f);
            }
        }

        [Theory]
        [InlineData(112f, 0.42f, 47.04f)]
        [InlineData(112f, 1.5f, 112f)]
        [InlineData(-1f, 0.5f, 0f)]
        public void GlyphSideIsAShareOfTheSocket(float socket, float share, float want) =>
            Assert.Equal(want, GameScreenMath.GlyphSide(socket, share), 3);

        // M6 batch 2 families are stable API names (AugaSkin asks for them by string).
        [Fact]
        public void BatchTwoFamilyNames()
        {
            Assert.Equal("LoadScreen", SkinFamilies.LoadScreen);
            Assert.Equal("Chat", SkinFamilies.Chat);
            Assert.Equal("Popups", SkinFamilies.Popups);
            Assert.Equal("TextDialogs", SkinFamilies.TextDialogs);
            Assert.Equal("Compendium", SkinFamilies.Compendium);
            Assert.Equal("SplitDialog", SkinFamilies.SplitDialog);
        }
    }
}
