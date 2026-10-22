using System;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.Ownership;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.MapScreen
{
    // Large map screen (mockup m4-map-a): hudroot/MiniMap/large (Image TextBackdrop 1620x880, Minimap.m_largeRoot)
    // gets the standard panel stack, the map image (RawImage "map", Minimap.m_mapImageLarge) is inset left of a legend
    // column (frame_inset well) that houses the vanilla pin panels: IconPanel (Icon0..4 + mouse hints, OnPressedIcon /
    // ToggleIconFilter wiring kept), IconPanel2 (death / boss / mod filters, e.g. Epic Loot's AdventurePinFilter*),
    // IconPingPanel, PublicPanel / SharedPanel toggles. The cursor-biome text (m_biomeNameLarge, rewritten each frame,
    // Minimap.cs:2646) sits on a header plaque on the top edge. NameField stays where vanilla puts it (at the click,
    // Minimap.cs:819-824). Graphics are restyled through the role pass when the map opens (pins, markers and the
    // Jotunn overlay layers inside the map are content; Jotunn's MinimapOverlayPanel gets wells / checkboxes).
    public sealed partial class MinimapSkin
    {
        private static readonly string[] LegendPanels = { "IconPanel", "IconPanel2", "IconPingPanel" };
        private static readonly string[] TogglePanels = { "PublicPanel", "SharedPanel" };

        private RectTransform _large, _legend, _title, _sockets;
        private readonly System.Collections.Generic.List<Image> _socketImages = new System.Collections.Generic.List<Image>();
        private readonly System.Collections.Generic.List<Image> _iconScratch = new System.Collections.Generic.List<Image>();
        private Image _legendImage, _titleImage;
        private PanelDecor _decor;
        private bool _largeDirty = true;
        private float _inset = -1f;

        public void MarkLargeDirty() => _largeDirty = true;

        private void BuildLarge()
        {
            _large = _map.m_largeRoot ? _map.m_largeRoot.transform as RectTransform : null;
            if (!_large || !_map.m_mapImageLarge) return;
            if (_large.TryGetComponent<Image>(out var backdrop)) backdrop.enabled = false;
            _decor = PanelDecor.Ensure(_large, "size.panel.glass.inset", glass: true, plaque: false);

            _legend = HudArtRect("RuneSkin_Legend", _large);
            _legend.SetSiblingIndex(_map.m_mapImageLarge.transform.GetSiblingIndex() + 1);
            _legendImage = _legend.gameObject.AddComponent<Image>();
            _legendImage.raycastTarget = true; // clicks on the legend never fall through to the map
            foreach (var panel in LegendPanels) Hide(_large.Find(panel));
            foreach (var panel in TogglePanels) Hide(_large.Find(panel));
            // Recessed cells behind the pin-type icons (Codex r1), own container under the panels.
            _sockets = HudArtRect("RuneSkin_Sockets", _legend);
            foreach (var panel in LegendPanels) Adopt(panel);
            foreach (var panel in TogglePanels) Adopt(panel);

            _title = HudArtRect("RuneSkin_MapTitle", _large);
            _titleImage = _title.gameObject.AddComponent<Image>();
            _titleImage.raycastTarget = false;
            var biome = _map.m_biomeNameLarge;
            if (biome)
            {
                biome.rectTransform.SetParent(_title, false);
                Stretch(biome.rectTransform);
                biome.alignment = TextAlignmentOptions.Center;
                biome.textWrappingMode = TextWrappingModes.NoWrap;
                biome.enableAutoSizing = false;
            }
            // Above the gameplay HUD (hotbar / resource cluster drew over the map frame, Codex r1): own sorting.
            var canvas = _large.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = (_large.GetComponentInParent<Canvas>() is Canvas parentCanvas && parentCanvas != canvas ? parentCanvas.sortingOrder : 0) + 5;
            _large.gameObject.AddComponent<GraphicRaycaster>();
            ScreenRegistry.Register(_large, () => Context(_large));
        }

        private static RectTransform HudArtRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        // Vanilla panel backdrops (TextBackdrop on the panel and its "Bkg" child) off: the legend well replaces them.
        private static void Hide(Transform panel)
        {
            if (!panel) return;
            if (panel.TryGetComponent<Image>(out var img)) img.enabled = false;
            var bkg = panel.Find("Bkg");
            if (bkg && bkg.TryGetComponent<Image>(out var b)) b.enabled = false;
        }

        private void Adopt(string panel)
        {
            var t = _large.Find(panel) as RectTransform;
            if (!t) return;
            t.SetParent(_legend, false);
            t.anchorMin = t.anchorMax = new Vector2(0.5f, 1f);
            t.pivot = new Vector2(0.5f, 1f);
        }

        private void LargeLateUpdate()
        {
            if (_largeDirty)
            {
                _largeDirty = false;
                ScreenRegistry.Restyle(_large);
                _inset = -1f;
            }
            var inset = _decor ? _decor.ContentInset() : 0f;
            var legendW = ThemeTokens.GetFloat("size.map.legend", 300f);
            var gap = ThemeTokens.GetFloat("size.map.gap", 12f);
            if (!Mathf.Approximately(inset, _inset))
            {
                _inset = inset;
                var map = _map.m_mapImageLarge.rectTransform;
                map.anchorMin = Vector2.zero;
                map.anchorMax = Vector2.one;
                map.pivot = new Vector2(0.5f, 0.5f);
                map.offsetMin = new Vector2(inset, inset);
                map.offsetMax = new Vector2(-(inset + legendW + gap), -inset);
                _legend.anchorMin = new Vector2(1f, 0f);
                _legend.anchorMax = Vector2.one;
                _legend.pivot = new Vector2(1f, 0.5f);
                _legend.offsetMin = new Vector2(-(inset + legendW), inset);
                _legend.offsetMax = new Vector2(-inset, -inset);
                _title.anchorMin = _title.anchorMax = new Vector2(0.5f, 1f);
                _title.pivot = new Vector2(0.5f, 0.5f);
                _title.anchoredPosition = new Vector2(-(legendW + gap) * 0.5f, 0f);
                _title.sizeDelta = new Vector2(ThemeTokens.GetFloat("size.map.plaque.w", 380f), ThemeTokens.GetFloat("size.map.plaque.h", 50f));
                if (_map.m_biomeNameLarge)
                {
                    _map.m_biomeNameLarge.fontSize = ThemeTokens.GetFloat("size.map.font.title", 26f);
                    var font = ThemeTokens.GetFont("font.display");
                    if (font) _map.m_biomeNameLarge.font = font;
                    _map.m_biomeNameLarge.color = ThemeTokens.GetColor("color.text.header", Color.white);
                }
            }
            StackLegend(gap);
            Sockets();
            // Title plaque: cursor biome, else the player's biome (small map text) when the cursor is off the map.
            var big = _map.m_biomeNameLarge;
            if (big && string.IsNullOrEmpty(big.text) && _map.m_biomeNameSmall) big.text = _map.m_biomeNameSmall.text;
        }

        // The owned player HUD cluster draws over the map frame whatever the sorting (Codex r2): hidden while the
        // large map is open (CanvasGroup alpha, the cluster keeps updating), restored when it closes.
        private CanvasGroup _clusterGroup;
        private bool _clusterHidden;

        private void HideHudCluster(bool hide)
        {
            if (hide == _clusterHidden) return;
            var cluster = PlayerHud.HudCluster.Instance;
            if (!cluster) { _clusterHidden = false; return; }
            if (!_clusterGroup && !cluster.TryGetComponent(out _clusterGroup)) _clusterGroup = cluster.gameObject.AddComponent<CanvasGroup>();
            _clusterGroup.alpha = hide ? 0f : 1f;
            _clusterHidden = hide;
        }

        private void Sockets()
        {
            _iconScratch.Clear();
            foreach (var panel in LegendPanels)
            {
                var t = _legend.Find(panel);
                if (!t || !t.gameObject.activeSelf) continue;
                foreach (var b in t.GetComponentsInChildren<Button>(false))
                    if (b.targetGraphic is Image img && img.sprite) _iconScratch.Add(img);
            }
            var sprite = ThemeTokens.GetSprite("sprite.slot.recess");
            while (_socketImages.Count < _iconScratch.Count)
            {
                var rt = HudArtRect("Socket", _sockets);
                var img = rt.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                _socketImages.Add(img);
            }
            var k = _legend.lossyScale.x > 0f ? 1f / _legend.lossyScale.x : 1f;
            for (var i = 0; i < _socketImages.Count; i++)
            {
                var s = _socketImages[i];
                var on = i < _iconScratch.Count && sprite;
                if (s.gameObject.activeSelf != on) s.gameObject.SetActive(on);
                if (!on) continue;
                if (s.sprite != sprite)
                {
                    s.sprite = sprite;
                    s.type = Image.Type.Sliced;
                    s.pixelsPerUnitMultiplier = Restyler.CanvasPpuFactor(s);
                }
                var icon = _iconScratch[i].rectTransform;
                var size = icon.rect.size * (icon.lossyScale.x * k) * ThemeTokens.GetFloat("ratio.map.socket", 1.2f);
                var rt = s.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                if ((rt.sizeDelta - size).sqrMagnitude > 0.25f) rt.sizeDelta = size;
                var world = icon.TransformPoint(icon.rect.center);
                if ((rt.position - world).sqrMagnitude > 1e-4f) rt.position = world;
            }
        }

        // Pin panels side by side on top (each keeps its own size: mods add filters), toggle panels stacked below.
        private void StackLegend(float gap)
        {
            var pad = ThemeTokens.GetFloat("size.craft.well.pad", 16f);
            var rowW = 0f;
            var rowH = 0f;
            var n = 0;
            foreach (var panel in LegendPanels)
            {
                var t = _legend.Find(panel) as RectTransform;
                if (!t || !t.gameObject.activeSelf) continue;
                rowW += t.rect.width + (n++ > 0 ? gap : 0f);
                rowH = Mathf.Max(rowH, t.rect.height);
            }
            var x = -rowW * 0.5f;
            foreach (var panel in LegendPanels)
            {
                var t = _legend.Find(panel) as RectTransform;
                if (!t || !t.gameObject.activeSelf) continue;
                Place(t, new Vector2(x + t.rect.width * 0.5f, -pad));
                x += t.rect.width + gap;
            }
            var y = -pad - rowH - gap * 2f;
            foreach (var panel in TogglePanels)
            {
                var t = _legend.Find(panel) as RectTransform;
                if (!t || !t.gameObject.activeSelf) continue;
                Place(t, new Vector2(0f, y));
                y -= t.rect.height + gap;
            }
        }

        private static void Place(RectTransform t, Vector2 p)
        {
            if ((t.anchoredPosition - p).sqrMagnitude > 0.01f) t.anchoredPosition = p;
        }

        // ---------------------------------------------------------------- classification

        private ScreenContext Context(Transform root)
        {
            var ctx = new ScreenContext(ScreenId + ".Large", OwnershipRegistry.Minimap);
            if (_sockets) ctx.Exclude(_sockets, "RuneSkin legend sockets (own slot_cell_recess graphics)");
            if (_legendImage) ctx.Role(_legendImage, "craft.well", "map legend column");
            if (_titleImage) ctx.Role(_titleImage, "decor.plaque", "map title plaque (cursor biome)");
            var mapImage = _map.m_mapImageLarge ? _map.m_mapImageLarge.transform : null;
            foreach (var g in root.GetComponentsInChildren<Graphic>(true))
            {
                if (ctx.AdapterRoles.ContainsKey(g)) continue;
                if (g.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                var role = Role(root, mapImage, g, out var reason);
                if (role != null) ctx.Role(g, role, reason);
            }
            return ctx;
        }

        private static bool Under(Transform t, Transform parent)
        {
            for (var p = t; p; p = p.parent)
                if (p == parent) return true;
            return false;
        }

        private static Transform Ancestor(Transform t, Transform root, string name)
        {
            for (var p = t; p && p != root; p = p.parent)
                if (p.name.StartsWith(name, StringComparison.Ordinal)) return p;
            return null;
        }

        private static string Role(Transform root, Transform mapImage, Graphic g, out string reason)
        {
            var t = g.transform;
            var sprite = g is Image im && im.sprite ? im.sprite.name : null;
            if (t == root) { reason = "large map backdrop (panel stack replaces it)"; return "skip"; }
            if (g is TMP_Text tmp)
            {
                if (tmp.GetComponentInParent<TMP_InputField>(true)) { reason = "pin name input text"; return "content"; }
                if (Under(t, mapImage)) { reason = "pin / overlay text inside the map"; return "content"; }
                reason = "large map label (body font)";
                return "map.text";
            }
            if (g is Text) { reason = "legacy text (Jotunn overlay panel)"; return "text.legacy"; }
            if (g is TMP_SubMeshUI) { reason = "TMP sub-mesh"; return "content"; }
            var overlay = Ancestor(t, root, "MinimapOverlayPanel");
            if (overlay)
            {
                if (t == overlay) { reason = "Jotunn overlay panel background"; return "craft.well"; }
                var sel = g.GetComponentInParent<Selectable>(true);
                if (sel is Button && sel.targetGraphic == g) { reason = "Jotunn overlay panel button"; return "button"; }
                if (t.name == "Checkmark") { reason = "Jotunn overlay toggle checkmark"; return "map.check"; }
                if (sprite == "Container_Diamond") { reason = "Jotunn overlay toggle box"; return "map.checkbox"; }
                reason = "Jotunn overlay group (kept)";
                return "skip";
            }
            if (Under(t, mapImage)) { reason = "map image / pins / markers / mod layers (content)"; return "content"; }
            if (g is RawImage) { reason = "raw image (content)"; return "content"; }
            if (t.name == "Selected" && sprite == "selection_frame") { reason = "selected pin type frame"; return "slot.focus"; }
            if (sprite == "TextInputBG") { reason = "pin name field"; return "craft.search"; }
            if (sprite == "Container_Diamond") { reason = "map toggle box"; return "map.checkbox"; }
            if (sprite == "checkbox_marker_filtered") { reason = "map toggle check"; return "map.check"; }
            if (sprite == "TextBackdrop") { reason = "vanilla panel backdrop (legend well replaces it, hidden)"; return "skip"; }
            if (sprite == null) { reason = "sprite-less hit area"; return "skip"; }
            reason = $"map icon / hint '{sprite}' (content)";
            return "content";
        }

        private void RecordLarge()
        {
            if (_large) ScreenRegistry.Restyle(_large);
        }
    }
}
