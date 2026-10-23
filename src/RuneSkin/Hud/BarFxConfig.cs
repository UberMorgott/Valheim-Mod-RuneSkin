using BepInEx.Configuration;
using RuneSkin.Theme;
using UnityEngine;

namespace RuneSkin.PlayerHud
{
    // [HudBars] config of the living bar fills (barfx, user-approved v3 2026-10-23): per bar on/off + intensity, one
    // shared speed. Read live by BarView (material written only when a value changes).
    public static class BarFxConfig
    {
        public static ConfigEntry<bool> HealthOn { get; private set; }
        public static ConfigEntry<float> HealthIntensity { get; private set; }
        public static ConfigEntry<bool> StaminaOn { get; private set; }
        public static ConfigEntry<float> StaminaIntensity { get; private set; }
        public static ConfigEntry<bool> EitrOn { get; private set; }
        public static ConfigEntry<float> EitrIntensity { get; private set; }
        public static ConfigEntry<float> Speed { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            HealthOn = config.Bind("HudBars", "HealthEffects", true,
                "Living health bar: blood flow, clotted lost segment, splash on damage, wave on heal. Off = the plain glossy fill.");
            HealthIntensity = config.Bind("HudBars", "HealthIntensity", 1f, new ConfigDescription(
                "Strength of the health bar effects (0 = plain fill, 1 = approved look).", new AcceptableValueRange<float>(0f, 1.5f)));
            StaminaOn = config.Bind("HudBars", "StaminaEffects", true,
                "Living stamina bar: amber energy flow, sparkles while regenerating, dim flicker when exhausted. Off = the plain glossy fill.");
            StaminaIntensity = config.Bind("HudBars", "StaminaIntensity", 1f, new ConfigDescription(
                "Strength of the stamina bar effects (0 = plain fill, 1 = approved look).", new AcceptableValueRange<float>(0f, 1.5f)));
            EitrOn = config.Bind("HudBars", "EitrEffects", true,
                "Living eitr bar: glowing core, pulsing veins, rune glints, crackle at the end, pulse on spend. Off = the plain glossy fill.");
            EitrIntensity = config.Bind("HudBars", "EitrIntensity", 1f, new ConfigDescription(
                "Strength of the eitr bar effects (0 = plain fill, 1 = approved look).", new AcceptableValueRange<float>(0f, 1.5f)));
            Speed = config.Bind("HudBars", "EffectSpeed", 1f, new ConfigDescription(
                "Animation speed of the living bar fills (all bars).", new AcceptableValueRange<float>(0.25f, 3f)));
        }

        // Live on/off and intensity of a pool's effects (Plain = always off).
        public static void For(BarFxPool pool, out bool on, out float intensity)
        {
            switch (pool)
            {
                case BarFxPool.Blood: on = HealthOn?.Value ?? true; intensity = HealthIntensity?.Value ?? 1f; break;
                case BarFxPool.Energy: on = StaminaOn?.Value ?? true; intensity = StaminaIntensity?.Value ?? 1f; break;
                case BarFxPool.Arcane: on = EitrOn?.Value ?? true; intensity = EitrIntensity?.Value ?? 1f; break;
                default: on = false; intensity = 0f; break;
            }
        }

        public static BarFxTuning Tuning => new BarFxTuning
        {
            HitShare = ThemeTokens.GetFloat("share.hud.bar.fx.hit", 0.01f),
            HealShare = ThemeTokens.GetFloat("share.hud.bar.fx.heal", 0.04f),
            PulseShare = ThemeTokens.GetFloat("share.hud.bar.fx.pulse", 0.03f),
            ExhaustOn = ThemeTokens.GetFloat("share.hud.bar.fx.exhaust.on", 0.005f),
            ExhaustOff = ThemeTokens.GetFloat("share.hud.bar.fx.exhaust.off", 0.2f),
            RegenHold = ThemeTokens.GetFloat("time.hud.bar.fx.regen", 0.35f),
        };

        // The shared effect clock (seconds since the HUD started, unwrapped: no wrap jump; float precision stays below
        // 1 ms for ~4.5 h and 8 ms at ~36 h). Written once per frame as a shader global; events use the same clock.
        public static readonly int ClockId = Shader.PropertyToID("_RuneSkinFxClock");
        private static double _start = -1;
        public static float Now { get; private set; }

        // Event counters since start (harness check barfx-events reads them by reflection; diagnostics only).
        public static int Hits { get; private set; }
        public static int Heals { get; private set; }
        public static int Pulses { get; private set; }
        public static int RegenChanges { get; private set; }
        public static int ExhaustChanges { get; private set; }

        internal static void Count(BarFxState.Changed changed)
        {
            if ((changed & BarFxState.Changed.Hit) != BarFxState.Changed.None) Hits++;
            if ((changed & BarFxState.Changed.Heal) != BarFxState.Changed.None) Heals++;
            if ((changed & BarFxState.Changed.Pulse) != BarFxState.Changed.None) Pulses++;
            if ((changed & BarFxState.Changed.Regen) != BarFxState.Changed.None) RegenChanges++;
            if ((changed & BarFxState.Changed.Exhaust) != BarFxState.Changed.None) ExhaustChanges++;
        }

        public static void TickClock()
        {
            var t = Time.unscaledTimeAsDouble;
            if (_start < 0) _start = t;
            Now = (float)(t - _start);
            Shader.SetGlobalFloat(ClockId, Now);
        }
    }
}
