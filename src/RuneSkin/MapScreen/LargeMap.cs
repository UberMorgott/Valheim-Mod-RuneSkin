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

        private RectTransform _large, _legend;

        private Image _legendImage;
        private RuneWindowHeader _header;
        private PanelDecor _decor;
        private bool _largeDirty = true;
        private float _inset = -1f, _hintBand = -1f, _top = -1f;

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

            foreach (var panel in LegendPanels) Adopt(panel);
            foreach (var panel in TogglePanels) Adopt(panel);
            BuildLegendRows();

            // The map window's one header (RuneWindowHeader, user 2026-10-26): title = the cursor / player biome text
            // (m_biomeNameLarge, rewritten by vanilla each frame, Minimap.cs:2646, mirrored); dragging it moves the whole
            // window (MovableWindow on m_largeRoot: the side panel is part of the window, fixed in place), a double-click
            // resets it; the maximize / restore button sits in its aux slot (MapWindowButton, LargeMap.Maximize).
            MovableWindow.Ensure(_large, WindowMove.Map);
            _header = RuneWindowHeader.Ensure(_large, WindowMove.Map, _large);
            if (_header)
            {
                if (_map.m_biomeNameLarge) _header.Adopt(_map.m_biomeNameLarge, "$runeskin_win_map");
                else _header.Fallback("$runeskin_win_map").Text("$runeskin_win_map");
                _header.Stretch(MaxButtonShare);
            }
            BuildMaxButton();
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
            Maximize();
            var inset = _decor ? _decor.ContentInset() : 0f;
            var legendW = ThemeTokens.GetFloat("size.map.legend", 300f);
            var gap = ThemeTokens.GetFloat("size.map.gap", 12f);
            var band = HintBand();
            // Content rect (user 2026-10-26: the map texture ran under the side panel, an empty strip right of it): the
            // window content inset on every side, under the header at the top; the side panel docked right, the map image
            // exactly the rest left of it (vanilla UpdateMap / ScreenToWorldPoint read m_mapImageLarge's rect every
            // frame - uv aspect = rect aspect, pins at MapPointToLocalGuiPos - so pins, clicks and pings follow any size).
            var headerH = _header ? ((RectTransform)_header.transform).rect.height : 0f;
            var top = Mathf.Max(inset, headerH + gap * 0.5f);
            if (!Mathf.Approximately(inset, _inset) || !Mathf.Approximately(band, _hintBand) || !Mathf.Approximately(top, _top))
            {
                _inset = inset;
                _hintBand = band;
                _top = top;
                var map = _map.m_mapImageLarge.rectTransform;
                map.anchorMin = Vector2.zero;
                map.anchorMax = Vector2.one;
                map.pivot = new Vector2(0.5f, 0.5f);
                map.offsetMin = new Vector2(inset, inset + band);
                map.offsetMax = new Vector2(-(inset + legendW + gap), -top);
                _legend.anchorMin = new Vector2(1f, 0f);
                _legend.anchorMax = Vector2.one;
                _legend.pivot = new Vector2(1f, 0.5f);
                _legend.offsetMin = new Vector2(-(inset + legendW), inset);
                _legend.offsetMax = new Vector2(-inset, -top);
            }
            PlaceMaxButton();
            LegendRows(inset);
            // Every frame while open: a label vanilla activates late (DelayActivation 1 s, Minimap.cs:883-890) or a zoom /
            // pan re-place (UpdatePins) is hung under its pin and decluttered before the canvas draws (PinLabels.cs).
            PlaceLabels();
            // Title plaque: cursor biome, else the player's biome (small map text) when the cursor is off the map.
            var big = _map.m_biomeNameLarge;
            if (big && string.IsNullOrEmpty(big.text) && _map.m_biomeNameSmall) big.text = _map.m_biomeNameSmall.text;
        }

        // The owned player HUD cluster draws over the map frame whatever the sorting (Codex r2): hidden while the
        // large map is open (CanvasGroup alpha + RenderGate: not drawn under the map, the cluster keeps updating), restored
        // when it closes.
        public const string MapOwner = "largemap";
        private CanvasGroup _clusterGroup;
        private bool _clusterHidden;

        private void HideHudCluster(bool hide)
        {
            if (hide == _clusterHidden) return;
            _clusterHidden = hide;
            var cluster = PlayerHud.HudCluster.Instance;
            if (cluster)
            {
                if (!_clusterGroup && !cluster.TryGetComponent(out _clusterGroup)) _clusterGroup = cluster.gameObject.AddComponent<CanvasGroup>();
                _clusterGroup.alpha = hide ? 0f : 1f;
                RuneSkin.Restyle.RenderGate.Set(cluster, MapOwner, hide);
            }
            // The hotbar (and the summon frames riding it) sits outside the map frame top-left (run 20261022-043546):
            // hidden with the cluster, as in mockup m4-map-a. With the HUD strip the vanilla bars are never drawn (the strip
            // owns the hotbar, HudCluster.HideForeign): leave them alone.
            if (cluster) return;
            foreach (var bar in UnityEngine.Object.FindObjectsByType<HotkeyBar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!bar.TryGetComponent<CanvasGroup>(out var g)) g = bar.gameObject.AddComponent<CanvasGroup>();
                g.alpha = hide ? 0f : 1f;
                g.blocksRaycasts = !hide;
                RuneSkin.Restyle.RenderGate.Set(bar, MapOwner, hide);
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

            if (_rows) ctx.Exclude(_rows, "RuneSkin legend rows (own sockets, names, checkboxes, players, zoom)");
            if (_footer) ctx.Exclude(_footer, "RuneSkin map key hint line");
            if (_legendImage) ctx.Role(_legendImage, "craft.well", "map legend column");
            if (_maxButton) ctx.Exclude(_maxButton.transform, "RuneSkin map window maximize / restore button (own art)");
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
