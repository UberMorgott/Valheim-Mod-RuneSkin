using System.Linq;
using RuneSkin.Restyle;
using Xunit;

namespace RuneSkin.Tests
{
    // Overdraw track: hidden = not rendered (RenderGate ledger, fade floor, cover geometry).
    public class RenderGateTests
    {
        [Fact]
        public void FadeWritesEndValuesAtOnceAndStepsAtMost60Hz()
        {
            const float step = RenderGateMath.FadeWriteInterval;
            Assert.True(RenderGateMath.FadeWrite(0.9f, 1f, 1f, 0f));        // reached the target: written the same frame
            Assert.True(RenderGateMath.FadeWrite(0.1f, 0f, 0f, 0f));        // faded out: written (park reads 0)
            Assert.False(RenderGateMath.FadeWrite(0.2f, 0.25f, 1f, step * 0.5f)); // fast frames: held under one 60 Hz period
            Assert.True(RenderGateMath.FadeWrite(0.2f, 0.25f, 1f, step));   // 60 fps and slower: every frame
            Assert.False(RenderGateMath.FadeWrite(1f, 1f, 1f, 10f));        // settled: no write
            Assert.True(RenderGateMath.FadeWrite(0f, 0.05f, 1f, float.PositiveInfinity)); // first step of an open
        }
        [Fact]
        public void FirstOwnerConcealsLastOwnerReveals()
        {
            var l = new GateLedger<int>();
            Assert.Equal(GateLedger<int>.Change.Concealed, l.Set(1, "fullwindow", true));
            Assert.Equal(GateLedger<int>.Change.None, l.Set(1, "cover", true));
            Assert.Equal(GateLedger<int>.Change.None, l.Set(1, "fullwindow", false));
            Assert.True(l.Has(1));
            Assert.Equal(GateLedger<int>.Change.Revealed, l.Set(1, "cover", false));
            Assert.False(l.Has(1));
        }

        [Fact]
        public void RepeatedWritesAreNoChange()
        {
            var l = new GateLedger<int>();
            l.Set(7, "fade", true);
            Assert.Equal(GateLedger<int>.Change.None, l.Set(7, "fade", true));
            Assert.Equal(GateLedger<int>.Change.None, l.Set(8, "fade", false)); // never held
            Assert.Equal(GateLedger<int>.Change.Revealed, l.Set(7, "fade", false));
            Assert.Equal(GateLedger<int>.Change.None, l.Set(7, "fade", false));
        }

        [Fact]
        public void ReleaseAllRevealsOnlyKeysNoOtherOwnerHolds()
        {
            var l = new GateLedger<int>();
            l.Set(1, "cover", true);
            l.Set(2, "cover", true);
            l.Set(2, "largemap", true);
            var revealed = l.ReleaseAll("cover");
            Assert.Equal(new[] { 1 }, revealed.ToArray());
            Assert.True(l.Holds(2, "largemap"));
            Assert.False(l.Holds(2, "cover"));
            Assert.Equal(1, l.Count);
        }

        [Theory]
        [InlineData(0f, true)]
        [InlineData(0.0005f, true)]
        [InlineData(0.001f, true)]
        [InlineData(0.002f, false)]
        [InlineData(1f, false)]
        [InlineData(float.NaN, true)]
        public void FadedOutMeansGated(float alpha, bool gated) => Assert.Equal(gated, RenderGateMath.Concealed(alpha));

        [Fact]
        public void InsideNeedsAllFourEdgesWithinTolerance()
        {
            Assert.True(RenderGateMath.Inside(0, 0, 100, 100, 10, 10, 90, 90, 0f));
            Assert.True(RenderGateMath.Inside(0, 0, 100, 100, -0.4f, 0, 100.4f, 100, 0.5f));
            Assert.False(RenderGateMath.Inside(0, 0, 100, 100, -1f, 10, 90, 90, 0.5f));
            Assert.False(RenderGateMath.Inside(0, 0, 100, 100, 10, 10, 90, 101f, 0.5f));
        }

        [Fact]
        public void OverlapExcludesTouchingEdges()
        {
            Assert.True(RenderGateMath.Overlaps(0, 0, 100, 100, 99, 99, 120, 120));
            Assert.False(RenderGateMath.Overlaps(0, 0, 100, 100, 100, 0, 120, 100));
        }

        [Fact]
        public void InteriorScalesDesignMarginsToTheWindow()
        {
            // Stats chrome at 2560x1440: design 1920x1080 margins 48/56/48/32 scale by 4/3; top = high-y edge.
            RenderGateMath.Interior(0, 0, 2560, 1440, 1920, 1080, 48, 56, 48, 32, out var x0, out var y0, out var x1, out var y1);
            Assert.Equal(64f, x0, 3);
            Assert.Equal(2496f, x1, 3);
            Assert.Equal(1440f - 56f * 4f / 3f, y1, 3);
            Assert.Equal(32f * 4f / 3f, y0, 3);
        }

        [Fact]
        public void InteriorOfALetterboxedWindowKeepsItsOffset()
        {
            // 21:9 screen: the 16:9 window centred at x 400..2960 (2560 wide), full height 1440.
            RenderGateMath.Interior(400, 0, 2960, 1440, 1920, 1080, 48, 56, 48, 32, out var x0, out _, out var x1, out _);
            Assert.Equal(464f, x0, 3);
            Assert.Equal(2896f, x1, 3);
        }

        [Fact]
        public void InteriorSwallowedByMarginsIsEmpty()
        {
            RenderGateMath.Interior(0, 0, 50, 50, 1920, 1080, 1000, 0, 1000, 0, out var x0, out var y0, out var x1, out var y1);
            Assert.True(x0 >= x1 && y0 >= y1);
        }
    }
}
