using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    public class HudCenteringTests
    {
        // Bar values: the box spans the window (or the unlocked part), so its centre is the window centre.
        [Theory]
        [InlineData(300f, 200f, false, 300f)]
        [InlineData(300f, 200f, true, 200f)]
        [InlineData(300f, 400f, true, 300f)]
        [InlineData(-1f, 0f, false, 0f)]
        public void ValueSpanCoversTheWindow(float l, float rs, bool reserve, float want) => Assert.Equal(want, HudMath.ValueSpan(l, rs, reserve), 3);

        // A status icon centred in its cell: icon rect centre == cell centre on both axes (pivot / anchor centre,
        // offset 0), side = cell x share; the timer band takes the free space under it.
        [Theory]
        [InlineData(62f, 0.6f)]
        [InlineData(42f, 0.6f)]
        [InlineData(80f, 0.5f)]
        public void IconCentreIsCellCentre(float cell, float share)
        {
            var side = HudMath.IconSide(cell, share);
            var iconMin = (cell - side) * 0.5f;
            Assert.Equal(cell * 0.5f, iconMin + side * 0.5f, 3);
            Assert.Equal(cell - iconMin - side, iconMin, 3); // same margin above and below
            Assert.True(HudMath.TimerBand(cell, share, 0f) <= iconMin + 0.001f);
        }

        // Fill-end rim: sprite aspect at the window height, whole px, never thinner than 2.
        [Theory]
        [InlineData(6f, 48f, 21f, 3f)]
        [InlineData(6f, 48f, 8f, 2f)]
        [InlineData(0f, 48f, 21f, 0f)]
        public void EdgeWidthFollowsTheSprite(float sw, float sh, float h, float want) => Assert.Equal(want, HudMath.EdgeWidth(sw, sh, h), 3);

        [Theory]
        [InlineData("color.hud.health", "color.hud.edge.health")]
        [InlineData("color.hud.shield", "color.hud.edge.shield")]
        [InlineData("color.other", "color.hud.edge")]
        [InlineData(null, "color.hud.edge")]
        public void EdgeColourPerBar(string fill, string want) => Assert.Equal(want, HudMath.EdgeColorToken(fill));

        [Fact]
        public void TimerBandKeepsItsMinimum() => Assert.Equal(17f, HudMath.TimerBand(62f, 0.6f, 17f), 3);
    }
}
