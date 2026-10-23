using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    // StripMath.TargetFrame (target panel, user screenshot 2026-10-23): both end medallions placed at the strip ends
    // (run 20261023-175632: the right one was never placed), rails continuous under the wing-tip diamonds (stub from
    // the diamond to the run, the run itself pinned to the target), the lowest piece reported for the flush placement.
    public class TargetFrameTests
    {
        private static float T7(string name, float fallback) => fallback;

        private static StripMath.Layout Layout(float width)
        {
            var l = new StripMath.Layout { BarsX = width / 2f - 540f, BarsW = 1080f, BarsY = 0f, BarsH = 92f };
            l.RightX = l.BarsX + l.BarsW;
            l.RightW = width - l.RightX;
            return l;
        }

        [Theory]
        [InlineData(2330f)]
        [InlineData(2000f)]
        public void EndMedallions_AtBothStripEnds(float width)
        {
            var f = StripMath.TargetFrame(Layout(width), width, T7);
            Assert.Equal(0f, f.EndL.X, 3);
            Assert.Equal(width, f.EndR.Right, 3);
            Assert.True(f.EndR.W > 0f && f.EndR.H > 0f);
            if (width >= 2300f) Assert.True(f.KnotR.W > 0f && f.KnotR.X > f.RailR.X); // full span: the diamond past the run
        }

        [Fact]
        public void Rails_ContinuousUnderTheDiamonds()
        {
            var f = StripMath.TargetFrame(Layout(2330f), 2330f, T7);
            Assert.True(f.StubL.X <= f.KnotL.X + 0.01f);           // under the whole left diamond
            Assert.True(f.StubL.Right > f.RailL.X);                // overlaps the run's start
            Assert.True(f.StubR.X < f.RailR.Right);                // overlaps the right run's end
            Assert.True(f.StubR.Right >= f.KnotR.Right - 0.01f);   // under the whole right diamond
            Assert.Equal(486f, f.RailL.X, 3);                      // the run keeps the target's own start
        }

        [Fact]
        public void Bottom_IsTheLowestPiece()
        {
            var f = StripMath.TargetFrame(Layout(2330f), 2330f, T7);
            Assert.Equal(-10f, f.Bottom, 3); // end medallion tips (end.y fallback -10) hang below the wings
        }

        [Fact]
        public void Covers_SlotOverDiamondCentre()
        {
            var knot = new StripMath.Box(448f, 26f, 40f, 43f);
            Assert.True(StripMath.Covers(440f, 75f, knot));
            Assert.False(StripMath.Covers(340f, 75f, knot));
        }
    }
}
