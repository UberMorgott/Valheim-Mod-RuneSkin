using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    // StripMath.TargetFrame, centre wings (user 2026-10-26, mockups bottom-hud-center / -v2): each wing tip sits against
    // the bar block's inner diamond (one connector), the end medallion past the outer end, one rail from inside the wing
    // tip under the block tip; no wing on a side = no medallion there and the rail starts under the inner diamond.
    public class TargetFrameTests
    {
        private static float T7(string name, float fallback) => fallback;

        private static StripMath.Layout Layout(int food, int potions, float width = 2330f)
        {
            var l = new StripMath.Layout { BarsX = width / 2f - 540f, BarsW = 1080f, BarsY = 0f, BarsH = 92f };
            l.WingL = StripMath.Wing(StripLayoutTests.CutL, food, l.BarsX - 36f, 95f);
            l.WingR = StripMath.Wing(StripLayoutTests.CutR, potions, l.BarsX + l.BarsW + 36f, 95f);
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
            foreach (var b in new[] { f.EndL, f.EndR, f.WingL, f.WingR })
                Assert.Equal(0f, b.W);
            // the block's own rail extensions start under its inner diamonds, never out in the scene
            Assert.Equal(f.Knot2L.X, f.CapL.X, 3);
            Assert.Equal(f.Knot2R.Right, f.CapR.Right, 3);
        }

        // One connector (mockup v2): the wing tip reaches under the block's inner diamond, the rail runs from inside the
        // wing tip (its see-through chevron notches) under the diamond into the block, at one height on both sides.
        [Fact]
        public void WingTipsMeetTheInnerDiamonds_RailUnderTheJoint()
        {
            var l = Layout(3, 3);
            var f = StripMath.TargetFrame(l, T7);
            Assert.True(l.WingL.Inner > f.Knot2L.X && l.WingL.Inner < f.Knot2L.Right, $"left tip {l.WingL.Inner} vs diamond {f.Knot2L.X}..{f.Knot2L.Right}");
            Assert.True(l.WingR.Inner > f.Knot2R.X && l.WingR.Inner < f.Knot2R.Right, $"right tip {l.WingR.Inner} vs diamond {f.Knot2R.X}..{f.Knot2R.Right}");
            Assert.True(f.CapL.X <= l.WingL.Inner - 30f + 0.01f && f.CapL.Right >= l.BarsX + 20f);
            Assert.True(f.CapR.Right >= l.WingR.Inner + 30f - 0.01f && f.CapR.X <= l.BarsX + l.BarsW - 20f);
            Assert.Equal(f.CapL.Y, f.CapR.Y, 3);
            Assert.Equal(f.CapL.H, f.CapR.H, 3);
        }

        // The shipped theme: both wing tips land on the block's inner diamonds; the rails at the rail sprite's rows.
        [Fact]
        public void Theme_WingTipsOnTheInnerDiamonds()
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
            Assert.InRange(l.WingL.Inner, f.Knot2L.X, f.Knot2L.Right);
            Assert.InRange(l.WingR.Inner, f.Knot2R.X, f.Knot2R.Right);
            Assert.DoesNotContain("strip7.icon", json); // pool icon sockets removed (user 2026-10-26)
            Assert.DoesNotContain("\"sprite.hud.strip7.knot\"", json); // no separate wing-tip diamond (mockup v2: one connector)
            Assert.Equal(13f, f.CapL.Y, 2);
            Assert.Equal(13f, f.CapR.Y, 2);
        }

        [Fact]
        public void Bottom_IsTheLowestPiece()
        {
            var f = StripMath.TargetFrame(Layout(3, 3), T7);
            Assert.Equal(-10f, f.Bottom, 3); // end medallion tips (end.y fallback -10) hang below the wings
        }
    }
}