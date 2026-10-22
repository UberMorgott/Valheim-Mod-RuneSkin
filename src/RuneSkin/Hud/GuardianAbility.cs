using System;
using RuneSkin.API;
using UnityEngine;

namespace RuneSkin.PlayerHud
{
    // Guardian (boss) power as an ability-zone slot (first cell of the zone, mockup hud-4M3), registered through the same
    // RuneSkin.API registry other mods use. Data = vanilla Hud.UpdateGuardianPower's source Player.GetGuardianPowerHUD
    // (Player.cs:6313: m_guardianSE + m_guardianPowerCooldown); active = the power's own SE in SEMan (ring = remaining /
    // ttl), cooldown sweep = left / m_cooldown. Key label = the vanilla "GP" binding (Player.cs:993, default F), read
    // live every 2 s. Click = Player.StartGuardianPower (vanilla's gp button, Hud.OnGuardianPowerClicked). The active SE is
    // kept out of the status rows (ActiveHash).
    public static class GuardianAbility
    {
        public static int ActiveHash { get; private set; }
        private static IDisposable _slot;
        private static string _key;
        private static float _keyAt;

        public static void Register()
        {
            if (_slot != null) return;
            _slot = Skin.RegisterAbilitySlot(EpicLootBridge.Owner, new AbilitySlotDescriptor
            {
                Id = "guardian.power",
                Order = 10, // first cell (mockup hud-4M3: F guardian, then the Epic Loot set skill)
                Round = false,
                GetState = State,
                Activate = p => p.StartGuardianPower()
            });
        }

        // Latin key label from the binding path ("<Keyboard>/f" -> "F"): GetBoundKeyString prints the key in the active
        // keyboard layout (Russian layout: F -> "А"); key hints stay Latin. Reflection (no Input System reference),
        // called every 2 s only.
        public static string KeyLabel(string button)
        {
            try
            {
                if (ZInput.instance == null) return null;
                if (!(HarmonyLib.AccessTools.Field(typeof(ZInput), "m_buttons")?.GetValue(ZInput.instance) is System.Collections.IDictionary buttons) || !buttons.Contains(button)) return null;
                var def = buttons[button];
                if (def == null) return null;
                var action = def.GetType().GetProperty("ButtonAction")?.GetValue(def);
                if (!(action?.GetType().GetProperty("bindings")?.GetValue(action) is System.Collections.IEnumerable bindings)) return null;
                foreach (var b in bindings)
                {
                    var path = b.GetType().GetProperty("effectivePath")?.GetValue(b) as string;
                    if (string.IsNullOrEmpty(path)) continue;
                    var key = path.Substring(path.LastIndexOf('/') + 1);
                    return path.StartsWith("<Mouse>", StringComparison.Ordinal) ? "M" + key.Replace("Button", "") : EpicLootBridge.Label(key);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[RuneSkin] key label for {button}: {e.Message}"); }
            return null;
        }

        private static AbilitySlotState State(Player p)
        {
            p.GetGuardianPowerHUD(out var se, out var cooldown);
            if (!se)
            {
                ActiveHash = 0;
                return default;
            }
            if (Time.unscaledTime >= _keyAt && ZInput.instance != null)
            {
                _keyAt = Time.unscaledTime + 2f;
                _key = KeyLabel("GP");
            }
            var hash = se.NameHash();
            ActiveHash = hash;
            var active = p.GetSEMan().GetStatusEffect(hash);
            var s = new AbilitySlotState { Valid = true, Icon = se.m_icon, Key = _key };
            if (active)
            {
                s.ActiveLeft = StatusSource.Remaining(active);
                s.ActiveTotal = active.m_ttl;
            }
            else if (cooldown > 0f)
            {
                s.CooldownLeft = cooldown;
                s.CooldownTotal = se.m_cooldown;
            }
            return s;
        }
    }
}
