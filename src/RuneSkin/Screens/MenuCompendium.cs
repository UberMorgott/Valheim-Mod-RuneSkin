using System.Collections;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Screens
{
    // Pause menu Compendium entry (family MenuCompendium, batch P: was an AugaSkin feature). Vanilla opens the
    // Compendium (InventoryGui.m_textsDialog, TextsDialog) only from the inventory's Info panel (OnOpenTexts,
    // InventoryGui.cs:2335-2343). The entry is a copy of the Settings entry right below it; a click closes the menu,
    // opens the inventory and, once its root is live (the Show animation activates it a frame later,
    // InventoryGui.cs:1037), takes the same OnOpenTexts path, so Esc closes it like vanilla (InventoryGui.cs:539-542).
    // Added before MenuAdapter's Start postfix (Priority.Last) styles the entries, spliced into vanilla's explicit
    // gamepad chain after Settings (Menu.UpdateNavigation, Menu.cs:148-197).
    [HarmonyPatch]
    public static class MenuCompendium
    {
        public const string EntryName = "RuneSkinCompendium";
        public const string Label = "$inventory_texts"; // vanilla Compendium title token

        [HarmonyPatch(typeof(Menu), nameof(Menu.Start))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.High)]
        private static void Start_Postfix(Menu __instance)
        {
            if (!__instance || !__instance.m_settingsButton || !__instance.menuEntriesParent) return;
            if (__instance.menuEntriesParent.Find(EntryName)) return;
            var settings = __instance.m_settingsButton;
            var go = Object.Instantiate(settings.gameObject, settings.transform.parent, false);
            go.name = EntryName;
            go.transform.SetSiblingIndex(settings.transform.GetSiblingIndex() + 1);
            var button = go.GetComponent<Button>();
            // The copy carries Settings' persistent OnSettings listener: a fresh event, only ours.
            button.onClick = new Button.ButtonClickedEvent();
            var menu = __instance;
            button.onClick.AddListener(() => Open(menu));
            var label = go.GetComponentInChildren<TMP_Text>(true);
            if (label) label.text = Label;
            Localization.instance?.Localize(go.transform);
            menu.UpdateNavigation(); // Start built the chain before the entry existed (Menu.cs:124)
            Plugin.Log.LogInfo("[RuneSkin] pause menu: Compendium entry added after Settings");
        }

        [HarmonyPatch(typeof(Menu), nameof(Menu.UpdateNavigation))]
        [HarmonyPostfix]
        private static void UpdateNavigation_Postfix(Menu __instance)
        {
            var entry = __instance && __instance.menuEntriesParent ? __instance.menuEntriesParent.Find(EntryName) : null;
            var settings = __instance ? __instance.m_settingsButton : null;
            if (!entry || !settings || !entry.TryGetComponent<Button>(out var compendium)) return;
            var nav = settings.navigation;
            var below = nav.selectOnDown;
            if (below == compendium) return; // already spliced
            nav.selectOnDown = compendium;
            settings.navigation = nav;
            compendium.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = settings, selectOnDown = below };
            if (!below) return;
            var n = below.navigation;
            n.selectOnUp = compendium;
            below.navigation = n;
        }

        private static void Open(Menu menu)
        {
            var gui = InventoryGui.instance;
            if (!menu || !gui) return;
            menu.Hide();
            gui.Show(null);
            gui.StartCoroutine(OpenTexts(gui));
        }

        private static IEnumerator OpenTexts(InventoryGui gui)
        {
            // Bounded wait for the inventory root (a closed inventory = the player cancelled: nothing to open).
            for (var i = 0; i < 120 && gui && InventoryGui.IsVisible() && !gui.m_textsDialog.transform.parent.gameObject.activeInHierarchy; i++)
                yield return null;
            if (gui && InventoryGui.IsVisible()) gui.OnOpenTexts();
        }
    }
}
