using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    // Reserve tooltip of the strip bars (HudStrip.ReserveTooltip) and its stats-window twin: shown only when reserved.
    public class ReserveTipTests
    {
        [Theory]
        [InlineData(0f, 0f, false)]
        [InlineData(0.2f, 0.2f, false)]
        [InlineData(0.3f, 0.2f, true)]
        [InlineData(40f, 0f, true)]
        [InlineData(0f, 25f, true)]
        [InlineData(-5f, 0.4f, false)]
        [InlineData(float.NaN, 30f, false)]
        public void ShownOnlyWhenSomethingIsReserved(float auras, float summons, bool shown) =>
            Assert.Equal(shown, HudMath.ReserveShown(auras, summons));

        [Fact]
        public void TextListsAvailableAndEachNonZeroPart()
        {
            Assert.Equal("Available: 120 / 200\nAuras: 50\nSummons: 30",
                HudMath.ReserveText(120f, 200f, 50f, 30f, "Available", "Auras", "Summons"));
            Assert.Equal("Available: 150 / 200\nSummons: 50",
                HudMath.ReserveText(149.6f, 200.2f, 0.2f, 50f, "Available", "Auras", "Summons"));
        }
    }
}
