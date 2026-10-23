using System.IO;
using System.Linq;
using System.Text;
using RuneSkin.API;
using RuneSkin.InfoScreens;
using RuneSkin.Theme;
using Xunit;

namespace RuneSkin.Tests
{
    // Character stats window (batch P, stats-full-b): regeneration per second, formatting without allocation helpers,
    // design-frame mapping, stacking / scroll, theme tokens of the family.
    public class StatsMathTests
    {
        private static ThemeFile Default()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("RuneSkin.default-theme.json");
            using var r = new StreamReader(s!);
            return ThemeJson.Parse(r.ReadToEnd());
        }

        [Fact]
        public void HealthRegenIsTickHealOverTenSeconds()
        {
            // Three dishes regen 2 + 2 + 3 = 7 per 10 s tick (Player.UpdateFood), no multiplier -> 0.7 / s.
            Assert.Equal(0.7f, StatsMath.HealthRegen(7f, 1f), 3);
            // Status-effect multiplier (SEMan.ModifyHealthRegen) scales the tick.
            Assert.Equal(1.05f, StatsMath.HealthRegen(7f, 1.5f), 3);
            Assert.Equal(0f, StatsMath.HealthRegen(0f, 2f));
            Assert.Equal(6f, StatsMath.HealthTickIn(4f), 3);
            Assert.Equal(10f, StatsMath.HealthTickIn(-1f), 3);
            Assert.Equal(0f, StatsMath.HealthTickIn(12f), 3);
        }

        [Fact]
        public void StaminaRegenDoublesWhenEmpty()
        {
            // Defaults m_staminaRegen 5, time multiplier 1, world rate 1: full = 5 / s, empty = 10 / s.
            Assert.Equal(5f, StatsMath.StaminaRegen(5f, 1f, 1f, 1f, 1f, 0f, 1f), 3);
            Assert.Equal(10f, StatsMath.StaminaRegen(5f, 1f, 0f, 1f, 1f, 0f, 1f), 3);
            // Blocking 0.8, multiplier 1.5, flat +2 (same factor, not multiplied), world rate 2.
            Assert.Equal((5f * 0.8f * 1.5f + 2f * 0.8f) * 2f, StatsMath.StaminaRegen(5f, 1f, 1f, 0.8f, 1.5f, 2f, 2f), 3);
            Assert.Equal(0f, StatsMath.StaminaRegen(5f, 1f, 0.5f, 0f, 1f, 2f, 1f));
        }

        [Fact]
        public void EitrRegenHasNoWorldRate()
        {
            Assert.Equal(5f, StatsMath.EitrRegen(5f, 1f, 1f, 1f, 0f), 3);
            Assert.Equal(7.5f, StatsMath.EitrRegen(5f, 0.5f, 1f, 1f, 0f), 3);
            Assert.Equal(5f * 1.2f + 1f, StatsMath.EitrRegen(5f, 1f, 1f, 1.2f, 1f), 3);
        }

        [Fact]
        public void FormattingUsesDecimalCommaAndTypographicMinus()
        {
            var sb = new StringBuilder();
            Assert.Equal("+2,4", StatsMath.AppendDecimal(sb, 2.44f, true, true).ToString());
            sb.Clear();
            Assert.Equal("15.0", StatsMath.AppendDecimal(sb, 15f, false, false).ToString());
            sb.Clear();
            Assert.Equal("−10", StatsMath.AppendInt(sb, -10.2f, true).ToString());
            sb.Clear();
            Assert.Equal("+12", StatsMath.AppendInt(sb, 12f, true).ToString());
            sb.Clear();
            Assert.Equal("0", StatsMath.AppendInt(sb, 0.2f, true).ToString());
            sb.Clear();
            Assert.Equal("1,5", StatsMath.AppendCompact(sb, 1.5f, true).ToString());
            sb.Clear();
            Assert.Equal("3", StatsMath.AppendCompact(sb, 3f, true).ToString());
            sb.Clear();
            Assert.Equal("16:15", StatsMath.AppendClock(sb, 975f).ToString());
            sb.Clear();
            Assert.Equal("0:05", StatsMath.AppendClock(sb, 4.2f).ToString());
            sb.Clear();
            Assert.Equal("1:02:03", StatsMath.AppendClock(sb, 3723f).ToString());
            sb.Clear();
            Assert.Equal("1209", StatsMath.Digits(sb, 1209).ToString());
        }

        [Fact]
        public void SemanticFlipsForCosts()
        {
            Assert.Equal(1, StatsMath.Semantic(12f, true));
            Assert.Equal(-1, StatsMath.Semantic(-5f, true));
            Assert.Equal(1, StatsMath.Semantic(-10f, false));
            Assert.Equal(0, StatsMath.Semantic(0.3f, true));
            Assert.Equal(1, StatsMath.ReductionSemantic(0.69f, 0.39f));
            Assert.Equal(-1, StatsMath.ReductionSemantic(0.2f, 0.39f));
            Assert.Equal(0, StatsMath.ReductionSemantic(0.39f, 0.39f));
            var sb = new StringBuilder();
            StatsMath.Open(sb, 1, "7FD36B", "F0963A").Append("+8%");
            Assert.Equal("<color=#7FD36B>+8%</color>", StatsMath.Close(sb, 1).ToString());
        }

        [Fact]
        public void DesignFrameMapsCentredAndUniform()
        {
            Assert.Equal(1f, StatsMath.FitScale(1920f, 1080f), 4);
            Assert.Equal(4f / 3f, StatsMath.FitScale(2560f, 1440f), 4);
            // Ultrawide 2560x1080: height-bound, frame centred horizontally.
            var s = StatsMath.FitScale(2560f, 1080f);
            Assert.Equal(1f, s, 4);
            var b = StatsMath.Map(2560f, 1080f, s, 0f, 0f, 1920f, 1080f);
            Assert.Equal(320f, b.X, 3);
            Assert.Equal(0f, b.Y, 3);
            var card = StatsMath.Card(3, 1, 68f, 134f, 439f, 420f, 448.3f, 434.5f);
            Assert.Equal(12, Skin.ApiVersion);
            Assert.True(card.Right <= 1852f + 1f && card.Bottom <= 990f, card.ToString());
        }

        [Fact]
        public void StackAndScroll()
        {
            var heights = new[] { 32f, 28f, 50f };
            var tops = new float[3];
            Assert.Equal(110f, StatsMath.Stack(heights, 3, 10f, tops), 3);
            Assert.Equal(new[] { 10f, 42f, 70f }, tops);
            Assert.Equal(0f, StatsMath.ClampScroll(50f, 100f, 120f));
            Assert.Equal(80f, StatsMath.ClampScroll(500f, 400f, 320f));
            Assert.Equal(0f, StatsMath.ClampScroll(-5f, 400f, 320f));
        }

        [Fact]
        public void ThemeShipsTheStatsFamily()
        {
            var t = Default();
            Assert.Empty(ThemeSchema.Validate(t));
            Assert.Contains(t.families, f => f.family == SkinFamilies.CharacterStats);
            foreach (var token in t.families.Single(f => f.family == SkinFamilies.CharacterStats).requires.Where(r => r.StartsWith("sprite.", System.StringComparison.Ordinal)))
                Assert.Contains(t.sprites, s => s.token == token);
            foreach (var role in new[] { "stats.chrome.tl", "stats.chrome.br", "stats.divider", "stats.hit", "stats.icon", "stats.title", "stats.header", "stats.label", "stats.value", "stats.sub" })
                Assert.Contains(t.roles, r => r.role == role);
            // Every card / row icon the reader names has a sprite token.
            foreach (var icon in new[] { "res", "resist", "move", "defense", "food", "rest", "combat", "mt" })
                Assert.Contains(t.sprites, s => s.token == "sprite.stats.head." + icon);
            foreach (var icon in new[] { "heart", "pulse", "bolt", "bolt2", "drop", "pulse2", "blunt", "slash", "pierce", "fire", "frost", "lightning", "poison", "spirit", "chop", "pickaxe",
                         "walk", "run", "swim", "weight", "rundrain", "dodge", "jump", "armor", "block", "parry", "fork", "flask", "hourglass", "bed", "clock",
                         "sword", "flame", "attackspeed", "backstab", "double", "lifesteal", "stagger", "boots", "ring", "es", "recharge", "breakdelay", "potion", "food", "potency" })
                Assert.Contains(t.sprites, s => s.token == "sprite.stats.icon." + icon);
            Assert.Contains(t.floats, f => f.token == "size.stats.card.px");
        }
    }
}
