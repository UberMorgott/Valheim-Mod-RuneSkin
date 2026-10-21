using System;

namespace RuneSkin.PlayerHud
{
    // Status-effect taxonomy (augaskin-engine-recon.md "Status-effect HUD" + Codex rounds 1-2): capability (what the
    // effect can do, multi-label, from its fields) -> presentation (strip icon always; stripe only with a real
    // forecast; one tint per bar; one primary frame overlay). Pure: EffectFacts is filled from the live
    // StatusEffect by StatusSource. Unknown semantics = Marker (icon only, never numeric, never "harmless").
    [Flags]
    public enum Capability
    {
        None = 0,
        DamageOverTime = 1,   // pool-based damage with a forecast (SE_Poison, SE_Burning)
        Drain = 2,            // damage / stamina drain without a defensible remaining amount
        RegenUp = 4,
        RegenDown = 8,        // regen suppression (Cold, Freezing, Wet ...)
        Restore = 16,         // heal / stamina / eitr over time or up front
        Control = 32,         // slow, freeze, root
        Defense = 64,
        Offense = 128,
        Utility = 256,
        Vulnerable = 512,     // resistance turned into weakness
        Marker = 1024         // no readable numeric effect
    }

    public enum FrameOverlay
    {
        None = 0,
        Wet = 1,
        Frost = 2
    }

    // Damage type of a forecast stripe; order = fixed stripe order next to the fill end.
    public enum DotKind
    {
        None = 0,
        Poison = 1,
        Fire = 2,
        Spirit = 3,
        Other = 4
    }

    // Field values read from one live StatusEffect (neutral = no effect: regen multipliers 1, speed 0).
    public struct EffectFacts
    {
        public string ClassName;
        public string Name;              // m_name (diagnostics)
        public bool IsWet, IsFrost, IsFreezing, IsCold, IsBurning, IsSpirit, IsPoison, IsSmoked, IsRested;
        public float HealthOverTime, HealthPerTick, HealthUpFront, StaminaUpFront, EitrUpFront;
        public float StaminaOverTime, EitrOverTime, StaminaDrainPerSec;
        public float HealthRegen, StaminaRegen, EitrRegen;
        public float Speed;
        public int ResistUps, ResistDowns;
        public float DamageModifiers;    // sum of percentage damage modifiers
        public float RaiseSkill, Carry, Stealth, Noise, FallDamage, StaminaUse;
        public float Armor;
        public bool HasForecast;         // a readable remaining-damage pool exists
    }

    public struct EffectClass
    {
        public Capability Caps;
        public bool Harmful;
        public int Category;             // sort: 0 DoT / control, 1 drain / regen suppression, 2 other harmful, 3 buff, 4 marker
        public DotKind Dot;              // stripe kind when Caps has DamageOverTime
        public string TintBar;           // "health" / "stamina" / "eitr" or null
        public string TintToken;         // colour token of the tint
        public int TintPriority;
        public FrameOverlay Overlay;
        public int OverlayPriority;
    }

    public static class EffectTaxonomy
    {
        private const float Eps = 0.0001f;

        public static EffectClass Classify(in EffectFacts f)
        {
            var caps = Capability.None;
            var dot = DotKind.None;
            if (f.IsPoison || f.ClassName == "SE_Poison") { caps |= f.HasForecast ? Capability.DamageOverTime : Capability.Drain; dot = DotKind.Poison; }
            if (f.ClassName == "SE_Burning")
            {
                caps |= f.HasForecast ? Capability.DamageOverTime : Capability.Drain;
                dot = f.IsSpirit ? DotKind.Spirit : DotKind.Fire;
            }
            if (f.IsSmoked || f.ClassName == "SE_Smoke") caps |= Capability.Drain;
            // SE_Stats: negative per-tick health is damage; negative health over time is NOT a DoT (SE_Stats.cs:162,
            // base only inits positive HoT) -> drain at most.
            if (f.HealthPerTick < -Eps) caps |= Capability.Drain;
            if (f.HealthPerTick > Eps || f.HealthOverTime > Eps || f.StaminaOverTime > Eps || f.EitrOverTime > Eps ||
                f.HealthUpFront > Eps || f.StaminaUpFront > Eps || f.EitrUpFront > Eps)
                caps |= Capability.Restore;
            if (f.StaminaOverTime < -Eps || f.EitrOverTime < -Eps || f.StaminaDrainPerSec > Eps) caps |= Capability.Drain;
            if (Up(f.HealthRegen) || Up(f.StaminaRegen) || Up(f.EitrRegen)) caps |= Capability.RegenUp;
            if (Down(f.HealthRegen) || Down(f.StaminaRegen) || Down(f.EitrRegen)) caps |= Capability.RegenDown;
            if (f.Speed < -Eps || f.IsFrost) caps |= Capability.Control;
            if (f.ResistUps > 0 || f.Armor > Eps) caps |= Capability.Defense;
            if (f.ResistDowns > 0) caps |= Capability.Vulnerable;
            if (f.DamageModifiers > Eps) caps |= Capability.Offense;
            if (Math.Abs(f.RaiseSkill) > Eps || f.Carry > Eps || f.Stealth > Eps || f.Noise < -Eps || f.FallDamage < -Eps ||
                f.StaminaUse < -Eps || f.Speed > Eps)
                caps |= Capability.Utility;
            if (caps == Capability.None) caps = Capability.Marker;

            var harmful = (caps & (Capability.DamageOverTime | Capability.Drain | Capability.RegenDown | Capability.Control |
                                   Capability.Vulnerable)) != Capability.None || f.IsWet || f.IsCold || f.IsFreezing;
            var c = new EffectClass { Caps = caps, Harmful = harmful, Dot = dot };
            c.Category = (caps & (Capability.DamageOverTime | Capability.Control)) != Capability.None ? 0
                : (caps & (Capability.Drain | Capability.RegenDown)) != Capability.None ? 1
                : harmful ? 2
                : (caps & Capability.Marker) != Capability.None ? 4
                : 3;

            // One tint per bar (priority picks): DoTs tint health in their colour.
            switch (dot)
            {
                case DotKind.Poison: Tint(ref c, "health", "color.dot.poison", 30); break;
                case DotKind.Fire: Tint(ref c, "health", "color.dot.fire", 28); break;
                case DotKind.Spirit: Tint(ref c, "health", "color.dot.spirit", 26); break;
            }
            if (c.TintBar == null && f.IsFreezing) Tint(ref c, "health", "color.dot.frost", 20);

            // One primary frame overlay: Freezing > Frost > Cold > Wet.
            if (f.IsFreezing) { c.Overlay = FrameOverlay.Frost; c.OverlayPriority = 40; }
            else if (f.IsFrost) { c.Overlay = FrameOverlay.Frost; c.OverlayPriority = 30; }
            else if (f.IsCold) { c.Overlay = FrameOverlay.Frost; c.OverlayPriority = 20; }
            else if (f.IsWet) { c.Overlay = FrameOverlay.Wet; c.OverlayPriority = 10; }
            return c;
        }

        private static bool Up(float regen) => regen > 1f + Eps;
        private static bool Down(float regen) => regen < 1f - Eps && regen >= 0f;

        private static void Tint(ref EffectClass c, string bar, string token, int priority)
        {
            c.TintBar = bar;
            c.TintToken = token;
            c.TintPriority = priority;
        }

        // Strip order: category, then priority (higher first), then a stable key.
        public static int Compare(int catA, int prioA, int keyA, int catB, int prioB, int keyB)
        {
            if (catA != catB) return catA.CompareTo(catB);
            if (prioA != prioB) return prioB.CompareTo(prioA);
            return keyA.CompareTo(keyB);
        }
    }
}
