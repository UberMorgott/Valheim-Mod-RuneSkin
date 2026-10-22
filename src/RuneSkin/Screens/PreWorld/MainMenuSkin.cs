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
            Changelog(fejd);
            TabSelected(root);
            DefaultCancel(fejd.m_removeCharacterDialog);
            // The character's name in the delete confirm (OnCharacterRemove :2857 writes it): the header size of the
            // restyle no longer fits its short vanilla line box and TMP truncation hid the whole line (run
            // 20261022-064153); it overflows its box instead, one line.
            if (fejd.m_removeCharacterName)
            {
                fejd.m_removeCharacterName.overflowMode = TextOverflowModes.Overflow;
                fejd.m_removeCharacterName.textWrappingMode = TextWrappingModes.NoWrap;
                // "Name (save source)" at 40 px ran past the 540 dialog (run 20261022-065352): shrinks to the width.
                fejd.m_removeCharacterName.fontSizeMax = ThemeTokens.GetFloat("size.dialog.subject.font", 30f);
                fejd.m_removeCharacterName.fontSizeMin = 16f;
                fejd.m_removeCharacterName.enableAutoSizing = true;
            }
            DefaultCancel(fejd.m_removeWorldDialog);
            DialogButtons(fejd.m_removeCharacterDialog);
            DialogButtons(fejd.m_removeWorldDialog);
            Spinner(fejd.m_loading);
            Spinner(fejd.m_pleaseWait);
            CharacterList.Build(fejd);
            ScreenRegistry.Register(root, () => Context(fejd));
            ScreenRegistry.Restyle(root);
            WorldRow(fejd);
            // Start-game mode tabs (Начать игру / Присоединиться): taller plates, larger labels (Codex M5 r3).
            if (fejd.m_startGamePanel)
            {
                // The mode handler, not the server filter tabs (those live under ServerListGui).
                var servers = fejd.m_startGamePanel.GetComponentInChildren<ServerListGui>(true);
                foreach (var h in fejd.m_startGamePanel.GetComponentsInChildren<TabHandler>(true))
                    if (!servers || (h.transform != servers.transform && !h.transform.IsChildOf(servers.transform) && h != servers.m_serverListTabHandler))
                        TabLabels(h, "size.startgame.tab");
                Field(fejd.m_serverPassword);
                if (servers)
                {
                    Field(servers.m_filterInputField);
                    Field(servers.m_addServerTextInput);
                }
            }
            CharacterFraming.Ensure(fejd);
            if (fejd.m_worldListElement) RegisterTemplate(fejd.m_worldListElement.transform, fejd);
            Plugin.Log.LogInfo("[RuneSkin] main menu attached");
        }

        // After every Start postfix (mods add menu entries / version lines there): plate new entries, restyle again.
        public static void Refresh(FejdStartup fejd)
        {
            Plates(fejd);
            Layout(fejd);
            ScreenRegistry.Restyle(fejd.transform);
            // Modded notice (m_moddedText): the body face is wider than the vanilla one, the fixed box cut the last line
            // (run 20261022-105515): shrinks to fit.
            if (fejd.m_moddedText && fejd.m_moddedText.TryGetComponent<TMP_Text>(out var modded) && !modded.enableAutoSizing)
            {
                modded.fontSizeMax = modded.fontSize;
                modded.fontSizeMin = ThemeTokens.GetFloat("size.text.fit.min", 13f);
                modded.enableAutoSizing = true;
            }
            if (fejd.m_versionLabel)
            {
                fejd.m_versionLabel.alignment = TextAlignmentOptions.BottomRight;
                fejd.m_versionLabel.overflowMode = TextOverflowModes.Overflow;
            }
        }

        // Changelog (FejdStartup.m_changeLog, "Canvas Changelog/ChangeLog"; OnButtonShowChangelog toggles it): vanilla
        // draws its window as a darken_blob image (485x600 behind the topic and the scroll text). darken_blob is the
        // scrim sprite, not a panel sprite, so the panel pass left the window a bare scrim. That image is the window:
        // switched off under a RuneSkin panel stack like every other vanilla panel, kept inside the screen.
        private static void Changelog(FejdStartup fejd)
        {
            if (!fejd.m_changeLog) return;
            var window = (RectTransform)fejd.m_changeLog.transform;
            foreach (var img in window.GetComponentsInChildren<Image>(true))
            {
                if (img.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                var sprite = img.TryGetComponent<PanelSwap>(out var swapped) ? swapped.OrigSprite : img.sprite ? img.sprite.name : null;
                var rt = img.rectTransform;
                if (sprite == null || !sprite.StartsWith("darken", StringComparison.Ordinal) || (rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one)) continue;
                PanelSwap.Apply(img, sprite);
                PanelDecor.Ensure(rt, "size.panel.pad", glass: true, plaque: false);
            }
            OnScreen.Ensure(window);
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

        // World rows (mockup m5-world-a: two-line rows): the template (FejdStartup.m_worldListElement, 519x28 vanilla)
        // grows to size.world.row.h and the vanilla row pitch (m_worldListElementStep, UpdateWorldList :1311-1328) with
        // it; name on the top line, seed + modifiers under it, save-source icon at the right edge. Runs in Awake,
        // before the first UpdateWorldList clones the template.
        private static void WorldRow(FejdStartup fejd)
        {
            var row = fejd.m_worldListElement ? fejd.m_worldListElement.transform as RectTransform : null;
            if (!row || row.GetComponent<LiftMark>()) return;
            row.gameObject.AddComponent<LiftMark>();
            var h = ThemeTokens.GetFloat("size.world.row.h", 58f);
            if (Mathf.Approximately(row.anchorMin.y, row.anchorMax.y)) row.sizeDelta = new Vector2(row.sizeDelta.x, h);
            fejd.m_worldListElementStep = h + ThemeTokens.GetFloat("size.world.row.gap", 6f);
            // The vanilla selection image is tinted orange; under row_recipe_selected that read as a solid orange bar
            // (run 20261022-065352): untinted, the sprite carries the selection.
            // The vanilla image is sprite-less (a flat fill): the template carries the selection sprite itself so every
            // clone copies it (run 20261022-071214: clones drew a flat white block over the row text).
            if (row.Find("selected") is Transform sel && sel.TryGetComponent<Image>(out var selImg))
            {
                selImg.color = Color.white;
                var s = ThemeTokens.GetSprite("sprite.row.recipe.selected");
                if (s)
                {
                    selImg.sprite = s;
                    selImg.type = Image.Type.Sliced;
                    selImg.fillCenter = true;
                }
                else selImg.color = new Color(1f, 1f, 1f, 0.15f);
                selImg.raycastTarget = false;
            }
            // Codex M5 r4: metadata under the name (mockup: "Сид: ... • ..."): name across the top line, seed left and
            // modifiers right on the second line, both muted.
            var sub = ThemeTokens.GetFloat("size.world.sub.font", 17f);
            Place(row, "name", new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(16f, 0f), new Vector2(-46f, -2f), ThemeTokens.GetFloat("size.world.name.font", 24f), TextAlignmentOptions.BottomLeft);
            Place(row, "seed", Vector2.zero, new Vector2(0.45f, 0.5f), new Vector2(16f, 4f), new Vector2(0f, 0f), sub, TextAlignmentOptions.TopLeft);
            Place(row, "modifiers", new Vector2(0.45f, 0f), new Vector2(1f, 0.5f), new Vector2(8f, 4f), new Vector2(-46f, 0f), sub, TextAlignmentOptions.TopRight);
            foreach (var icon in new[] { "source_cloud", "source_local", "source_legacy" })
                if (row.Find(icon) is RectTransform ir)
                {
                    ir.anchorMin = ir.anchorMax = new Vector2(1f, 0.5f);
                    ir.pivot = new Vector2(1f, 0.5f);
                    ir.anchoredPosition = new Vector2(-12f, 0f);
                    ir.sizeDelta = new Vector2(26f, 26f);
                }
        }

        // Tab labels of a vanilla tab row (server filters: 4 tabs x 125 ref share the 520 list width, "Сообщество"
        // clipped at the knot caps, Codex M5 r3): the label spans the plate minus a small inset and shrinks to fit;
        // optional taller plates (start-game mode tabs). Tokens <prefix>.inset / .font / .font.min / .h.
        public static void TabLabels(TabHandler handler, string prefix)
        {
            var inset = ThemeTokens.GetFloat(prefix + ".inset", 14f);
            var font = ThemeTokens.GetFloat(prefix + ".font", 18f);
            var min = ThemeTokens.GetFloat(prefix + ".font.min", 13f);
            var h = ThemeTokens.GetFloat(prefix + ".h", 0f);
            var w = ThemeTokens.GetFloat(prefix + ".w", 0f);
            var buttons = new List<RectTransform>();
            foreach (var tab in handler.m_tabs)
                if (tab != null && tab.m_button) buttons.Add((RectTransform)tab.m_button.transform);
            // Widest plate that keeps a gap to the nearest sibling tab (centres stay vanilla).
            if (w > 0f)
            {
                var gap = ThemeTokens.GetFloat(prefix + ".gap", 8f);
                foreach (var a in buttons)
                    foreach (var b in buttons)
                        if (a != b && a.parent == b.parent) w = Mathf.Min(w, Mathf.Abs(a.localPosition.x - b.localPosition.x) - gap);
            }
            foreach (var rt in buttons)
            {
                Grow(rt, w, h);
            }
            foreach (var rt in buttons)
            {
                foreach (var t in rt.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (PreWorldKit.IsKeyHint(t.transform, rt)) continue;
                    var tr = t.rectTransform;
                    tr.anchorMin = Vector2.zero;
                    tr.anchorMax = Vector2.one;
                    tr.offsetMin = new Vector2(inset, 0f);
                    tr.offsetMax = new Vector2(-inset, 0f);
                    t.alignment = TextAlignmentOptions.Center;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                    t.fontSizeMax = font;
                    t.fontSizeMin = min;
                    t.fontSize = font;
                    t.enableAutoSizing = true;
                }
            }
        }

        // Grows a vanilla rect to at least w x h about its pivot, whatever its anchors: fixed anchors through sizeDelta,
        // stretched axes through the offsets; a parent layout group reads the LayoutElement (0 = axis untouched).
        public static void Grow(RectTransform rt, float w, float h)
        {
            if (!rt) return;
            var size = rt.rect.size;
            var d = new Vector2(w > 0f ? Mathf.Max(0f, w - size.x) : 0f, h > 0f ? Mathf.Max(0f, h - size.y) : 0f);
            if (d == Vector2.zero) return;
            var fixedX = Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x);
            var fixedY = Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y);
            var sd = rt.sizeDelta;
            if (fixedX) sd.x += d.x;
            if (fixedY) sd.y += d.y;
            rt.sizeDelta = sd;
            if (!fixedX)
            {
                rt.offsetMin -= new Vector2(d.x * rt.pivot.x, 0f);
                rt.offsetMax += new Vector2(d.x * (1f - rt.pivot.x), 0f);
            }
            if (!fixedY)
            {
                rt.offsetMin -= new Vector2(0f, d.y * rt.pivot.y);
                rt.offsetMax += new Vector2(0f, d.y * (1f - rt.pivot.y));
            }
            if (rt.parent && rt.parent.GetComponent<LayoutGroup>())
            {
                if (!rt.TryGetComponent<LayoutElement>(out var le)) le = rt.gameObject.AddComponent<LayoutElement>();
                if (w > 0f) le.minWidth = le.preferredWidth = Mathf.Max(le.preferredWidth, w);
                if (h > 0f) le.minHeight = le.preferredHeight = Mathf.Max(le.preferredHeight, h);
            }
        }

        // Text input fields of the start-game panel (world password FejdStartup.m_serverPassword, server filter
        // ServerListGui.m_filterInputField, add-server address): vanilla 206x30 TextInputBG boxes whose text area covers
        // only the left half. Mockup m5-world-a: a taller field_search box (role "field" from the classifier), text
        // across the box with an inset, body-size text; selection highlight in the rune glow instead of the vanilla
        // white block (Codex M5 final: "password / filter field retain vanilla styling").
        public static void Field(TMP_InputField input)
        {
            if (!input) return;
            var rt = (RectTransform)input.transform;
            Grow(rt, ThemeTokens.GetFloat("size.field.w", 0f), ThemeTokens.GetFloat("size.field.h", 40f));
            var inset = ThemeTokens.GetFloat("size.field.inset", 12f);
            if (input.textViewport)
            {
                var vp = input.textViewport;
                vp.anchorMin = Vector2.zero;
                vp.anchorMax = Vector2.one;
                vp.offsetMin = new Vector2(inset, 3f);
                vp.offsetMax = new Vector2(-inset, -3f);
                foreach (var child in vp.GetComponentsInChildren<RectTransform>(true))
                {
                    if (child == vp) continue;
                    child.anchorMin = Vector2.zero;
                    child.anchorMax = Vector2.one;
                    child.offsetMin = child.offsetMax = Vector2.zero;
                }
            }
            var font = ThemeTokens.GetFloat("size.field.font", 19f);
            foreach (var t in new[] { input.textComponent, input.placeholder as TMP_Text })
            {
                if (!t) continue;
                t.enableAutoSizing = false;
                t.fontSize = font;
                t.verticalAlignment = VerticalAlignmentOptions.Middle;
            }
            input.pointSize = font;
            input.selectionColor = ThemeTokens.GetColor("color.field.selection", new Color(0.35f, 0.85f, 1f, 0.35f));
        }

        private static void Place(Transform row, string child, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax, float size, TextAlignmentOptions align)
        {
            if (!(row.Find(child) is RectTransform rt) || !rt.TryGetComponent<TMP_Text>(out var t)) return;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
            t.enableAutoSizing = false;
            t.fontSize = size;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            // Overflow, not Ellipsis: a line box shorter than the line height made TMP drop the whole line (character
            // list, run 20261022-053130); long texts run past their box instead of vanishing.
            t.overflowMode = TextOverflowModes.Overflow;
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
                        if (!PreWorldKit.IsKeyHint(t.transform, b.transform))
                        {
                            t.enableAutoSizing = false;
                            t.fontSize = ThemeTokens.GetFloat("size.menu.link.font", 16f);
                            t.alignment = TextAlignmentOptions.Center;
                            t.overflowMode = TextOverflowModes.Overflow;
                            // Label across the plate (the vanilla text box sat left: labels read small and off-centre).
                            var tr = t.rectTransform;
                            tr.anchorMin = Vector2.zero;
                            tr.anchorMax = Vector2.one;
                            tr.offsetMin = new Vector2(24f, 0f);
                            tr.offsetMax = new Vector2(-24f, 0f);
                        }
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

        // Remove-dialog buttons sized for the modal (Codex M5 r1: much too small): wider / taller plates, larger labels;
        // centres stay vanilla (the two sit ~160 ref apart, so 150 keeps a gap).
        private static void DialogButtons(GameObject dialog)
        {
            if (!dialog) return;
            var w = ThemeTokens.GetFloat("size.dialog.button.w", 150f);
            var h = ThemeTokens.GetFloat("size.dialog.button.h", 46f);
            foreach (var b in dialog.GetComponentsInChildren<Button>(true))
            {
                var rt = (RectTransform)b.transform;
                if (!Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x)) continue; // stretched layouts keep theirs
                rt.sizeDelta = new Vector2(Mathf.Max(rt.sizeDelta.x, w), Mathf.Max(rt.sizeDelta.y, h));
                foreach (var t in b.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (PreWorldKit.IsKeyHint(t.transform, b.transform)) continue;
                    // The vanilla label box is shorter than a 22 px line: TMP truncation dropped the label (run
                    // 20261022-065352). The label fills the plate and overflows instead.
                    var tr = t.rectTransform;
                    tr.anchorMin = Vector2.zero;
                    tr.anchorMax = Vector2.one;
                    tr.offsetMin = tr.offsetMax = Vector2.zero;
                    t.alignment = TextAlignmentOptions.Center;
                    t.overflowMode = TextOverflowModes.Overflow;
                    t.enableAutoSizing = false;
                    t.fontSize = ThemeTokens.GetFloat("size.dialog.button.font", 22f);
                }
            }
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
                // Menu entries read with the lit rim of the mockup (m5-menu-a: every entry glows, hover brighter).
                ButtonPlate.Ensure(button, "button.menu");
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
                    // Any entry of the menu column (a mod's entry cloned from a plated one copies the ButtonPlate
                    // without its plate reference, run 20261022-105202: its label stayed on the display face).
                    if (b.GetComponent<ButtonPlate>())
                        foreach (var t in b.GetComponentsInChildren<TMP_Text>(true))
                            if (!PreWorldKit.IsKeyHint(t.transform, b.transform)) ctx.Role(t, "button.label.menu", "main menu entry label (plated, rune face)");
            ctx.Role(fejd.m_versionLabel, "text.version", "FejdStartup.m_versionLabel");
            // Merch store button (m_merchStoreButtonParent): its target graphic is the merch picture (sprite
            // merch_overlay_final_*); as a "button" it became an empty plate (run 20261022-105515). Picture kept.
            if (fejd.m_merchStoreButtonParent) ctx.Exclude(fejd.m_merchStoreButtonParent.transform, "merch store picture button (vanilla art)");
            Danger(ctx, fejd.m_removeCharacterDialog);
            Danger(ctx, fejd.m_removeWorldDialog);
            if (fejd.m_removeCharacterName) ctx.Role(fejd.m_removeCharacterName, "dialog.subject", "delete confirm: the character's name (gold)");
            // Primary actions (mockups m5-char-a / m5-world-a: Start = lit plate, gold label).
            Primary(ctx, fejd.m_csStartButton);
            Primary(ctx, fejd.m_worldStart);
            foreach (var handler in root.GetComponentsInChildren<TabHandler>(true))
                foreach (var tab in handler.m_tabs)
                    if (tab?.m_button && tab.m_button.targetGraphic) ctx.Role(tab.m_button.targetGraphic, "tab", "TabHandler tab button");
            CharacterList.Roles(ctx);
            // World row clones under the list: same roles as their template (the root pass would call the sprite-less
            // vanilla selection a scrim).
            if (fejd.m_worldListRoot)
                foreach (Transform clone in fejd.m_worldListRoot)
                {
                    if (clone.Find("bkg") is Transform cb && cb.TryGetComponent<Image>(out var cbi)) ctx.Role(cbi, "craft.row", "world row plate");
                    if (clone.Find("selected") is Transform cs && cs.TryGetComponent<Image>(out var csi)) ctx.Role(csi, "craft.row.selected", "world row selection (vanilla toggles it)");
                    foreach (var n in new[] { "modifiers", "seed" })
                        if (clone.Find(n) is Transform ct && ct.TryGetComponent<TMP_Text>(out var ctx2)) ctx.Role(ctx2, "text.muted", "world row second line");
                    if (clone.Find("name") is Transform cn && cn.TryGetComponent<TMP_Text>(out var cnt)) ctx.Role(cnt, "row.label", "world row name (placed by WorldRow, no plate fit)");
                }
            // World row template: a list row (row_recipe plates like the character list), muted second-line texts.
            if (fejd.m_worldListElement && root == fejd.m_worldListElement.transform)
            {
                if (root.Find("bkg") is Transform bkg && bkg.TryGetComponent<Image>(out var bi)) ctx.Role(bi, "craft.row", "world row plate");
                if (root.Find("selected") is Transform sel && sel.TryGetComponent<Image>(out var si)) ctx.Role(si, "craft.row.selected", "world row selection (vanilla toggles it)");
                foreach (var n in new[] { "modifiers", "seed" })
                    if (root.Find(n) is Transform tt && tt.TryGetComponent<TMP_Text>(out var tx)) ctx.Role(tx, "text.muted", "world row second line");
                if (root.Find("name") is Transform nt && nt.TryGetComponent<TMP_Text>(out var nx)) ctx.Role(nx, "row.label", "world row name (placed by WorldRow, no plate fit)");
            }
            PreWorldKit.Common(ctx, root);
            return ctx;
        }

        // Confirm-delete dialogs: the Yes button is the danger action (art: button_danger_*).
        private static void Danger(ScreenContext ctx, GameObject dialog)
        {
            if (!dialog) return;
            foreach (var b in dialog.GetComponentsInChildren<Button>(true))
                if (b.name.IndexOf("Yes", StringComparison.OrdinalIgnoreCase) >= 0 && b.targetGraphic)
                {
                    if (b.targetGraphic is Image) ctx.Role(b.targetGraphic, "button.danger", "remove dialog confirm (danger)");
                    foreach (var t in b.GetComponentsInChildren<TMP_Text>(true))
                        if (!PreWorldKit.IsKeyHint(t.transform, b.transform)) ctx.Role(t, "button.label.danger", "remove dialog confirm label (danger red)");
                }
        }

        public static void Primary(ScreenContext ctx, Button b)
        {
            if (!b) return;
            if (b.targetGraphic is Image) ctx.Role(b.targetGraphic, "button.primary", "primary action (lit plate)");
            foreach (var t in b.GetComponentsInChildren<TMP_Text>(true))
                if (!PreWorldKit.IsKeyHint(t.transform, b.transform)) ctx.Role(t, "button.label.primary", "primary action label (gold)");
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
                if (__instance.m_serverListTabHandler) MainMenuSkin.TabLabels(__instance.m_serverListTabHandler, "size.server.tab");
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
