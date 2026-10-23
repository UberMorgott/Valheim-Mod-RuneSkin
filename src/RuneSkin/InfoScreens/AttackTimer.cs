using System.Collections.Generic;
using UnityEngine;

namespace RuneSkin.InfoScreens
{
    // Measured attack cycle of the local player per attack animation (Attack.m_attackAnimation, primary / secondary):
    // seconds from Humanoid.StartAttack to the end of the attack animation state (Humanoid.InAttack false), averaged
    // (EMA). Includes the real animation speed (skills, Epic Loot attack speed, status effects). The stats window's
    // damage-per-second line uses it once the weapon was swung; no allocation after the first sample per animation.
    internal static class AttackTimer
    {
        private static readonly Dictionary<string, float> Seconds = new Dictionary<string, float>(32);
        private static string _anim;
        private static float _start;
        private static bool _seen;

        public static bool Running => _anim != null;

        public static void Started(Attack attack, bool secondary)
        {
            if (attack == null || string.IsNullOrEmpty(attack.m_attackAnimation)) { _anim = null; return; }
            _anim = attack.m_attackAnimation;
            _start = Time.time;
            _seen = false;
        }

        public static void Seen() => _seen = true;

        public static void Ended()
        {
            if (!_seen)
            {
                // Not in the attack state yet; give up only when it never starts (blocked attack).
                if (Time.time - _start > 1f) _anim = null;
                return;
            }
            var d = Time.time - _start;
            if (_anim != null && d > 0.1f && d < 6f)
                Seconds[_anim] = Seconds.TryGetValue(_anim, out var old) ? Mathf.Lerp(old, d, 0.3f) : d;
            _anim = null;
        }

        public static float Get(string anim) => anim != null && Seconds.TryGetValue(anim, out var s) ? s : 0f;
    }
}
