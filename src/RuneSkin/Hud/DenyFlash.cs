using System;

namespace RuneSkin.PlayerHud
{
    // "Not enough" warning pulse of one bar (pure, unit-tested). Vanilla signals a refused action through
    // Hud.StaminaBarEmptyFlash / EitrBarEmptyFlash (Hud.cs:1729-1745: the "NoStamina" animator trigger, set only while the
    // bar is not already in that state) and Hud.FlashHealthBar (attack health cost, Attack.cs:400-404). Trigger mirrors that
    // gate: a call while a pulse runs is ignored, so a per-frame caller (run / block with an empty bar) restarts nothing.
    public sealed class DenyFlash
    {
        public const float NeverAt = -100f;

        public float Duration { get; }
        public int Pulses { get; }
        public float StartedAt { get; private set; } = NeverAt;
        public int Count { get; private set; }

        public DenyFlash(float duration = 0.6f, int pulses = 3)
        {
            Duration = duration > 0f ? duration : 0.6f;
            Pulses = Math.Max(1, pulses);
        }

        public bool Active(float now) => now >= StartedAt && now - StartedAt < Duration;

        // Starts a pulse at `now` unless one runs; true = started.
        public bool Trigger(float now)
        {
            if (Active(now)) return false;
            StartedAt = now;
            Count++;
            return true;
        }

        // Warning strength 0..1: Pulses sin^2 humps over Duration (0 at start, between pulses and at the end).
        public float Intensity(float now)
        {
            var t = now - StartedAt;
            if (t < 0f || t >= Duration) return 0f;
            var s = Math.Sin(Math.PI * Pulses * t / Duration);
            return (float)(s * s);
        }

        // Shared cooldown of the error sound across all bars: true = may play at `now` (and records it).
        public static bool Gate(ref float lastAt, float now, float cooldown)
        {
            if (now >= lastAt && now - lastAt < cooldown) return false;
            lastAt = now;
            return true;
        }
    }
}
