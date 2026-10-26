using System;
using HarmonyLib;
using RuneSkin.GameScreens;
using RuneSkin.Ownership;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.ModScreens
{
    // Vanilla leftovers no other family owned (M7c), restyled in place through the role pass (M5 / M6 form rules):
    // - InventoryExtras: the achievements panel (InventoryGui.m_achievementsPanel; list rows cloned from
    //   m_achievementsElementPrefab under m_achievementsListRoot on every open: restyled after OnOpenAchievements), the
    //   variant dialog (m_variantDialog; Epic Loot clones it for its augment / craft result dialogs), the dragged item
    //   (m_dragItemPrefab template: the clone under the cursor keeps the styled template's look).
    // - Radial: the radial menu (Hud.m_radialMenu, Valheim.UI.RadialBase): elements are built per Open, restyled after
    //   it; texts in the theme faces, the wheel art and item icons stay (content).
    public static class VanillaExtras
    {
        public static void Inventory(InventoryGui gui)
        {
            if (!OwnershipRegistry.IsOwned(OwnershipRegistry.InventoryExtras) || !gui) return;
            if (gui.m_achievementsPanel)
                Form(gui.m_achievementsPanel.transform, "InventoryExtras.Achievements", OwnershipRegistry.InventoryExtras, (ctx, r) =>
                {
                    Icons(ctx, r);
                    PopupSkin.Backdrops(ctx, r);
                });
            if (gui.m_achievementsElementPrefab && !gui.m_achievementsElementPrefab.transform.IsChildOf(gui.transform))
                Form(gui.m_achievementsElementPrefab.transform, "InventoryExtras.AchievementRow", OwnershipRegistry.InventoryExtras, (ctx, r) => Icons(ctx, r), panels: false);
            if (gui.m_variantDialog)
                Form(gui.m_variantDialog.transform, "InventoryExtras.Variant", OwnershipRegistry.InventoryExtras, (ctx, r) =>
                {
                    Icons(ctx, r);
                    PopupSkin.Backdrops(ctx, r);
                });
            if (gui.m_dragItemPrefab)
                Form(gui.m_dragItemPrefab.transform, "InventoryExtras.DragItem", OwnershipRegistry.InventoryExtras, (ctx, r) =>
                {
                    foreach (var img in r.GetComponentsInChildren<Image>(true)) ctx.Role(img, "content", "dragged item icon");
                    foreach (var t in r.GetComponentsInChildren<TMP_Text>(true)) ctx.Role(t, "text.body", "dragged stack amount");
                }, panels: false);
        }

        // Own registration + RootMark: an ancestor family's pass (Inventory, Hud) skips the subtree.
        private static void Form(Transform root, string id, string family, Action<Classify.ScreenContext, Transform> roles, bool panels = true)
        {
            if (!root) return;
            if (!root.TryGetComponent<Restyle.RootMark>(out var m)) m = root.gameObject.AddComponent<Restyle.RootMark>();
            m.ScreenId = id;
            m.Owner = "RuneSkin";
            GameScreenKit.Form(root, id, family, roles, panels);
        }

        // Item / achievement icons inside the rows keep their art.
        private static void Icons(Classify.ScreenContext ctx, Transform r)
        {
            foreach (var img in r.GetComponentsInChildren<Image>(true))
                // Also when the icon is the row's button target (achievement rows: a plate would turn the icon into a
                // cell whose texts overflow it).
                if (img.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                    ctx.Role(img, "content", "icon (kept)");
        }

        public static void Radial(Hud hud)
        {
            if (!OwnershipRegistry.IsOwned(OwnershipRegistry.Radial) || !hud || !hud.m_radialMenu) return;
            Form(hud.m_radialMenu.transform, "Radial", OwnershipRegistry.Radial, (ctx, r) =>
            {
                foreach (var img in r.GetComponentsInChildren<Image>(true)) ctx.Role(img, "content", "radial wheel art / icon (kept)");
            }, panels: false);
        }
    }

    [HarmonyPatch]
    public static class VanillaExtrasAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => AnyOwned();

        public static bool AnyOwned() => OwnershipRegistry.IsOwned(OwnershipRegistry.InventoryExtras) || OwnershipRegistry.IsOwned(OwnershipRegistry.Radial);

        private static void Try(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] {what} restyle failed (vanilla look stays): {e}"); }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(PatchOrder.InventoryExtras)]
        private static void Inventory_Postfix(InventoryGui __instance) => Try("inventory extras", () => VanillaExtras.Inventory(__instance));

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnOpenAchievements))]
        [HarmonyPostfix]
        private static void Achievements_Postfix(InventoryGui __instance)
        {
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.InventoryExtras) && __instance.m_achievementsPanel)
                Try("achievements", () =>
                {
                    GameScreenKit.Restyle(__instance.m_achievementsPanel);
                    // An info window like skills / trophies (AchievementsWindow: the docked right panel, its one header with drag /
                    // double-click reset), not the vanilla full-screen-centred frame.
                    InfoScreens.AchievementsWindow.Ensure(__instance);
                });
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(PatchOrder.HudExtras)]
        private static void Hud_Postfix(Hud __instance) => Try("radial menu", () => VanillaExtras.Radial(__instance));

        [HarmonyPatch(typeof(Valheim.UI.RadialBase), nameof(Valheim.UI.RadialBase.Open))]
        [HarmonyPostfix]
        private static void Open_Postfix(Valheim.UI.RadialBase __instance)
        {
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Radial) && __instance) Try("radial menu", () => GameScreenKit.Restyle(__instance));
        }
    }
}
