using System;

namespace RuneSkin.PlayerHud
{
    // Pure state of a centre gauge (unit-tested): shown while the value moves or is non-zero and recent, faded out
    // after the idle delay; a held state (full rage, staggered) keeps it.
    public struct GaugeState
    {
        public float Last;       // last sampled value (0..1)
        public float ChangedAt;  // time of the last change
        public float Alpha;      // current opacity 0..1 (x Opacity in the view)

        // One step: value 0..1, hold = keep shown regardless of idle (full rage / staggered), now = time, dt = frame time,
        // fade = seconds for a full fade in / out. Returns the target visibility.
        public bool Step(float value, bool hold, float now, float dt, float idleDelay, float fade)
        {
            if (Math.Abs(value - Last) > 0.002f) ChangedAt = now;
            Last = value;
            var show = hold || value > 0.001f && now - ChangedAt <= idleDelay;
            var rate = fade > 0f ? dt / fade : 1f;
            Alpha = show ? Math.Min(1f, Alpha + rate) : Math.Max(0f, Alpha - rate);
            return show;
        }
    }
}
