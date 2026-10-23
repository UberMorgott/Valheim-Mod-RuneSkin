using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RuneSkin.Theme;
using Xunit;

namespace RuneSkin.Tests
{
    // 0.15.1: enemy bars sized to the approved mockup's on-screen proportion, normal tier without the emblem socket,
    // boss-c weld cut for the world boss bar.
    public class BarProportionTests
    {
        private static ThemeFile Default()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("RuneSkin.default-theme.json");
            using var r = new StreamReader(s!);
            return ThemeJson.Parse(r.ReadToEnd());
        }

        private static float Float(ThemeFile t, string token) => t.floats.Single(f => f.token == token).value;

        // enemy-b.png (1672 x 941 = a 16:9 screen), the 1x NORMAL thumbnail: window 131 x 13 px.
        // Canvas units at any 16:9 resolution = 1920 x 1080 (GuiScaler), so the token must be the same screen share.
        [Fact]
        public void EnemyBarMatchesTheMockupScreenShare()
        {
            var t = Default();
            Assert.InRange(Float(t, "size.enemy.bar.w") / 1920f, 131f / 1672f - 0.003f, 131f / 1672f + 0.003f);
            Assert.InRange(Float(t, "size.enemy.bar.h") / 1080f, 13f / 941f - 0.001f, 13f / 941f + 0.001f);
        }

        // 0.18.2: the overhead enemy name reads on bright sky / foliage: near-white like the player bar values, the same
        // dilate as size.hud.value.dilate (outline 0.22 alone ate the regular face), at least 16 canvas px.
        [Fact]
        public void EnemyNameIsLegible()
        {
            var t = Default();
            var hex = t.colors.Single(c => c.token == "color.enemy.name").value;
            Assert.Equal(t.colors.Single(c => c.token == "color.hud.text").value, hex);
            Assert.Equal(Float(t, "size.hud.value.dilate"), Float(t, "size.enemy.name.dilate"));
            Assert.True(Float(t, "size.enemy.name.font") >= 16f);
            Assert.Contains(t.fonts, f => f.token == "font.body.bold");
        }

        [Theory]
        [InlineData(true, "HudBaseBoss(Clone)", true)]
        [InlineData(false, "HudBase(Clone)", false)]   // a normal mob
        [InlineData(false, "HudBaseBoss(Clone)", false)] // flag lowered (Vaettir: a second boss drawn as a mob)
        [InlineData(true, "HudBase(Clone)", false)]    // flagged but built from the mob template
        [InlineData(true, "VT_TargetPanel", false)]    // Vaettir's target panel (a copy of the boss template)
        public void OnlyRealBossesGetTheBossBar(bool isBoss, string gui, bool want) =>
            Assert.Equal(want, RuneSkin.WorldHud.EnemyHudMath.BossBar(isBoss, gui, "HudBaseBoss"));

        [Fact]
        public void AlertIconIsStackedAboveTheName()
        {
            // icon 12 tall, gap 3, name text top at 20: icon centre 29 -> its bottom (23) clears the text top by the gap
            var y = RuneSkin.WorldHud.EnemyHudMath.AlertAboveY(20f, 12f, 3f);
            Assert.Equal(29f, y, 3);
            Assert.Equal(3f, (y - 6f) - 20f, 3);
        }

        [Fact]
        public void BossBarSpanAndScreenFit()
        {
            Assert.Equal(800f, RuneSkin.WorldHud.EnemyHudMath.BossSpan(600f, 110f, 10f), 3);
            var fit = RuneSkin.WorldHud.EnemyHudMath.BossScreenFit(800f, 1920f, 1080f, 0.64f);
            Assert.Equal(0.64f, 800f * fit / 1920f, 3);
            // Ultrawide 3440x1440 -> canvas 2580x1080: the 16:9 box (1920) sets the size.
            Assert.Equal(0.64f * 1920f, 800f * RuneSkin.WorldHud.EnemyHudMath.BossScreenFit(800f, 2580f, 1080f, 0.64f), 1);
            Assert.Equal(1f, RuneSkin.WorldHud.EnemyHudMath.BossScreenFit(800f, 0f, 1080f, 0.64f));
        }

        [Fact]
        public void SparkFramesCycleAndCrossFade()
        {
            Assert.Equal(0, RuneSkin.WorldHud.EnemyHudMath.SheetFrame(0f, 10f, 4, out var b0));
            Assert.Equal(0f, b0);
            Assert.Equal(1, RuneSkin.WorldHud.EnemyHudMath.SheetFrame(0.15f, 10f, 4, out var b1));
            Assert.Equal(0.5f, b1, 3);
            Assert.Equal(0, RuneSkin.WorldHud.EnemyHudMath.SheetFrame(0.4f, 10f, 4, out _));
            Assert.Equal(0, RuneSkin.WorldHud.EnemyHudMath.SheetFrame(5f, 0f, 4, out _));
            // enemy-b: unique throws the most sparks, normal the fewest.
            Assert.True(RuneSkin.WorldHud.EnemyHudMath.SparkAlpha(3) > RuneSkin.WorldHud.EnemyHudMath.SparkAlpha(2));
            Assert.True(RuneSkin.WorldHud.EnemyHudMath.SparkAlpha(1) > RuneSkin.WorldHud.EnemyHudMath.SparkAlpha(0));
            Assert.Equal(3, RuneSkin.WorldHud.EnemyHudMath.TierIndex("sprite.frame.enemy.unique"));
            Assert.Equal(0, RuneSkin.WorldHud.EnemyHudMath.TierIndex("sprite.frame.enemy.normal"));
        }

        [Fact]
        public void EffectFramesAreThemed()
        {
            var t = Default();
            for (var i = 0; i < 4; i++)
            {
                Assert.Equal("fx_stagger_" + i, t.sprites.Single(s => s.token == "sprite.fx.stagger." + i).asset);
                Assert.Equal("fx_gas_" + i, t.sprites.Single(s => s.token == "sprite.fx.gas." + i).asset);
            }
            Assert.Equal("bar_boss_fill_c", t.sprites.Single(s => s.token == "sprite.bar.boss.fill.c").asset);
            Assert.Equal("bar_boss_trough_c", t.sprites.Single(s => s.token == "sprite.bar.boss.trough.c").asset);
        }

        [Fact]
        public void BossFlareIsThemed()
        {
            Assert.Equal("boss_flare", Default().sprites.Single(s => s.token == "sprite.boss.flare").asset);
        }

        // The normal frame has no emblem socket: symmetric end borders, so the bar centres over the mob.
        [Fact]
        public void NormalFrameHasNoSocket()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "art", "sprites", "sprites.json"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var json = File.ReadAllText(Path.Combine(dir!.FullName, "art", "sprites", "sprites.json"));
            var m = Regex.Match(json, "\"name\":\\s*\"frame_enemy_normal\"[\\s\\S]*?\"border\":\\s*\\{([^}]*)\\}");
            Assert.True(m.Success);
            int Side(string k) => int.Parse(Regex.Match(m.Groups[1].Value, "\"" + k + "\":\\s*(\\d+)").Groups[1].Value);
            Assert.Equal(Side("left"), Side("right"));
            var normalLeft = Side("left");
            var magic = Regex.Match(json, "\"name\":\\s*\"frame_enemy_magic\"[\\s\\S]*?\"left\":\\s*(\\d+)");
            Assert.True(int.Parse(magic.Groups[1].Value) > 2 * normalLeft, "magic keeps its emblem socket (left border well over the plain cap)");
        }
    }
}
