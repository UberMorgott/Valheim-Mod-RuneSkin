using RuneSkin.WorldHud;
using Xunit;

namespace RuneSkin.Tests
{
    // Speech bubble sizing (SpeechMath): content-sized, capped width, padding, distance fade.
    public class SpeechBubbleTests
    {
        [Theory]
        [InlineData(180.2f, 360f, 182f)]  // short line: its own width (ceil + 1), not the cap
        [InlineData(359.5f, 360f, 360f)]  // just under the cap: the cap
        [InlineData(900f, 360f, 360f)]    // long line: wraps at the cap
        [InlineData(float.NaN, 360f, 360f)]
        [InlineData(-5f, 440f, 440f)]
        public void WrapWidthCapsAtMax(float oneLine, float max, float expected) =>
            Assert.Equal(expected, SpeechMath.WrapWidth(oneLine, max));

        [Fact]
        public void BubbleIsTextPlusPadding()
        {
            SpeechMath.BubbleSize(182f, 23.4f, 10f, 6f, out var w, out var h);
            Assert.Equal(202f, w);
            Assert.Equal(36f, h);
        }

        [Fact]
        public void EmptyTextHasNoBubble()
        {
            SpeechMath.BubbleSize(0f, 20f, 10f, 6f, out var w, out var h);
            Assert.Equal(0f, w);
            Assert.Equal(0f, h);
        }

        // The Wizardry line that drew the 700x250 panel: one line of ~21 glyphs at 20 px stays a small fraction of a
        // 1920 reference width (harness bound 25 %).
        [Fact]
        public void ShortLineStaysCompact()
        {
            var wrap = SpeechMath.WrapWidth(230f, 440f);
            SpeechMath.BubbleSize(wrap, 24f, 10f, 6f, out var w, out var h);
            Assert.True(w / 1920f < 0.25f, $"share {w / 1920f}");
            Assert.True(h < 60f);
        }

        [Fact]
        public void MaxWidthBubbleUnderQuarterScreen()
        {
            SpeechMath.BubbleSize(SpeechMath.WrapWidth(5000f, 440f), 120f, 10f, 6f, out var w, out _);
            Assert.True(w / 1920f < 0.25f, $"share {w / 1920f}");
        }

        [Theory]
        [InlineData(5f, 20f, 0.25f, 1f)]
        [InlineData(15f, 20f, 0.25f, 1f)]
        [InlineData(17.5f, 20f, 0.25f, 0.5f)]
        [InlineData(20f, 20f, 0.25f, 0f)]
        [InlineData(25f, 20f, 0.25f, 0f)]
        [InlineData(25f, 0f, 0.25f, 1f)]   // no cull distance: never faded
        [InlineData(19f, 20f, 0f, 1f)]     // no fade share: vanilla hard cut
        public void FadesOverLastShareOfCullDistance(float d, float cull, float share, float expected) =>
            Assert.Equal(expected, SpeechMath.DistanceFade(d, cull, share), 3);
    }
}
