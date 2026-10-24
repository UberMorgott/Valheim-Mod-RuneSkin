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
            Assert.Equal(f.RailL.X, f.StubL.Right, 3);             // abuts the run's start (tiling starts at the joint)
            Assert.Equal(f.RailR.Right, f.StubR.X, 3);             // abuts the right run's end
            Assert.True(f.StubR.Right >= f.KnotR.Right - 0.01f);   // under the whole right diamond
            Assert.Equal(486f, f.RailL.X, 3);                      // the run keeps the target's own start
        }

        // User crops 0.20.0 (wing tips): the stubs start under the wing tip's chevron, whose notches are see-through.
        [Fact]
        public void Stubs_StartInsideTheWingTips()
        {
            var f = StripMath.TargetFrame(Layout(2330f), 2330f, T7);
            Assert.True(f.StubL.X <= f.WingL.Right - 30f + 0.01f, $"stub L {f.StubL.X} vs wing end {f.WingL.Right}");
            Assert.True(f.StubR.Right >= f.WingR.X + 30f - 0.01f, $"stub R {f.StubR.Right} vs wing start {f.WingR.X}");
        }

        // User crops 0.20.0 (wing tips): the rail's opaque rows (sprite minus its transparent padding) fill exactly the band
        // of the rail stubs baked into the wing tips (left 17..72, right 19..74 strip units), no step at the joint.
        [Fact]
        public void RailOpaqueBand_MatchesTheWingStubs()
        {
            var f = StripMath.TargetFrame(Layout(2330f), 2330f, T7);
            Assert.Equal(17f, f.RailL.Y + f.RailL.H * 12f / 121f, 2);
            Assert.Equal(72f, f.RailL.Y + f.RailL.H * (1f - 18f / 121f), 2);
            Assert.Equal(19f, f.RailR.Y + f.RailR.H * 11f / 132f, 2);
            Assert.Equal(74f, f.RailR.Y + f.RailR.H * (1f - 14f / 132f), 2);
            foreach (var b in new[] { f.StubL, f.CapL }) { Assert.Equal(f.RailL.Y, b.Y, 3); Assert.Equal(f.RailL.H, b.H, 3); }
            foreach (var b in new[] { f.StubR, f.CapR }) { Assert.Equal(f.RailR.Y, b.Y, 3); Assert.Equal(f.RailR.H, b.H, 3); }
        }

        [Fact]
        public void RailBand_FallsBackWithoutABand()
        {
            StripMath.RailBand(0f, 0f, 0.1f, 0.1f, 15.8f, 60.6f, out var y, out var h);
            Assert.Equal(15.8f, y, 3);
            Assert.Equal(60.6f, h, 3);
            StripMath.RailBand(10f, 50f, 0.5f, 0.5f, 1f, 2f, out y, out h); // all padding: fallback
            Assert.Equal(1f, y, 3);
        }

        // User crops 0.20.0: a hard step and see-through gaps between the rail ends and the bar block tips. The rail runs
        // on (same y / height) into the block, under its tip, on both sides.
        [Fact]
        public void Rails_RunOnUnderTheBarBlockTips()
        {
            var l = Layout(2330f);
            var f = StripMath.TargetFrame(l, 2330f, T7);
            Assert.True(f.CapL.X <= f.RailL.Right && f.CapL.Right >= l.BarsX + 20f, $"{f.CapL.X}..{f.CapL.Right} vs rail end {f.RailL.Right}, block {l.BarsX}");
            Assert.Equal(f.RailL.Right, f.CapL.X, 3); // abuts: the cap's tiling starts at the run's end
            Assert.Equal(f.RailR.X, f.CapR.Right, 3);
            Assert.Equal(f.RailL.Y, f.CapL.Y, 3);
            Assert.Equal(f.RailL.H, f.CapL.H, 3);
            var blockR = l.BarsX + l.BarsW;
            Assert.True(f.CapR.X <= blockR - 20f && f.CapR.Right >= f.RailR.X, $"{f.CapR.X}..{f.CapR.Right} vs rail start {f.RailR.X}, block end {blockR}");
            Assert.Equal(f.RailR.Y, f.CapR.Y, 3);
            Assert.Equal(f.RailR.H, f.CapR.H, 3);
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
