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
using RuneSkin.Compat;

namespace RuneSkin.ModScreens
{
    // Roles of the fallback units (ForeignUi). Generic rules for any mod window, plus the precise adapters of the M7
    // mod panels (art\README.md "M7 sprites"), which add roles from the mods' own component wiring on top:
    // - EpicLootEnchanting (EpicLoot_UnityLib.EnchantingTableUI, Epic Loot's EnchantingUI prefab and its dialogs):
    //   TabHandler tabs = tab plates (craft.tab), one uniform cell each (ElTabCell: M7 glyph glyph_ench_* centred, caption
    //   under it on every tab, lock badge on the icon while Epic Loot shows the feature locked), column fit in its mask
    //   (ElTabColumn). Since 0.15 its texts are TMP (VanillaFont sets Epic Loot's face on first enable; ForeignUi
    //   restyles texts written after our pass), inputs / dropdowns TMP_InputField / TMP_Dropdown (classifier roles).
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
            [ModCompat.EpicLootEnchantingUi] = OwnershipRegistry.EpicLootEnchanting,
            [ModCompat.EpicLootMerchantPanel] = OwnershipRegistry.EpicLootBoard,
            [ModCompat.EpicLootTemperPanel] = OwnershipRegistry.EpicLootTemper,
            [ModCompat.EpicLootAbilityBar] = OwnershipRegistry.ModHud,
            [ModCompat.EpicLootDebugText] = OwnershipRegistry.ModHud,
            [ModCompat.EpicLootSocketsUi] = OwnershipRegistry.ModUi,
            // VNEI attaches its window to the crafting panel (a Crafting family root).
            [ModCompat.VneiBaseUi] = OwnershipRegistry.ModUi,
        };

        private static readonly string[] PreciseFamilies = { OwnershipRegistry.EpicLootEnchanting, OwnershipRegistry.EpicLootBoard, OwnershipRegistry.EpicLootTemper, OwnershipRegistry.ModHud };

        public static bool AnyPreciseOwned() => Array.Exists(PreciseFamilies, OwnershipRegistry.IsOwned);

        public static string HintFor(Type t) => t != null && t.FullName != null && Hints.TryGetValue(t.FullName, out var f) ? f : null;

        // A mod panel docked inside the crafting window (VNEI's browser fills its body on the VNEI tab) is part of that
        // window: no panel frame, header or offset of its own (its frame would climb over the crafting tabs).
        public static bool InCrafting(Transform unit)
        {
            var gui = InventoryGui.instance;
            return gui && gui.m_crafting && unit.IsChildOf(gui.m_crafting);
        }

        // ---------------------------------------------------------------- before the role pass

        // Window art the mod switched off itself (Image disabled, not by RuneSkin's PanelSwap): the mod does not draw
        // that window, so RuneSkin draws no panel stack for it (VNEI docked into crafting hides its own woodpanel).
        public static bool OwnerHidden(Image img) => img && ForeignMath.IsOwnerHidden(img.enabled, img.TryGetComponent<PanelSwap>(out _));

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
                if (depth > 1 || !img.sprite || img.TryGetComponent<PanelSwap>(out _) || OwnerHidden(img)) continue;
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

        // A mod window (its panel stack on the unit root or a direct child, PanelSwap) gets the one window header
        // (RuneWindowHeader) over the top band of that panel: title = the mod's own title text there (rune face), else the
        // unit's name; the mod keeps placing the unit, the user offset rides on top (MovableWindow, WindowMove).
        public static void Movable(Transform unit, string id)
        {
            if (!(unit is RectTransform ur) || InCrafting(unit)) return;
            foreach (var swap in unit.GetComponentsInChildren<PanelSwap>(true))
            {
                var t = swap.transform;
                if ((t != unit && t.parent != unit) || !(t is RectTransform panel)) continue;
                var key = "mod." + id;
                MovableWindow.Ensure(ur, key);
                RuneWindowHeader.TopBand(panel, key, ur, ForeignMath.WindowTitle(unit.name, id), adoptClose: true);
                return;
            }
        }

        // ---------------------------------------------------------------- roles

        public static void Precise(ScreenContext ctx, Transform root, string family)
        {
            if (family == OwnershipRegistry.EpicLootEnchanting || family == OwnershipRegistry.EpicLootBoard || family == OwnershipRegistry.EpicLootTemper)
            {
                // Epic Loot's augment / craft result dialogs (clones of the vanilla variant dialog) inside the window.
                ElDialogs.Roles(ctx, root);
                foreach (var tabs in root.GetComponentsInChildren<TabHandler>(true))
                    foreach (var tab in tabs.m_tabs)
                        if (tab != null && tab.m_button && tab.m_button.targetGraphic is Image tg && !ctx.AdapterRoles.ContainsKey(tg))
                        {
                            ctx.Role(tg, "craft.tab", "mod panel tab (TabHandler)");
                            // The tab's own icon (Epic Loot *_tabicon) takes the M7 kit glyph of that tab.
                            if (family == OwnershipRegistry.EpicLootEnchanting)
                            {
                                EnchantTab(ctx, tab.m_button, tg);
                                continue;
                            }
                            var glyph = ForeignMath.BoardGlyph(tab.m_button.name);
                            foreach (var icon in tab.m_button.GetComponentsInChildren<Image>(true))
                                if (icon != tg && icon.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                                    ctx.Role(icon, glyph != null && ThemeTokens.HasRole(glyph) ? glyph : "content", glyph != null ? "tab icon -> M7 kit glyph" : "tab icon (mod art kept)");
                            foreach (var t in tab.m_button.GetComponentsInChildren<TMP_Text>(true)) ctx.Role(t, "craft.tab.label", "tab label");
                            foreach (var t in tab.m_button.GetComponentsInChildren<Text>(true)) ctx.Role(t, "legacy.label", "tab label (legacy text)");
                        }
            }
            if (family == OwnershipRegistry.EpicLootBoard || family == OwnershipRegistry.EpicLootTemper)
                foreach (var img in root.GetComponentsInChildren<Image>(true))
                {
                    if (ctx.AdapterRoles.ContainsKey(img)) continue;
                    // Drag parts (Epic Loot 0.15 TraderPanelDrag): the clear hit surface stays clear, the handle keeps its art.
                    if (img.name == ModCompat.EpicLootDragSurface) ctx.Role(img, "skip", "trader panel drag surface (clear hit area)");
                    else if (img.name == ModCompat.EpicLootDragHandle) ctx.Role(img, "content", "trader panel move handle (mod art kept)");
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

        // One Epic Loot enchanting tab (ForeignMath.ElTabPartOf): icon -> the M7 kit glyph of the tab (Epic Loot's art
        // where the kit has none), Epic Loot's lock container -> the lock badge socket, its padlock -> the lock glyph,
        // caption -> tab label (ElTabCell draws the caption every tab shows and places all of them).
        private static void EnchantTab(ScreenContext ctx, Button button, Image plate)
        {
            var glyph = ForeignMath.EnchantGlyph(button.name);
            foreach (var img in button.GetComponentsInChildren<Image>(true))
            {
                if (img == plate || ctx.AdapterRoles.ContainsKey(img) || img.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                switch (ForeignMath.ElTabPartOf(img.name, img.transform.parent ? img.transform.parent.name : null))
                {
                    case ForeignMath.ElTabPart.Icon:
                        ctx.Role(img, glyph != null && ThemeTokens.HasRole(glyph) ? glyph : "content", glyph != null ? "tab icon -> M7 kit glyph" : "tab icon (mod art kept)");
                        break;
                    case ForeignMath.ElTabPart.LockBadge:
                        ctx.Role(img, "badge.ench.lock", "tab lock container (FeatureStatus.LockedContainer) -> lock badge");
                        break;
                    case ForeignMath.ElTabPart.LockGlyph:
                        ctx.Role(img, "glyph.ench.lock", "tab padlock -> lock badge glyph");
                        break;
                }
            }
            foreach (var t in button.GetComponentsInChildren<TMP_Text>(true))
                if (!ctx.AdapterRoles.ContainsKey(t)) ctx.Role(t, "craft.tab.label", "tab label");
            foreach (var t in button.GetComponentsInChildren<Text>(true)) ctx.Role(t, "legacy.label", "tab label (legacy text)");
        }

        // Rules every foreign window gets (after the precise ones, before PreWorldKit.Common): icon buttons keep their
        // glyph art, legacy texts take the theme colour (titles the rune face), list rows inside a scroll view are rows.
        public static void Generic(ScreenContext ctx, Transform root)
        {
            var header = ThemeTokens.GetFloat("size.text.header.min", 28f);
            LegacyDropdowns(ctx, root);
            WideToggles(ctx, root);
            InsideControls(ctx, root);
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

        // A toggle drawn as a wide labelled plate (Epic Loot rarity filters): the checkbox art would shrink the plate to
        // a square under its label; it is a selectable row instead (its check mark stays a check, a full-size highlight
        // is the row selection).
        private static void WideToggles(ScreenContext ctx, Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Toggle>(true))
            {
                if (!(t.targetGraphic is Image box) || ctx.AdapterRoles.ContainsKey(box) || t.GetComponentInParent<Dropdown>(true) || t.GetComponentInParent<TMP_Dropdown>(true)) continue;
                var r = box.rectTransform.rect;
                if (r.height <= 0f || r.width < r.height * 1.6f) continue;
                if (!t.GetComponentInChildren<Text>(true) && !t.GetComponentInChildren<TMP_Text>(true)) continue;
                ctx.Role(box, "craft.row", "wide labelled toggle plate");
                if (t.graphic is Image check && check != box)
                {
                    var c = check.rectTransform.rect;
                    ctx.Role(check, c.width >= r.width * 0.8f ? "craft.row.selected" : "toggle.check", "wide toggle state");
                }
            }
        }

        // Legacy uGUI dropdowns (UnityEngine.UI.Dropdown; the classifier knows TMP_Dropdown): same roles.
        private static void LegacyDropdowns(ScreenContext ctx, Transform root)
        {
            foreach (var d in root.GetComponentsInChildren<Dropdown>(true))
            {
                if (d.targetGraphic is Image field) ctx.Role(field, "dropdown.field", "legacy Dropdown.targetGraphic");
                foreach (var img in d.GetComponentsInChildren<Image>(true))
                {
                    if (img == d.targetGraphic || ctx.AdapterRoles.ContainsKey(img)) continue;
                    var inTemplate = d.template && img.transform.IsChildOf(d.template);
                    if (d.template && img.rectTransform == d.template) ctx.Role(img, "dropdown.list", "legacy Dropdown.template");
                    else if (inTemplate && img.GetComponentInParent<Toggle>(true) is Toggle t)
                        ctx.Role(img, t.graphic == img ? "toggle.check" : t.targetGraphic == img ? "dropdown.item" : "content", "legacy dropdown item");
                    else if (inTemplate && img.GetComponentInParent<Scrollbar>(true) is Scrollbar bar)
                        ctx.Role(img, bar.targetGraphic == img ? "scrollbar.handle" : bar.gameObject == img.gameObject ? "scrollbar.track" : "content", "legacy dropdown list scrollbar");
                    else if (inTemplate) ctx.Role(img, "skip", "legacy dropdown list structure");
                    else if (img.name.IndexOf("Arrow", StringComparison.OrdinalIgnoreCase) >= 0) ctx.Role(img, "dropdown.arrow", "legacy Dropdown arrow");
                }
            }
        }

        // Images inside a control that are not its wired parts (target / check mark / handle / fill / track): state
        // overlays (glow, focus), item icons, stars, lock marks - the mod's art, kept; a vanilla input backdrop is a field.
        private static void InsideControls(ScreenContext ctx, Transform root)
        {
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (ctx.AdapterRoles.ContainsKey(img) || img.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                var sel = img.GetComponentInParent<Selectable>(true);
                if (!sel || sel.targetGraphic == img || img.GetComponentInParent<TMP_Dropdown>(true) || img.GetComponentInParent<Scrollbar>(true)) continue;
                if (sel is Toggle tog && tog.graphic == img) continue;
                if (sel is Slider sl && (sl.fillRect == img.rectTransform || sl.handleRect == img.rectTransform || sl.gameObject == img.gameObject)) continue;
                if (img.GetComponent<Mask>() || img.GetComponent<RectMask2D>() || img.GetComponent<ScrollRect>()) continue;
                var sprite = img.GetComponent<SkinMark>() is SkinMark sm && !string.IsNullOrEmpty(sm.OriginalSprite) ? sm.OriginalSprite : img.sprite ? img.sprite.name : null;
                if (sprite == null || Classifier.BySprite(sprite, ctx.Family) != null || PreWorldKit.IsPanelSprite(sprite)) continue;
                if (sprite.StartsWith("InputFieldBackground", StringComparison.Ordinal)) ctx.Role(img, "field", $"input backdrop '{sprite}'");
                else ctx.Role(img, "content", $"mod art inside a control '{sprite}' (kept)");
            }
        }

        // ---------------------------------------------------------------- after the role pass

        // Tab glyphs of the M7 kit left of the label, only where the plate leaves room beside the centred label.
        public static void After(Transform unit, string family)
        {
            if (family == OwnershipRegistry.EpicLootEnchanting || family == OwnershipRegistry.EpicLootBoard) ElDialogs.After(unit);
            if (family == OwnershipRegistry.EpicLootEnchanting)
            {
                // Uniform tab grid: column inside its mask, every tab the same cell (icon, caption, lock badge).
                foreach (var tabs in unit.GetComponentsInChildren<TabHandler>(true))
                {
                    ElTabColumn.Ensure(tabs);
                    foreach (var tab in tabs.m_tabs)
                        if (tab != null && tab.m_button) ElTabCell.Ensure(tab.m_button);
                }
                return;
            }
            if (family != OwnershipRegistry.EpicLootBoard) return;
            foreach (var tabs in unit.GetComponentsInChildren<TabHandler>(true))
                foreach (var tab in tabs.m_tabs)
                    if (tab != null && tab.m_button)
                        Glyph(tab.m_button, ForeignMath.BoardGlyph(tab.m_button.name));
            if (family == OwnershipRegistry.EpicLootBoard)
                foreach (var b in unit.GetComponentsInChildren<Button>(true))
                    if (!b.transform.Find(GlyphName)) Glyph(b, ForeignMath.BoardGlyph(b.name));
        }

        private const string GlyphName = "RuneSkinTabGlyph";

        private static void Glyph(Button b, string role)
        {
            if (role == null || !ThemeTokens.HasRole(role) || !(b.transform is RectTransform rt)) return;
            if (rt.Find(GlyphName)) return;
            // The tab already has an icon of its own (it takes the kit glyph through its role).
            foreach (var own in b.GetComponentsInChildren<Image>(true))
                if (own != b.targetGraphic && own.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0) return;
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
