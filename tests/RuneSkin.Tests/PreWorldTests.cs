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

        // M5 polish roles: lit menu entries, primary (Start / Ok) plates with gold labels, danger-red confirm label.
        [Theory]
        [InlineData("button.menu", "sprite.button.hover", null)]
        [InlineData("button.primary", "sprite.button.selected", null)]
        [InlineData("button.label.primary", null, "color.text.item.title")]
        [InlineData("button.label.danger", null, "color.text.missing")]
        public void PolishRoles(string role, string sprite, string textColor)
        {
            using var stream = typeof(RuneSkin.Theme.ThemeSchema).Assembly.GetManifestResourceStream("RuneSkin.default-theme.json");
            Assert.NotNull(stream);
            using var reader = new System.IO.StreamReader(stream);
            var theme = RuneSkin.Theme.ThemeJson.Parse(reader.ReadToEnd());
            var rule = System.Array.Find(theme.roles, r => r.role == role);
            Assert.NotNull(rule);
            if (sprite != null) Assert.Equal(sprite, rule.sprite);
            if (textColor != null) Assert.Equal(textColor, rule.textColor);
        }

        [Fact]
        public void TabColumnSteps()
        {
            Assert.Equal(0f, PreWorldMath.TabOffset(0, 52f, 8f));
            Assert.Equal(120f, PreWorldMath.TabOffset(2, 52f, 8f));
        }
    }
}
