using System.Collections.Generic;
using UnityEngine;

namespace RuneSkin.InfoScreens
{
    // Which composition a hovered row shows (StatsWindow.Row.Explain; Arg = damage type index / attack index).
    internal enum Explain
    {
        None, Health, Stamina, Eitr, HealthRegen, StaminaRegen, EitrRegen, Resist, Jog, Run, Swim, Weight, RunDrain, Dodge,
        Jump, SwimDrain, Armor, Block, Parry, BlockStamina, Damage, Dps, AttackStamina,
    }

    // The breakdown of each stat (StatBreakdown): the same vanilla formulas the rows use (decompiled 1.0.20, V =
    // assembly_valheim), split into attributed parts. Status effects are asked one by one through their own virtual
    // Modify* (so any mod's status effect counts with its name); whatever the game's total differs from the attributed
    // parts by (Epic Loot postfixes on SEMan / ItemData, other mods' patches) is one "enchantments and other" line, so the
    // lines always add up to the value the row shows. Built only for the hovered row.
    internal sealed partial class StatsReader
    {
        private HitData _hit = new HitData();
        private readonly List<StatusEffect> _effects = new List<StatusEffect>(32);

        public void ExplainInto(Player p, Explain id, int arg, StatBreakdown bd)
        {
            _p = p;
            switch (id)
            {
                case InfoScreens.Explain.Health: Pool(bd, 0); break;
                case InfoScreens.Explain.Stamina: Pool(bd, 1); break;
                case InfoScreens.Explain.Eitr: Pool(bd, 2); break;
                case InfoScreens.Explain.HealthRegen: HealthRegen(bd); break;
                case InfoScreens.Explain.StaminaRegen: StaminaRegen(bd); break;
                case InfoScreens.Explain.EitrRegen: EitrRegen(bd); break;
                case InfoScreens.Explain.Resist: Resist(bd, arg); break;
                case InfoScreens.Explain.Jog: Speed(bd, false); break;
                case InfoScreens.Explain.Run: Speed(bd, true); break;
                case InfoScreens.Explain.Swim: SwimSpeed(bd); break;
                case InfoScreens.Explain.Weight: Weight(bd); break;
                case InfoScreens.Explain.RunDrain: RunDrain(bd); break;
                case InfoScreens.Explain.Dodge: Dodge(bd); break;
                case InfoScreens.Explain.Jump: Jump(bd); break;
                case InfoScreens.Explain.SwimDrain: SwimDrain(bd); break;
                case InfoScreens.Explain.Armor: Armor(bd); break;
                case InfoScreens.Explain.Block: Block(bd, false); break;
                case InfoScreens.Explain.Parry: Block(bd, true); break;
                case InfoScreens.Explain.BlockStamina: BlockStamina(bd); break;
                case InfoScreens.Explain.Damage: Damage(bd); break;
                case InfoScreens.Explain.Dps: Dps(bd); break;
                case InfoScreens.Explain.AttackStamina: AttackStamina(bd, arg); break;
                default: bd.Clear(""); break;
            }
        }

        private string Total => Loc("$runeskin_bd_total");
        private string Other => Loc("$runeskin_bd_other");
        private string PerSecond => Loc("$runeskin_stats_per_second");

        private List<StatusEffect> Effects()
        {
            _effects.Clear();
            _effects.AddRange(_p.GetSEMan().GetStatusEffects());
            return _effects;
        }

        // Equipped items (weapons, armor, utility).
        private IEnumerable<ItemDrop.ItemData> Worn()
        {
            var items = _p.GetInventory().GetAllItems();
            for (var i = 0; i < items.Count; i++)
                if (items[i] != null && items[i].m_equipped) yield return items[i];
        }

        // Residual between the game's value and the attributed sum: mods that patch the vanilla method.
        private void Residual(StatBreakdown bd, float game, float attributed, bool higherIsBetter, string unit = "", float epsilon = 0.05f) =>
            bd.Add(Other, null, game - attributed, higherIsBetter, unit, epsilon);

        // ---- max pools: V Player.GetTotalFoodValue :2470-2481 (base + foods), then GetMaxHealth / GetMaxStamina /
        // GetMaxEitr (mods add on top: Epic Loot, MorgottTweaks) ----

        private void Pool(StatBreakdown bd, int which)
        {
            var p = _p;
            bd.Clear(Loc(which == 0 ? "$runeskin_stats_health" : which == 1 ? "$runeskin_stats_stamina" : "$runeskin_stats_eitr"));
            var sum = which == 0 ? p.m_baseHP : which == 1 ? p.m_baseStamina : 0f;
            if (which < 2) bd.Base(Loc("$runeskin_bd_base"), sum);
            foreach (var f in p.m_foods)
            {
                var v = which == 0 ? f.m_health : which == 1 ? f.m_stamina : f.m_eitr;
                if (v < 0.05f) continue;
                bd.Add(Loc(f.m_item.m_shared.m_name), null, v, true);
                sum += v;
            }
            var game = which == 0 ? p.GetMaxHealth() : which == 1 ? p.GetMaxStamina() : p.GetMaxEitr();
            // Reservations (MorgottTweaks auras / summons through the resource providers) lower the game's max: own lines,
            // the same parts the HUD bar's reserve tooltip shows (Codex call 2026-10-28: gamepad reads them here).
            var pool = which == 0 ? API.ResourcePools.Health : which == 1 ? API.ResourcePools.Stamina : API.ResourcePools.Eitr;
            if (Api.Providers.TryResource(pool, p, false, out var s) && Mathf.Abs(s.Available - game) <= 0.5f + 0.01f * game &&
                PlayerHud.HudMath.ReserveShown(s.ReservedAuras, s.ReservedSummons))
            {
                bd.Add(Loc("$runeskin_reserve_auras"), null, -s.ReservedAuras, true);
                bd.Add(Loc("$runeskin_reserve_summons"), null, -s.ReservedSummons, true);
                sum -= s.ReservedAuras + s.ReservedSummons;
            }
            Residual(bd, game, sum, true);
            bd.Result(Total, game);
        }

        // ---- regeneration ----

        // V Player.UpdateFood :2449-2466: every 10 s, sum of the foods' m_foodRegen x SEMan.ModifyHealthRegen.
        private void HealthRegen(StatBreakdown bd)
        {
            var p = _p;
            bd.Clear(Loc("$runeskin_stats_health_regen"));
            var sum = 0f;
            foreach (var f in p.m_foods)
            {
                var v = f.m_item.m_shared.m_foodRegen;
                if (v < 0.05f) continue;
                bd.Add(Loc(f.m_item.m_shared.m_name), null, v / StatsMath.FoodTick, true, PerSecond, 0.005f);
                sum += v;
            }
            if (sum <= 0f)
            {
                bd.Note(Loc("$runeskin_bd_no_food_regen"));
                bd.Result(Total, 0f, PerSecond);
                return;
            }
            var el = StatsSources.El(p, "AddHealthRegen");
            bd.Add(Loc("$runeskin_bd_enchant"), null, el / StatsMath.FoodTick, true, PerSecond, 0.005f);
            sum += el;
            var mult = 1f;
            foreach (var se in Effects())
            {
                var m = 1f;
                se.ModifyHealthRegen(ref m);
                bd.Pct(EffectName(se), null, m - 1f, true);
                mult += m - 1f;
            }
            var game = 1f;
            p.GetSEMan().ModifyHealthRegen(ref game);
            bd.Pct(Other, null, game - mult, true);
            bd.Note(Loc("$runeskin_bd_tick_note"));
            bd.Result(Total, StatsMath.HealthRegen(sum, game), PerSecond);
        }

        // V Player.UpdateStats :2099-2117.
        private void StaminaRegen(StatBreakdown bd)
        {
            var p = _p;
            bd.Clear(Loc("$runeskin_stats_stamina_regen"));
            var max = p.GetMaxStamina();
            var fill = max > 0f ? p.GetStamina() / max : 1f;
            bd.Base(Loc("$runeskin_bd_base"), p.m_staminaRegen, PerSecond);
            bd.Add(Loc("$runeskin_bd_low_fill"), null, (1f - Mathf.Clamp01(fill)) * p.m_staminaRegen * p.m_staminaRegenTimeMultiplier, true, PerSecond);
            var factor = StaminaFactor(p);
            if (factor < 1f) bd.Mul(Loc(factor <= 0f ? "$runeskin_bd_busy" : "$runeskin_bd_blocking"), null, factor, true);
            var mult = 1f;
            foreach (var se in Effects())
            {
                var m = 1f;
                se.ModifyStaminaRegen(ref m);
                bd.Pct(EffectName(se), null, m - 1f, true);
                mult += m - 1f;
            }
            var game = 1f;
            p.GetSEMan().ModifyStaminaRegen(ref game);
            bd.Pct(Other, null, game - mult, true);
            var flat = StatsSources.ResourceAffixes ? StatsSources.El(p, "AddStaminaRegen") : 0f;
            bd.Add(Loc("$runeskin_bd_enchant"), null, flat * factor, true, PerSecond);
            bd.Mul(Loc("$runeskin_bd_world"), null, Game.m_staminaRegenRate, true);
            if (p.m_staminaRegenTimer > 0f) bd.Note(Loc("$runeskin_bd_regen_delay"));
            bd.Result(Total, StatsMath.StaminaRegen(p.m_staminaRegen, p.m_staminaRegenTimeMultiplier, fill, factor, game, flat, Game.m_staminaRegenRate), PerSecond);
        }

        // V Player.UpdateStats :2133-2152 (+ equipment eitr regen modifier, ItemData m_eitrRegenModifier).
        private void EitrRegen(StatBreakdown bd)
        {
            var p = _p;
            bd.Clear(Loc("$runeskin_stats_eitr_regen"));
            var max = p.GetMaxEitr();
            if (max <= 0f)
            {
                bd.Note(Loc("$runeskin_bd_no_eitr"));
                bd.Result(Total, 0f, PerSecond);
                return;
            }
            var fill = p.GetEitr() / max;
            bd.Base(Loc("$runeskin_bd_base"), p.m_eiterRegen, PerSecond);
            bd.Add(Loc("$runeskin_bd_low_fill"), null, (1f - Mathf.Clamp01(fill)) * p.m_eiterRegen, true, PerSecond);
            var factor = EitrFactor(p);
            if (factor < 1f) bd.Mul(Loc(factor <= 0f ? "$runeskin_bd_busy" : "$runeskin_bd_blocking"), null, factor, true);
            var mult = 1f;
            foreach (var se in Effects())
            {
                var m = 1f;
                se.ModifyEitrRegen(ref m);
                bd.Pct(EffectName(se), null, m - 1f, true);
                mult += m - 1f;
            }
            foreach (var it in Worn())
            {
                bd.Pct(Loc(it.m_shared.m_name), null, it.m_shared.m_eitrRegenModifier, true);
                mult += it.m_shared.m_eitrRegenModifier;
            }
            var game = 1f;
            p.GetSEMan().ModifyEitrRegen(ref game);
            game += p.GetEquipmentEitrRegenModifier();
            bd.Pct(Other, null, game - mult, true);
            var flat = StatsSources.ResourceAffixes ? StatsSources.El(p, "AddEitrRegen") : 0f;
            bd.Add(Loc("$runeskin_bd_enchant"), null, flat * factor, true, PerSecond);
            bd.Result(Total, StatsMath.EitrRegen(p.m_eiterRegen, fill, factor, game, flat), PerSecond);
        }

        // ---- resistances: the reference hit through the damage pipeline step by step (StatsReader.Reduction) ----

        private void Resist(StatBreakdown bd, int index)
        {
            var p = _p;
            var type = ResistOrder[index];
            bd.Clear(Loc(ResistWords[index]));
            var probe = Probe;
            bd.Base(Loc("$runeskin_bd_reference_hit"), probe);
            SetProbe(type, probe);
            try
            {
                StatsSources.ElResistance?.Invoke(p, _probe);
                StatsSources.ElNight?.Invoke(p, _probe);
            }
            catch (System.Exception) { /* logged by the row */ }
            var afterEl = _probe.m_damage.GetTotalDamage();
            bd.Add(Loc("$runeskin_bd_enchant"), null, afterEl - probe, false);
            var mods = p.GetDamageModifiers();
            var mod = mods.GetModifier(type);
            if (mod != HitData.DamageModifier.Normal)
                bd.Note(Loc("$runeskin_bd_modifier") + ": " + Loc(ModifierWord(mod)) + Sources(type));
            if (StatsSources.ArmorResistance != null) StatsSources.ArmorResistance(_probe, mods, out _, p);
            else _probe.ApplyResistance(mods, out _);
            var afterRes = _probe.m_damage.GetTotalDamage();
            bd.Add(Loc("$runeskin_bd_resistance"), null, afterRes - afterEl, false);
            _probe.ApplyArmor(p.GetBodyArmor());
            var after = _probe.m_damage.GetTotalDamage();
            bd.Add(Loc("$runeskin_stats_armor"), null, after - afterRes, false);
            bd.Result(Loc("$runeskin_bd_taken"), after);
            bd.Note(Loc("$runeskin_bd_reduction") + " " + Mathf.RoundToInt((1f - after / probe) * 100f) + "%");
        }

        private void SetProbe(HitData.DamageType type, float v)
        {
            _probe.m_damage = default;
            switch (type)
            {
                case HitData.DamageType.Blunt: _probe.m_damage.m_blunt = v; break;
                case HitData.DamageType.Slash: _probe.m_damage.m_slash = v; break;
                case HitData.DamageType.Pierce: _probe.m_damage.m_pierce = v; break;
                case HitData.DamageType.Fire: _probe.m_damage.m_fire = v; break;
                case HitData.DamageType.Frost: _probe.m_damage.m_frost = v; break;
                case HitData.DamageType.Lightning: _probe.m_damage.m_lightning = v; break;
                case HitData.DamageType.Poison: _probe.m_damage.m_poison = v; break;
                case HitData.DamageType.Spirit: _probe.m_damage.m_spirit = v; break;
            }
        }

        private static string ModifierWord(HitData.DamageModifier m)
        {
            switch (m)
            {
                case HitData.DamageModifier.Resistant: return "$inventory_resistant";
                case HitData.DamageModifier.VeryResistant: return "$inventory_veryresistant";
                case HitData.DamageModifier.Weak: return "$inventory_weak";
                case HitData.DamageModifier.VeryWeak: return "$inventory_veryweak";
                case HitData.DamageModifier.Immune: return "$inventory_immune";
                case HitData.DamageModifier.SlightlyResistant: return "$inventory_slightlyresistant";
                case HitData.DamageModifier.SlightlyWeak: return "$inventory_slightlyweak";
                case HitData.DamageModifier.Ignore: return "$inventory_ignore";
                default: return "$inventory_normal";
            }
        }

        // Worn items (ItemData m_shared.m_damageModifiers, V Humanoid/Player GetDamageModifiers) and status effects
        // (ModifyDamageMods) that set a modifier for `type`: " (name, name)".
        private string Sources(HitData.DamageType type)
        {
            var sb = _sources;
            sb.Clear();
            foreach (var it in Worn())
                foreach (var dm in it.m_shared.m_damageModifiers)
                    if (dm.m_type == type && dm.m_modifier != HitData.DamageModifier.Normal) { Sep(sb); sb.Append(Loc(it.m_shared.m_name)); }
            foreach (var se in Effects())
            {
                var m = default(HitData.DamageModifiers);
                se.ModifyDamageMods(ref m);
                if (m.GetModifier(type) != HitData.DamageModifier.Normal) { Sep(sb); sb.Append(EffectName(se)); }
            }
            return sb.Length > 0 ? " (" + sb + ")" : "";
        }

        private readonly System.Text.StringBuilder _sources = new System.Text.StringBuilder(128);

        private static void Sep(System.Text.StringBuilder sb)
        {
            if (sb.Length > 0) sb.Append(", ");
        }

        // ---- movement: V Character.cs:1624 / 1635 / 1488, Player.cs:7139-7147 ----

        private void Speed(StatBreakdown bd, bool run)
        {
            var p = _p;
            bd.Clear(Loc(run ? "$runeskin_stats_run" : "$runeskin_stats_jog"));
            var baseSpeed = run ? p.m_runSpeed : p.m_speed;
            bd.Base(Loc("$runeskin_bd_base"), 100f, "%");
            var factor = 1f;
            if (run)
            {
                var skill = p.m_skills.GetSkillFactor(Skills.SkillType.Run) * 0.25f;
                bd.Pct(SkillName(Skills.SkillType.Run), null, skill, true);
                factor *= 1f + skill;
            }
            var gear = 0f;
            foreach (var it in Worn())
            {
                var m = it.m_shared.m_movementModifier * (run ? 1.5f : 1f);
                bd.Pct(Loc(it.m_shared.m_name), null, m * factor, true);
                gear += m;
            }
            factor *= 1f + gear;
            var speed = baseSpeed * factor;
            var dir = p.m_currentVel;
            var attributed = speed;
            foreach (var se in Effects())
            {
                var s = speed;
                se.ModifySpeed(speed, ref s, p, dir);
                bd.Pct(EffectName(se), null, (s - speed) / baseSpeed, true);
                attributed += s - speed;
            }
            var game = baseSpeed * (run ? p.GetRunSpeedFactor() : p.GetJogSpeedFactor());
            p.GetSEMan().ApplyStatusEffectSpeedMods(ref game, dir);
            bd.Pct(Other, null, (game - attributed) / baseSpeed, true);
            bd.Result(Total, game / baseSpeed * 100f, "%");
        }

        private void SwimSpeed(StatBreakdown bd)
        {
            var p = _p;
            bd.Clear(Loc("$runeskin_stats_swim"));
            bd.Base(Loc("$runeskin_bd_base"), 100f, "%");
            var dir = p.m_currentVel;
            var attributed = p.m_swimSpeed;
            foreach (var se in Effects())
            {
                var s = p.m_swimSpeed;
                se.ModifySpeed(p.m_swimSpeed, ref s, p, dir);
                bd.Pct(EffectName(se), null, (s - p.m_swimSpeed) / p.m_swimSpeed, true);
                attributed += s - p.m_swimSpeed;
            }
            var game = p.m_swimSpeed;
            p.GetSEMan().ApplyStatusEffectSpeedMods(ref game, dir);
            bd.Pct(Other, null, (game - attributed) / p.m_swimSpeed, true);
            bd.Result(Total, game / p.m_swimSpeed * 100f, "%");
        }

        // V Player.GetMaxCarryWeight :5062: m_maxCarryWeight + SEMan.ModifyMaxCarryWeight, x Game.m_carryWeightRate.
        private void Weight(StatBreakdown bd)
        {
            var p = _p;
            bd.Clear(Loc("$runeskin_stats_weight"));
            bd.Base(Loc("$runeskin_bd_base"), p.m_maxCarryWeight);
            var sum = p.m_maxCarryWeight;
            foreach (var se in Effects())
            {
                var l = p.m_maxCarryWeight;
                se.ModifyMaxCarryWeight(p.m_maxCarryWeight, ref l);
                bd.Add(EffectName(se), null, l - p.m_maxCarryWeight, true);
                sum += l - p.m_maxCarryWeight;
            }
            var limit = p.m_maxCarryWeight;
            p.GetSEMan().ModifyMaxCarryWeight(limit, ref limit);
            Residual(bd, limit, sum, true);
            bd.Mul(Loc("$runeskin_bd_world"), null, Game.m_carryWeightRate, true);
            bd.Result(Total, p.GetMaxCarryWeight());
            bd.Note(Loc("$runeskin_bd_carried") + " " + Mathf.RoundToInt(p.GetInventory().GetTotalWeight()));
        }

        // ---- stamina costs ----

        // V Player.CheckRun :2525-2529 (cost per second).
        private void RunDrain(StatBreakdown bd)
        {
            var p = _p;
            bd.Clear(Loc("$runeskin_stats_run_drain"));
            var b = p.m_runStaminaDrain;
            bd.Base(Loc("$runeskin_bd_base"), b, PerSecond);
            var drain = b * StatsMath.RunDrainSkill(p.m_skills.GetSkillFactor(Skills.SkillType.Run));
            bd.Add(SkillName(Skills.SkillType.Run), null, drain - b, false, PerSecond);
            var afterSkill = drain;
            foreach (var it in Worn())
                bd.Add(Loc(it.m_shared.m_name), null, -afterSkill * it.m_shared.m_movementModifier, false, PerSecond);
            drain -= drain * p.GetEquipmentMovementModifier();
            var afterMove = drain;
            foreach (var it in Worn())
                bd.Add(Loc(it.m_shared.m_name), null, afterMove * it.m_shared.m_runStaminaModifier, false, PerSecond);
            drain += drain * p.GetEquipmentRunStaminaModifier();
            var dir = p.m_currentVel;
            var attributed = drain;
            foreach (var se in Effects())
            {
                var d = drain;
                se.ModifyRunStaminaDrain(drain, ref d, dir);
                bd.Add(EffectName(se), null, d - drain, false, PerSecond);
                attributed += d - drain;
            }
            var game = drain;
            p.GetSEMan().ModifyRunStaminaDrain(drain, ref game, dir);
            Residual(bd, game, attributed, false, PerSecond);
            bd.Mul(Loc("$runeskin_bd_world"), null, Game.m_staminaRate, false);
            bd.Result(Total, game * Game.m_staminaRate, PerSecond);
        }

        // V Player.GetDodgeStaminaUse :5791-5798.
        private void Dodge(StatBreakdown bd)
        {
            var p = _p;
            bd.Clear(Loc("$runeskin_stats_dodge"));
            var b = p.m_dodgeStaminaUsage;
            bd.Base(Loc("$runeskin_bd_base"), b);
            foreach (var it in Worn())
                bd.Add(Loc(it.m_shared.m_name), null, b * (it.m_shared.m_dodgeStaminaModifier - it.m_shared.m_movementModifier), false);
            var use = b - b * p.GetEquipmentMovementModifier() + b * p.GetEquipmentDodgeStaminaModifier();
            var attributed = use;
            foreach (var se in Effects())
            {
                var u = use;
                se.ModifyDodgeStaminaUsage(use, ref u);
                bd.Add(EffectName(se), null, u - use, false);
                attributed += u - use;
            }
            var game = use;
            p.GetSEMan().ModifyDodgeStaminaUsage(use, ref game);
            Residual(bd, game, attributed, false);
            var skill = StatsMath.RunDrainSkill(p.m_skills.GetSkillFactor(Skills.SkillType.Dodge));
            bd.Add(SkillName(Skills.SkillType.Dodge), null, game * skill - game, false);
            bd.Mul(Loc("$runeskin_bd_world"), null, Game.m_staminaRate, false);
            bd.Result(Total, game * skill * Game.m_staminaRate);
        }

        // V Player.OnJump :2623-2626 (x Game.m_moveStaminaRate).
        private void Jump(StatBreakdown bd)
        {
            var p = _p;
            bd.Clear(Loc("$runeskin_stats_jump"));
            var b = p.m_jumpStaminaUsage;
            bd.Base(Loc("$runeskin_bd_base"), b);
            foreach (var it in Worn())
                bd.Add(Loc(it.m_shared.m_name), null, b * (it.m_shared.m_jumpStaminaModifier - it.m_shared.m_movementModifier), false);
            var use = b - b * p.GetEquipmentMovementModifier() + b * p.GetEquipmentJumpStaminaModifier();
            var attributed = use;
            foreach (var se in Effects())
            {
                var u = use;
                se.ModifyJumpStaminaUsage(use, ref u);
                bd.Add(EffectName(se), null, u - use, false);
                attributed += u - use;
            }
            var game = use;
            p.GetSEMan().ModifyJumpStaminaUsage(use, ref game);
            Residual(bd, game, attributed, false);
            bd.Mul(Loc("$runeskin_bd_world"), null, Game.m_moveStaminaRate * Game.m_staminaRate, false);
            bd.Result(Total, game * Game.m_moveStaminaRate * Game.m_staminaRate);
        }

        // V Player.OnSwimming :2630-2639 (per second while moving in water).
        private void SwimDrain(StatBreakdown bd)
        {
            var p = _p;
            bd.Clear(Loc("$runeskin_stats_swim_drain"));
            bd.Base(Loc("$runeskin_bd_base"), p.m_swimStaminaDrainMinSkill, PerSecond);
            var use = StatsMath.SwimDrain(p.m_swimStaminaDrainMinSkill, p.m_swimStaminaDrainMaxSkill, p.m_skills.GetSkillFactor(Skills.SkillType.Swim));
            bd.Add(SkillName(Skills.SkillType.Swim), null, use - p.m_swimStaminaDrainMinSkill, false, PerSecond);
            var before = use;
            foreach (var it in Worn())
                bd.Add(Loc(it.m_shared.m_name), null, before * it.m_shared.m_swimStaminaModifier, false, PerSecond);
            use += use * p.GetEquipmentSwimStaminaModifier();
            var attributed = use;
            foreach (var se in Effects())
            {
                var u = use;
                se.ModifySwimStaminaUsage(use, ref u);
                bd.Add(EffectName(se), null, u - use, false, PerSecond);
                attributed += u - use;
            }
            var game = use;
            p.GetSEMan().ModifySwimStaminaUsage(use, ref game);
            Residual(bd, game, attributed, false, PerSecond);
            bd.Mul(Loc("$runeskin_bd_world"), null, Game.m_moveStaminaRate * Game.m_staminaRate, false);
            bd.Result(Total, game * Game.m_moveStaminaRate * Game.m_staminaRate, PerSecond);
        }

        // ---- defense ----

        // V Player.GetBodyArmor :6862-6881: chest + legs + helmet + shoulders (ItemData.GetArmor), then SEMan.ApplyArmorMods.
        private void Armor(StatBreakdown bd)
        {
            var p = _p;
            bd.Clear(Loc("$runeskin_stats_armor"));
            var sum = 0f;
            AddArmor(bd, p.m_chestItem, ref sum);
            AddArmor(bd, p.m_legItem, ref sum);
            AddArmor(bd, p.m_helmetItem, ref sum);
            AddArmor(bd, p.m_shoulderItem, ref sum);
            var attributed = sum;
            foreach (var se in Effects())
            {
                var a = sum;
                se.ModifyArmorMods(ref a);
                bd.Add(EffectName(se), null, a - sum, true);
                attributed += a - sum;
            }
            var game = p.GetBodyArmor();
            Residual(bd, game, attributed, true);
            bd.Result(Total, game);
            bd.Note(Loc("$runeskin_bd_reduction") + " " + Mathf.RoundToInt(ArmorShare() * 100f) + "%");
        }

        private void AddArmor(StatBreakdown bd, ItemDrop.ItemData it, ref float sum)
        {
            if (it == null) return;
            var a = it.GetArmor();
            bd.Add(Loc(it.m_shared.m_name), null, a, true);
            sum += a;
        }

        // V ItemData.GetBlockPower :722-735 (base + per level, x (1 + skill x 0.5)); parry x m_timedBlockBonus x
        // SEMan.ModifyTimedBlockBonus (Humanoid.cs:1762-1768).
        private void Block(StatBreakdown bd, bool parry)
        {
            var p = _p;
            bd.Clear(Loc(parry ? "$runeskin_stats_parry" : "$runeskin_stats_block"));
            var blocker = p.GetCurrentBlocker();
            if (blocker == null)
            {
                bd.Note(Loc("$runeskin_bd_no_blocker"));
                return;
            }
            var basePower = blocker.m_shared.m_blockPower;
            bd.Base(Loc(blocker.m_shared.m_name), basePower);
            var withLevel = blocker.GetBaseBlockPower(blocker.m_quality);
            bd.Add(Loc("$runeskin_bd_quality"), null, withLevel - basePower, true);
            var power = blocker.GetBlockPower(p.GetSkillFactor(Skills.SkillType.Blocking));
            bd.Add(SkillName(Skills.SkillType.Blocking), null, power - withLevel, true);
            if (!parry)
            {
                bd.Result(Total, power);
                return;
            }
            if (blocker.m_shared.m_timedBlockBonus <= 1f)
            {
                bd.Note(Loc("$runeskin_bd_no_parry"));
                return;
            }
            bd.Mul(Loc("$runeskin_bd_parry_bonus"), null, blocker.m_shared.m_timedBlockBonus, true);
            var bonus = blocker.m_shared.m_timedBlockBonus;
            foreach (var se in Effects())
            {
                var b = bonus;
                se.ModifyTimedBlockBonus(ref b);
                bd.Mul(EffectName(se), null, b / bonus, true);
            }
            var game = blocker.m_shared.m_timedBlockBonus;
            p.GetSEMan().ModifyTimedBlockBonus(ref game);
            bd.Result(Total, power * game);
        }

        // V Humanoid.BlockAttack :1781-1784: a full block costs m_blockStaminaDrain (x the blocked share), x (1 +
        // equipment block stamina), SEMan.ModifyBlockStaminaUsage; a perfect block costs m_perfectBlockStaminaDrain.
        private void BlockStamina(StatBreakdown bd)
        {
            var p = _p;
            bd.Clear(Loc("$runeskin_stats_block_stamina"));
            var b = p.m_blockStaminaDrain;
            bd.Base(Loc("$runeskin_bd_base"), b);
            foreach (var it in Worn())
                bd.Add(Loc(it.m_shared.m_name), null, b * it.m_shared.m_blockStaminaModifier, false);
            var use = b + b * p.GetEquipmentBlockStaminaModifier();
            var attributed = use;
            foreach (var se in Effects())
            {
                var u = use;
                se.ModifyBlockStaminaUsage(use, ref u);
                bd.Add(EffectName(se), null, u - use, false);
                attributed += u - use;
            }
            var game = use;
            p.GetSEMan().ModifyBlockStaminaUsage(use, ref game, false);
            Residual(bd, game, attributed, false);
            bd.Mul(Loc("$runeskin_bd_world"), null, Game.m_staminaRate, false);
            bd.Result(Total, game * Game.m_staminaRate);
            bd.Note(Loc("$runeskin_bd_perfect_block") + ": " + StatsMath.Format(PerfectBlockStamina(p), _comma));
            bd.Note(Loc("$runeskin_bd_block_note"));
        }

        private static float PerfectBlockStamina(Player p)
        {
            var use = p.m_perfectBlockStaminaDrain;
            use += use * p.GetEquipmentBlockStaminaModifier();
            p.GetSEMan().ModifyBlockStaminaUsage(use, ref use, false);
            return use * Game.m_staminaRate;
        }

        public float BlockStaminaCost(Player p)
        {
            var use = p.m_blockStaminaDrain;
            use += use * p.GetEquipmentBlockStaminaModifier();
            p.GetSEMan().ModifyBlockStaminaUsage(use, ref use, false);
            return use * Game.m_staminaRate;
        }

        // ---- combat ----

        private static readonly string[] DamageWords =
        {
            "$inventory_blunt", "$inventory_slash", "$inventory_pierce", "$inventory_chop", "$inventory_pickaxe",
            "$inventory_fire", "$inventory_frost", "$inventory_lightning", "$inventory_poison", "$inventory_spirit",
        };

        // Element colour index (ResistIcons order) per DamageWords entry; chop / pickaxe have none.
        private static readonly int[] DamageColor = { 0, 1, 2, -1, -1, 3, 4, 5, 6, 7 };

        private static float TypeValue(HitData.DamageTypes d, int i)
        {
            switch (i)
            {
                case 0: return d.m_blunt;
                case 1: return d.m_slash;
                case 2: return d.m_pierce;
                case 3: return d.m_chop;
                case 4: return d.m_pickaxe;
                case 5: return d.m_fire;
                case 6: return d.m_frost;
                case 7: return d.m_lightning;
                case 8: return d.m_poison;
                default: return d.m_spirit;
            }
        }

        private readonly string[] _elemHex = new string[8];

        public string[] ElementHex()
        {
            for (var i = 0; i < _elemHex.Length; i++) _elemHex[i] = ColorUtility.ToHtmlStringRGB(ElementColor(i));
            return _elemHex;
        }

        private bool Weapon(out ItemDrop.ItemData weapon)
        {
            weapon = _p.GetCurrentWeapon();
            return weapon != null;
        }

        // Per hit (V Attack.DoMeleeAttack :1380-1422): ItemData.GetDamage (base + per level, V ItemDrop.cs:698-711, + world
        // level) x the random skill factor (Skills.GetRandomSkillFactor :188) x status effects (SEMan.ModifyAttack ->
        // SE_Stats.ModifyAttack: m_damageModifier for the skill, m_percentigeDamageModifiers).
        private void Damage(StatBreakdown bd)
        {
            var p = _p;
            bd.Clear(Loc("$runeskin_stats_damage"));
            if (!Weapon(out var w))
            {
                bd.Note(Loc("$runeskin_bd_no_weapon"));
                return;
            }
            var sh = w.m_shared;
            var game = w.GetDamage();
            var total = game.GetTotalDamage();
            for (var i = 0; i < DamageWords.Length; i++)
            {
                var v = TypeValue(game, i);
                if (v < 0.5f) continue;
                bd.Share(Loc(DamageWords[i]), v, total > 0f ? v / total : 0f, DamageColor[i]);
            }
            bd.Base(Loc("$runeskin_bd_weapon_base"), sh.m_damages.GetTotalDamage());
            var levels = sh.m_damagesPerLevel.GetTotalDamage() * Mathf.Max(0, w.m_quality - 1);
            bd.Add(Loc("$runeskin_bd_quality"), null, levels, true);
            var vanilla = sh.m_damages.GetTotalDamage() + levels;
            if (w.m_worldLevel > 0 && Game.instance)
            {
                var d = sh.m_damages;
                if (w.m_quality > 1) d.Add(sh.m_damagesPerLevel, w.m_quality - 1);
                d.IncreaseEqually(w.m_worldLevel * (float)Game.instance.m_worldLevelGearBaseDamage, true); // utility damage kept separate
                bd.Add(Loc("$runeskin_bd_world_level"), null, d.GetTotalDamage() - vanilla, true);
                vanilla = d.GetTotalDamage();
            }
            Residual(bd, total, vanilla, true);
            StatsMath.SkillRange(p.GetSkillFactor(sh.m_skillType), out var min, out var max);
            bd.Range(SkillName(sh.m_skillType), null, min, max);
            var factor = 1f;
            foreach (var se in Effects())
            {
                var f = AttackFactor(se, sh.m_skillType, game);
                bd.Mul(EffectName(se), null, f, true);
                factor *= f;
            }
            _hit.m_damage = game;
            p.GetSEMan().ModifyAttack(sh.m_skillType, ref _hit);
            var after = _hit.m_damage.GetTotalDamage();
            var gameFactor = total > 0f ? after / total : 1f;
            bd.Mul(Other, null, factor > 0f ? gameFactor / factor : 1f, true);
            bd.ResultRange(Loc("$runeskin_bd_per_hit"), after * min, after * max);
            bd.Note(Loc("$runeskin_bd_damage_note"));
        }

        // Damage factor one status effect applies to this weapon's hit.
        private float AttackFactor(StatusEffect se, Skills.SkillType skill, HitData.DamageTypes d)
        {
            var before = d.GetTotalDamage();
            if (before <= 0f) return 1f;
            _hit.m_damage = d;
            se.ModifyAttack(skill, ref _hit);
            return _hit.m_damage.GetTotalDamage() / before;
        }

        // Mean hit of the current weapon (skill factor mean x status effects), the number the DPS row divides.
        public float MeanHit(Player p, ItemDrop.ItemData w, float multiplier = 1f)
        {
            var d = w.GetDamage();
            _hit.m_damage = d;
            p.GetSEMan().ModifyAttack(w.m_shared.m_skillType, ref _hit);
            return _hit.m_damage.GetTotalDamage() * Mathf.Lerp(0.4f, 1f, p.GetSkillFactor(w.m_shared.m_skillType)) * multiplier;
        }

        private void Dps(StatBreakdown bd)
        {
            var p = _p;
            bd.Clear(Loc("$runeskin_stats_dps"));
            if (!Weapon(out var w))
            {
                bd.Note(Loc("$runeskin_bd_no_weapon"));
                return;
            }
            var hit = MeanHit(p, w);
            bd.Base(Loc("$runeskin_bd_mean_hit"), hit);
            var measured = AttackTimer.Get(w.m_shared.m_attack?.m_attackAnimation);
            var duration = measured > 0f ? measured : AttackDuration(p, w);
            if (duration <= 0.05f)
            {
                bd.Note(Loc("$runeskin_bd_no_timing"));
                return;
            }
            bd.Note(Loc("$runeskin_bd_swing") + ": " + StatsMath.Format(duration, _comma) + " " + Loc("$runeskin_stats_sec") + " (" +
                    Loc(measured > 0f ? "$runeskin_bd_measured" : "$runeskin_bd_animation") + ")");
            var speed = measured > 0f ? 1f : 1f + StatsSources.El(p, "ModifyAttackSpeed", 0.01f);
            bd.Mul(Loc("$runeskin_stats_attack_speed"), null, speed, true);
            bd.Result("≈ " + Total, hit * speed / duration, PerSecond);
            bd.Note(Loc("$runeskin_bd_dps_note"));
        }

        // V Attack.GetAttackStamina :468-487: m_attackStamina x (1 + equipment attack stamina), SEMan.ModifyAttackStaminaUsage,
        // minus 33% x weapon skill.
        private void AttackStamina(StatBreakdown bd, int index)
        {
            var p = _p;
            bd.Clear(Loc(index == 0 ? "$runeskin_stats_attack_stamina" : "$runeskin_stats_secondary"));
            if (!Weapon(out var w))
            {
                bd.Note(Loc("$runeskin_bd_no_weapon"));
                return;
            }
            var attack = index == 0 ? w.m_shared.m_attack : w.m_shared.m_secondaryAttack;
            if (attack == null)
            {
                bd.Note(Loc("$runeskin_bd_no_weapon"));
                return;
            }
            if (index == 1)
            {
                bd.Base(Loc("$runeskin_bd_mean_hit"), MeanHit(p, w, attack.m_damageMultiplier));
                bd.Mul(Loc("$runeskin_bd_attack_mult"), null, attack.m_damageMultiplier, true);
            }
            var b = attack.m_attackStamina;
            bd.Base(Loc("$runeskin_bd_stamina_base"), b);
            foreach (var it in Worn())
                bd.Add(Loc(it.m_shared.m_name), null, b * it.m_shared.m_attackStaminaModifier, false);
            var use = b * (1f + p.GetEquipmentAttackStaminaModifier());
            var attributed = use;
            foreach (var se in Effects())
            {
                var u = use;
                se.ModifyAttackStaminaUsage(use, ref u);
                bd.Add(EffectName(se), null, u - use, false);
                attributed += u - use;
            }
            var game = use;
            p.GetSEMan().ModifyAttackStaminaUsage(use, ref game);
            Residual(bd, game, attributed, false);
            var skill = StatsMath.AttackSkillCut(game, p.GetSkillFactor(w.m_shared.m_skillType));
            bd.Add(SkillName(w.m_shared.m_skillType), null, -skill, false);
            bd.Mul(Loc("$runeskin_bd_world"), null, Game.m_staminaRate, false);
            bd.Result(Total, (game - skill) * Game.m_staminaRate);
        }

        public float AttackStaminaCost(Player p, ItemDrop.ItemData w, Attack attack)
        {
            if (attack == null || attack.m_attackStamina <= 0f) return 0f;
            var use = attack.m_attackStamina * (1f + p.GetEquipmentAttackStaminaModifier());
            p.GetSEMan().ModifyAttackStaminaUsage(use, ref use);
            return (use - StatsMath.AttackSkillCut(use, p.GetSkillFactor(w.m_shared.m_skillType))) * Game.m_staminaRate;
        }

        public float SwimDrainCost(Player p)
        {
            var use = StatsMath.SwimDrain(p.m_swimStaminaDrainMinSkill, p.m_swimStaminaDrainMaxSkill, p.m_skills.GetSkillFactor(Skills.SkillType.Swim));
            use += use * p.GetEquipmentSwimStaminaModifier();
            p.GetSEMan().ModifySwimStaminaUsage(use, ref use);
            return use * Game.m_moveStaminaRate * Game.m_staminaRate;
        }

        private readonly Dictionary<Skills.SkillType, string> _skillNames = new Dictionary<Skills.SkillType, string>(16);

        // "Навык: Мечи 35" style: localized skill name + current level.
        private string SkillName(Skills.SkillType skill)
        {
            if (!_skillNames.TryGetValue(skill, out var name))
            {
                name = Loc("$skill_" + skill.ToString().ToLowerInvariant());
                _skillNames[skill] = name;
            }
            return Loc("$runeskin_bd_skill") + " " + name + " " + Mathf.FloorToInt(_p.GetSkillLevel(skill));
        }
    }
}
