using System.Text;
using RuneSkin.InfoScreens;
using Xunit;

namespace RuneSkin.Tests
{
    // Stat breakdown model + the vanilla skill steps it attributes (decompiled 1.0.20 values).
    public class StatBreakdownTests
    {
        [Fact]
        public void SkillRangeMatchesGetRandomSkillFactor()
        {
            // Skills.GetRandomSkillFactor :188-195: Lerp(0.4, 1, skill) +-0.15, clamped to 0..1.
            StatsMath.SkillRange(0f, out var min, out var max);
            Assert.Equal(0.25f, min, 3);
            Assert.Equal(0.55f, max, 3);
            StatsMath.SkillRange(0.5f, out min, out max);
            Assert.Equal(0.55f, min, 3);
            Assert.Equal(0.85f, max, 3);
            StatsMath.SkillRange(1f, out min, out max);
            Assert.Equal(0.85f, min, 3);
            Assert.Equal(1f, max, 3);
        }

        [Fact]
        public void StaminaSkillStepsMatchVanilla()
        {
            // Attack.GetAttackStamina :481: -33% x skill (a 20 stamina swing at skill 100 costs 13.4).
            Assert.Equal(6.6f, StatsMath.AttackSkillCut(20f, 1f), 3);
            Assert.Equal(0f, StatsMath.AttackSkillCut(20f, 0f), 3);
            // Player.CheckRun / GetDodgeStaminaUse: Lerp(1, 0.5, skill).
            Assert.Equal(1f, StatsMath.RunDrainSkill(0f), 3);
            Assert.Equal(0.75f, StatsMath.RunDrainSkill(0.5f), 3);
            Assert.Equal(0.5f, StatsMath.RunDrainSkill(1f), 3);
            // Player.OnSwimming: Lerp(m_swimStaminaDrainMinSkill 5, m_swimStaminaDrainMaxSkill 2, skill).
            Assert.Equal(5f, StatsMath.SwimDrain(5f, 2f, 0f), 3);
            Assert.Equal(3.5f, StatsMath.SwimDrain(5f, 2f, 0.5f), 3);
            Assert.Equal(2f, StatsMath.SwimDrain(5f, 2f, 1f), 3);
        }

        [Fact]
        public void FactorKeepsTwoDecimalsTrimmed()
        {
            Assert.Equal("1,25", StatsMath.AppendFactor(new StringBuilder(), 1.25f, true).ToString());
            Assert.Equal("0.5", StatsMath.AppendFactor(new StringBuilder(), 0.5f, false).ToString());
            Assert.Equal("2", StatsMath.AppendFactor(new StringBuilder(), 2f, true).ToString());
            Assert.Equal("0,85", StatsMath.AppendFactor(new StringBuilder(), 0.85f, true).ToString());
        }

        [Fact]
        public void BreakdownSkipsNoOpsAndRendersParts()
        {
            var bd = new StatBreakdown();
            bd.Clear("Урон");
            bd.Base("Оружие", 52f);
            bd.Add("Уровень предмета", null, 12f, true);
            bd.Add("Ничего", null, 0.01f, true); // below epsilon: skipped
            bd.Mul("Ничего", null, 1.001f, true); // no-op factor: skipped
            bd.Range("Навык Мечи 50", null, 0.55f, 0.85f);
            bd.Mul("Медовуха", null, 1.1f, true);
            bd.ResultRange("За удар", 38.7f, 59.8f);
            Assert.Equal(5, bd.Count);
            Assert.Equal(12f, bd.Sum(StatBreakdown.Op.Add), 3);
            var sb = new StringBuilder();
            bd.Render(sb, true, "00FF00", "FF0000", "AAAAAA");
            var text = sb.ToString();
            Assert.Contains("Оружие:  52", text);
            Assert.Contains("<color=#00FF00>+12</color>", text);
            Assert.Contains("×0,55–0,85", text);
            Assert.Contains("<color=#00FF00>×1,1</color>", text);
            Assert.Contains("<b>За удар:  38,7–59,8</b>", text);
            Assert.DoesNotContain("Ничего", text);
        }

        [Fact]
        public void CostsColourLowerAsBetter()
        {
            var bd = new StatBreakdown();
            bd.Clear("Цена удара");
            bd.Base("База", 20f);
            bd.Add("Навык", null, -6.6f, false);
            bd.Add("Броня", null, 2f, false);
            var sb = new StringBuilder();
            bd.Render(sb, true, "00FF00", "FF0000", "AAAAAA");
            Assert.Contains("<color=#00FF00>−6,6</color>", sb.ToString());
            Assert.Contains("<color=#FF0000>+2</color>", sb.ToString());
        }

        [Fact]
        public void PoolIsBounded()
        {
            var bd = new StatBreakdown();
            bd.Clear("x");
            for (var i = 0; i < StatBreakdown.Capacity + 5; i++) bd.Add("a", null, 1f, true);
            Assert.Equal(StatBreakdown.Capacity, bd.Count);
            Assert.True(bd.Overflow);
        }
    }
}
