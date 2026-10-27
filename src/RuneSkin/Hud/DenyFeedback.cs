using UnityEngine;

namespace RuneSkin.PlayerHud
{
    // Refused-action feedback on RuneSkin's bars: vanilla's own "not enough" calls (HudAdapter prefixes on
    // Hud.StaminaBarEmptyFlash / EitrBarEmptyFlash / FlashHealthBar; the max-health upgrade flash from
    // Player.SetMaxHealth(float, bool), Player.cs:6152-6158, is not a refusal and is skipped) start a warning pulse on
    // the matching bar (theme color.hud.bar.deny, time.hud.bar.deny, count.hud.bar.deny.pulses) and play one error sound
    // (vanilla has none for this). Sound (Codex call 2026-10-26): the craft-fail sound of a vanilla crafting station
    // (CraftingStation.m_craftItemDoneFailEffects, CraftingStation.cs:54) - only its sound prefab (ZSFX),
    // not the whole EffectList - with a locked door's sound (Door.m_lockedEffects, Door.cs:25) as the fallback; one sound
    // per 0.6 s across all bars. No new audio asset.
    public static class DenyFeedback
    {
        public static readonly DenyFlash Health = Make(), Stamina = Make(), Eitr = Make();

        // Diagnostics for the harness (reflection): sounds started, the last sound's ZSFX, the chosen prefab.
        public static int Sounds { get; private set; }
        public static ZSFX LastSound { get; private set; }
        public static string SoundName { get; private set; }

        private static float _soundAt = DenyFlash.NeverAt;
        private static GameObject _sound;
        private static bool _searched;

        // While Player.SetMaxHealth(float, true) runs: its FlashHealthBar is the upgrade flash.
        internal static bool InMaxHealthUpgrade;

        private static DenyFlash Make() => new DenyFlash(
            Theme.ThemeTokens.GetFloat("time.hud.bar.deny", 0.6f), (int)Theme.ThemeTokens.GetFloat("count.hud.bar.deny.pulses", 3f));

        public static float Now => Time.unscaledTime;

        public static void Deny(DenyFlash bar)
        {
            if (!HudCluster.Instance || !bar.Trigger(Now)) return;
            if (DenyFlash.Gate(ref _soundAt, Now, Theme.ThemeTokens.GetFloat("time.hud.bar.deny.sound", 0.6f))) PlaySound();
        }

        private static void PlaySound()
        {
            var player = Player.m_localPlayer;
            if (!player) return;
            if (!_searched) Find();
            if (!_sound) return;
            // local only: vanilla sfx prefabs carry a ZNetView (EffectList.Create spawns them networked); this cue is the
            // local player's own, so no ZDO is created (ZNetView.m_forceDisableInit, ZNetView.cs:45)
            GameObject go;
            ZNetView.m_forceDisableInit = true;
            try { go = Object.Instantiate(_sound, player.GetHeadPoint(), Quaternion.identity); }
            finally { ZNetView.m_forceDisableInit = false; }
            Object.Destroy(go, 5f);
            LastSound = go.GetComponentInChildren<ZSFX>();
            Sounds++;
        }

        // First sound-only object (a ZSFX with no renderers / lights under it) of a station's craft-fail
        // effects, else of a door's locked effects.
        private static void Find()
        {
            var scene = ZNetScene.instance;
            if (!scene) return;
            _searched = true;
            foreach (var prefab in scene.m_prefabs)
                if (prefab && prefab.TryGetComponent<CraftingStation>(out var station) && Pick(station.m_craftItemDoneFailEffects)) break;
            if (!_sound)
                foreach (var prefab in scene.m_prefabs)
                    if (prefab && prefab.TryGetComponent<Door>(out var door) && Pick(door.m_lockedEffects)) break;
            SoundName = _sound ? _sound.name : null;
            Plugin.Log.LogInfo($"[RuneSkin] deny sound: {SoundName ?? "none (flash only)"}");
        }

        private static bool Pick(EffectList list)
        {
            if (list?.m_effectPrefabs == null) return false;
            foreach (var e in list.m_effectPrefabs)
            {
                var p = e?.m_prefab;
                if (!p || !e.m_enabled) continue;
                var sfx = p.GetComponentInChildren<ZSFX>(true); // ZSFX drives the AudioSource
                if (!sfx) continue;
                // Only the ZSFX's own object, and only if it is sound and nothing else. The whole composite prefab used to be
                // spawned at the head: vanilla fx_UpgradeStation_Fail (bundle c4210710) = lightning particles + lights +
                // child AOE_AREA, an Aoe of 60 lightning, radius 2.42, m_hitFriendly - a real red lightning strike that took
                // the player's health at every empty-bar refusal.
                var go = sfx.gameObject;
                var foreign = Foreign(go);
                if (foreign != null)
                {
                    Plugin.Log.LogInfo($"[RuneSkin] deny sound: skip {p.name} ({foreign})");
                    continue;
                }
                _sound = go;
                return true;
            }
            return false;
        }

        // First component under `go` that is not part of a plain sound object (Transform, AudioSource, ZSFX,
        // TimedDestruction, ZNetView), as "Object/Type"; null = sound only. Allow-list, so visuals (renderers, lights),
        // colliders, Aoe / damage and any other behaviour are all rejected.
        private static string Foreign(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                // AudioSource by name: the project does not reference UnityEngine.AudioModule
                if (!c || c is Transform || c is ZSFX || c is TimedDestruction || c is ZNetView ||
                    c.GetType().FullName == "UnityEngine.AudioSource") continue;
                return c.gameObject.name + "/" + c.GetType().Name;
            }
            return null;
        }
    }
}
