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

        // Vanilla's "not enough" signals (vanilla bars hidden, their animators run on): the warning pulse + error sound on
        // RuneSkin's bars (DenyFeedback). Prefixes, so a vanilla animator fault never swallows the feedback.
        [HarmonyPatch(typeof(global::Hud), nameof(global::Hud.StaminaBarEmptyFlash))]
        [HarmonyPrefix]
        private static void StaminaBarEmptyFlash_Prefix() => DenyFeedback.Deny(DenyFeedback.Stamina);

        [HarmonyPatch(typeof(global::Hud), nameof(global::Hud.EitrBarEmptyFlash))]
        [HarmonyPrefix]
        private static void EitrBarEmptyFlash_Prefix() => DenyFeedback.Deny(DenyFeedback.Eitr);

        // FlashHealthBar = attack health cost not covered (Attack.cs:400-404) or a max-health upgrade
        // (Player.SetMaxHealth(float, bool), Player.cs:6152-6158): only the first is a refusal.
        [HarmonyPatch(typeof(global::Hud), nameof(global::Hud.FlashHealthBar))]
        [HarmonyPrefix]
        private static void FlashHealthBar_Prefix()
        {
            if (!DenyFeedback.InMaxHealthUpgrade) DenyFeedback.Deny(DenyFeedback.Health);
        }

        [HarmonyPatch(typeof(global::Player), nameof(global::Player.SetMaxHealth), typeof(float), typeof(bool))]
        [HarmonyPrefix]
        private static void SetMaxHealth_Prefix() => DenyFeedback.InMaxHealthUpgrade = true;

        [HarmonyPatch(typeof(global::Player), nameof(global::Player.SetMaxHealth), typeof(float), typeof(bool))]
        [HarmonyFinalizer]
        private static void SetMaxHealth_Finalizer() => DenyFeedback.InMaxHealthUpgrade = false;
    }
}
