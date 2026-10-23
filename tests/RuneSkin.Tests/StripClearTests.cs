using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    // StripClearMath: bottom-anchored HUD elements (build piece info + requirements first, bug 2026-10-23) rise to the
    // strip's live top + gap, from home, at every strip height.
    public class StripClearTests
    {
        [Theory]
        // screen height, strip top px (taller strip with the raised hotbar row), gap px, element drawn bottom at home
        [InlineData(720f, 150f, 8f, 60f)]
        [InlineData(1080f, 214f, 12f, 90f)]
        [InlineData(1440f, 286f, 16f, 120f)]
        public void Lift_PutsCoveredElementOnStripTopPlusGap(float screenH, float stripTop, float gap, float homeBottom)
        {
            var lift = StripClearMath.Lift(homeBottom, 700f, 1200f, stripTop, 40f, 1880f, gap);
            Assert.Equal(stripTop + gap, homeBottom + lift, 3);
            Assert.True(StripClearMath.Clear(homeBottom + lift, 700f, 1200f, stripTop, 40f, 1880f, gap));
            Assert.True(homeBottom + lift < screenH);
        }

        [Fact]
        public void Lift_ZeroWhenAlreadyClear()
        {
            Assert.Equal(0f, StripClearMath.Lift(400f, 700f, 1200f, 214f, 40f, 1880f, 12f));
            Assert.Equal(0f, StripClearMath.Lift(226f, 700f, 1200f, 214f, 40f, 1880f, 12f));
        }

        [Fact]
        public void Lift_ZeroWithoutHorizontalOverlapOrStrip()
        {
            Assert.Equal(0f, StripClearMath.Lift(10f, 0f, 30f, 214f, 40f, 1880f, 12f)); // left of the strip
            Assert.Equal(0f, StripClearMath.Lift(10f, 1890f, 1920f, 214f, 40f, 1880f, 12f)); // right of it
            Assert.Equal(0f, StripClearMath.Lift(10f, 700f, 1200f, 0f, 40f, 1880f, 12f)); // HUD hidden
        }

        [Fact]
        public void Lift_RecomputedFromHome_NoAccumulation()
        {
            // 1440 strip lifted the element 182 px; switching to 1080 measures from home, not from the lifted spot
            const float home = 120f;
            var lifted = StripClearMath.Lift(home, 700f, 1200f, 286f, 40f, 2520f, 16f);
            var measuredNow = home + lifted;
            var again = StripClearMath.Lift(StripClearMath.BottomAtHome(measuredNow, lifted), 700f, 1200f, 286f, 40f, 2520f, 16f);
            Assert.Equal(lifted, again, 3);
            var smaller = StripClearMath.Lift(StripClearMath.BottomAtHome(measuredNow, lifted), 700f, 1200f, 150f, 40f, 2520f, 8f);
            Assert.Equal(38f, smaller, 3);
        }

        [Fact]
        public void Lift_NegativeGapCountsAsZero()
        {
            Assert.Equal(100f, StripClearMath.Lift(100f, 0f, 10f, 200f, 0f, 100f, -5f), 3);
        }

        [Fact]
        public void Units_ConvertThroughParentScale()
        {
            Assert.Equal(50f, StripClearMath.Units(100f, 2f), 3);
            Assert.Equal(0f, StripClearMath.Units(100f, 0f));
        }

        [Fact]
        public void SiblingBelow_StripMovesUnderEveryElement()
        {
            Assert.Equal(3, StripClearMath.SiblingBelow(9, new[] { 5, 3, 7 }));
            Assert.Equal(-1, StripClearMath.SiblingBelow(2, new[] { 5, 3, 7 }));
            Assert.Equal(-1, StripClearMath.SiblingBelow(4, new int[0]));
            Assert.Equal(-1, StripClearMath.SiblingBelow(4, new[] { -1 }));
        }
    }
}
