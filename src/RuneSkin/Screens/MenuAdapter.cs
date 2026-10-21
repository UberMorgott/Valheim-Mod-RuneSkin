using HarmonyLib;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.Responsive;
using RuneSkin.Restyle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Screens
{
    // Pause menu (Esc), family "Menu". The vanilla Menu keeps its objects, fields and logic (Menu.cs:120-403); RuneSkin
    // restyles it in place after Menu.Start (scene object, Start hides it and builds navigation, Menu.cs:120-134) and
    // re-applies on every Menu.Show (Menu.cs:221-246), which catches entries other mods add later (idempotent: marks
    // skip unchanged graphics). Runs after AugaSkin, whose Menu.Start postfix adds the Compendium entry.
    // Installed only when RuneSkin owns "Menu" (Plugin.PatchFamilies).
    [HarmonyPatch]
    public static class MenuAdapter
    {
        public const string ScreenId = "Menu";

        [HarmonyPatch(typeof(Menu), nameof(Menu.Start))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("morgott.valheim.augaskin")]
        private static void Start_Postfix(Menu __instance) => Apply(__instance);

        [HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("morgott.valheim.augaskin")]
        private static void Show_Postfix(Menu __instance) => Apply(__instance);

        private static void Apply(Menu menu)
        {
            if (!menu) return;
            var root = menu.transform;
            if (menu.menuEntriesParent)
            {
                // Entry states via their owning components (MenuEntryStyle), then the panel stack behind the entries.
                foreach (var button in menu.menuEntriesParent.GetComponentsInChildren<Button>(true))
                    MenuEntryStyle.Ensure(button, button.GetComponentInChildren<TMP_Text>(true));
                PanelDecor.Ensure(menu.menuEntriesParent, "size.panel.pad", glass: true, plaque: true);
            }
            ScreenRegistry.Register(root, () => Context(menu));
            ScreenRegistry.Restyle(root);
            // SkinScale on the dialogs' content roots, never on the canvas (GuiScaler owns scaleFactor).
            ResponsiveContext.ApplyScale(menu.m_menuDialog);
            ResponsiveContext.ApplyScale(menu.m_quitDialog);
            ResponsiveContext.ApplyScale(menu.m_logoutDialog);
        }

        // Classifier step (b): what the vanilla fields tell about the menu.
        private static ScreenContext Context(Menu menu)
        {
            var ctx = new ScreenContext(ScreenId, Ownership.OwnershipRegistry.Menu);
            // Controller glyph map (Menu.m_gamepadRoot, shown by HandleInputLayoutChanged Menu.cs:136-146): content art.
            ctx.Exclude(menu.m_gamepadRoot ? menu.m_gamepadRoot.transform : null, "gamepad map (Menu.m_gamepadRoot, Menu.cs:78/141-145): controller art");
            // Settings / player list / feedback are their own screens, instantiated under the menu on demand
            // (Menu.OnSettings :414, OnCurrentPlayers :428, OnButtonFeedback :582); not owned in M0.
            if (menu.m_settingsInstance) ctx.Exclude(menu.m_settingsInstance.transform, "Settings screen (Menu.OnSettings, Menu.cs:414), own family");
            if (menu.m_currentPlayersInstance) ctx.Exclude(menu.m_currentPlayersInstance.transform, "player list (Menu.OnCurrentPlayers, Menu.cs:428), own family");
            foreach (var feedback in menu.GetComponentsInChildren<Feedback>(true))
                if (feedback.transform != menu.transform) ctx.Exclude(feedback.transform, "Feedback dialog (Menu.OnButtonFeedback, Menu.cs:582), own family");

            // Entries: text-only buttons under menuEntriesParent (Menu.cs:58, laid out by LayoutRebuilder :375).
            if (menu.menuEntriesParent)
            {
                foreach (var button in menu.menuEntriesParent.GetComponentsInChildren<Button>(true))
                {
                    if (button.targetGraphic is TMP_Text label)
                        ctx.Role(label, "menu.entry", "Menu.menuEntriesParent entry label (Button.targetGraphic)");
                    foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
                        if (text != button.targetGraphic)
                            ctx.Role(text, "menu.entry", "Menu.menuEntriesParent entry label");
                }
            }
            // "Last saved" line (Menu.lastSaveText, Menu.cs:54, written in Update :366): body text.
            ctx.Role(menu.lastSaveText, "text.body", "Menu.lastSaveText");
            return ctx;
        }
    }
}
