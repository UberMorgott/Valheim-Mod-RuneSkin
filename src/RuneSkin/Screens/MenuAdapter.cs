using HarmonyLib;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.Responsive;
using RuneSkin.Restyle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RuneSkin.Ownership;
using RuneSkin.Compat;

namespace RuneSkin.Screens
{
    // Pause menu (Esc), family "Menu". The vanilla Menu keeps its objects, fields and logic (Menu.cs:120-403); RuneSkin
    // restyles it in place after Menu.Start (scene object, Start hides it and builds navigation, Menu.cs:120-134) and
    // re-applies on every Menu.Show (Menu.cs:221-246), which catches entries other mods add later (idempotent: marks
    // skip unchanged graphics).
    // Installed only when RuneSkin owns "Menu" (Plugin.PatchFamilies).
    [HarmonyPatch]
    public static class MenuAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Menu);

        public const string ScreenId = "Menu";

        [HarmonyPatch(typeof(Menu), nameof(Menu.Start))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Start_Postfix(Menu __instance) => Apply(__instance);

        [HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Show_Postfix(Menu __instance)
        {
            Apply(__instance);
            __instance.StartCoroutine(FitPanel(__instance));
        }

        // Show sets which entries exist (SetButtonsEnabled, Menu.cs:248) and flags m_rebuildLayout; the next
        // Menu.Update rebuilds the entry column (Menu.cs:373-376). Fit the panel stack once after that frame.
        private static System.Collections.IEnumerator FitPanel(Menu menu)
        {
            yield return null;
            yield return null;
            if (!menu || !menu.menuEntriesParent) yield break;
            var panel = menu.menuEntriesParent.Find(PanelDecor.ChildName);
            if (panel && panel.TryGetComponent<PanelDecor>(out var decor)) decor.FitToChildren("size.panel.pad");
        }

        private static void Apply(Menu menu)
        {
            if (!menu) return;
            var root = menu.transform;
            if (menu.menuEntriesParent)
            {
                // Entry states via their owning components (MenuEntryStyle), then the panel stack behind the entries.
                foreach (var button in menu.menuEntriesParent.GetComponentsInChildren<Button>(true))
                    MenuEntryStyle.Ensure(button, button.GetComponentInChildren<TMP_Text>(true));
                // No header plaque: the pause menu has no title (menuEntriesParent holds only the entry buttons,
                // Menu.cs:58), and an empty plaque reads as a missing label.
                PanelDecor.Ensure(menu.menuEntriesParent, "size.panel.pad", glass: true, plaque: false);
            }
            // Confirm dialogs: one framed box each, sized to content, on the modal band (DialogFrame).
            DialogFrame.Apply(menu.m_quitDialog);
            DialogFrame.Apply(menu.m_logoutDialog);
            DialogFrame.Apply(menu.m_cloudStorageWarning ? menu.m_cloudStorageWarning.transform : null);
            DialogFrame.Apply(menu.m_cloudStorageWarningNextSave ? menu.m_cloudStorageWarningNextSave.transform : null);
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
            // Legacy uGUI labels of buttons other mods put on the menu root: Epic Loot's QuickConfigBroker
            // (Common.QuickConfigBroker.EnsurePauseButton, EpicLoot.dll decompile 2026-10-24) adds "ModQuickConfigButton"
            // to Menu.m_root through Jotunn GUIManager.CreateButton - its Image is the Button.targetGraphic (button role),
            // its label a UnityEngine.UI.Text the classifier has no rule for ("unknown Menu: ... Text not handled", 0.21.1
            // menufix run 20261024-194221). The plate's label face, like every RuneSkin button label.
            foreach (var button in menu.GetComponentsInChildren<Button>(true))
                foreach (var label in button.GetComponentsInChildren<Text>(true))
                    if (label.GetComponentInParent<Button>(true) == button)
                        ctx.Role(label, "button.label", "legacy Text label of a mod button on the menu (Jotunn CreateButton)");
            // Vanilla panel images under a dialog's panel stack (DialogFrame): off, reported skipped.
            foreach (var img in menu.GetComponentsInChildren<Image>(true))
                if (img.TryGetComponent<PreWorld.PanelSwap>(out _))
                    ctx.Role(img, "skip", "vanilla dialog panel image (RuneSkin panel stack replaces it, off; DialogFrame)");
            // "Last saved" line (Menu.lastSaveText, Menu.cs:54, written in Update :366): body text.
            ctx.Role(menu.lastSaveText, "text.body", "Menu.lastSaveText");
            return ctx;
        }
    }
}
