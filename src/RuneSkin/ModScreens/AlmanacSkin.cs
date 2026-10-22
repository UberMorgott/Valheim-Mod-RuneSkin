using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.Ownership;
using RuneSkin.Restyle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.ModScreens
{
    // Family "Almanac": the window of the Almanac mod (RustyMods.Almanac, our fork 3.8.0.4; it replaces the vanilla
    // trophies panel - its OnOpenTrophies prefix shows it and hides the inventory). Restyle only: Almanac's own
    // geometry, content and behaviour stay; no prefab swap, the bundle asset itself is never touched (Unity refuses
    // children under a persistent asset; AugaSkin hit that). Built from Almanac's sources (Almanac\UI\AlmanacPanel.cs,
    // UI_Patches.cs): its InventoryGui.Awake postfix instantiates bundle prefab "AlmanacUI" as IngameGui/HUD/"Almanac"
    // (AlmanacPanel: bkg + darken, topic, Tabs/<name> {Text, Selected/Text}, ListView / ButtonView (GridView:
    // Viewport/ListRoot + Scrollbar, templates Viewport/ListElement {Selected, Background(Button)/Icon, Notice, Name,
    // Description} and Viewport/Button {Selected, Text}), TextView, GamblePanel, Description (RightPanel: Icon, Name,
    // MainButton, Requirements, InfoView), SearchField / SearchField (1), Currency, Closebutton). Texts are legacy
    // UnityEngine.UI.Text: theme colour and the theme face as a legacy Font (bundle PT Sans / Cormorant). Elements are cloned per tab
    // (GridView / TextView / InfoView Create*): a postfix on every Create* / On*Tab / Show marks the window dirty and
    // the next LateUpdate restyles it (styled templates make clones cheap: their marks carry the originals).
    public sealed class AlmanacSkin : MonoBehaviour
    {
        public const string ScreenId = "Almanac";
        public const string PluginGuid = "RustyMods.Almanac";
        private static AlmanacSkin _instance;
        private static bool _patched;

        private RectTransform _root;
        private bool _dirty = true;
        private Transform _form;

        public static void Attach(InventoryGui gui, Harmony harmony)
        {
            var hud = gui && gui.transform.parent ? gui.transform.parent.Find("HUD") : null;
            var root = hud ? hud.Find("Almanac") as RectTransform : null;
            if (!root || root.GetComponent<AlmanacSkin>()) return;
            root.gameObject.AddComponent<AlmanacSkin>().Init(root);
            PatchRefresh(harmony);
        }

        private void Init(RectTransform root)
        {
            _instance = this;
            _root = root;
            // The window is bkg's rect (1310x800 in the bundle prefab; the root is only the grid area): the panel stack
            // goes under bkg (first child: behind every other window child), bkg's own image stops drawing. Almanac's
            // transparent mode deactivates bkg: then the stack goes with it and its own dim shows instead.
            var bkg = root.Find("bkg") as RectTransform;
            if (bkg && bkg.TryGetComponent<Image>(out var b)) b.enabled = false;
            PanelDecor.Ensure(bkg ? bkg : root, "size.panel.glass.inset", glass: true, plaque: false);
            ScreenRegistry.Register(root, () => Context(_root, ScreenId));
            ScreenRegistry.Restyle(root);
            Plugin.Log.LogInfo("[RuneSkin] Almanac window attached");
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public static void MarkDirty()
        {
            if (_instance) _instance._dirty = true;
        }

        private void LateUpdate()
        {
            if (!_dirty || !_root.gameObject.activeInHierarchy) return;
            _dirty = false;
            ScreenRegistry.Restyle(_root);
            NeutralScrollbars(_root);
            FormPanel();
        }

        // Almanac's scrollbars keep a green ColorTint from its bundle: our scrollbar art is tinted by the sprite states
        // only (white tint), like every other RuneSkin scrollbar (Codex review: greenish bars off-kit).
        private static void NeutralScrollbars(Transform root)
        {
            foreach (var bar in root.GetComponentsInChildren<Scrollbar>(true))
            {
                if (bar.transition == Selectable.Transition.ColorTint && bar.colors.normalColor != Color.white) bar.colors = ColorBlock.defaultColorBlock;
                if (bar.targetGraphic && bar.targetGraphic.color != Color.white) bar.targetGraphic.color = Color.white;
                if (bar.TryGetComponent<Image>(out var track) && track.color != Color.white) track.color = Color.white;
            }
        }

        // AlmanacPanel.Start instantiates the form modal (static AlmanacPanel.formPanel) next to the panel: its own root.
        private void FormPanel()
        {
            if (_form) return;
            var panel = AccessTools.TypeByName("Almanac.UI.AlmanacPanel");
            var field = panel != null ? AccessTools.Field(panel, "formPanel") : null;
            if (field == null || !field.IsStatic) return;
            if (!(field.GetValue(null) is Component form) || !form) return;
            _form = form.transform;
            ScreenRegistry.Register(_form, () => Context(_form, ScreenId + ".Form"));
            ScreenRegistry.Restyle(_form);
        }

        // Postfixes on the methods that create or refill elements (patched once, after Almanac's Awake built the panel:
        // patching earlier would run FormPanel's static initializer before Almanac loaded its bundle - AugaSkin note).
        private static void PatchRefresh(Harmony harmony)
        {
            if (_patched) return;
            _patched = true;
            var panel = AccessTools.TypeByName("Almanac.UI.AlmanacPanel");
            if (panel == null) { Plugin.Log.LogWarning("[RuneSkin] Almanac present without Almanac.UI.AlmanacPanel: new elements stay unstyled until reopened"); return; }
            var postfix = new HarmonyMethod(typeof(AlmanacSkin), nameof(Refresh_Postfix)) { priority = Priority.Last };
            var count = 0;
            var types = new List<Type> { panel };
            types.AddRange(panel.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
            foreach (var type in types)
            {
                if (type.IsGenericTypeDefinition) continue;
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (m.IsAbstract || m.IsGenericMethodDefinition || m.GetMethodBody() == null) continue;
                    var hit = type == panel
                        ? m.Name == "Show" || m.Name == "Start" || m.Name.StartsWith("On", StringComparison.Ordinal) && m.Name.EndsWith("Tab", StringComparison.Ordinal)
                        : m.Name.StartsWith("Create", StringComparison.Ordinal);
                    if (!hit) continue;
                    try
                    {
                        harmony.Patch(m, postfix: postfix);
                        count++;
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"[RuneSkin] Almanac refresh hook {type.Name}.{m.Name} not installed: {e.GetBaseException().Message}");
                    }
                }
            }
            Plugin.Log.LogInfo($"[RuneSkin] Almanac refresh hooks: {count}");
        }

        private static void Refresh_Postfix() => MarkDirty();

        // ---------------------------------------------------------------- classification

        private static ScreenContext Context(Transform root, string screenId)
        {
            var ctx = new ScreenContext(screenId, OwnershipRegistry.Almanac);
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                var t = image.transform;
                if (image.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                var role = ImageRole(root, image, out var reason);
                if (role != null) ctx.Role(image, role, reason);
            }
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                var t = text.transform;
                if (text.GetComponentInParent<InputField>(true)) ctx.Role(text, "content", "input field text / placeholder (user content)");
                else if (t.name == "topic" && t.parent == root) ctx.Role(text, "text.legacy.title", "window title");
                else if (t.parent && t.parent.name == "Selected" && InTab(root, t)) ctx.Role(text, "text.legacy.accent", "selected tab label");
                else if (t.name == "Name" && t.parent && t.parent.name == "Description") ctx.Role(text, "text.legacy.accent", "detail title (item title colour)");
                else ctx.Role(text, "text.legacy", "Almanac text (legacy uGUI: theme colour, legacy body face)");
            }
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                if (!(text.GetComponent<SkinMark>() is SkinMark m && m.Internal))
                    ctx.Role(text, text.GetComponentInParent<TMP_InputField>(true) ? "content" : "text.body", "Almanac TMP text");
            return ctx;
        }

        private static bool InTab(Transform root, Transform t)
        {
            for (var p = t; p && p != root; p = p.parent)
                if (p.parent && p.parent.name == "Tabs" && p.parent.parent == root) return true;
            return false;
        }

        // Element of a grid (ListRoot child) or its template (Viewport/ListElement, Viewport/Button).
        private static Transform Element(Transform root, Transform t)
        {
            for (var p = t; p && p != root; p = p.parent)
                if (p.parent && (p.parent.name == "ListRoot" || p.parent.name == "Viewport" && (p.name == "ListElement" || p.name == "Button"))) return p;
            return null;
        }

        private static string ImageRole(Transform root, Image image, out string reason)
        {
            var t = image.transform;
            reason = null;
            if (t.parent == root && t.name == "bkg") { reason = "Almanac window background (replaced by the panel stack)"; return "skip"; }
            if (t.parent == root && t.name == "darken") { reason = "Almanac transparent-mode dim (kept)"; return "skip"; }
            // Grid / text viewports: clip graphic drawn with the vanilla dark field (InputFieldBackground) -> the well.
            if ((image.GetComponent<Mask>() is Mask mask && mask.showMaskGraphic || image.GetComponent<RectMask2D>()) && image.sprite && image.sprite.name.StartsWith("InputFieldBackground", StringComparison.Ordinal))
            {
                reason = "grid / text viewport (drawn clip graphic: RectMask2D or shown Mask) as a well";
                return "craft.well";
            }            // Structure (masks, viewports, scrollbars) goes to component semantics.
            if (image.GetComponent<Mask>() || image.GetComponent<RectMask2D>() || image.GetComponentInParent<Scrollbar>(true) || image.GetComponent<ScrollRect>()) return null;
            var selectable = image.GetComponentInParent<Selectable>(true);
            var target = selectable && selectable.targetGraphic == image;
            if (InTab(root, t))
            {
                if (target) { reason = "Almanac tab button"; return "craft.tab"; }
                if (t.name == "Selected") { reason = "Almanac selected-tab overlay"; return "almanac.tab.selected"; }
                reason = "tab child";
                return "content";
            }
            var element = Element(root, t);
            if (element)
            {
                if (t.name == "Selected") { reason = "grid element selection rim"; return element.name.StartsWith("Button", StringComparison.Ordinal) ? "craft.row.selected" : "slot.focus"; }
                if (target && t.name == "Background") { reason = "grid item cell (Element.Background button)"; return "slot.recess"; }
                if (target) { reason = "list button row (ElementButton)"; return "craft.row"; }
                reason = "element icon / notice";
                return "content";
            }
            if (selectable is InputField && target) { reason = "search field"; return "craft.well"; }
            if (target) { reason = "Almanac button"; return "button"; }
            // Content panes (GridView / TextView / lottery roots, the description's info list): sprite-less dark fills in
            // the bundle -> the well (Codex r3: a solid black block otherwise).
            if (!image.sprite && (t.parent == root || t.parent && t.parent.name == "Description") && (t.name == "ListView" || t.name == "ButtonView" || t.name == "TextView" || t.name == "GamblePanel"))
            {
                reason = "Almanac content pane (sprite-less dark fill) as a well";
                return "craft.well";
            }
            if (!image.sprite) { reason = "sprite-less container / hit area (kept)"; return "skip"; }
            var sprite = image.sprite.name;
            if (sprite.StartsWith("panel_separator", StringComparison.Ordinal) || sprite.StartsWith("BraidLine", StringComparison.Ordinal))
            {
                reason = $"Almanac divider '{sprite}'";
                return "divider";
            }
            if (sprite.StartsWith("woodpanel", StringComparison.Ordinal) || sprite.StartsWith("panel_", StringComparison.Ordinal) || sprite == "item_background" || sprite == "InputFieldBackground")
            {
                reason = $"Almanac inner panel '{sprite}' (well)";
                return "craft.well";
            }
            reason = $"Almanac graphic '{sprite}' (icon / decor, kept)";
            return "content";
        }
    }

    // Family "Almanac" hook: after every InventoryGui.Awake postfix (Almanac builds its window in its own).
    [HarmonyPatch]
    public static class AlmanacAdapter
    {
        internal static Harmony Harmony;

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(AlmanacSkin.PluginGuid, "morgott.valheim.augaskin")]
        private static void Awake_Postfix(InventoryGui __instance)
        {
            try { AlmanacSkin.Attach(__instance, Harmony); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] Almanac restyle failed (its own look stays): {e}"); }
        }
    }
}
