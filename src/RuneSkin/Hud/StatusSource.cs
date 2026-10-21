using System;
using System.Collections.Generic;
using RuneSkin.Api;
using RuneSkin.API;
using UnityEngine;

namespace RuneSkin.PlayerHud
{
    // Reads live status effects into EffectFacts (classified once per effect type + hash, cached) and computes pending
    // damage only from real game state: SE_Poison m_damageLeft / m_damagePerHit (SE_Poison.cs:19-23), SE_Burning
    // fire / spirit pools (SE_Burning.cs:11-17), capped by the ticks left before the effect expires (Wet ages Burning
    // 6x while fire is left, SE_Burning.cs:30-34) and scaled like ApplyDamage scales a player's damage
    // (Game.m_localDamgeTakenRate, Character.cs:2432). Registered descriptors (Skin.RegisterStatusEffect) win.
    public static class StatusSource
    {
        private static readonly Dictionary<long, EffectClass> Cache = new Dictionary<long, EffectClass>();
        private static int _revision = -1;

        public static EffectClass Classify(StatusEffect se, out bool registered)
        {
            registered = false;
            if (_revision != Providers.Revision)
            {
                Cache.Clear();
                _revision = Providers.Revision;
            }
            var hash = se.NameHash();
            if (Providers.TryEffect(hash, out var d))
            {
                registered = true;
                return FromDescriptor(se, d);
            }
            var key = ((long)se.GetType().GetHashCode() << 32) ^ (uint)hash;
            if (Cache.TryGetValue(key, out var c)) return c;
            c = EffectTaxonomy.Classify(Facts(se));
            Cache[key] = c;
            return c;
        }

        private static EffectClass FromDescriptor(StatusEffect se, StatusEffectDescriptor d)
        {
            var auto = EffectTaxonomy.Classify(Facts(se));
            var c = new EffectClass
            {
                Caps = auto.Caps,
                Harmful = d.Harmful,
                Category = d.Harmful ? ((d.Channels & EffectChannels.PendingStripe) != EffectChannels.None ? 0 : 2) : 3,
                Dot = (d.Channels & EffectChannels.PendingStripe) != EffectChannels.None && !d.NoForecast ? DotKind.Other : DotKind.None
            };
            if ((d.Channels & EffectChannels.BarTint) != EffectChannels.None && !string.IsNullOrEmpty(d.ColorToken))
            {
                c.TintBar = string.IsNullOrEmpty(d.Bar) ? ResourcePools.Health : d.Bar;
                c.TintToken = d.ColorToken;
                c.TintPriority = d.Priority;
            }
            if ((d.Channels & EffectChannels.Overlay) != EffectChannels.None)
            {
                c.Overlay = auto.Overlay != FrameOverlay.None ? auto.Overlay : FrameOverlay.Frost;
                c.OverlayPriority = d.Priority;
            }
            return c;
        }

        public static EffectFacts Facts(StatusEffect se)
        {
            var hash = se.NameHash();
            var f = new EffectFacts
            {
                ClassName = se.GetType().Name,
                Name = se.m_name,
                IsWet = hash == SEMan.s_statusEffectWet,
                IsFrost = hash == SEMan.s_statusEffectFrost,
                IsFreezing = hash == SEMan.s_statusEffectFreezing,
                IsCold = hash == SEMan.s_statusEffectCold,
                IsBurning = hash == SEMan.s_statusEffectBurning,
                IsSpirit = hash == SEMan.s_statusEffectSpirit,
                IsPoison = hash == SEMan.s_statusEffectPoison,
                IsSmoked = hash == SEMan.s_statusEffectSmoked,
                IsRested = hash == SEMan.s_statusEffectRested,
                HealthRegen = 1f,
                StaminaRegen = 1f,
                EitrRegen = 1f,
                HasForecast = se is SE_Poison || se is SE_Burning
            };
            if (se is SE_Stats s)
            {
                f.HealthOverTime = s.m_healthOverTime;
                f.HealthPerTick = s.m_healthPerTick;
                f.HealthUpFront = s.m_healthUpFront;
                f.StaminaUpFront = s.m_staminaUpFront;
                f.EitrUpFront = s.m_eitrUpFront;
                f.StaminaOverTime = s.m_staminaOverTime;
                f.EitrOverTime = s.m_eitrOverTime;
                f.StaminaDrainPerSec = s.m_staminaDrainPerSec;
                f.HealthRegen = s.m_healthRegenMultiplier;
                f.StaminaRegen = s.m_staminaRegenMultiplier;
                f.EitrRegen = s.m_eitrRegenMultiplier;
                f.Speed = s.m_speedModifier;
                f.Armor = s.m_addArmor;
                f.RaiseSkill = s.m_raiseSkillModifier;
                f.Carry = s.m_addMaxCarryWeight;
                f.Stealth = s.m_stealthModifier;
                f.Noise = s.m_noiseModifier;
                f.FallDamage = s.m_fallDamageModifier;
                f.StaminaUse = s.m_runStaminaDrainModifier + s.m_jumpStaminaUseModifier + s.m_attackStaminaUseModifier +
                               s.m_dodgeStaminaUseModifier + s.m_swimStaminaUseModifier;
                if (s.m_mods != null)
                    foreach (var m in s.m_mods)
                        switch (m.m_modifier)
                        {
                            case HitData.DamageModifier.Resistant:
                            case HitData.DamageModifier.VeryResistant:
                            case HitData.DamageModifier.SlightlyResistant:
                            case HitData.DamageModifier.Immune:
                                f.ResistUps++;
                                break;
                            case HitData.DamageModifier.Weak:
                            case HitData.DamageModifier.VeryWeak:
                            case HitData.DamageModifier.SlightlyWeak:
                                f.ResistDowns++;
                                break;
                        }
                var p = s.m_percentigeDamageModifiers;
                f.DamageModifiers = p.m_damage + p.m_blunt + p.m_slash + p.m_pierce + p.m_chop + p.m_pickaxe + p.m_fire +
                                    p.m_frost + p.m_lightning + p.m_poison + p.m_spirit;
                if (s.m_damageModifier > 1f) f.DamageModifiers += s.m_damageModifier - 1f;
            }
            return f;
        }

        // Pending damage of one effect on the local player (units after the player damage rate), 0 = no forecast.
        public static float Pending(StatusEffect se, out DotKind kind, out float spirit)
        {
            kind = DotKind.None;
            spirit = 0f;
            var rate = Game.m_localDamgeTakenRate;
            if (se is SE_Poison poison)
            {
                kind = DotKind.Poison;
                var ticks = Ticks(se, poison.m_damageInterval, 1f, poison.m_timer);
                return Mathf.Min(poison.m_damageLeft, poison.m_damagePerHit * ticks) * rate;
            }
            if (se is SE_Burning burning)
            {
                var wet = burning.m_fireDamageLeft > 0f && se.m_character && se.m_character.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet);
                var ticks = Ticks(se, burning.m_damageInterval, wet ? 6f : 1f, burning.m_timer);
                var fire = Mathf.Min(burning.m_fireDamageLeft, burning.m_fireDamagePerHit * ticks) * rate;
                spirit = Mathf.Min(burning.m_spiritDamageLeft, burning.m_spiritDamagePerHit * ticks) * rate;
                kind = fire > 0f ? DotKind.Fire : DotKind.Spirit;
                return fire;
            }
            if (Providers.TryEffect(se.NameHash(), out var d) && d.PendingDamage != null && !d.NoForecast)
            {
                try
                {
                    var v = d.PendingDamage(se);
                    if (v.HasValue && v.Value > 0f && !float.IsInfinity(v.Value))
                    {
                        kind = DotKind.Other;
                        return v.Value;
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[RuneSkin] effect forecast {se.name} failed: {e.Message}");
                }
            }
            return 0f;
        }

        // Ticks still to come before the effect ends: one at timer <= 0, then one per interval of remaining real time.
        private static float Ticks(StatusEffect se, float interval, float ageFactor, float timer)
        {
            if (se.m_ttl <= 0f) return float.MaxValue;
            var left = Mathf.Max(0f, se.m_ttl - se.GetDuration()) / Mathf.Max(1f, ageFactor);
            if (interval <= 0f) return 1f;
            if (left < Mathf.Max(0f, timer)) return 0f;
            return 1f + Mathf.Floor((left - Mathf.Max(0f, timer)) / interval);
        }

        // Status-strip timer: the effect's own icon text when its type overrides GetIconText (mod "12%", "5/s";
        // SE_Cozy), else whole seconds of the remaining ttl (StatusEffect.GetIconText :209 shows ttl - duration).
        private static readonly Dictionary<Type, bool> OwnText = new Dictionary<Type, bool>();

        public static bool HasOwnIconText(StatusEffect se)
        {
            var t = se.GetType();
            if (!OwnText.TryGetValue(t, out var own))
            {
                var m = t.GetMethod(nameof(StatusEffect.GetIconText), Type.EmptyTypes);
                own = m != null && m.DeclaringType != typeof(StatusEffect);
                OwnText[t] = own;
            }
            return own;
        }

        public static float Remaining(StatusEffect se) => se.m_ttl > 0f ? se.m_ttl - se.GetDuration() : 0f;
    }
}
