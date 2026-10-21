using HarmonyLib;

namespace RuneSkin.InfoScreens
{
    // Families "Skills" / "Trophies" hooks (installed only while owned, with Inventory): build after every other
    // InventoryGui.Awake postfix; re-lay out after vanilla filled the lists (SkillsDialog.Setup, UpdateTrophyList).
    [HarmonyPatch]
    public static class SkillsAdapter
    {
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("morgott.valheim.augaskin")]
        private static void Awake_Postfix(InventoryGui __instance)
        {
            try { SkillsSkin.Attach(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError($"[RuneSkin] skills panel build failed (vanilla look stays): {e}"); }
        }

        [HarmonyPatch(typeof(SkillsDialog), nameof(SkillsDialog.Setup))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Setup_Postfix(Player player)
        {
            try { SkillsSkin.Instance?.AfterSetup(player); }
            catch (System.Exception e) { Plugin.Log.LogError($"[RuneSkin] skills layout failed: {e}"); }
        }
    }

    [HarmonyPatch]
    public static class TrophiesAdapter
    {
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("morgott.valheim.augaskin")]
        private static void Awake_Postfix(InventoryGui __instance)
        {
            try { TrophiesSkin.Attach(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError($"[RuneSkin] trophies panel build failed (vanilla look stays): {e}"); }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateTrophyList))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void UpdateTrophyList_Postfix()
        {
            try { TrophiesSkin.Instance?.AfterUpdateList(); }
            catch (System.Exception e) { Plugin.Log.LogError($"[RuneSkin] trophies layout failed: {e}"); }
        }
    }
}
