using System;

namespace RuneSkin.PlayerHud
{
    // Drawn fill of a HUD bar (user 2026-10-23: with a small max HP the fill jumped in whole-point steps). The drawn share
    // follows the live share as a critically damped spring (no overshoot, starts and stops softly - no big first-frame
    // step like a plain exponential): `seconds` = time to cover 95 % of a change, frame-rate independent, on the true
    // float value, so the fill edge moves through sub-pixel positions. Pure (unit-tested on net8).
    public static class BarFill
    {
        // shown < 0 = no previous frame (snap). seconds <= 0 = no smoothing (snap). Within 1e-5 of the target with ~no
        // velocity it snaps (no endless tail). vel = share per second, kept by the caller.
        public static float Smooth(float shown, ref float vel, float target, float dt, float seconds)
        {
            if (float.IsNaN(target)) target = 0f;
            target = Math.Max(0f, Math.Min(1f, target));
            if (shown < 0f || seconds <= 0f) { vel = 0f; return target; }
            if (dt <= 0f) return shown;
            // critically damped: x(t) = (1 + w t) e^(-w t) of the gap; 95 % covered at w t = 4.744
            var w = 4.744f / seconds;
            var x = shown - target;
            var e = (float)Math.Exp(-w * dt);
            var t = (vel + w * x) * dt;
            var next = target + (x + t) * e;
            vel = (vel - w * t) * e;
            // never past the target (float error at large dt)
            if ((x > 0f && next < target) || (x < 0f && next > target)) { next = target; vel = 0f; }
            if (Math.Abs(target - next) < 1e-5f && Math.Abs(vel) < 1e-3f) { vel = 0f; return target; }
            return next;
        }
    }
}
