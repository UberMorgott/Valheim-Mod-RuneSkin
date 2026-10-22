using System;
using System.Collections.Generic;
using RuneSkin.Classify;
using RuneSkin.GameScreens;
using RuneSkin.Ownership;
using RuneSkin.PreWorld;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.ModScreens
{
    // Roles of the fallback units (ForeignUi). Generic rules for any mod window, plus the precise adapters of the M7
    // mod panels (art\README.md "M7 sprites"), which add roles from the mods' own component wiring on top:
    // - EpicLootEnchanting (EpicLoot_UnityLib.EnchantingTableUI, Epic Loot's EnchantingUI prefab and its dialogs):
    //   TabHandler tabs = tab plates (craft.tab) with the M7 tab glyph left of the label (glyph_ench_*), labels in the
    //   display face.
    // - EpicLootBoard (EpicLoot.Adventure.MerchantPanel under the StoreGui at Haldor) / EpicLootTemper (EpicLoot.TemperPanel
    //   at Hildir): list rows = recipe rows, board tabs with glyph_board_*.
    // - ModHud: other plugins' parts under the HUD root (Epic Loot AbilityBar / DebugText, level texts): texts in the
    //   theme faces, slots as HUD sockets.
    // - ModUi: the rest (Jotunn windows, Epic Loot socket panel and popups, main menu lines of other mods).
    public static class ForeignRoles
    {
        // Component type (full name) -> precise family. These start a unit even inside a vanilla family root (Epic
        // Loot's trader panels live under the StoreGui).
        private static readonly Dictionary<string, string> Hints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["EpicLoot_UnityLib.EnchantingTableUI"] = OwnershipRegistry.EpicLootEnchanting,
            ["EpicLoot.Adventure.MerchantPanel"] = OwnershipRegistry.EpicLootBoard,
            ["EpicLoot.TemperPanel"] = OwnershipRegistry.EpicLootTemper,
            ["EpicLoot.Abilities.AbilityBar"] = OwnershipRegistry.ModHud,
            ["EpicLoot.DebugText"] = OwnershipRegistry.ModHud,
            ["EpicLoot.ShardStones.SocketsUI"] = OwnershipRegistry.ModUi,
            // VNEI attaches its window to the crafting panel (a Crafting family root).
            ["VNEI.UI.BaseUI"] = OwnershipRegistry.ModUi,
        };

        private static readonly string[] PreciseFamilies = { OwnershipRegistry.EpicLootEnchanting, OwnershipRegistry.EpicLootBoard, OwnershipRegistry.EpicLootTemper, OwnershipRegistry.ModHud };

        public static bool AnyPreciseOwned() => Array.Exists(PreciseFamilies, OwnershipRegistry.IsOwned);

        public static string HintFor(Type t) => t != null && t.FullName != null && Hints.TryGetValue(t.FullName, out var f) ? f : null;

        // ---------------------------------------------------------------- before the role pass

        // Window art of the mod's own (a sprite the theme does not map) on the unit root or a child covering most of
        // it: the panel stack, like vanilla wood panels (PreWorldKit.Panels covers those).
        public static void Before(Transform unit, string family)
        {
            if (!(unit is RectTransform ur)) return;
            var min = ThemeTokens.GetFloat("size.preworld.panel.min", 120f);
            var area = Mathf.Max(1f, ur.rect.width * ur.rect.height);
            foreach (var img in unit.GetComponentsInChildren<Image>(true))
            {
                if (!img || img.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                var depth = img.transform == unit ? 0 : img.transform.parent == unit ? 1 : 2;
                if (depth > 1 || !img.sprite || img.TryGetComponent<PanelSwap>(out _)) continue;
                if (img.GetComponentInParent<Selectable>(true) || img.GetComponent<Mask>() || img.GetComponent<ScrollRect>()) continue;
                var sprite = img.sprite.name;
                if (PreWorldKit.IsPanelSprite(sprite) || Classifier.BySprite(sprite, family) != null) continue;
                var r = img.rectTransform.rect;
                var share = r.width * r.height / area;
                if (!ForeignMath.IsWindowPanel(depth, share, r.width, r.height, min, img.type == Image.Type.Sliced || img.type == Image.Type.Tiled)) continue;
                PanelSwap.Apply(img, sprite);
                PanelDecor.Ensure(img.rectTransform, "size.panel.pad", glass: true, plaque: false);
            }
        }

        // ---------------------------------------------------------------- roles

        public static void Precise(ScreenContext ctx, Transform root, string family)
        {
            if (family == OwnershipRegistry.EpicLootEnchanting || family == OwnershipRegistry.EpicLootBoard || family == OwnershipRegistry.EpicLootTemper)
            {
                foreach (var tabs in root.GetComponentsInChildren<TabHandler>(true))
                    foreach (var tab in tabs.m_tabs)
                        if (tab != null && tab.m_button && tab.m_button.targetGraphic is Image tg && !ctx.AdapterRoles.ContainsKey(tg))
                        {
                            ctx.Role(tg, "craft.tab", "mod panel tab (TabHandler)");
                            foreach (var t in tab.m_button.GetComponentsInChildren<TMP_Text>(true)) ctx.Role(t, "craft.tab.label", "tab label");
                            foreach (var t in tab.m_button.GetComponentsInChildren<Text>(true)) ctx.Role(t, "legacy.label", "tab label (legacy text)");
                        }
            }
            if (family == OwnershipRegistry.ModHud)
            {
                // HUD parts: no panel stack around HUD widgets; texts in the theme faces, frames kept as content.
                foreach (var img in root.GetComponentsInChildren<Image>(true))
                {
                    if (ctx.AdapterRoles.ContainsKey(img) || img.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                    if (img.GetComponentInParent<Selectable>(true)) continue;
                    ctx.Role(img, "content", "mod HUD art (kept)");
                }
            }
        }

        // Rules every foreign window gets (after the precise ones, before PreWorldKit.Common): icon buttons keep their
        // glyph art, legacy texts take the theme colour (titles the rune face), list rows inside a scroll view are rows.
        public static void Generic(ScreenContext ctx, Transform root)
        {
            var header = ThemeTokens.GetFloat("size.text.header.min", 28f);
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (ctx.AdapterRoles.ContainsKey(img) || img.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                var sel = img.GetComponentInParent<Selectable>(true);
                if (!(sel is Button b) || b.targetGraphic != img || !img.sprite) continue;
                var sprite = img.GetComponent<SkinMark>() is SkinMark sm && !string.IsNullOrEmpty(sm.OriginalSprite) ? sm.OriginalSprite : img.sprite.name;
                if (Classifier.BySprite(sprite, ctx.Family) != null || PreWorldKit.IsPanelSprite(sprite)) continue;
                // A Simple-type sprite on a button is its glyph (close cross, arrow, item icon), not a plate.
                if (img.type == Image.Type.Simple) ctx.Role(img, "content", $"icon button glyph '{sprite}' (mod art kept)");
            }
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                if (ctx.AdapterRoles.ContainsKey(t)) continue;
                var input = t.GetComponentInParent<InputField>(true);
                if (input) { ctx.Role(t, input.placeholder == t ? "legacy.muted" : "legacy.body", "legacy input text"); continue; }
                if (t.GetComponentInParent<Button>(true)) { ctx.Role(t, "legacy.label", "legacy button label"); continue; }
                ctx.Role(t, t.fontSize >= header ? "legacy.title" : "legacy.body", t.fontSize >= header ? "legacy title (rune face)" : "legacy text (theme colour)");
            }
        }

        // ---------------------------------------------------------------- after the role pass

        // Tab glyphs of the M7 kit left of the label, only where the plate leaves room beside the centred label.
        public static void After(Transform unit, string family)
        {
            if (family != OwnershipRegistry.EpicLootEnchanting && family != OwnershipRegistry.EpicLootBoard) return;
            foreach (var tabs in unit.GetComponentsInChildren<TabHandler>(true))
                foreach (var tab in tabs.m_tabs)
                    if (tab != null && tab.m_button)
                        Glyph(tab.m_button, family == OwnershipRegistry.EpicLootEnchanting ? ForeignMath.EnchantGlyph(tab.m_button.name) : ForeignMath.BoardGlyph(tab.m_button.name));
            if (family == OwnershipRegistry.EpicLootBoard)
                foreach (var b in unit.GetComponentsInChildren<Button>(true))
                    if (!b.transform.Find(GlyphName)) Glyph(b, ForeignMath.BoardGlyph(b.name));
        }

        private const string GlyphName = "RuneSkinTabGlyph";

        private static void Glyph(Button b, string role)
        {
            if (role == null || !ThemeTokens.HasRole(role) || !(b.transform is RectTransform rt)) return;
            if (rt.Find(GlyphName)) return;
            var r = rt.rect;
            var side = Mathf.Round(r.height * 0.6f);
            var pad = Mathf.Round(r.height * 0.2f);
            float label = 0f;
            var tmp = b.GetComponentInChildren<TMP_Text>(true);
            if (tmp) label = tmp.preferredWidth;
            else if (b.GetComponentInChildren<Text>(true) is Text lt) label = lt.preferredWidth;
            // Room: the centred label must clear the glyph (pad + side + pad on the left).
            if (side < 10f || r.width * 0.5f - label * 0.5f < pad * 2f + side) return;
            var img = GameScreenKit.Decor(rt, GlyphName, role, "M7 tab glyph (left of the label)");
            var g = img.rectTransform;
            g.anchorMin = g.anchorMax = new Vector2(0f, 0.5f);
            g.pivot = new Vector2(0f, 0.5f);
            g.anchoredPosition = new Vector2(pad, 0f);
            g.sizeDelta = new Vector2(side, side);
            Api.ScreenRegistry.Restyle(Api.ScreenRegistry.RootOf(rt));
        }
    }
}
