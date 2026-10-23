using System;

namespace RuneSkin.PlayerHud
{
    // Pool look of a HUD strip bar on the RuneSkin/UI Bar shader (its _Pool property).
    public enum BarFxPool
    {
        Plain = -1,
        Blood = 0,
        Energy = 1,
        Arcane = 2,
    }

    // Thresholds of the bar events (theme share.hud.bar.fx.* / time.hud.bar.fx.regen).
    public struct BarFxTuning
    {
        public float HitShare;      // HP drop >= this share of max in one sample = damage event (splash, slosh)
        public float HealShare;     // HP rise >= this share of max in one sample = heal event (wave)
        public float PulseShare;    // eitr drop >= this share of max in one sample = spend pulse
        public float ExhaustOn;     // stamina share at or below which the bar turns exhausted
        public float ExhaustOff;    // stamina share at or above which exhaustion ends
        public float RegenHold;     // seconds the regen sparkle stays after the last rise

        public static BarFxTuning Default => new BarFxTuning
        {
            HitShare = 0.01f,
            HealShare = 0.04f,
            PulseShare = 0.03f,
            ExhaustOn = 0.005f,
            ExhaustOff = 0.2f,
            RegenHold = 0.35f,
        };
    }

    // Pure event state of one living bar fill (unit-tested). Step samples the value once per frame and turns real
    // changes into event clocks (the shader animates from the clock, so each event is written once, not per frame):
    // blood = hit / heal, energy = regenerating / exhausted, arcane = spend pulse. A max change (food eaten / expired)
    // resyncs without an event.
    public sealed class BarFxState
    {
        public const float Never = -100f;

        [Flags]
        public enum Changed
        {
            None = 0,
            Hit = 1,
            Heal = 2,
            Pulse = 4,
            Regen = 8,
            Exhaust = 16,
        }

        public float HitAt { get; private set; } = Never;
        public float HitSize { get; private set; }
        public float HealAt { get; private set; } = Never;
        public float PulseAt { get; private set; } = Never;
        public bool Regen { get; private set; }
        public bool Exhaust { get; private set; }

        private float _last = -1f, _lastMax = -1f, _regenLeft;

        // Forget the last sample (pool switched, bar re-shown): the next Step only records.
        public void Resync()
        {
            _last = -1f;
            _regenLeft = 0f;
        }

        public Changed Step(BarFxPool pool, float cur, float max, float now, float dt, BarFxTuning tune)
        {
            var changed = Changed.None;
            if (max <= 0f || pool == BarFxPool.Plain)
            {
                Resync();
                return SetFlags(false, false, changed);
            }
            var share = cur / max;
            if (_last < 0f || Math.Abs(max - _lastMax) > 0.5f)
            {
                _last = cur;
                _lastMax = max;
                return pool == BarFxPool.Energy ? SetFlags(Regen, ExhaustStep(share), changed) : changed;
            }
            var delta = (cur - _last) / max;
            _last = cur;
            switch (pool)
            {
                case BarFxPool.Blood:
                    if (delta <= -tune.HitShare) { HitAt = now; HitSize = -delta; changed |= Changed.Hit; }
                    else if (delta >= tune.HealShare) { HealAt = now; changed |= Changed.Heal; }
                    return changed;
                case BarFxPool.Arcane:
                    if (delta <= -tune.PulseShare) { PulseAt = now; changed |= Changed.Pulse; }
                    return changed;
                default:
                    if (delta > 1e-5f) _regenLeft = tune.RegenHold;
                    else _regenLeft = Math.Max(0f, _regenLeft - dt);
                    var regen = _regenLeft > 0f && share < 0.999f;
                    return SetFlags(regen, ExhaustStep(share), changed);
            }

            bool ExhaustStep(float s) => s <= tune.ExhaustOn || Exhaust && s < tune.ExhaustOff;
        }

        private Changed SetFlags(bool regen, bool exhaust, Changed changed)
        {
            if (regen != Regen) { Regen = regen; changed |= Changed.Regen; }
            if (exhaust != Exhaust) { Exhaust = exhaust; changed |= Changed.Exhaust; }
            return changed;
        }
    }
}
