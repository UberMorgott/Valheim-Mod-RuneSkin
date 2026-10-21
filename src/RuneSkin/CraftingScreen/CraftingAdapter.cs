using HarmonyLib;

namespace RuneSkin.CraftingScreen
{
    // Family "Crafting" hooks (installed only while RuneSkin owns Crafting and Inventory). No transpilers, no skipped
    // originals: InventoryGui.Awake builds the skin after every other Awake postfix (VNEI, CraftingFilter, Epic Loot,
    // RecipeChains add their objects there); InventoryGui.Update postfix after them; SetupRequirementList prefix
    // only grows the requirement cell array so vanilla's paging (InventoryGui.cs:1665) never pages.
    [HarmonyPatch]
    public static class CraftingAdapter
    {
        private const string AugaSkin = "morgott.valheim.augaskin";
        private const string ValheimPlus = "org.bepinex.plugins.valheim_plus";
        private const string Vnei = "com.maxsch.valheim.vnei";

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(AugaSkin, Vnei)]
        private static void Awake_Postfix(InventoryGui __instance)
        {
            try { CraftingSkin.Attach(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError($"[RuneSkin] crafting skin build failed (vanilla look stays): {e}"); }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(AugaSkin, ValheimPlus, Vnei)]
        private static void Update_Postfix()
        {
            var skin = CraftingSkin.Instance;
            if (!skin) return;
            try { skin.Frame(); }
            catch (System.Exception e) { Fault(e); }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirementList))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void SetupRequirementList_Prefix(InventoryGui __instance)
        {
            var skin = CraftingSkin.Instance;
            var recipe = __instance.m_selectedRecipe.Recipe;
            if (!skin || !recipe || recipe.m_resources == null) return;
            try { skin.EnsureRequirementCells(recipe.m_resources.Length); }
            catch (System.Exception e) { Fault(e); }
        }

        private static int _faults;

        private static void Fault(System.Exception e)
        {
            if (_faults++ < 5) Plugin.Log.LogError($"[RuneSkin] crafting frame failed: {e}");
        }
    }
}
