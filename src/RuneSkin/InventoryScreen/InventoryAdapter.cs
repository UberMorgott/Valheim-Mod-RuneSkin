using HarmonyLib;

namespace RuneSkin.InventoryScreen
{
    // Family "Inventory" hooks (installed only while RuneSkin owns it). No transpilers, no skipped originals:
    // InventoryGui.Awake (InventoryGui.cs:353) builds the skin after every other Awake postfix; InventoryGui.Update
    // (:501) and InventoryGrid.UpdateGui (InventoryGrid.cs:245) postfixes run after EquipmentAndQuickSlots (which
    // re-places its cells / backgrounds in both every frame) and ValheimPlus (container scrollbar layout prefix).
    [HarmonyPatch]
    public static class InventoryAdapter
    {
        private const string Eaqs = EaqsBridge.PluginGuid;
        private const string AugaSkin = "morgott.valheim.augaskin";
        private const string ValheimPlus = "org.bepinex.plugins.valheim_plus";

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(AugaSkin, Eaqs)]
        private static void Awake_Postfix(InventoryGui __instance)
        {
            try { InventorySkin.Attach(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError($"[RuneSkin] inventory skin build failed (vanilla layout stays): {e}"); }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(AugaSkin, Eaqs, ValheimPlus)]
        private static void Update_Postfix()
        {
            var skin = InventorySkin.Instance;
            if (!skin) return;
            try { skin.Frame(); }
            catch (System.Exception e) { Fault(e); }
        }

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(AugaSkin, Eaqs, ValheimPlus)]
        private static void UpdateGui_Postfix(InventoryGrid __instance)
        {
            var skin = InventorySkin.Instance;
            if (!skin) return;
            try { skin.AfterGrid(__instance); }
            catch (System.Exception e) { Fault(e); }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        [HarmonyPostfix]
        private static void Hide_Postfix() => InventorySkin.Instance?.OnHide();

        private static int _faults;

        private static void Fault(System.Exception e)
        {
            if (_faults++ < 5) Plugin.Log.LogError($"[RuneSkin] inventory frame failed: {e}");
        }
    }
}
