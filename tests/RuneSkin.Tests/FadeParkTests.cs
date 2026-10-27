using RuneSkin.Compat;
using Xunit;

namespace RuneSkin.Tests
{
    // FadeParkMath: another mod's hidden subtree waits outside the inventory's fade group and is home whenever shown.
    public class FadeParkTests
    {
        [Fact]
        public void HiddenAtHomeInsideFadeGroupParks() =>
            Assert.Equal(FadeParkMove.Park, FadeParkMath.Decide(shown: false, home: true, hostInFade: true));

        [Fact]
        public void ShownWhileParkedGoesHome() =>
            Assert.Equal(FadeParkMove.Home, FadeParkMath.Decide(shown: true, home: false, hostInFade: true));

        [Fact]
        public void HiddenAndParkedStays() =>
            Assert.Equal(FadeParkMove.None, FadeParkMath.Decide(shown: false, home: false, hostInFade: true));

        [Fact]
        public void ShownAtHomeStays() =>
            Assert.Equal(FadeParkMove.None, FadeParkMath.Decide(shown: true, home: true, hostInFade: true));

        [Fact]
        public void VneiTabUpOnlyWhileItsBrowserShows()
        {
            Assert.False(RuneSkin.CraftingScreen.CraftingMath.VneiUp(panelActive: true, browserKnown: true, browserShown: false)); // craft tab
            Assert.True(RuneSkin.CraftingScreen.CraftingMath.VneiUp(panelActive: true, browserKnown: true, browserShown: true));
            Assert.False(RuneSkin.CraftingScreen.CraftingMath.VneiUp(panelActive: false, browserKnown: true, browserShown: true));
            Assert.True(RuneSkin.CraftingScreen.CraftingMath.VneiUp(panelActive: true, browserKnown: false, browserShown: false));
        }

        [Fact]
        public void HostOutsideTheFadeGroupNeverParksAndTakesItsSubtreeBack()
        {
            Assert.Equal(FadeParkMove.None, FadeParkMath.Decide(shown: false, home: true, hostInFade: false));
            Assert.Equal(FadeParkMove.Home, FadeParkMath.Decide(shown: false, home: false, hostInFade: false));
        }
    }
}
