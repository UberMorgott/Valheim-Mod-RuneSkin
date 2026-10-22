using RuneSkin.PreWorld;
using Xunit;

namespace RuneSkin.Tests
{
    // M5 pre-world helpers.
    public class PreWorldTests
    {
        [Theory]
        [InlineData("woodpanel_settings", true)]
        [InlineData("woodpanel_trophys", true)]
        [InlineData("panel_bkg_128", true)]
        [InlineData("panel_interior_bkg_128", true)]
        [InlineData("button", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void PanelSprites(string sprite, bool expected) => Assert.Equal(expected, PreWorldMath.IsPanelSprite(sprite));

        [Theory]
        [InlineData("morgott", "M")]
        [InlineData("  эйрик", "Э")]
        [InlineData("", "?")]
        [InlineData("   ", "?")]
        [InlineData(null, "?")]
        public void Initials(string name, string expected) => Assert.Equal(expected, PreWorldMath.Initial(name));

        [Fact]
        public void TabColumnSteps()
        {
            Assert.Equal(0f, PreWorldMath.TabOffset(0, 52f, 8f));
            Assert.Equal(120f, PreWorldMath.TabOffset(2, 52f, 8f));
        }
    }
}
