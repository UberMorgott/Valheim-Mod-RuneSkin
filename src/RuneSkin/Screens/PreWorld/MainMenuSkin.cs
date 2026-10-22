using System;
using System.Collections.Generic;
using HarmonyLib;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.Ownership;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuneSkin.PreWorld
{
    // Family "MainMenu" (M5, mockups m5-menu-a / m5-char-a / m5-world-a / m5-load-a): FejdStartup's screens restyled in
    // place. Every panel is a child of FejdStartup and exists at Awake (HideAll only deactivates them, FejdStartup.cs:
    // 531-542), so one registered root covers the main menu, character select / create, start game (world list + join
    // tab), create world, server options, the remove / version / connection-failed dialogs and the loading screens.
    // - main menu: the vanilla entry column (MenuList/MenuEntries, text buttons animated by an Animator) gets button
    //   plates (ButtonPlate, button_* SpriteSwap); mod entries (ConfigurationManager...) are plated on Start too;
    //   version text = text.version;
    // - panels: vanilla wood / backdrop images become the RuneSkin panel stack (PreWorldKit.Panels);
    // - character select: a scrolling character list (CharacterList) replaces the left / right carousel (vanilla
    //   m_profileIndex + UpdateCharacterList drive it, FejdStartup.cs:2711), delete confirm = danger button with the
    //   initial focus on Cancel (OnCharacterRemove :2852);
    // - controls by component (Classifier control families): toggles = checkbox, sliders, input fields, tabs of every
    //   TabHandler = tab_* (selected = non-interactable, TabHandler.SetActiveTab);
    // - loading (m_loading, LoadMainScene :2957) and please-wait panels get the rune spinner (no real progress there).
    public static class MainMenuSkin
    {
        public const string ScreenId = "MainMenu";

        public static void Attach(FejdStartup fejd)
        {
            var root = fejd.transform;
            Plates(fejd);
            Layout(fejd);
            var menuList = fejd.m_menuList ? fejd.m_menuList.transform : null;
            PreWorldKit.Panels(root, "size.panel.pad", img => (menuList && img.transform.IsChildOf(menuList)) || VanillaCorner(img.transform, root));
            TabSelected(root);
            DefaultCancel(fejd.m_removeCharacterDialog);
            DefaultCancel(fejd.m_removeWorldDialog);
            Spinner(fejd.m_loading);
            Spinner(fejd.m_pleaseWait);
            CharacterList.Build(fejd);
            ScreenRegistry.Register(root, () => Context(fejd));
            ScreenRegistry.Restyle(root);
            if (fejd.m_worldListElement) RegisterTemplate(fejd.m_worldListElement.transform, fejd);
            Plugin.Log.LogInfo("[RuneSkin] main menu attached");
        }

        // After every Start postfix (mods add menu entries / version lines there): plate new entries, restyle again.
        public static void Refresh(FejdStartup fejd)
        {
            Plates(fejd);
            Layout(fejd);
            ScreenRegistry.Restyle(fejd.transform);
            if (fejd.m_versionLabel)
            {
                fejd.m_versionLabel.alignment = TextAlignmentOptions.BottomRight;
                fejd.m_versionLabel.overflowMode = TextOverflowModes.Overflow;
            }
        }

        // Row templates instantiated by vanilla (world rows FejdStartup.cs:1319, server rows ServerListGui.cs:608):
        // restyled once, every clone copies the styled properties.
        public static void RegisterTemplate(Transform template, FejdStartup fejd)
        {
            if (!template) return;
            PreWorldKit.Panels(template, "size.panel.pad.small");
            ScreenRegistry.Register(template, () => Context(fejd, template, ScreenId + "." + template.name.Replace("(Clone)", "")));
            ScreenRegistry.Restyle(template);
        }

        // Main menu composition (mockup m5-menu-a): logo top-left, the entry column under it on the left, no panel
        // behind the column (the vanilla MenuList backdrop is off). Positions only; the Animator-free MenuList keeps
        // its VerticalLayoutGroup.
        private static void Layout(FejdStartup fejd)
        {
            if (fejd.m_menuList && fejd.m_menuList.transform is RectTransform list)
            {
                list.anchorMin = list.anchorMax = new Vector2(0f, 0.5f);
                list.pivot = new Vector2(0f, 1f);
                list.anchoredPosition = new Vector2(ThemeTokens.GetFloat("size.menu.left", 110f), ThemeTokens.GetFloat("size.menu.top", 140f));
                foreach (var img in list.GetComponents<Image>()) PanelSwap.Apply(img, img.sprite ? img.sprite.name : null);
                var entries = list.Find("MenuEntries");
                if (entries) foreach (var img in entries.GetComponents<Image>()) PanelSwap.Apply(img, img.sprite ? img.sprite.name : null);
            }
            // Logo stays vanilla (the menu animator drives it). Bottom-left text links (changelog, EULA, log) as small
            // plated buttons.
            var links = fejd.m_mainMenu ? fejd.m_mainMenu.transform.Find("BottomLeftButtons") : null;
            if (links)
                foreach (var b in links.GetComponentsInChildren<Button>(true))
                {
                    if (b.GetComponent<ButtonPlate>()) continue;
                    ButtonPlate.Ensure(b);
                    var rt = (RectTransform)b.transform;
                    rt.sizeDelta = new Vector2(Mathf.Max(rt.sizeDelta.x, ThemeTokens.GetFloat("size.menu.link.w", 240f)), ThemeTokens.GetFloat("size.menu.link.h", 36f));
                    foreach (var t in b.GetComponentsInChildren<TMP_Text>(true))
                        if (!PreWorldKit.IsKeyHint(t.transform, b.transform)) { t.enableAutoSizing = false; t.fontSize = ThemeTokens.GetFloat("size.menu.link.font", 16f); t.alignment = TextAlignmentOptions.Center; }
                }
            if (links && links.TryGetComponent<VerticalLayoutGroup>(out var lv)) lv.spacing = Mathf.Max(lv.spacing, 6f);
            // Scaled screens (Codex M5 r1: panels / rows / buttons read small next to the mockups): the start-game
            // panel, the character action bar, remove dialogs; positions stay vanilla, the whole subtree scales.
            Scale(fejd.m_startGamePanel ? fejd.m_startGamePanel.transform.Find("Panel") : null, "scale.preworld.startgame", 1.2f);
            Scale(fejd.m_selectCharacterPanel ? fejd.m_selectCharacterPanel.transform.Find("BottomWindow") : null, "scale.preworld.charbar", 1.15f);
            Scale(fejd.m_removeCharacterDialog ? fejd.m_removeCharacterDialog.transform.Find("Dialog") : null, "scale.preworld.dialog", 1.3f);
            Scale(fejd.m_removeWorldDialog ? fejd.m_removeWorldDialog.transform.Find("Dialog") : null, "scale.preworld.dialog", 1.3f);
            // Character bar: lifted clear of the screen bottom (its panel stack pad + caps spilled off-screen).
            var bar = fejd.m_selectCharacterPanel ? fejd.m_selectCharacterPanel.transform.Find("BottomWindow") as RectTransform : null;
            if (bar && !bar.GetComponent<LiftMark>())
            {
                bar.gameObject.AddComponent<LiftMark>();
                bar.anchoredPosition += new Vector2(0f, ThemeTokens.GetFloat("size.preworld.charbar.lift", 110f));
            }
            // The list header names the screen: the vanilla floating topic is off.
            var topic = fejd.m_selectCharacterPanel ? fejd.m_selectCharacterPanel.transform.Find("Topic") : null;
            if (topic && topic.TryGetComponent<TMP_Text>(out var topicText)) topicText.enabled = false;
        }

        // Main-menu Feedback / ContactInfo boxes stay vanilla (their content is vanilla-driven; a RuneSkin frame around
        // them read as an empty panel, run 20261022-053130).
        private static bool VanillaCorner(Transform t, Transform root)
        {
            for (var p = t; p && p != root; p = p.parent)
                if (p.name == "Feedback" || p.name == "ContactInfo") return true;
            return false;
        }

        private static void Scale(Transform t, string token, float fallback)
        {
            if (!t) return;
            var k = ThemeTokens.GetFloat(token, fallback);
            t.localScale = new Vector3(k, k, 1f);
        }

        // Remove dialogs: Cancel is the UIGroupHandler default element (gamepad focus, UIGroupHandler.cs:141), so the
        // first confirm never lands on Delete.
        private static void DefaultCancel(GameObject dialog)
        {
            if (!dialog) return;
            var group = dialog.GetComponentInChildren<UIGroupHandler>(true);
            if (!group) return;
            foreach (var b in dialog.GetComponentsInChildren<Button>(true))
                if (b.name.IndexOf("No", StringComparison.Ordinal) >= 0 || b.name.IndexOf("Cancel", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    group.m_defaultElement = b.gameObject;
                    return;
                }
        }

        private static void Plates(FejdStartup fejd)
        {
            if (!fejd.m_menuList) return;
            var w = ThemeTokens.GetFloat("size.menu.entry.w", 400f);
            var h = ThemeTokens.GetFloat("size.menu.entry.h", 60f);
            foreach (var button in fejd.m_menuList.GetComponentsInChildren<Button>(true))
            {
                if (button.GetComponent<ButtonPlate>()) continue;
                if (!(button.targetGraphic is TMP_Text) && button.transition != Selectable.Transition.Animation) continue;
                ButtonPlate.Ensure(button);
                if (!button.TryGetComponent<LayoutElement>(out var le)) le = button.gameObject.AddComponent<LayoutElement>();
                le.minWidth = le.preferredWidth = w;
                le.minHeight = le.preferredHeight = h;
                var rt = (RectTransform)button.transform;
                rt.sizeDelta = new Vector2(w, h);
                foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (PreWorldKit.IsKeyHint(label.transform, button.transform)) continue;
                    label.alignment = TextAlignmentOptions.Center;
                    label.enableAutoSizing = false;
                    label.fontSize = ThemeTokens.GetFloat("size.menu.entry.font", 25f);
                    label.textWrappingMode = TextWrappingModes.NoWrap;
                    label.overflowMode = TextOverflowModes.Ellipsis;
                    label.fontStyle |= FontStyles.UpperCase;
                    var lr = label.rectTransform;
                    lr.anchorMin = Vector2.zero;
                    lr.anchorMax = Vector2.one;
                    lr.offsetMin = new Vector2(h * 0.9f, 0f);
                    lr.offsetMax = new Vector2(-h * 0.9f, 0f);
                }
            }
            var entries = fejd.m_menuList.transform.Find("MenuEntries");
            if (entries && entries.TryGetComponent<VerticalLayoutGroup>(out var v))
            {
                v.spacing = ThemeTokens.GetFloat("size.menu.entry.gap", 14f);
                v.childAlignment = TextAnchor.UpperLeft;
            }
        }

        // Tab buttons (any TabHandler): the vanilla "Selected" highlight child (wood art, toggled by SetActiveTab) is off;
        // the selected tab reads from its disabled sprite (tab_selected) instead.
        public static void TabSelected(Transform root)
        {
            foreach (var handler in root.GetComponentsInChildren<TabHandler>(true))
                foreach (var tab in handler.m_tabs)
                {
                    if (tab == null || !tab.m_button) continue;
                    TabLabelState.Ensure(tab.m_button);
                    var sel = tab.m_button.transform.Find("Selected");
                    if (!sel) continue;
                    foreach (var img in sel.GetComponentsInChildren<Image>(true))
                        PanelSwap.Apply(img, img.sprite ? img.sprite.name : null);
                }
        }

        private static void Spinner(GameObject host)
        {
            if (!host || host.transform.Find("RuneSkin_Spinner")) return;
            var s = RuneSpinner.Add(host.transform, "RuneSkin_Spinner", ThemeTokens.GetFloat("size.spinner", 72f));
            var rt = s.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, ThemeTokens.GetFloat("size.spinner.offset", 70f));
        }

        private static ScreenContext Context(FejdStartup fejd, Transform root = null, string id = ScreenId)
        {
            root = root ? root : fejd.transform;
            var ctx = new ScreenContext(id, OwnershipRegistry.MainMenu);
            // Character creation colour sliders keep the vanilla gradient tracks (mockup m5-char-a: trough frame +
            // vanilla gradient + diamond handle): only their handles take the slider art.
            if (fejd.m_newCharacterPanel)
                foreach (var s in fejd.m_newCharacterPanel.GetComponentsInChildren<Slider>(true))
                    foreach (var img in s.GetComponentsInChildren<Image>(true))
                        if (s.handleRect != img.rectTransform && s.targetGraphic != img) ctx.Role(img, "content", "character colour slider gradient (vanilla)");
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name;
                if (n == "Logo" || n.StartsWith("Mist", StringComparison.Ordinal) || n == "BLACK" || n == "Scaled 3D Viewport" || n == "CinematicsMenuEntries" || n == "Feedback" || n == "ContactInfo")
                    ctx.Exclude(t, $"'{n}': scene art (logo, mist, fade, 3D viewport, cinematics list kept vanilla)");
            }
            if (fejd.m_menuList)
                foreach (var b in fejd.m_menuList.GetComponentsInChildren<Button>(true))
                    if (b.GetComponent<ButtonPlate>() is ButtonPlate p && p.Plate)
                        foreach (var t in b.GetComponentsInChildren<TMP_Text>(true))
                            if (!PreWorldKit.IsKeyHint(t.transform, b.transform)) ctx.Role(t, "button.label", "main menu entry label (plated)");
            ctx.Role(fejd.m_versionLabel, "text.version", "FejdStartup.m_versionLabel");
            Danger(ctx, fejd.m_removeCharacterDialog);
            Danger(ctx, fejd.m_removeWorldDialog);
            foreach (var handler in root.GetComponentsInChildren<TabHandler>(true))
                foreach (var tab in handler.m_tabs)
                    if (tab?.m_button && tab.m_button.targetGraphic) ctx.Role(tab.m_button.targetGraphic, "tab", "TabHandler tab button");
            CharacterList.Roles(ctx);
            PreWorldKit.Common(ctx, root);
            return ctx;
        }

        // Confirm-delete dialogs: the Yes button is the danger action (art: button_danger_*).
        private static void Danger(ScreenContext ctx, GameObject dialog)
        {
            if (!dialog) return;
            foreach (var b in dialog.GetComponentsInChildren<Button>(true))
                if (b.name.IndexOf("Yes", StringComparison.OrdinalIgnoreCase) >= 0 && b.targetGraphic)
                    ctx.Role(b.targetGraphic, "button.danger", "remove dialog confirm (danger)");
        }

        // Delete confirm opens with the focus on Cancel (mockup m5-char-a, Codex pick): Enter never deletes by accident.
        public static void FocusCancel(GameObject dialog)
        {
            if (!dialog || !EventSystem.current) return;
            foreach (var b in dialog.GetComponentsInChildren<Button>(false))
                if (b.name.IndexOf("No", StringComparison.Ordinal) >= 0 || b.name.IndexOf("Cancel", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    EventSystem.current.SetSelectedGameObject(b.gameObject);
                    return;
                }
        }
    }

    // One-time position lift of a vanilla panel (Awake + Start both lay out).
    public sealed class LiftMark : MonoBehaviour
    {
    }

    // Tab label colour by state (TabHandler.SetActiveTab makes the selected tab non-interactable): gold on the
    // selected tab, body text otherwise. Polls the Button, no vanilla hook needed.
    public sealed class TabLabelState : MonoBehaviour
    {
        public Button Button;
        public TMP_Text Label;

        private void LateUpdate()
        {
            if (!Button || !Label) return;
            // Every frame against the wanted colour: a restyle pass (role button.label) may write the body colour back.
            var want = ThemeTokens.GetColor(Button.interactable ? "color.text.body" : "color.text.item.title", Label.color);
            if (Label.color != want) Label.color = want;
        }

        public static void Ensure(Button b)
        {
            if (!b || b.GetComponent<TabLabelState>()) return;
            var label = b.GetComponentInChildren<TMP_Text>(true);
            if (!label) return;
            var s = b.gameObject.AddComponent<TabLabelState>();
            s.Button = b;
            s.Label = label;
        }
    }

    [HarmonyPatch]
    public static class MainMenuAdapter
    {
        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Awake_Postfix(FejdStartup __instance)
        {
            try { MainMenuSkin.Attach(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] main menu restyle failed (vanilla menu stays): {e}"); }
        }

        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Start))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Start_Postfix(FejdStartup __instance)
        {
            try { MainMenuSkin.Refresh(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] main menu refresh failed: {e}"); }
        }

        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.UpdateCharacterList))]
        [HarmonyPostfix]
        private static void UpdateCharacterList_Postfix(FejdStartup __instance)
        {
            try { CharacterList.Sync(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] character list sync failed: {e}"); }
        }

        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.OnCharacterRemove))]
        [HarmonyPostfix]
        private static void OnCharacterRemove_Postfix(FejdStartup __instance) => MainMenuSkin.FocusCancel(__instance.m_removeCharacterDialog);

        [HarmonyPatch(typeof(ServerListGui), nameof(ServerListGui.Awake))]
        [HarmonyPostfix]
        private static void ServerList_Postfix(ServerListGui __instance)
        {
            try
            {
                if (__instance.m_serverListElement && FejdStartup.instance)
                    MainMenuSkin.RegisterTemplate(__instance.m_serverListElement.transform, FejdStartup.instance);
            }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] server row restyle failed: {e}"); }
        }

        // The scene's UnifiedPopup (vanilla notices / warnings, UnifiedPopup.cs:78-97) restyled once in its Awake.
        [HarmonyPatch(typeof(UnifiedPopup), nameof(UnifiedPopup.Awake))]
        [HarmonyPostfix]
        private static void Popup_Postfix(UnifiedPopup __instance)
        {
            try
            {
                var root = __instance.transform;
                PreWorldKit.Panels(root, "size.panel.pad");
                ScreenRegistry.Register(root, () =>
                {
                    var ctx = new ScreenContext("UnifiedPopup", OwnershipRegistry.MainMenu);
                    PreWorldKit.Common(ctx, root);
                    return ctx;
                });
                ScreenRegistry.Restyle(root);
            }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] popup restyle failed: {e}"); }
        }
    }
}
