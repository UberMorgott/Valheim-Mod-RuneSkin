using System;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.Ownership;
using RuneSkin.PreWorld;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.WorldHud
{
    // Family "BuildHud" (mockup m6-build-a, art\README.md "M6 sprites"): the build menu (BuildUi, hudroot/BuildUIV2:
    // mode tabs, tag list, search, piece grid, favourites dropdown) and Hud.m_buildHud (SelectedInfo strip, hovered
    // piece author window) restyled through the role pass, as the M5 forms: vanilla panels -> panel stack
    // (PreWorldKit.Panels), mode tabs = tab plates, tag rows = craft rows (selected = m_toggledOnObject), piece buttons
    // = slot_cell_recess sockets (icons content), search = field_search, keyboard hint plates = plate_key, requirement
    // cells = socket_req. Hud.m_pieceSelectionWindow (the pre-V2 menu, deactivated by Hud.Awake :382) is excluded.
    public static class BuildHudSkin
    {
        public const string ScreenId = "BuildHud";

        public static void Attach(Hud hud)
        {
            if (hud.m_buildUi) Register(hud.m_buildUi.transform, ScreenId);
            if (hud.m_buildHud)
            {
                StripWell(hud.m_buildHud.transform.Find("SelectedInfo"));
                Register(hud.m_buildHud.transform, ScreenId + ".Selected");
            }
            // Hovered piece author window (crossplay author name / platform id; kit popup): its own root unless it lives
            // under the selected-piece strip (then that registration covers it).
            var author = hud.m_hoveredPieceAuthorWindow ? hud.m_hoveredPieceAuthorWindow.transform : null;
            if (author && !(hud.m_buildHud && author.IsChildOf(hud.m_buildHud.transform))) Register(author, ScreenId + ".Author");
            Plugin.Log.LogInfo("[RuneSkin] build HUD attached");
        }

        private static void Register(Transform root, string id)
        {
            PreWorldKit.Panels(root, "size.panel.pad");
            ScreenRegistry.Register(root, () => Context(root, id));
            ScreenRegistry.Restyle(root);
        }

        // Selected-piece strip (mockup m6-build-a: frame_inset well): vanilla draws its backgrounds only in some states
        // (Bkg2 objects off with the menu open, run 20261022-101946), so the strip gets its own well behind everything.
        private const string WellName = "RuneSkin_StripWell";

        private static void StripWell(Transform info)
        {
            if (!(info is RectTransform rt) || rt.Find(WellName)) return;
            var go = new GameObject(WellName, typeof(RectTransform), typeof(Image));
            var wr = (RectTransform)go.transform;
            wr.SetParent(rt, false);
            wr.SetAsFirstSibling();
            wr.anchorMin = Vector2.zero;
            wr.anchorMax = Vector2.one;
            var pad = Theme.ThemeTokens.GetFloat("size.build.strip.pad", 10f);
            wr.offsetMin = new Vector2(-pad, -pad);
            wr.offsetMax = new Vector2(pad, pad);
            go.GetComponent<Image>().raycastTarget = false;
        }

        public static void Templates(BuildUi ui)
        {
            if (ui.m_pieceButtonPrefab) RegisterTemplate(ui.m_pieceButtonPrefab.transform);
            if (ui.m_tagButtonPrefab) RegisterTemplate(ui.m_tagButtonPrefab.transform);
        }

        private static void RegisterTemplate(Transform template)
        {
            if (ScreenRegistry.ContextOf(template) != null) return;
            ScreenRegistry.Register(template, () => Context(template, ScreenId + "." + template.name.Replace("(Clone)", "")));
            ScreenRegistry.Restyle(template);
        }

        public static void Refresh(BuildUi ui) => ScreenRegistry.Restyle(ui.transform);

        private static ScreenContext Context(Transform root, string id)
        {
            var ctx = new ScreenContext(id, OwnershipRegistry.BuildHud);
            var hud = Hud.instance;
            if (hud && hud.m_pieceSelectionWindow && hud.m_pieceSelectionWindow.transform.IsChildOf(root))
                ctx.Exclude(hud.m_pieceSelectionWindow.transform, "Hud.m_pieceSelectionWindow: pre-V2 build menu, deactivated by Hud.Awake");
            foreach (var h in root.GetComponentsInChildren<TabHandler>(true))
                foreach (var tab in h.m_tabs)
                    if (tab != null && tab.m_button && tab.m_button.targetGraphic) ctx.Role(tab.m_button.targetGraphic, "tab", "BuildUi mode tab (TabHandler)");
            foreach (var tag in root.GetComponentsInChildren<BuildUiTagButton>(true))
            {
                if (tag.TryGetComponent<Selectable>(out var sel) && sel.targetGraphic) ctx.Role(sel.targetGraphic, "craft.row", "build tag row");
                else if (tag.TryGetComponent<Image>(out var row)) ctx.Role(row, "craft.row", "build tag row");
                if (tag.m_toggledOnObject && tag.m_toggledOnObject.TryGetComponent<Image>(out var on)) ctx.Role(on, "craft.row.selected", "build tag selected (m_toggledOnObject)");
                foreach (var t in tag.GetComponentsInChildren<TMP_Text>(true)) ctx.Role(t, "text.body", "build tag label");
                foreach (var img in tag.GetComponentsInChildren<Image>(true))
                    if (!ctx.AdapterRoles.ContainsKey(img) && img.sprite) ctx.Role(img, "content", $"build tag icon '{img.sprite.name}'");
            }
            foreach (var piece in root.GetComponentsInChildren<BuildUiPieceButton>(true))
            {
                if (piece.m_button && piece.m_button.targetGraphic) ctx.Role(piece.m_button.targetGraphic, "slot.recess", "build piece socket");
                else if (piece.TryGetComponent<Image>(out var back)) ctx.Role(back, "slot.recess", "build piece socket");
                foreach (var img in piece.GetComponentsInChildren<Image>(true))
                    if (!ctx.AdapterRoles.ContainsKey(img)) ctx.Role(img, "content", "build piece icon / marker (vanilla)");
            }
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                var sprite = img.sprite ? img.sprite.name : null;
                if (img.GetComponent<Restyle.SkinMark>() is Restyle.SkinMark m && !string.IsNullOrEmpty(m.OriginalSprite)) sprite = m.OriginalSprite;
                // Keyboard hint plates (InputHelp/MK hints Left / Right, SearchBarHint/Mouse): TextBackdrop boxes.
                if (img.name == WellName) ctx.Role(img, "well", "selected-piece strip well (frame_inset)");
                else if ((sprite == "TextBackdrop" || sprite == "key_base") && Under(img.transform, root, "hint")) ctx.Role(img, "key.plate", "keyboard hint plate");
                // Selected-piece strip: requirement cells (res_bkg) and the strip panel.
                else if (img.name.StartsWith("res_bkg", StringComparison.Ordinal)) ctx.Role(img, "craft.socket", "build requirement cell");
                else if (img.name == "res_icon" || img.name == "piece_icon" || img.name == "snappingHint" || img.name == "player_portrait") ctx.Role(img, "content", "piece / resource icon");
            }
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (ctx.AdapterRoles.ContainsKey(t)) continue;
                if (t.name == "piece_name") ctx.Role(t, "text.display", "selected piece name (display serif)");
                else if (t.name == "res_amount") ctx.Role(t, "skill.level", "requirement amount");
            }
            PreWorldKit.Common(ctx, root);
            return ctx;
        }

        private static bool Under(Transform t, Transform root, string fragment)
        {
            for (var p = t; p && p != root; p = p.parent)
                if (p.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
