using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    // StripMath.TargetFrame, centre wings (user 2026-10-26, mockup bottom-hud-center): every wing-side piece hangs off
    // the wing's inner end - end medallion past the outer end, wing-tip diamond and rail run between the wing and the bar
    // block, the rail continuous under the diamonds; no wing on a side = no medallion / diamond / rail there.
    public class TargetFrameTests
    {
        private static float T7(string name, float fallback) => fallback;

        private static StripMath.Layout Layout(int food, int potions, float width = 2330f)
        {
            var l = new StripMath.Layout { BarsX = width / 2f - 540f, BarsW = 1080f, BarsY = 0f, BarsH = 92f };
            l.WingL = StripMath.Wing(StripLayoutTests.CutL, food, l.BarsX - 76f, 95f);
            l.WingR = StripMath.Wing(StripLayoutTests.CutR, potions, l.BarsX + l.BarsW + 78f, 95f);
            return l;
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(3, 4)]
        public void EndMedallions_CapTheOuterEnds(int food, int potions)
        {
            var l = Layout(food, potions);
            var f = StripMath.TargetFrame(l, T7);
            Assert.Equal(l.WingL.X - 85f, f.EndL.X, 3);
            Assert.Equal(l.WingR.Right + 60.5f - 132.5f, f.EndR.X, 3); // wing.r.right / end.r.w fallbacks
            Assert.True(f.EndL.W > 0f && f.EndR.W > 0f);
            Assert.Equal(l.WingL.X, f.WingL.X, 3);
            Assert.Equal(l.WingR.Right, f.WingR.Right, 3);
        }

        [Fact]
        public void NoSlots_NoWingPieces()
        {
            var l = Layout(0, 0);
            var f = StripMath.TargetFrame(l, T7);
            foreach (var b in new[] { f.EndL, f.EndR, f.WingL, f.WingR, f.KnotL, f.KnotR, f.RailL, f.RailR, f.StubL, f.StubR })
                Assert.Equal(0f, b.W);
            // the block's own tip extensions start under its inner diamonds, never out in the scene
            Assert.Equal(f.Knot2L.X, f.CapL.X, 3);
            Assert.Equal(f.Knot2R.Right, f.CapR.Right, 3);
        }

        [Fact]
        public void Rails_ContinuousFromTheWingTipToTheBlock()
        {
            var l = Layout(3, 3);
            var f = StripMath.TargetFrame(l, T7);
            Assert.True(f.StubL.X <= f.KnotL.X + 0.01f);           // under the whole left diamond
            Assert.Equal(f.RailL.X, f.StubL.Right, 3);             // abuts the run's start (tiling starts at the joint)
            Assert.Equal(f.RailR.Right, f.StubR.X, 3);             // abuts the right run's end
            Assert.True(f.StubR.Right >= f.KnotR.Right - 0.01f);   // under the whole right diamond
            Assert.True(f.StubL.X <= l.WingL.Inner - 30f + 0.01f); // stubs start inside the wing tips (see-through notches)
            Assert.True(f.StubR.Right >= l.WingR.Inner + 30f - 0.01f);
            Assert.Equal(f.RailL.Right, f.CapL.X, 3);
            Assert.Equal(f.RailR.X, f.CapR.Right, 3);
            Assert.True(f.CapL.Right >= l.BarsX + 20f && f.CapR.X <= l.BarsX + l.BarsW - 20f);
            foreach (var b in new[] { f.StubL, f.CapL }) { Assert.Equal(f.RailL.Y, b.Y, 3); Assert.Equal(f.RailL.H, b.H, 3); }
            foreach (var b in new[] { f.StubR, f.CapR }) { Assert.Equal(f.RailR.Y, b.Y, 3); Assert.Equal(f.RailR.H, b.H, 3); }
        }

        // The shipped theme: wing-tip diamond against the block's inner diamond (mockup: the two diamonds meet), both sides.
        [Fact]
        public void Theme_DiamondsAShortRailApart()
        {
            using var s = typeof(RuneSkin.Theme.ThemeSchema).Assembly.GetManifestResourceStream("RuneSkin.default-theme.json");
            using var r = new System.IO.StreamReader(s!);
            var json = r.ReadToEnd();
            float Tok(string name, float fb)
            {
                var m = System.Text.RegularExpressions.Regex.Match(json, "\"size\\.hud\\.strip7\\." + System.Text.RegularExpressions.Regex.Escape(name) + "\",\\s*\"value\":\\s*(-?[0-9.]+)");
                return m.Success ? float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : fb;
            }
            var l = new StripMath.Layout { BarsX = 625f, BarsW = 1080f, BarsH = 92f };
            l.WingL = StripMath.Wing(StripLayoutTests.CutL, 3, l.BarsX - Tok("wing.join", 0f), 95f);
            l.WingR = StripMath.Wing(StripLayoutTests.CutR, 4, l.BarsX + l.BarsW + Tok("wing.r.join", 0f), 95f);
            var f = StripMath.TargetFrame(l, Tok);
            Assert.InRange(f.Knot2L.X - f.KnotL.Right, -1f, 3f);
            Assert.InRange(f.KnotR.X - f.Knot2R.Right, -1f, 3f);
            Assert.DoesNotContain("strip7.icon", json); // pool icon sockets removed (user 2026-10-26)
            foreach (var b in new[] { f.RailL, f.RailR, f.StubL, f.StubR, f.CapL, f.CapR }) Assert.Equal(13f, b.Y, 2);
        }

        [Fact]
        public void Bottom_IsTheLowestPiece()
        {
            var f = StripMath.TargetFrame(Layout(3, 3), T7);
            Assert.Equal(-10f, f.Bottom, 3); // end medallion tips (end.y fallback -10) hang below the wings
        }
    }
}
