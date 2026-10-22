using System.Collections.Generic;
using HarmonyLib;
using RuneSkin.Ownership;

namespace RuneSkin.PlayerHud
{
    // Family "Hud" (installed only while RuneSkin owns it). Hud.Awake (Hud.cs:379) postfix builds RuneSkin's cluster
    // after every other Awake postfix (EAQS / Epic Loot add their HUD parts there). Hud.UpdateStatusEffects
    // (Hud.cs:1635) is replaced while the cluster exists: its input list (SEMan.GetHUDStatusEffects, Hud.cs:527) goes
    // to the owned status strip, which takes over the vanilla duties (hidden filter, flash, cooldown, icon text,
    // m_isNew acknowledgement). No transpilers; the vanilla bar code runs on, hidden.
    [HarmonyPatch]
    public static class HudAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Hud);

        [HarmonyPatch(typeof(global::Hud), nameof(global::Hud.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(PatchOrder.Hud)]
        private static void Awake_Postfix(global::Hud __instance)
        {
            try
            {
                HudCluster.Attach(__instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[RuneSkin] HUD cluster build failed (vanilla HUD stays): {e}");
            }
        }

        [HarmonyPatch(typeof(global::Hud), nameof(global::Hud.UpdateStatusEffects))]
        [HarmonyPrefix]
        private static bool UpdateStatusEffects_Prefix(List<StatusEffect> statusEffects)
        {
            var cluster = HudCluster.Instance;
            if (!cluster) return true;
            cluster.Feed(statusEffects);
            return false;
        }
    }
}
