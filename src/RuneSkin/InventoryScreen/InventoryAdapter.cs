using HarmonyLib;
using RuneSkin.Ownership;
using RuneSkin.Compat;

namespace RuneSkin.InventoryScreen
{
    // Family "Inventory" hooks (installed only while RuneSkin owns it). No transpilers, no skipped originals (one scoped
    // exception: Update_Prefix, the hidden first open's frames during the spawn's wakeup cutscene):
    // InventoryGui.Awake (InventoryGui.cs:353) builds the skin after every other Awake postfix; InventoryGui.Update
    // (:501) and InventoryGrid.UpdateGui (InventoryGrid.cs:245) postfixes run after EquipmentAndQuickSlots (which
    // re-places its cells / backgrounds in both every frame) and ValheimPlus (container scrollbar layout prefix).
    [HarmonyPatch]
    public static class InventoryAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Inventory);

        private const string Eaqs = EaqsBridge.PluginGuid;
        private const string ValheimPlus = ModCompat.ValheimPlusGuid;

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(Eaqs)]
        private static void Awake_Postfix(InventoryGui __instance)
        {
            try { InventorySkin.Attach(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError($"[RuneSkin] inventory skin build failed (vanilla layout stays): {e}"); }
        }

        // Hidden first open only (InventorySkin.GhostHoldsCutscene): vanilla's cutscene close skipped while the spawn's
        // wakeup animation plays under the transparent, input-less ghost. Every other frame: vanilla runs untouched.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Update_Prefix()
        {
            var skin = InventorySkin.Instance;
            try { return !(skin && skin.GhostHoldsCutscene()); }
            catch (System.Exception e) { Fault(e); return true; }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(Eaqs, ValheimPlus)]
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
        [HarmonyAfter(Eaqs, ValheimPlus)]
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

        // The dragged item (InventoryGui.m_dragGo, InventoryGui.cs:2245, a child of the inventory under the GUI root
        // canvas) drew UNDER the HUD strip, whose canvas sorts above the GUI while the inventory is open (drop target,
        // HudCluster.AboveInventory): the drag visual gets its own override-sorted canvas above the strip.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupDragItem))]
        [HarmonyPostfix]
        private static void SetupDragItem_Postfix(InventoryGui __instance)
        {
            try { InventorySkin.DragOnTop(__instance); }
            catch (System.Exception e) { Fault(e); }
        }

        // The panels parked after the close fade come back before vanilla Show touches them (InventorySkin.Fade).
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Show_Prefix() => InventorySkin.Instance?.Unpark();

        // Hidden first open (InventorySkin.GhostOpen): while it is shown (and the one frame IsVisible lags behind Hide),
        // the mouse stays captured as if the inventory were closed - the vanilla capture branch of
        // GameCamera.UpdateMouseCapture (GameCamera.cs, !InventoryGui.IsVisible()), so no cursor appears over the
        // loading screen. Other screens (menu, radial, popups) are untouched: only the branch vanilla would have taken.
        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
        [HarmonyPostfix]
        private static void UpdateMouseCapture_Postfix(bool ___m_mouseCapture)
        {
            var skin = InventorySkin.Instance;
            if (!skin || !skin.GhostCursor || !___m_mouseCapture || Hud.InRadial() || TextInput.IsVisible() || Menu.IsActive() ||
                Minimap.IsOpen() || StoreGui.IsVisible() || Hud.IsPieceSelectionVisible() || PlayerCustomizaton.BarberBlocksLook() ||
                UnifiedPopup.IsVisible() || ZNet.IsPasswordDialogShowing()) return;
            ZCursor.LockState = UnityEngine.CursorLockMode.Locked;
            ZCursor.Hide();
        }

        // Character preview refresh events (CharacterPreview.MarkDirty), local player only: every equipment change
        // ends in Humanoid.SetupEquipment -> Player.SetupVisEquipment(m_visEquipment, false) (equip, unequip, hide /
        // show hand items, SetHair / SetBeard; Humanoid.cs:1428-1432, :1957); model and colours change through
        // Player.SetPlayerModel / SetSkinColor / SetHairColor (Player.cs:6012-6042). The preview's own call (its clone's
        // VisEquipment) is not an event.
        [HarmonyPatch(typeof(Player), nameof(Player.SetupVisEquipment))]
        [HarmonyPostfix]
        private static void SetupVisEquipment_Postfix(Player __instance, VisEquipment visEq)
        {
            if (__instance == Player.m_localPlayer && visEq == __instance.m_visEquipment) CharacterPreview.MarkDirty();
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetPlayerModel))]
        [HarmonyPatch(typeof(Player), nameof(Player.SetSkinColor))]
        [HarmonyPatch(typeof(Player), nameof(Player.SetHairColor))]
        [HarmonyPostfix]
        private static void Appearance_Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) CharacterPreview.MarkDirty();
        }

        private static int _faults;

        private static void Fault(System.Exception e)
        {
            if (_faults++ < 5) Plugin.Log.LogError($"[RuneSkin] inventory frame failed: {e}");
        }
    }
}
