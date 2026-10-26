using System.Collections.Generic;
using System.Text;
using RuneSkin.Theme;
using UnityEngine;
using Kind = RuneSkin.InfoScreens.StatsWindow.Kind;

namespace RuneSkin.InfoScreens
{
    // Fills the stats window cards from the live game objects, the way the game computes each value (decompiled
    // 1.0.20 lines per row; V = assembly_valheim). Status effects and mods that patch those vanilla methods (Epic Loot's
    // SEMan postfixes, MorgottTweaks) are counted through them; Epic Loot effects with no vanilla hook through its own
    // sum (StatsSources.El); MorgottTweaks slots and energy shield through its public read APIs. Strings: localized
    // words cached per language, numbers written as characters (StatsMath), so a refresh allocates nothing.
    internal sealed partial class StatsReader
    {
        private static readonly HitData.DamageType[] ResistOrder =
        {
            HitData.DamageType.Blunt, HitData.DamageType.Slash,
            HitData.DamageType.Pierce, HitData.DamageType.Fire,
            HitData.DamageType.Frost, HitData.DamageType.Lightning,
            HitData.DamageType.Poison, HitData.DamageType.Spirit,
        };

        private static readonly string[] ResistIcons = { "blunt", "slash", "pierce", "fire", "frost", "lightning", "poison", "spirit" };
        private static readonly string[] ResistWords =
        {
            "$inventory_blunt", "$inventory_slash", "$inventory_pierce", "$inventory_fire", "$inventory_frost",
            "$inventory_lightning", "$inventory_poison", "$inventory_spirit",
        };

        private readonly Dictionary<string, string> _words = new Dictionary<string, string>(128);
        private readonly Dictionary<string, string> _lower = new Dictionary<string, string>(32);
        private readonly HitData _probe = new HitData();
        private string _language;
        private StatsWindow _w;
        private Player _p;
        private bool _comma;

        public bool Comma => _comma;
        public string GoodHex { get; private set; } = "9FD66B";
        public string BadHex { get; private set; } = "F08A3A";
        public string SubHex { get; private set; } = "A9B3B7";

        public void Begin(StatsWindow w, Player p)
        {
            _w = w;
            _p = p;
            var loc = Localization.instance;
            var lang = loc != null ? loc.GetSelectedLanguage() : "English";
            if (!ReferenceEquals(lang, _language) && lang != _language)
            {
                _language = lang;
                _words.Clear();
                _lower.Clear();
                _seLine.Clear();
                _skillNames.Clear();
            }
            _comma = lang == "Russian";
            GoodHex = ColorUtility.ToHtmlStringRGB(ThemeTokens.GetColor("color.stats.good", new Color(0.5f, 0.83f, 0.42f)));
            SubHex = ColorUtility.ToHtmlStringRGB(ThemeTokens.GetColor("color.stats.sub", new Color(0.66f, 0.7f, 0.72f)));
            BadHex = ColorUtility.ToHtmlStringRGB(ThemeTokens.GetColor("color.stats.bad", new Color(0.94f, 0.59f, 0.23f)));
        }

        // First tooltip line of a status effect, localized, cached per effect name ("" when none).
        private readonly Dictionary<string, string> _seLine = new Dictionary<string, string>(32);

        public string EffectLine(StatusEffect se)
        {
            if (_seLine.TryGetValue(se.m_name, out var v)) return v;
            v = "";
            try
            {
                var tip = Localization.instance != null ? Localization.instance.Localize(se.GetTooltipString()) : "";
                foreach (var line in tip.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.Length > 0) { v = t; break; }
                }
            }
            catch (System.Exception) { v = ""; }
            _seLine[se.m_name] = v;
            return v;
        }

        // Display name of a status effect: an equipment effect (the SE of a worn item, ItemData.m_shared.m_equipStatusEffect)
        // is named after its item ("Ранец", not a mod's "Ранец Уровень 1 Эффект"); any other effect by its localized
        // m_name.
        public string EffectName(StatusEffect se)
        {
            if (_p)
            {
                var items = _p.GetInventory().GetAllItems();
                for (var i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    var eq = it != null && it.m_equipped ? it.m_shared.m_equipStatusEffect : null;
                    if (eq && eq.NameHash() == se.NameHash()) return Loc(it.m_shared.m_name);
                }
            }
            return Loc(se.m_name);
        }

        // Localized word (cached per language; "$key" or plain text).
        public string Loc(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (_words.TryGetValue(key, out var v)) return v;
            v = Localization.instance != null ? Localization.instance.Localize(key) : key;
            _words[key] = v;
            return v;
        }

        private string Lower(string key)
        {
            if (_lower.TryGetValue(key, out var v)) return v;
            v = Loc(key).ToLowerInvariant();
            _lower[key] = v;
            return v;
        }

        public void Fill(int card, StatsWindow.Card c)
        {
            switch (card)
            {
                case 0: Resources(c); break;
                case 1: Resists(c); break;
                case 2: Movement(c); break;
                case 3: Defense(c); break;
                case 4: Food(c); break;
                case 5: Rest(c); break;
                case 6: Combat(c); break;
                case 7: Morgott(c); break;
            }
        }

        private StatsWindow.Row Row(StatsWindow.Card c, Kind kind, string icon, string label)
        {
            var r = _w.Next(c, kind);
            r.IconToken = icon != null ? "sprite.stats.icon." + icon : null;
            if (label != null) r.L.Append(Loc(label));
            return r;
        }

        private void Pool(StatsWindow.Card c, string icon, string label, float cur, float max, string bar, Explain explain)
        {
            var r = Row(c, Kind.Pair, icon, label);
            r.Explain = explain;
            StatsMath.Digits(r.V, Mathf.CeilToInt(Mathf.Max(0f, cur))).Append(" / ");
            StatsMath.Digits(r.V, Mathf.CeilToInt(Mathf.Max(0f, max)));
            var b = _w.Next(c, Kind.Bar);
            b.BarToken = "sprite.stats.bar." + bar;
            b.FillShare = max > 0f ? cur / max : 0f;
        }

        private void Rate(StringBuilder sb, float perSecond)
        {
            var sem = perSecond > 0.05f ? 1 : 0;
            StatsMath.Open(sb, sem, GoodHex, BadHex);
            StatsMath.AppendDecimal(sb, perSecond, _comma, true).Append(Loc("$runeskin_stats_per_second"));
            StatsMath.Close(sb, sem);
        }

        // ---- 1 resources: V Player.cs UpdateFood :2449-2466, UpdateStats :2099-2152 ----

        private void Resources(StatsWindow.Card c)
        {
            var p = _p;
            Pool(c, "heart", "$runeskin_stats_health", p.GetHealth(), p.GetMaxHealth(), "hp", Explain.Health);
            // Health: sum of the foods' m_foodRegen (+ Epic Loot AddHealthRegen, inserted into that sum by its UpdateFood
            // transpiler, EL ModifyPlayerRegen.cs:107-110) x SEMan.ModifyHealthRegen, once per 10 s tick.
            var foodRegen = 0f;
            foreach (var f in p.m_foods) foodRegen += f.m_item.m_shared.m_foodRegen;
            if (foodRegen > 0f) foodRegen += StatsSources.El(p, "AddHealthRegen");
            var hpMult = 1f;
            p.GetSEMan().ModifyHealthRegen(ref hpMult);
            var hp = Row(c, Kind.PairSub, "pulse", "$runeskin_stats_health_regen");
            hp.Explain = Explain.HealthRegen;
            Rate(hp.V, StatsMath.HealthRegen(foodRegen, hpMult));
            if (foodRegen > 0f)
            {
                hp.S.Append('(').Append(Loc("$runeskin_stats_tick_in")).Append(' ');
                StatsMath.Digits(hp.S, Mathf.CeilToInt(StatsMath.HealthTickIn(p.m_foodRegenTimer))).Append(' ').Append(Loc("$runeskin_stats_sec")).Append(')');
            }

            var maxSt = p.GetMaxStamina();
            Pool(c, "bolt", "$runeskin_stats_stamina", p.GetStamina(), maxSt, "stamina", Explain.Stamina);
            var stMult = 1f;
            p.GetSEMan().ModifyStaminaRegen(ref stMult);
            var stFlat = StatsSources.ResourceAffixes ? StatsSources.El(p, "AddStaminaRegen") : 0f;
            var stFill = maxSt > 0f ? p.GetStamina() / maxSt : 1f;
            var st = Row(c, Kind.PairSub, "bolt2", "$runeskin_stats_stamina_regen");
            st.Explain = Explain.StaminaRegen;
            Rate(st.V, StatsMath.StaminaRegen(p.m_staminaRegen, p.m_staminaRegenTimeMultiplier, stFill, StaminaFactor(p), stMult, stFlat, Game.m_staminaRegenRate));
            st.S.Append('(').Append(Loc("$runeskin_stats_upto")).Append(' ');
            StatsMath.AppendDecimal(st.S, StatsMath.StaminaRegen(p.m_staminaRegen, p.m_staminaRegenTimeMultiplier, 0f, 1f, stMult, stFlat, Game.m_staminaRegenRate), _comma, true)
                .Append(Loc("$runeskin_stats_per_second")).Append(' ').Append(Loc("$runeskin_stats_when_empty")).Append(')');

            var maxEi = p.GetMaxEitr();
            Pool(c, "drop", "$runeskin_stats_eitr", p.GetEitr(), maxEi, "eitr", Explain.Eitr);
            var eiMult = 1f;
            p.GetSEMan().ModifyEitrRegen(ref eiMult);
            eiMult += p.GetEquipmentEitrRegenModifier();
            var eiFlat = StatsSources.ResourceAffixes ? StatsSources.El(p, "AddEitrRegen") : 0f;
            var ei = Row(c, Kind.Pair, "pulse2", "$runeskin_stats_eitr_regen");
            ei.Explain = Explain.EitrRegen;
            Rate(ei.V, maxEi > 0f ? StatsMath.EitrRegen(p.m_eiterRegen, p.GetEitr() / maxEi, EitrFactor(p), eiMult, eiFlat) : 0f);
        }

        // V Player.cs:2104-2106: 0.8 while blocking; none while swimming off ground, attacking, dodging, wall-running,
        // encumbered.
        private static float StaminaFactor(Player p)
        {
            if ((p.IsSwimming() && !p.IsOnGround()) || p.InAttack() || p.InDodge() || p.m_wallRunning || p.IsEncumbered()) return 0f;
            return p.IsBlocking() ? 0.8f : 1f;
        }

        // V Player.cs:2136-2143.
        private static float EitrFactor(Player p)
        {
            if (p.InAttack() || p.InDodge()) return 0f;
            return p.IsBlocking() ? 0.8f : 1f;
        }

        // ---- 2 resistances: the share of a reference hit per type the damage pipeline removes now ----

        private void Resists(StatsWindow.Card c)
        {
            var armorOnly = ArmorShare();
            for (var i = 0; i < ResistOrder.Length; i += 2)
            {
                var r = _w.Next(c, Kind.Cell);
                r.IconToken = "sprite.stats.icon." + ResistIcons[i];
                r.Icon2Token = "sprite.stats.icon." + ResistIcons[i + 1];
                r.Explain = r.Explain2 = Explain.Resist;
                r.Arg = i;
                r.Arg2 = i + 1;
                // One theme colour per damage type tints its icon (cut icons mixed silver and painted).
                r.IconColor = ElementColor(i);
                r.Icon2Color = ElementColor(i + 1);
                r.L.Append(Loc(ResistWords[i]));
                r.L2.Append(Loc(ResistWords[i + 1]));
                Share(r.V, Reduction(ResistOrder[i]), armorOnly);
                Share(r.V2, Reduction(ResistOrder[i + 1]), armorOnly);
            }
        }

        private readonly Color[] _elem = new Color[8];
        private int _elemRevision = -1;

        private Color ElementColor(int i)
        {
            if (_elemRevision != ThemeTokens.Revision)
            {
                _elemRevision = ThemeTokens.Revision;
                for (var k = 0; k < _elem.Length; k++) _elem[k] = ThemeTokens.GetColor("color.stats.elem." + ResistIcons[k], Color.white);
            }
            return _elem[i];
        }

        private void Share(StringBuilder sb, float share, float armorOnly)
        {
            var sem = StatsMath.ReductionSemantic(share, armorOnly);
            StatsMath.Open(sb, sem, GoodHex, BadHex);
            StatsMath.AppendInt(sb, share * 100f, false).Append('%');
            StatsMath.Close(sb, sem);
        }

        private float Probe => Plugin.StatsReferenceHit != null ? Mathf.Max(1f, Plugin.StatsReferenceHit.Value) : 100f;

        // Character.RPC_Damage order: Epic Loot's prefix (ModifyResistance + DamageReductionAtNight .ModifyIncoming), the
        // resistances of Character.GetDamageModifiers (V Character.cs:2378-2379; MorgottTweaks [Armor] replaces that
        // step with ArmorResistance.ApplyResistance), then armor (HitData.ApplyArmor(GetBodyArmor), :2380-2383).
        private float Reduction(HitData.DamageType type)
        {
            var probe = Probe;
            _probe.m_damage = default;
            switch (type)
            {
                case HitData.DamageType.Blunt: _probe.m_damage.m_blunt = probe; break;
                case HitData.DamageType.Slash: _probe.m_damage.m_slash = probe; break;
                case HitData.DamageType.Pierce: _probe.m_damage.m_pierce = probe; break;
                case HitData.DamageType.Chop: _probe.m_damage.m_chop = probe; break;
                case HitData.DamageType.Pickaxe: _probe.m_damage.m_pickaxe = probe; break;
                case HitData.DamageType.Fire: _probe.m_damage.m_fire = probe; break;
                case HitData.DamageType.Frost: _probe.m_damage.m_frost = probe; break;
                case HitData.DamageType.Lightning: _probe.m_damage.m_lightning = probe; break;
                case HitData.DamageType.Poison: _probe.m_damage.m_poison = probe; break;
                case HitData.DamageType.Spirit: _probe.m_damage.m_spirit = probe; break;
            }
            try
            {
                StatsSources.ElResistance?.Invoke(_p, _probe);
                StatsSources.ElNight?.Invoke(_p, _probe);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[RuneSkin] stats: Epic Loot resistance step failed: {e.Message}");
            }
            var mods = _p.GetDamageModifiers();
            if (StatsSources.ArmorResistance != null) StatsSources.ArmorResistance(_probe, mods, out _, _p);
            else _probe.ApplyResistance(mods, out _);
            _probe.ApplyArmor(_p.GetBodyArmor());
            return 1f - _probe.m_damage.GetTotalDamage() / probe;
        }

        // Armor alone on a physical reference hit (HitData.ApplyArmor).
        private float ArmorShare()
        {
            var probe = Probe;
            _probe.m_damage = default;
            _probe.m_damage.m_slash = probe;
            _probe.ApplyArmor(_p.GetBodyArmor());
            return 1f - _probe.m_damage.GetTotalDamage() / probe;
        }

        // ---- 3 movement and weight ----

        private void Movement(StatsWindow.Card c)
        {
            var p = _p;
            var se = p.GetSEMan();
            var dir = p.m_currentVel;
            // Jog (V Character.cs:1624 m_speed x GetJogSpeedFactor, Player.cs:7139) + status-effect speed mods (SEMan :69).
            var jog = p.m_speed * p.GetJogSpeedFactor();
            se.ApplyStatusEffectSpeedMods(ref jog, dir);
            Percent(Explained(Row(c, Kind.Pair, "walk", "$runeskin_stats_jog"), Explain.Jog).V, StatsMath.ChangePercent(jog, p.m_speed), true, true);
            // Run (Character.cs:1635 m_runSpeed x GetRunSpeedFactor, Player.cs:7144-7147: run skill + 1.5 x equipment).
            var run = p.m_runSpeed * p.GetRunSpeedFactor();
            se.ApplyStatusEffectSpeedMods(ref run, dir);
            Percent(Explained(Row(c, Kind.Pair, "run", "$runeskin_stats_run"), Explain.Run).V, StatsMath.ChangePercent(run, p.m_runSpeed), true, true);
            // Swim (Character.cs:1488 m_swimSpeed + status-effect mods), as a share of the base swim speed.
            var swim = p.m_swimSpeed;
            se.ApplyStatusEffectSpeedMods(ref swim, dir);
            var sw = Explained(Row(c, Kind.Pair, "swim", "$runeskin_stats_swim"), Explain.Swim);
            var swimShare = p.m_swimSpeed > 0f ? swim / p.m_swimSpeed * 100f : 100f;
            var swimSem = StatsMath.Semantic(swimShare - 100f, true);
            StatsMath.Open(sw.V, swimSem, GoodHex, BadHex);
            StatsMath.AppendInt(sw.V, swimShare, false).Append('%');
            StatsMath.Close(sw.V, swimSem);
            // Weight (Inventory.GetTotalWeight / Player.GetMaxCarryWeight :5062 via SEMan.ModifyMaxCarryWeight); the
            // bonus over m_maxCarryWeight with the first status effect that adds carry weight (Megingjord's SE_Stats).
            var weight = p.GetInventory().GetTotalWeight();
            var max = p.GetMaxCarryWeight();
            var w = Explained(Row(c, Kind.PairSub, "weight", "$runeskin_stats_weight"), Explain.Weight);
            var over = weight > max ? -1 : 0;
            StatsMath.Open(w.V, over, GoodHex, BadHex);
            StatsMath.AppendInt(w.V, weight, false).Append(" / ");
            StatsMath.AppendInt(w.V, max, false);
            StatsMath.Close(w.V, over);
            var bonus = max - p.m_maxCarryWeight;
            if (Mathf.Abs(bonus) >= 0.5f)
            {
                var sem = bonus > 0f ? 1 : -1;
                StatsMath.Open(w.S, sem, GoodHex, BadHex);
                w.S.Append('(');
                StatsMath.AppendInt(w.S, bonus, true);
                var source = CarrySource();
                if (source != null) w.S.Append(' ').Append(source);
                w.S.Append(')');
                StatsMath.Close(w.S, sem);
            }
            // Run stamina drain (Player.CheckRun :2525-2529: run skill, equipment movement / run stamina, SEMan).
            var drain = p.m_runStaminaDrain * Mathf.Lerp(1f, 0.5f, p.m_skills.GetSkillFactor(Skills.SkillType.Run));
            drain -= drain * p.GetEquipmentMovementModifier();
            drain += drain * p.GetEquipmentRunStaminaModifier();
            se.ModifyRunStaminaDrain(drain, ref drain, dir);
            Percent(Explained(Row(c, Kind.Pair, "rundrain", "$runeskin_stats_run_drain"), Explain.RunDrain).V, StatsMath.ChangePercent(drain, p.m_runStaminaDrain), false, true);
            // Dodge (Player.GetDodgeStaminaUse :5791-5798) and jump (OnJump :2623-2626, x Game.m_moveStaminaRate).
            var dodge = p.GetDodgeStaminaUse();
            Cost(Explained(Row(c, Kind.Pair, "dodge", "$runeskin_stats_dodge"), Explain.Dodge).V, dodge, p.m_dodgeStaminaUsage);
            var jump = p.m_jumpStaminaUsage - p.m_jumpStaminaUsage * p.GetEquipmentMovementModifier() + p.m_jumpStaminaUsage * p.GetEquipmentJumpStaminaModifier();
            se.ModifyJumpStaminaUsage(jump, ref jump);
            Cost(Explained(Row(c, Kind.Pair, "jump", "$runeskin_stats_jump"), Explain.Jump).V, jump * Game.m_moveStaminaRate, p.m_jumpStaminaUsage);
            // Swimming (Player.OnSwimming :2630-2639, per second while moving in water).
            var swimBase = p.m_swimStaminaDrainMinSkill * Game.m_moveStaminaRate;
            Cost(Explained(Row(c, Kind.Pair, "swim", "$runeskin_stats_swim_drain"), Explain.SwimDrain).V, SwimDrainCost(p), swimBase);
        }

        private string CarrySource()
        {
            foreach (var se in _p.GetSEMan().GetStatusEffects())
                if (se is SE_Stats st && st.m_addMaxCarryWeight > 0f && !string.IsNullOrEmpty(st.m_name)) return EffectName(st);
            return null;
        }

        private void Percent(StringBuilder sb, float change, bool higherIsBetter, bool sign)
        {
            var sem = StatsMath.Semantic(change, higherIsBetter);
            StatsMath.Open(sb, sem, GoodHex, BadHex);
            StatsMath.AppendInt(sb, change, sign).Append('%');
            StatsMath.Close(sb, sem);
        }

        private void Cost(StringBuilder sb, float cost, float baseCost)
        {
            var sem = StatsMath.Semantic(StatsMath.ChangePercent(cost, baseCost), false);
            StatsMath.Open(sb, sem, GoodHex, BadHex);
            StatsMath.AppendCompact(sb, cost, _comma);
            StatsMath.Close(sb, sem);
        }

        // ---- 4 defense ----

        private void Defense(StatsWindow.Card c)
        {
            var p = _p;
            // Armor: Player.GetBodyArmor (V :6862), the value HitData.ApplyArmor consumes; share = a physical reference hit.
            var armor = Explained(Row(c, Kind.TallSub, "armor", "$runeskin_stats_armor"), Explain.Armor);
            StatsMath.AppendInt(armor.V, p.GetBodyArmor(), false);
            armor.S.Append('(').Append(Loc("$runeskin_stats_reduction")).Append(' ');
            StatsMath.AppendInt(armor.S, ArmorShare() * 100f, false).Append("%)");
            // Block: the item Humanoid.BlockAttack blocks with (GetCurrentBlocker, Humanoid.cs:492) at the Blocking skill
            // (ItemData.GetBlockPower :722); parry = x m_timedBlockBonus x SEMan.ModifyTimedBlockBonus (Humanoid.cs:1762-1768).
            var blocker = p.GetCurrentBlocker();
            var block = Explained(Row(c, Kind.Tall, "block", "$runeskin_stats_block"), Explain.Block);
            var parry = Explained(Row(c, Kind.Tall, "parry", "$runeskin_stats_parry"), Explain.Parry);
            // Stamina per fully blocked hit (Humanoid.BlockAttack :1781-1784).
            Cost(Explained(Row(c, Kind.Pair, "bolt", "$runeskin_stats_block_stamina"), Explain.BlockStamina).V, BlockStaminaCost(p), p.m_blockStaminaDrain);
            if (blocker == null)
            {
                block.V.Append('—');
                parry.V.Append('—');
                return;
            }
            var power = blocker.GetBlockPower(p.GetSkillFactor(Skills.SkillType.Blocking));
            StatsMath.AppendInt(block.V, power, false);
            if (blocker.m_shared.m_timedBlockBonus > 1f)
            {
                var bonus = blocker.m_shared.m_timedBlockBonus;
                p.GetSEMan().ModifyTimedBlockBonus(ref bonus);
                parry.V.Append('×');
                StatsMath.AppendCompact(parry.V, bonus, _comma).Append(" = ");
                StatsMath.AppendInt(parry.V, power * bonus, false);
            }
            else parry.V.Append('—');
        }

        // ---- 5 food (Player.m_foods: item, m_health / m_stamina / m_eitr, m_time left; slots) ----

        private void Food(StatsWindow.Card c)
        {
            var p = _p;
            foreach (var f in p.m_foods)
            {
                if (f?.m_item == null) continue;
                var r = _w.Next(c, Kind.Dish);
                r.IconSprite = f.m_item.GetIcon();
                r.Icon2Token = "sprite.stats.icon.hourglass";
                r.L.Append(Loc(f.m_item.m_shared.m_name));
                StatsMath.Open(r.S, 1, GoodHex, BadHex);
                if (f.m_health > 0f) StatsMath.AppendInt(r.S, f.m_health, true).Append(' ').Append(Loc("$runeskin_stats_hp_short"));
                if (f.m_stamina > 0f) StatsMath.AppendInt(r.S.Append(r.S.Length > 0 ? " " : ""), f.m_stamina, true).Append(' ').Append(Loc("$runeskin_stats_st_short"));
                if (f.m_eitr > 0f) StatsMath.AppendInt(r.S.Append(' '), f.m_eitr, true).Append(' ').Append(Loc("$runeskin_stats_ei_short"));
                StatsMath.Close(r.S, 1);
                if (f.m_item.m_shared.m_foodRegen > 0f)
                {
                    r.S.Append(", ").Append(Loc("$runeskin_stats_regen_short")).Append(' ');
                    StatsMath.AppendCompact(r.S, f.m_item.m_shared.m_foodRegen, _comma);
                }
                StatsMath.AppendClock(r.V, f.m_time);
            }
            var foodMax = StatsSources.FoodSlots != null ? StatsSources.FoodSlots(p) : 3;
            var fs = Row(c, Kind.Pair, "fork", "$runeskin_stats_food_slots");
            StatsMath.Digits(fs.V, p.m_foods.Count).Append(" / ");
            StatsMath.Digits(fs.V, foodMax);
            if (StatsSources.PotionSlots != null)
            {
                var ps = Row(c, Kind.Pair, "flask", "$runeskin_stats_potion_slots");
                if (StatsSources.PotionsActive != null) StatsMath.Digits(ps.V, StatsSources.PotionsActive(p)).Append(" / ");
                StatsMath.Digits(ps.V, StatsSources.PotionSlots(p));
            }
            // Potion strength (Epic Loot "PotionPotency", what MorgottTweaks scales potion effects by): next to the
            // potion slots, as both are about potions.
            if (StatsSources.EpicLoot) Percent(Row(c, Kind.Pair, "potency", "$runeskin_stats_potency").V, StatsSources.El(p, "PotionPotency"), true, true);
        }

        // ---- 6 rest (Player.GetComfortLevel :2001; SE "Rested" remaining time) ----

        // Rested (SE "Rested": time left, its regeneration bonus) or, while resting, the time until it starts (SE
        // "Resting" remaining); comfort level now (Player.GetComfortLevel :2001) and what this spot gives with its
        // sources, computed like SE_Rested.CalculateComfortLevel (SE_Rested.cs:68-90): 1 + shelter + the best piece of
        // each comfort group / name within 10 m (Piece.GetAllComfortPiecesInRadius).
        private void Rest(StatsWindow.Card c)
        {
            var p = _p;
            var se = p.GetSEMan();
            var rested = se.GetStatusEffect(SEMan.s_statusEffectRested);
            var resting = se.GetStatusEffect(SEMan.s_statusEffectResting);
            var r = Row(c, Kind.TallSub, "clock", "$runeskin_stats_rested");
            if (rested)
            {
                StatsMath.AppendClock(r.V, rested.GetRemaningTime()).Append(' ').Append(Loc("$runeskin_stats_left"));
                RestedBonus(r.S, rested as SE_Stats);
            }
            else
            {
                r.V.Append(Loc("$runeskin_stats_not_rested"));
                if (resting && resting.m_ttl > 0f)
                {
                    r.S.Append(Loc("$runeskin_stats_rested_in")).Append(' ');
                    StatsMath.AppendClock(r.S, resting.GetRemaningTime());
                }
                else RestedBonus(r.S, ObjectDB.instance ? ObjectDB.instance.GetStatusEffect(SEMan.s_statusEffectRested) as SE_Stats : null);
            }
            var here = ComfortHere(p);
            var comfort = Row(c, Kind.PairSub, "bed", "$runeskin_stats_comfort");
            StatsMath.Digits(comfort.V, p.GetComfortLevel());
            comfort.S.Append(Loc("$runeskin_stats_comfort_here")).Append(' ');
            StatsMath.Digits(comfort.S, here);
            // Sources of this spot's comfort.
            if (p.InShelter()) Source(c, "$runeskin_stats_shelter", 1);
            for (var i = 0; i < _comfortCount; i++) Source(c, _comfortPieces[i].m_name, _comfortPieces[i].GetComfort());
        }

        private void Source(StatsWindow.Card c, string name, int value)
        {
            var r = Row(c, Kind.Note, "bed", null);
            StatsMath.Open(r.L, 1, GoodHex, BadHex);
            StatsMath.AppendInt(r.L, value, true);
            StatsMath.Close(r.L, 1);
            r.L.Append(' ').Append(Loc(name));
        }

        // "+50% regen" style line from the rested effect's own multipliers (SE_Stats m_staminaRegenMultiplier).
        private void RestedBonus(StringBuilder sb, SE_Stats st)
        {
            if (!st || Mathf.Abs(st.m_staminaRegenMultiplier - 1f) < 0.005f) return;
            StatsMath.Open(sb, 1, GoodHex, BadHex);
            StatsMath.AppendInt(sb, (st.m_staminaRegenMultiplier - 1f) * 100f, true).Append("% ");
            StatsMath.Close(sb, 1);
            sb.Append(Loc("$runeskin_stats_regen_stamina_short"));
        }

        private static readonly List<Piece> ComfortScratch = new List<Piece>(32);
        private readonly Piece[] _comfortPieces = new Piece[16];
        private int _comfortCount;

        private static readonly System.Comparison<Piece> ComfortOrder = (x, y) =>
        {
            if (x.m_comfortGroup != y.m_comfortGroup) return x.m_comfortGroup.CompareTo(y.m_comfortGroup);
            var a = x.GetComfort();
            var b = y.GetComfort();
            if (a != b) return b.CompareTo(a);
            return string.CompareOrdinal(y.m_name, x.m_name);
        };

        private int ComfortHere(Player p)
        {
            _comfortCount = 0;
            var level = 1;
            if (!p.InShelter()) return level;
            level++;
            ComfortScratch.Clear();
            Piece.GetAllComfortPiecesInRadius(p.transform.position, 10f, ComfortScratch);
            ComfortScratch.Sort(ComfortOrder);
            for (var i = 0; i < ComfortScratch.Count; i++)
            {
                var piece = ComfortScratch[i];
                if (i > 0)
                {
                    var prev = ComfortScratch[i - 1];
                    if ((piece.m_comfortGroup != Piece.ComfortGroup.None && piece.m_comfortGroup == prev.m_comfortGroup) || piece.m_name == prev.m_name) continue;
                }
                level += piece.GetComfort();
                if (_comfortCount < _comfortPieces.Length) _comfortPieces[_comfortCount++] = piece;
            }
            return level;
        }
        // ---- 7 combat (current weapon: ItemData.GetDamage :673, m_backstabBonus; Epic Loot weapon effects) ----

        private void Combat(StatsWindow.Card c)
        {
            var p = _p;
            var weapon = p.GetCurrentWeapon();
            var name = Row(c, Kind.Name, "sword", null);
            // Bare hands are Humanoid.m_unarmedWeapon (its m_name is a plain English "Unarmed", not a token).
            var unarmed = weapon == null || (p.m_unarmedWeapon && weapon == p.m_unarmedWeapon.m_itemData);
            name.L.Append(unarmed ? Loc("$runeskin_stats_unarmed") : Loc(weapon.m_shared.m_name));
            var dmg = Explained(Row(c, Kind.Wide, "flame", "$runeskin_stats_damage"), Explain.Damage);
            if (weapon != null)
            {
                var d = weapon.GetDamage();
                var n = 0;
                n = Damage(dmg.V, n, d.m_blunt, "$inventory_blunt");
                n = Damage(dmg.V, n, d.m_slash, "$inventory_slash");
                n = Damage(dmg.V, n, d.m_pierce, "$inventory_pierce");
                n = Damage(dmg.V, n, d.m_chop, "$inventory_chop");
                n = Damage(dmg.V, n, d.m_pickaxe, "$inventory_pickaxe");
                n = Damage(dmg.V, n, d.m_fire, "$inventory_fire");
                n = Damage(dmg.V, n, d.m_frost, "$inventory_frost");
                n = Damage(dmg.V, n, d.m_lightning, "$inventory_lightning");
                n = Damage(dmg.V, n, d.m_poison, "$inventory_poison");
                n = Damage(dmg.V, n, d.m_spirit, "$inventory_spirit");
                if (n == 0) dmg.V.Append('0');
                // Damage per second, an estimate ("≈"): the average hit (base x the mean of the weapon skill's random
                // factor, Skills.GetRandomSkillFactor :188: Lerp(0.4, 1, skill) +-0.15) over the primary attack's
                // animation length (the animator clip named like Attack.m_attackAnimation), faster with Epic Loot attack
                // speed. Vanilla has no per-weapon attack rate number; without a matching clip only the per-hit line shows.
                // Measured swing of this attack (AttackTimer, includes the real animation speed); else the clip length
                // scaled by Epic Loot attack speed.
                var measured = AttackTimer.Get(weapon.m_shared.m_attack?.m_attackAnimation);
                var duration = measured > 0f ? measured : AttackDuration(p, weapon);
                if (duration > 0.05f)
                {
                    var hit = MeanHit(p, weapon);
                    var speed = measured > 0f ? 1f : 1f + StatsSources.El(p, "ModifyAttackSpeed", 0.01f);
                    var dps = Explained(Row(c, Kind.Pair, "flame", "$runeskin_stats_dps"), Explain.Dps);
                    dps.V.Append("\u2248 ");
                    StatsMath.AppendDecimal(dps.V, hit * speed / duration, _comma, false);
                }
            }
            else dmg.V.Append('—');
            if (weapon != null) AttackRows(c, p, weapon);
            Percent(Row(c, Kind.Pair, "attackspeed", "$runeskin_stats_attack_speed").V, StatsSources.El(p, "ModifyAttackSpeed"), true, true);
            var back = Row(c, Kind.Pair, "backstab", "$runeskin_stats_backstab");
            if (weapon != null)
            {
                back.V.Append('×');
                StatsMath.AppendCompact(back.V, weapon.m_shared.m_backstabBonus * (1f + StatsSources.El(p, "ModifyBackstab", 0.01f)), _comma);
            }
            else back.V.Append('—');
            Percent(Row(c, Kind.Pair, "double", "$runeskin_stats_double").V, StatsSources.El(p, "ChanceDoubleDamage"), true, false);
            Percent(Row(c, Kind.Pair, "lifesteal", "$runeskin_stats_lifesteal").V, StatsSources.El(p, "LifeSteal"), true, false);
            Percent(Row(c, Kind.Pair, "stagger", "$runeskin_stats_stagger").V, StatsSources.El(p, "ModifyStaggerDamage"), true, true);
            // No gear movement / carry-weight notes here: they explain the movement card's values (0.23.0 live report:
            // "-5% скорость (Громокровный меч)" under the combat rows repeated the walk / run figures). The walk / run rows'
            // breakdown lists every worn item's m_movementModifier (x1.5 running, Player.GetRunSpeedFactor :7144-7147)
            // and sums to the shown total (StatsExplain.Speed); the weight row names the carry bonus and its source.
        }

        private static StatsWindow.Row Explained(StatsWindow.Row r, Explain explain)
        {
            r.Explain = explain;
            return r;
        }

        // Stamina per swing of the primary attack (Attack.GetAttackStamina :468-487) and, when the weapon has a secondary
        // attack that differs (its own damage multiplier or stamina), its mean hit.
        private void AttackRows(StatsWindow.Card c, Player p, ItemDrop.ItemData weapon)
        {
            var primary = weapon.m_shared.m_attack;
            if (primary != null && primary.m_attackStamina > 0f)
                Cost(Explained(Row(c, Kind.Pair, "bolt", "$runeskin_stats_attack_stamina"), Explain.AttackStamina).V, AttackStaminaCost(p, weapon, primary), primary.m_attackStamina);
            var second = weapon.m_shared.m_secondaryAttack;
            if (second == null || string.IsNullOrEmpty(second.m_attackAnimation)) return;
            if (primary != null && Mathf.Approximately(second.m_damageMultiplier, primary.m_damageMultiplier) && Mathf.Approximately(second.m_attackStamina, primary.m_attackStamina)) return;
            var r = Row(c, Kind.Pair, "sword", "$runeskin_stats_secondary");
            r.Explain = Explain.AttackStamina;
            r.Arg = 1;
            r.V.Append("\u2248 ");
            StatsMath.AppendInt(r.V, MeanHit(p, weapon, second.m_damageMultiplier), false);
        }
        private RuntimeAnimatorController _clipsOf;
        private readonly Dictionary<string, float> _clips = new Dictionary<string, float>(256);
        private readonly Dictionary<string, float> _attackLength = new Dictionary<string, float>(32);

        public string LastClip { get; private set; } = "";

        // Length (s) of the animator clip of the weapon's primary attack: exact clip name, else the first clip whose
        // name contains the attack animation name (chained attacks: the first of the chain). Cached per controller.
        private float AttackDuration(Player p, ItemDrop.ItemData weapon)
        {
            var anim = weapon?.m_shared.m_attack?.m_attackAnimation;
            var animator = p.m_animator;
            if (string.IsNullOrEmpty(anim) || !animator || !animator.runtimeAnimatorController) return 0f;
            var ctrl = animator.runtimeAnimatorController;
            if (ctrl != _clipsOf)
            {
                _clipsOf = ctrl;
                _clips.Clear();
                _attackLength.Clear();
                foreach (var clip in ctrl.animationClips)
                    if (clip && !_clips.ContainsKey(clip.name)) _clips[clip.name] = clip.length;
            }
            if (_attackLength.TryGetValue(anim, out var len)) return len;
            len = 0f;
            LastClip = "";
            if (_clips.TryGetValue(anim, out var exact)) { len = exact; LastClip = anim; }
            else
                foreach (var pair in _clips)
                    if (pair.Key.IndexOf(anim, System.StringComparison.OrdinalIgnoreCase) >= 0) { len = pair.Value; LastClip = pair.Key; break; }
            _attackLength[anim] = len;
            Plugin.Log.LogInfo($"[RuneSkin] stats: attack '{anim}' -> clip '{LastClip}' {len:0.00} s");
            if (len <= 0f)
            {
                var sample = new System.Text.StringBuilder(512);
                foreach (var key in _clips.Keys)
                    if (key.IndexOf("attack", System.StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("swing", System.StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("punch", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        if (sample.Length < 480) sample.Append(key).Append(", ");
                Plugin.Log.LogInfo($"[RuneSkin] stats: {_clips.Count} clips, attack-like: {sample}");
            }
            return len;
        }

        private int Damage(StringBuilder sb, int n, float value, string type)
        {
            if (value < 0.5f) return n;
            if (n > 0) sb.Append(" + ");
            StatsMath.AppendInt(sb, value, false).Append(' ').Append(Lower(type));
            return n + 1;
        }

        // ---- 8 MorgottTweaks: energy shield (EnergyShieldInfo), slots, potion potency (Epic Loot "PotionPotency") ----

        private void Morgott(StatsWindow.Card c)
        {
            var p = _p;
            float cur = 0f, max = 0f, rate = 0f, delay = 0f, breakDelay = 0f;
            var es = StatsSources.EnergyShield != null && StatsSources.EnergyShield(p, out cur, out max, out rate, out delay, out breakDelay);
            var row = Row(c, Kind.Pair, "es", "$runeskin_stats_es");
            var bar = _w.Next(c, Kind.Bar);
            bar.BarToken = "sprite.stats.bar.es";
            if (es)
            {
                StatsMath.Digits(row.V, Mathf.CeilToInt(cur)).Append(" / ");
                StatsMath.Digits(row.V, Mathf.CeilToInt(max));
                bar.FillShare = max > 0f ? cur / max : 0f;
            }
            else
            {
                row.V.Append('—');
                bar.FillShare = 0f;
            }
            var re = Row(c, Kind.Pair, "recharge", "$runeskin_stats_recharge");
            if (es)
            {
                Rate(re.V, rate);
                re.V.Append(' ').Append(Loc("$runeskin_stats_after")).Append(' ');
                StatsMath.AppendDecimal(re.V, delay, _comma, false).Append(' ').Append(Loc("$runeskin_stats_sec"));
            }
            else re.V.Append('—');
            var br = Row(c, Kind.Pair, "breakdelay", "$runeskin_stats_break_delay");
            if (es) StatsMath.AppendCompact(br.V, breakDelay, _comma).Append(' ').Append(Loc("$runeskin_stats_sec"));
            else br.V.Append('—');
        }
    }
}
