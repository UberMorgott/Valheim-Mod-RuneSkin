using System.IO;
using System.Linq;
using RuneSkin.API;
using RuneSkin.PlayerHud;
using RuneSkin.Theme;
using Xunit;

namespace RuneSkin.Tests
{
    // Batch P: legacy faces, summon frames maths, ported families in the theme.
    public class BatchPTests
    {
        private static ThemeFile Default()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("RuneSkin.default-theme.json");
            using var r = new StreamReader(s!);
            return ThemeJson.Parse(r.ReadToEnd());
        }

        [Fact]
        public void BoldBodyTakesTheDisplayFace()
        {
            Assert.Equal("font.display", LegacyFaces.FaceToken("font.body", true));
            Assert.Equal("font.body", LegacyFaces.FaceToken("font.body", false));
            Assert.Equal("font.rune", LegacyFaces.FaceToken("font.rune", true));
            Assert.Equal("font.display", LegacyFaces.FaceToken("font.display", true));
        }

        [Fact]
        public void DefaultThemeShipsLegacyFacesAndCursor()
        {
            var t = Default();
            Assert.Empty(ThemeSchema.Validate(t));
            Assert.Equal("PT_Sans-Web-Regular", t.fonts.Single(f => f.token == "font.body").legacy);
            Assert.Equal("Cormorant-Bold", t.fonts.Single(f => f.token == "font.display").legacy);
            Assert.Null(t.fonts.Single(f => f.token == "font.rune").legacy);
            Assert.Equal("cursor", t.textures.Single(x => x.token == "texture.cursor").asset);
            foreach (var role in new[] { "legacy.body", "legacy.label", "legacy.muted", "text.legacy" })
                Assert.Equal("font.body", t.roles.Single(r => r.role == role).font);
        }

        [Fact]
        public void LegacyFaceOnALoadedFontIsRejected()
        {
            var t = Default();
            t.fonts.Single(f => f.token == "font.rune").legacy = "X";
            Assert.Contains(ThemeSchema.Validate(t), e => e.Contains("legacy face only for bundle fonts"));
            t = Default();
            t.textures = new[] { new AssetEntry { token = "texture.cursor", asset = "" } };
            Assert.Contains(ThemeSchema.Validate(t), e => e.Contains("texture 'texture.cursor': asset missing"));
        }

        [Fact]
        public void PortedFamiliesAreDeclared()
        {
            var families = Default().families.Select(f => f.family).ToList();
            foreach (var f in new[] { SkinFamilies.MenuCompendium, SkinFamilies.SummonFrames, SkinFamilies.PlayerShields, SkinFamilies.Cursor })
                Assert.Contains(f, families);
            Assert.Equal(9, Skin.ApiVersion);
        }

        [Theory]
        [InlineData(SummonState.Spawning, false, "runeskin_summon_spawning", -1)]
        [InlineData(SummonState.Alive, false, null, -1)]
        [InlineData(SummonState.Alive, true, "runeskin_summon_stay", -1)]
        [InlineData(SummonState.Dead, false, "runeskin_summon_respawn", 13)]
        [InlineData(SummonState.Off, true, "runeskin_summon_off", -1)]
        [InlineData(SummonState.NoReserve, false, "runeskin_summon_noreserve", -1)]
        [InlineData(SummonState.Waiting, false, "runeskin_summon_waiting", -1)]
        public void StatusLine(SummonState state, bool stay, string key, int seconds)
        {
            Assert.Equal(key, SummonMath.StatusKey(state, stay, 112.2, 100.0, out var s));
            Assert.Equal(seconds, s);
        }

        [Fact]
        public void RespawnNeverNegative()
        {
            SummonMath.StatusKey(SummonState.Dead, false, 90.0, 100.0, out var s);
            Assert.Equal(0, s);
        }

        [Theory]
        [InlineData(5f, 10f, 0.5f)]
        [InlineData(12f, 10f, 1f)]
        [InlineData(-1f, 10f, 0f)]
        [InlineData(5f, 0f, 0f)]
        [InlineData(float.NaN, 10f, 0f)]
        public void RatioClamped(float v, float max, float want) => Assert.Equal(want, SummonMath.Ratio(v, max), 3);

        [Fact]
        public void RowSideFollowsTheHotbar()
        {
            Assert.True(SummonMath.Below(1000f, 1080f));  // vanilla top-left hotbar: row under it
            Assert.False(SummonMath.Below(60f, 1080f));   // hotbar in a bottom strip: row on top of it
            Assert.Equal(100f, SummonMath.FrameX(2, 44f, 6f));
            Assert.True(SummonMath.Active(SummonState.Spawning));
            Assert.False(SummonMath.Active(SummonState.Dead));
        }
    }
}
