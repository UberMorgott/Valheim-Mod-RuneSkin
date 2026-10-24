using System;
using System.Collections.Generic;
using RuneSkin.API;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.Ownership;
using RuneSkin.Responsive;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RuneSkin.Compat;

namespace RuneSkin.InventoryScreen
{
    // Family "Inventory" (M2, layout A, inventory-layout.md section 9). Sole geometry owner of the InventoryGui player
    // panel (doll, grid, footer), the container / backpack dock, the info header and the crafting panel's PLACE (its
    // look stays with the other skin until M3). How it owns geometry without fighting vanilla:
    // - grid pitch: InventoryGrid.m_elementSpace, the field vanilla lays cells out with (InventoryGrid.cs:273) and
    //   sizes the grid root with every frame (:405); existing cells are moved once when pitch / width changes (the
    //   positions vanilla would give them), no destroy/rebuild (Codex M2 review #4);
    // - panel rects: written from the layout (Geo.Place, change-only); vanilla writes m_player.sizeDelta.y only in
    //   SetInventorySize (InventoryGui.cs:1723-1726), which the next frame's placement overrides;
    // - EAQS cells: EAQS re-places them every frame in its InventoryGrid.UpdateGui postfix (EquipmentPanel
    //   UpdateInventorySlots), so the doll places them right after it (AfterGrid, HarmonyAfter EAQS), no reparenting;
    // - layout math (InventoryMath) runs only when the input tuple changes.
    public sealed class InventorySkin : MonoBehaviour
    {
        public const string ScreenId = "Inventory";
        private const string RootName = "RuneSkinInventory";

        public static InventorySkin Instance { get; private set; }

        // Crafting column box of the current layout (canvas units, top-left origin; empty before the first layout).
        public Box CraftingBox => _layout != null && InventoryGui.IsVisible() ? _layout.Crafting : default;

        public float SkinScaleValue => _input.SkinScale;

        // Centre column box (canvas units; empty before the first layout).
        public Box CenterBox => _layout != null && InventoryGui.IsVisible() ? _layout.Center : default;

        private InventoryGui _gui;
        private RectTransform _canvas;
        private Canvas _rootCanvas;
        private RectTransform _decor, _centerHost, _chestHost, _packHost, _doll, _hotbarHeader, _backdrop, _silhouette;
        private CharacterPreview _preview;
        // Theme rect of the live preview inside the figure box (PreviewMath.Place): normalized x, y, w, h + inset.
        private float _previewX, _previewY, _previewW = 1f, _previewH = 1f, _previewInset = 8f;
        private readonly InventoryTokens _tokens = new InventoryTokens();
        private readonly InventoryInput _input = new InventoryInput();
        // _base = the docked layout (InventoryMath.Compute), _layout = it moved by the user's group offset (WindowMove).
        private InventoryLayout _layout, _base;
        private int _layoutHash;
        private GameObject _tooltipPrefab;

        // Doll
        private readonly Dictionary<string, DollCell> _registered = new Dictionary<string, DollCell>();
        private readonly Dictionary<string, InventoryRegistry.Slot> _registeredBy = new Dictionary<string, InventoryRegistry.Slot>();
        private readonly List<EaqsBridge.SlotInfo> _eaqs = new List<EaqsBridge.SlotInfo>();
        private readonly List<DollEntry> _entries = new List<DollEntry>();
        private int _eaqsReadFrame = -1000, _entriesRevision = -1, _dollScroll;

        // Backpack
        private RectTransform _packPanel;
        private InventoryGrid _packGrid;
        private Inventory _packInventory;

        // Actions
        private readonly Dictionary<RectTransform, ActionBar> _bars = new Dictionary<RectTransform, ActionBar>();

        // Grid bookkeeping: pitch / width last applied, overlays revision, hotbar style.
        private readonly Dictionary<InventoryGrid, GridState> _grids = new Dictionary<InventoryGrid, GridState>();

        // HUD quick bar hidden while the inventory shows its quick cells in the same place.
        private CanvasGroup _quickGroup;
        private Transform _quickBar;

        private sealed class GridState
        {
            public float Pitch = -1f, Width = -1f, Scale = -1f;
            public int Elements = -1, OverlayRevision = -1, ThemeRevision = -1, First;
            public Inventory Inventory;
            public float WantPitch, WantScale, WantCell;
            public int Visible = int.MaxValue;
            public bool NeedsReposition;
        }

        private struct DollEntry
        {
            public string Key, Glyph, Name;
            public int Order;
            public int Element; // EAQS element index, -1 = own cell
            public DollCell Cell;
        }

        public static void Attach(InventoryGui gui)
        {
            if (!gui || gui.GetComponent<InventorySkin>()) return;
            gui.gameObject.AddComponent<InventorySkin>().Init(gui);
        }

        private void Init(InventoryGui gui)
        {
            Instance = this;
            _gui = gui;
            _rootCanvas = gui.GetComponentInParent<Canvas>();
            if (_rootCanvas) _rootCanvas = _rootCanvas.rootCanvas;
            _canvas = _rootCanvas ? (RectTransform)_rootCanvas.transform : null;
            ReadTokens();
            var element = gui.m_playerGrid ? gui.m_playerGrid.m_elementPrefab : null;
            var tip = element ? element.GetComponent<InventoryElement>() : null;
            _tooltipPrefab = tip && tip.m_tooltip ? tip.m_tooltip.m_tooltipPrefab : null;
            BuildDecor();
            BuildDoll();
            HideVanillaBackgrounds();
            WindowMove.OnMoved(WindowMove.Inventory, Move);
            foreach (var root in OwnedRoots())
            {
                var r = root;
                var id = r == _gui.m_player ? ScreenId : r == _gui.m_info ? ScreenId + ".Info" : ScreenId + ".Template";
                ScreenRegistry.Register(r, () => Context(r, id));
                ScreenRegistry.Restyle(r);
            }
            Plugin.Log.LogInfo($"[RuneSkin] inventory layout A attached (EAQS {(EaqsBridge.Available ? "on" : "off")})");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RestoreQuickBar();
        }

        private IEnumerable<Transform> OwnedRoots()
        {
            if (_gui.m_player) yield return _gui.m_player;
            if (_gui.m_info) yield return _gui.m_info;
            // m_container sits under m_player (prefab): restyled with it.
            if (_gui.m_playerGrid && _gui.m_playerGrid.m_elementPrefab) yield return _gui.m_playerGrid.m_elementPrefab.transform;
            if (_gui.m_containerGrid && _gui.m_containerGrid.m_elementPrefab && _gui.m_containerGrid.m_elementPrefab != _gui.m_playerGrid.m_elementPrefab)
                yield return _gui.m_containerGrid.m_elementPrefab.transform;
        }

        private void ReadTokens()
        {
            float F(string token, float fallback) => ThemeTokens.GetFloat(token, fallback);
            var t = _tokens;
            t.Margin = F("size.inv.margin", t.Margin); t.Gap = F("size.inv.gap", t.Gap);
            t.DockW = F("size.inv.dock", t.DockW); t.CenterW = F("size.inv.center", t.CenterW); t.CraftW = F("size.inv.craft", t.CraftW);
            t.MinDockW = F("size.inv.dock.min", t.MinDockW); t.MinCraftW = F("size.inv.craft.min", t.MinCraftW);
            t.WorkspaceMaxW = F("size.inv.workspace", t.WorkspaceMaxW); t.PanelMaxH = F("size.inv.panel.h", t.PanelMaxH);
            t.InnerGap = F("size.inv.inner.gap", t.InnerGap); t.PanelActionsH = F("size.inv.panel.actions", t.PanelActionsH);
            t.HudBand = F("size.inv.hud.band", t.HudBand); t.HeaderH = F("size.inv.header", t.HeaderH); t.FooterH = F("size.inv.footer", t.FooterH);
            t.PanelHeaderH = F("size.inv.panel.header", t.PanelHeaderH); t.PanelPad = F("size.inv.panel.pad", t.PanelPad);
            t.GridCell = F("size.slot", t.GridCell); t.GridGap = F("size.inv.cell.gap", t.GridGap); t.HotbarHeaderH = StripHotbar ? 0f : F("size.inv.hotbar.header", t.HotbarHeaderH);
            t.ContainerCell = F("size.inv.container.cell", t.ContainerCell); t.ContainerGap = F("size.inv.container.gap", t.ContainerGap);
            t.DollCell = F("size.inv.doll.cell", t.DollCell); t.DollGap = F("size.inv.doll.gap", t.DollGap);
            t.DollColumns = F("size.inv.doll.columns", t.DollColumns); t.DollPad = F("size.inv.doll.pad", t.DollPad); t.DollFigureGap = F("size.inv.doll.figure.gap", t.DollFigureGap);
            t.DollFigureAspect = F("share.inv.doll.figure", t.DollFigureAspect); t.HeaderMinH = F("size.inv.header.min", t.HeaderMinH); t.InnerGapMin = F("size.inv.inner.gap.min", t.InnerGapMin);
            t.ScrollbarW = F("size.inv.scrollbar", t.ScrollbarW); t.MinCellPhysPx = F("floor.cell.px", t.MinCellPhysPx);
            _previewX = F("share.inv.preview.x", 0f); _previewY = F("share.inv.preview.y", 0f);
            _previewW = F("share.inv.preview.w", 1f); _previewH = F("share.inv.preview.h", 1f);
            _previewInset = F("size.inv.preview.inset", 8f);
        }

        // ---------------------------------------------------------------- build

        private void BuildDecor()
        {
            var parent = _gui.m_inventoryRoot as RectTransform;
            if (!parent) return;
            var go = new GameObject(RootName, typeof(RectTransform));
            _decor = (RectTransform)go.transform;
            _decor.SetParent(parent, false);
            _decor.SetAsFirstSibling(); // behind every vanilla panel
            _decor.anchorMin = Vector2.zero;
            _decor.anchorMax = Vector2.one;
            _decor.offsetMin = _decor.offsetMax = Vector2.zero;
            _centerHost = Host("Center");
            _chestHost = Host("Chest");
            _packHost = Host("Backpack");
        }

        private RectTransform Host(string name)
        {
            var rt = (RectTransform)new GameObject(RootName + "_" + name, typeof(RectTransform)).transform;
            rt.SetParent(_decor, false);
            PanelDecor.Ensure(rt, "size.panel.glass.inset", glass: true, plaque: false);
            rt.gameObject.SetActive(false);
            return rt;
        }

        private void BuildDoll()
        {
            if (!_gui.m_player) return;
            _doll = (RectTransform)new GameObject("RuneSkinDoll", typeof(RectTransform)).transform;
            _doll.SetParent(_gui.m_player, false);
            _doll.anchorMin = _doll.anchorMax = new Vector2(0.5f, 0.5f);
            _doll.sizeDelta = Vector2.zero;
            _hotbarHeader = (RectTransform)new GameObject("RuneSkinHotbarHeader", typeof(RectTransform)).transform;
            _hotbarHeader.SetParent(_gui.m_player, false);
            var header = _hotbarHeader.gameObject.AddComponent<Image>();
            header.raycastTarget = false;
            var mark = DollCell.Mark(header, "inventory.hotbar.header", "hotbar row header (grid row 1 = HotkeyBar 1-8, HotkeyBar.cs:90-142)");
            mark.Outcome = Restyler.Apply(header, mark, ThemeTokens.Role(mark.Role)) ? Outcome.Styled : Outcome.Degraded;
            header.enabled = mark.Outcome == Outcome.Styled;
            if (_gui.m_playerGrid) _hotbarHeader.SetSiblingIndex(_gui.m_playerGrid.transform.GetSiblingIndex());

            _backdrop = Decor("RuneSkinDollBackdrop", "doll.backdrop", "doll alcove behind the silhouette");
            _silhouette = Decor("RuneSkinDollFigure", "doll.silhouette", "carved warrior silhouette (fallback when the live preview is off)");
            if (Plugin.CharacterPreview == null || Plugin.CharacterPreview.Value)
            {
                _preview = CharacterPreview.Create(_doll, "RuneSkinCharacterPreview");
                _preview.transform.SetSiblingIndex(_silhouette.GetSiblingIndex());
            }
        }

        private RectTransform Decor(string name, string role, string reason)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(_doll, false);
            var image = rt.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            var mark = DollCell.Mark(image, role, reason);
            mark.Outcome = Restyler.Apply(image, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            image.enabled = mark.Outcome == Outcome.Styled;
            rt.gameObject.SetActive(false);
            return rt;
        }

        // Vanilla panel backgrounds under owned roots: replaced by RuneSkin's panel stacks (hidden, not destroyed).
        private void HideVanillaBackgrounds()
        {
            foreach (var panel in new[] { _gui.m_player, _gui.m_container, _gui.m_info })
            {
                if (!panel) continue;
                if (panel.TryGetComponent<Image>(out var own)) own.enabled = false;
                foreach (var child in Children.Of(panel))
                    if (IsVanillaBackground(child) && child.TryGetComponent<Image>(out var image))
                        image.enabled = false;
            }
        }

        // Footer stat plates: vanilla Armor / Weight, CurrencyPocket CoinPocketUI, MorgottTweaks MT_Trash.
        private static readonly string[] StatPlates = { "Armor", ModCompat.CoinPocketPlate, "Weight", ModCompat.MtTrashPlate };

        private static bool IsVanillaBackground(Transform t) =>
            t.name == "selected_frame" || t.name.StartsWith("BraidLineHorisontalMedium", StringComparison.Ordinal) && !t.parent.name.StartsWith("Title", StringComparison.Ordinal) ||
            t.name == "sunken" || t.name == "Bkg" || t.name == "Darken" || t.name == "bkg" || t.name == "SelectedFrame" || t.name.StartsWith("Eaqs", StringComparison.Ordinal) && t.name.EndsWith("Bkg", StringComparison.Ordinal);

        // ---------------------------------------------------------------- classification

        private ScreenContext Context(Transform root, string screenId)
        {
            var ctx = new ScreenContext(screenId, OwnershipRegistry.Inventory);
            // Every cell under the root, including cells vanilla just replaced (Object.Destroy is deferred,
            // InventoryGrid.cs:262-265) and the template itself.
            foreach (var e in root.GetComponentsInChildren<InventoryElement>(true))
            {
                var inPlayer = _gui.m_playerGrid && e.transform.IsChildOf(_gui.m_playerGrid.transform);
                var eaqs = inPlayer && Player.m_localPlayer && e.Position.y >= EaqsBridge.VisibleRows(int.MaxValue);
                ElementRoles(ctx, e, inPlayer && e.Position.y == 0, eaqs);
            }
            foreach (var grid in root.GetComponentsInChildren<InventoryGrid>(true))
                if (grid.m_gridRoot && grid.m_gridRoot.TryGetComponent<Image>(out var gr))
                    ctx.Role(gr, "skip", "InventoryGrid.m_gridRoot hit area (not a screen dim)");
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                var t = image.transform;
                var sprite = image.sprite ? image.sprite.name : "";
                if (t.name == "sunken") ctx.Role(image, "skip", "vanilla inset bevel (hidden, RuneSkin panel stack)");
                else if (t.name == "bkg" && t.parent && Array.IndexOf(StatPlates, t.parent.name) >= 0) ctx.Role(image, "plate.stat", "stat plate strip (footer: armour, coins, weight, trash)");
                else if (t.name == "Weight" && t.parent && (t.parent == _gui.m_player || t.parent.GetComponentInChildren<InventoryGrid>(true))) ctx.Role(image, "plate.stat", "weight plate (InventoryGui.m_weight / m_containerWeight)");
                else if (t.name == "weight_icon") ctx.Role(image, "glyph.stat.weight", "weight plate icon");
                else if (t.name == "armor_icon" && t.parent && t.parent.name == "Armor") ctx.Role(image, "glyph.stat.armor", "armor plate icon (InventoryGui.m_armor)");
                else if (t.name == "armor_icon" && t.parent && t.parent.name == ModCompat.MtTrashPlate) ctx.Role(image, "glyph.trash", "MorgottTweaks trash plate icon");
                else if (t.name == "armor_icon" && t.parent && t.parent.name == ModCompat.CoinPocketPlate) ctx.Role(image, "content", "MorgottTweaks coin pocket: item icon");
                else if (t.name == "Background" && t.parent && t.parent.parent == _gui.m_info) ctx.Role(image, "tab.background", "info tab button background (Info/<tab>/Background)");
                else if ((t.name == "Icon" || t.name == "Image") && t.parent && t.parent.parent == _gui.m_info) ctx.Role(image, "content", "info tab icon (Button.targetGraphic, keeps its picture)");
                else if (t.parent && t.parent.name == "TitlePanel" && t.parent.parent == _gui.m_info && sprite.StartsWith("BraidLine", StringComparison.Ordinal)) ctx.Role(image, "skip", "name row braid halves (hidden under the RuneSkin name plaque)");
                else if (t.parent == _gui.m_info && (t.name == "selected_frame" || sprite.StartsWith("BraidLine", StringComparison.Ordinal))) ctx.Role(image, "skip", "info panel frame / divider, replaced by the centre panel stack (hidden)");
                else if (t.name == "Checked" && t.parent && t.parent.parent == _gui.m_info) ctx.Role(image, "content", "PVP toggle state icon");
                else if (sprite.StartsWith("BraidLine", StringComparison.Ordinal)) ctx.Role(image, "divider", "vanilla braid divider (" + sprite + ")");
            }
            foreach (var panel in new[] { _gui.m_player, _gui.m_container, _gui.m_info, _packPanel })
            {
                if (!panel) continue;
                if (panel.TryGetComponent<Image>(out var own)) ctx.Role(own, "skip", "vanilla panel background (replaced by RuneSkin panel stack)");
                foreach (var child in Children.Of(panel))
                    if (IsVanillaBackground(child) && child.TryGetComponent<Image>(out var image))
                        ctx.Role(image, "skip", "vanilla / EAQS panel background (replaced by RuneSkin panel stack)");
            }
            foreach (var b in new[] { _gui.m_takeAllButton, _gui.m_stackAllButton })
                if (b && b.targetGraphic) ctx.Role(b.targetGraphic, "action.button", "vanilla Take all / Stack all as glyph action (InventoryGui.cs:408-409)");
            if (_packPanel)
                foreach (var b in _packPanel.GetComponentsInChildren<Button>(true))
                    // Grid cells are Buttons too (InventoryElement.m_button): they keep their slot.cell role.
                    if (b.targetGraphic && !b.GetComponentInParent<InventoryElement>(true)) ctx.Role(b.targetGraphic, "action.button", "backpack panel action button");
            // The crafting panel is not ours until M3.
            if (_gui.m_crafting) ctx.Exclude(_gui.m_crafting, "crafting panel: its own root (family Crafting) or the other skin's");
            return ctx;
        }

        private static void ElementRoles(ScreenContext ctx, InventoryElement e, bool hotbar, bool doll = false)
        {
            if (e.m_button && e.m_button.targetGraphic)
                ctx.Role(e.m_button.targetGraphic, doll ? "slot.recess.item" : "slot.cell", doll ? "EAQS doll cell (recessed, item zones)" : "InventoryElement.m_button target (slot cell)");
            ctx.Role(e.m_icon, "content", "InventoryElement.m_icon (item icon)");
            ctx.Role(e.m_equiped, "slot.marker.equipped", "InventoryElement.m_equiped marker (InventoryGrid.cs:334)");
            ctx.Role(e.m_queued, "content", "InventoryElement.m_queued marker");
            ctx.Role(e.m_noteleport, "content", "InventoryElement.m_noteleport marker");
            ctx.Role(e.m_food, "content", "InventoryElement.m_food marker");
            ctx.Role(e.m_dropFocus, "content", "InventoryElement.m_dropFocus (touch drop focus)");
            if (e.m_selected && e.m_selected.TryGetComponent<Image>(out var sel)) ctx.Role(sel, "content", "InventoryElement.m_selected (gamepad selection)");
            if (e.m_durability)
                foreach (var g in e.m_durability.GetComponentsInChildren<Graphic>(true))
                    ctx.Role(g, "content", "InventoryElement.m_durability bar");
            // Cell numbers: THE HUD number style, sized per cell by GridCellLook (theme restyle would reset them).
            ctx.Role(e.m_amount, "content", "InventoryElement.m_amount (GridCellLook: HUD number, bottom-right)");
            ctx.Role(e.m_quality, "content", "InventoryElement.m_quality (GridCellLook: HUD number, top-right)");
            var binding = e.transform.Find("binding");
            if (binding && binding.TryGetComponent<TMP_Text>(out var key)) ctx.Role(key, "text.display", "hotbar binding number (InventoryGrid.cs:286-294)");
        }

        // ---------------------------------------------------------------- per frame

        // InventoryGui.Update postfix, after EAQS.
        public void Frame()
        {
            // Closing (animator "visible" already off, IsVisible still true for a frame): no relayout - Hide closed the
            // container / backpack, a recompute would move the centre column mid-fade (run 20261023-161531: 576 -> 304).
            var closing = _gui && _gui.m_animator && !_gui.m_animator.GetBool(AnimVisible);
            if (closing || !InventoryGui.IsVisible() || !_canvas || !Player.m_localPlayer)
            {
                RestoreQuickBar();
                ClearBand();
                return;
            }
            FindBackpack();
            var hash = InputHash();
            if (hash != _layoutHash || _layout == null)
            {
                _layoutHash = hash;
                Compute();
            }
            else
            {
                // Reopened with an unchanged layout (band cleared on hide): ask for the band again.
                var cluster = PlayerHud.HudCluster.Instance;
                if (cluster && !cluster.BandWanted) RequestBand(_rootCanvas.scaleFactor > 0f ? _rootCanvas.scaleFactor : 1f);
            }
            _frameReady = true;
            UpdateRegisteredSlots();
            UpdateActions();
            HideQuickBar();
            DollScrollInput();
        }

        private bool _frameReady;

        // Geometry after the Animator: InventoryGui's open/close animation writes the panels' anchored positions every
        // frame after Update (in-game run 3: m_player at (40,10) instead of (576,32) at end of frame), so every
        // placement - panels, cells, doll, quick cells - runs here, after the animation and after EAQS / vanilla
        // UpdateGui (Update phase). Change-only writes.
        private void LateUpdate()
        {
            Fade();
            if (!_frameReady || _layout == null) return;
            _frameReady = false;
            if (!InventoryGui.IsVisible()) return;
            ApplyPanels();
            foreach (var pair in _grids)
            {
                var s = pair.Value;
                if (!pair.Key || !s.NeedsReposition) continue;
                s.NeedsReposition = false;
                Reposition(pair.Key, s.Visible, s.Scale, s.WantPitch > 0f ? Mathf.Clamp01((s.WantPitch - s.WantCell) / s.WantPitch) : 0f);
            }
            if (_gui.m_playerGrid && _grids.TryGetValue(_gui.m_playerGrid, out var ps)) PlaceEaqsCells(_gui.m_playerGrid, ps.Scale);
            if (_logPlacement > 0 && --_logPlacement == 0) StartCoroutine(LogAtEndOfFrame());
        }

        // ---- open / close fade (user call 2026-10-23) --------------------------------------------------------------
        // Root cause of the "glitchy redraw" on Tab close: InventoryGui.Hide clears the animator's "visible" bool, the
        // vanilla close clip then writes the panels' anchored positions (its own layout, not ours) and slides them off
        // while IsVisible() already turned false, so LateUpdate stopped re-placing them: the panels jumped to the
        // vanilla spots and slid out. Now the screen fades in place (CanvasGroup on the InventoryGui object, Layout.PanelFade
        // seconds, 0 = instant), the RuneSkin layout is re-applied over the clip during the fade-out, and once fully
        // transparent the panels are deactivated (nothing hidden keeps drawing) until InventoryGui.Show (Unpark prefix).
        private CanvasGroup _fade;
        private float _alpha;
        private readonly List<GameObject> _parked = new List<GameObject>(4);
        private static readonly int AnimVisible = Animator.StringToHash("visible");

        private void Fade()
        {
            if (!_gui || !_gui.m_animator) return;
            if (!_fade) _fade = _gui.GetComponent<CanvasGroup>() ?? _gui.gameObject.AddComponent<CanvasGroup>();
            var want = _gui.m_animator.GetBool(AnimVisible);
            if (want && _parked.Count > 0) Unpark();
            var dur = Mathf.Max(0f, Plugin.PanelFade?.Value ?? 0.15f);
            var target = want ? 1f : 0f;
            _alpha = dur <= 0f ? target : Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime / dur);
            if (!Mathf.Approximately(_fade.alpha, _alpha)) _fade.alpha = _alpha;
            if (_fade.blocksRaycasts != want) _fade.blocksRaycasts = want;
            if (_fade.interactable != want) _fade.interactable = want;
            if (want) return;
            if (_alpha > 0f)
            {
                if (_layout != null) ApplyPanels(); // pin over the vanilla close clip while fading out
            }
            else if (_parked.Count == 0) Park();
        }

        private void Park()
        {
            foreach (var t in new Transform[] { _gui.m_player, _gui.m_crafting, _gui.m_container, _gui.m_info })
                if (t && t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(false);
                    _parked.Add(t.gameObject);
                }
        }

        // InventoryGui.Show prefix (and any frame the animator wants the screen): the parked panels come back before
        // vanilla touches them.
        public void Unpark()
        {
            foreach (var go in _parked)
                if (go) go.SetActive(true);
            _parked.Clear();
        }

        // Drag visual on top of everything (user defect 2026-10-23: the icon slid under the strip): lifted onto the shared
        // top overlay sort (TopOverlay, above the strip and the GUI root; no raycaster, the drop still hits the cell).
        public static void DragOnTop(InventoryGui gui)
        {
            if (gui && gui.m_dragGo) Restyle.TopOverlay.Lift(gui.m_dragGo); // the one shared top layer (tooltips too)
        }

        public void OnHide()
        {
            if (_preview) _preview.Close();
            RestoreQuickBar();
            ClearBand();
        }

        private int InputHash()
        {
            unchecked
            {
                var h = Screen.width * 31 + Screen.height;
                h = h * 31 + (_rootCanvas ? _rootCanvas.scaleFactor.GetHashCode() : 0);
                h = h * 31 + ResponsiveContext.SkinScale.GetHashCode();
                h = h * 31 + Screen.safeArea.GetHashCode();
                var inv = Player.m_localPlayer.GetInventory();
                h = h * 31 + inv.GetWidth() * 101 + EaqsBridge.VisibleRows(inv.GetHeight()) + (StripHotbar ? 7919 : 0);
                var c = ContainerInventory();
                h = h * 31 + (c != null ? c.GetHashCode() ^ (c.GetWidth() * 101 + c.GetHeight()) : 0);
                h = h * 31 + (ContainerIsBackpack() ? 7 : 0);
                h = h * 31 + (_packInventory != null ? _packInventory.GetHashCode() ^ (_packInventory.GetWidth() * 101 + _packInventory.GetHeight()) : 0);
                h = h * 31 + DollStorageCount();
                h = h * 31 + InventoryRegistry.Revision;
                h = h * 31 + ThemeTokens.Revision;
                h = h * 31 + Mathf.RoundToInt(HudTopLogical(_rootCanvas ? Mathf.Max(_rootCanvas.scaleFactor, 0.01f) : 1f) / 4f);
                return h;
            }
        }

        // HUD cluster top (RuneSkin HUD, buff row reserved) in this canvas's logical units, top-left origin; 0 = no HUD.
        private static float HudTopLogical(float scale)
        {
            var cluster = PlayerHud.HudCluster.Instance;
            var top = cluster ? cluster.ReservedScreenTop : 0f;
            return top > 0f ? (Screen.height - top) / scale : 0f;
        }

        private Inventory ContainerInventory() => _gui.m_currentContainer ? _gui.m_currentContainer.GetInventory() : null;

        // AdventureBackpacks opens a worn backpack without a chest as InventoryGui.Show(player's own Container)
        // (fork PlayerExtensions.OpenBackpack): m_container then shows the backpack.
        private bool ContainerIsBackpack() =>
            _gui.m_currentContainer && Player.m_localPlayer && _gui.m_currentContainer.gameObject == Player.m_localPlayer.gameObject;

        // Backpack beside a chest: a panel registered as "inventory.backpack" (Skin.RegisterPanel), else the
        // AdventureBackpacks fork's docking API (ABAPI.GetBackpackPanel). No object-name fallback (perf audit B5: own
        // mods come in through APIs).
        private void FindBackpack()
        {
            var panel = InventoryRegistry.Panel("inventory.backpack");
            if (!panel) panel = AbApi.Panel();
            if (panel && !panel.gameObject.activeInHierarchy) panel = null;
            if (panel != _packPanel)
            {
                _packPanel = panel;
                _packGrid = panel ? panel.GetComponentInChildren<InventoryGrid>(true) : null;
                if (panel && ScreenRegistry.RootOf(panel) != panel)
                {
                    var p = (Transform)panel;
                    if (panel.TryGetComponent<Image>(out var own)) own.enabled = false;
                    foreach (var child in Children.Of(panel))
                        if (IsVanillaBackground(child) && child.TryGetComponent<Image>(out var image)) image.enabled = false;
                    ScreenRegistry.Register(p, () => Context(p, ScreenId + ".Backpack"));
                    ScreenRegistry.Restyle(p);
                }
            }
            _packInventory = _packPanel ? AbApi.Inventory() ?? (_packGrid ? _packGrid.GetInventory() : null) : null;
        }

        private int DollStorageCount()
        {
            RefreshEaqs();
            var n = 0;
            foreach (var s in _eaqs)
                if (s.Kind != EaqsBridge.Kind.Quick) n++;
            foreach (var s in InventoryRegistry.Slots)
                if (!s.Dead) n++;
            return n;
        }

        private void RefreshEaqs()
        {
            if (Time.frameCount - _eaqsReadFrame < 30 || !_gui.m_playerGrid) return;
            _eaqsReadFrame = Time.frameCount;
            EaqsBridge.ReadSlots(_gui.m_playerGrid.m_elements.Count, _eaqs);
        }

        private void Compute()
        {
            var inv = Player.m_localPlayer.GetInventory();
            var i = _input;
            var cr = _canvas.rect;
            i.CanvasW = cr.width;
            i.CanvasH = cr.height;
            var scale = _rootCanvas.scaleFactor > 0f ? _rootCanvas.scaleFactor : 1f;
            var safe = Screen.safeArea;
            i.Safe = new Box(safe.x / scale, (Screen.height - safe.yMax) / scale, safe.width / scale, safe.height / scale);
            i.PhysPerUnit = scale;
            i.SkinScale = ResponsiveContext.SkinScale;
            i.GridW = inv.GetWidth();
            // HUD strip: its hotbar IS row 0, so the grid shows the rows under it only (user call 2026-10-22).
            i.GridH = EaqsBridge.VisibleRows(inv.GetHeight()) - (StripHotbar ? 1 : 0);
            var chest = ContainerIsBackpack() ? null : ContainerInventory();
            var pack = ContainerIsBackpack() ? ContainerInventory() : _packInventory;
            i.ChestW = chest?.GetWidth() ?? 0;
            i.ChestH = chest?.GetHeight() ?? 0;
            i.BackpackW = pack?.GetWidth() ?? 0;
            i.BackpackH = pack?.GetHeight() ?? 0;
            i.DollStorage = DollStorageCount();
            i.HudTop = HudTopLogical(scale);
            ReadTokens();
            _tokens.PanelCapInset = CapKeepOut(_chestHost);
            _base = InventoryMath.Compute(i, _tokens);
            Move();
            RequestBand(scale);
            _entriesRevision = -1; // rebuild doll order
            _logPlacement = 3;
            Plugin.Log.LogInfo($"[RuneSkin] inventory layout {_layout.Mode}: dock {_layout.Dock} centre {_layout.Center} crafting {_layout.Crafting} " +
                               $"grid {_layout.Player} chest {_layout.ChestGrid} backpack {_layout.BackpackGrid} doll {_layout.DollCells.Rows}r/{_layout.DollCells.VisibleRows}v" +
                               (_layout.DockOverflow ? " dock overflow" : ""));
        }

        // HUD band profile (inventory-layout.md section 4/9, user call 2026-10-21): the HUD cluster re-lays out as one
        // wide band under the column block, aligned to its left / right edges. Its height comes back as the HUD top
        // (InputHash), so the next layout gives the columns the height the band freed.
        private void RequestBand(float scale)
        {
            var cluster = PlayerHud.HudCluster.Instance;
            if (!cluster || _base == null) return;
            // The band follows the docked block (the HUD stays put when the user moves the group).
            InventoryMath.BlockEdges(_base, out var left, out var right);
            var quick = 0;
            foreach (var s in _eaqs) if (s.Kind == EaqsBridge.Kind.Quick) quick++;
            cluster.SetBand(left * scale, right * scale, quick);
        }

        // Corner-cap keep-out of a dock host's panel stack (PanelDecor.ContentInset, host units) in canvas units.
        private float CapKeepOut(RectTransform host)
        {
            var stack = host ? host.Find(PanelDecor.ChildName) : null;
            if (!stack || !stack.TryGetComponent<PanelDecor>(out var decor)) return 0f;
            return decor.ContentInset() * host.lossyScale.x / Mathf.Max(Geo.WorldPerUnit(_canvas), 1e-6f);
        }

        private static void ClearBand()
        {
            var cluster = PlayerHud.HudCluster.Instance;
            if (cluster) cluster.ClearBand();
        }

        // The column block moved by the group offset (drag on any of its title bands: name row, dock panel headers,
        // crafting header, skills / trophies titles), clamped on screen above the HUD strip.
        private void Move()
        {
            if (_base == null) return;
            var o = WindowMove.Applied(WindowMove.Inventory, InventoryMath.Block(_base), _canvas);
            _layout = InventoryMath.Shift(_base, o.x, o.y);
        }

        // Harness: the group's docked block and where it is shown (canvas units).
        public Box DockedBlock => _base != null ? InventoryMath.Block(_base) : default;
        public Box ShownBlock => _layout != null ? InventoryMath.Block(_layout) : default;

        // ---------------------------------------------------------------- panels

        private int _logPlacement;

        private System.Collections.IEnumerator LogAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame(); // after every LateUpdate / layout rebuild: what is drawn
            if (_layout != null) LogPlacement();
        }

        // Placement self-check a few frames after a layout change: target box vs measured canvas box (diagnostics).
        private void LogPlacement()
        {
            var sb = new System.Text.StringBuilder("[RuneSkin] inventory placement");
            void One(string name, Component c, Box want)
            {
                if (!c) return;
                var b = Geo.BoxOf(_canvas, (RectTransform)c.transform);
                var off = Mathf.Abs(b.X - want.X) + Mathf.Abs(b.Y - want.Y);
                sb.Append($" {name}={b}{(off > 2f ? " OFF(want " + want + ")" : "")}");
            }
            var lg = _gui.m_player.GetComponent<LayoutGroup>();
            sb.Append($" playerLayoutGroup={(lg ? lg.GetType().Name : "none")} canvasMode={_rootCanvas.renderMode}");
            if (_gui.m_playerGrid && _gui.m_playerGrid.m_elements.Count > 0 && _gui.m_playerGrid.m_elements[0])
                sb.Append($" cell0={Geo.BoxOf(_canvas, (RectTransform)_gui.m_playerGrid.m_elements[0].transform)}");
            var armor = _gui.m_armor ? DirectChild(_gui.m_player, _gui.m_armor.transform) : null;
            if (armor) sb.Append($" armorPlate={Geo.BoxOf(_canvas, armor)} footer={_layout.Footer}");
            One("player", _gui.m_player, _layout.Center);
            var gi = Inset(_layout.Grid, _tokens.PanelPad * _input.SkinScale, _tokens.PanelPad * _input.SkinScale);
            var hh = _tokens.HotbarHeaderH * _input.SkinScale;
            One("grid", _gui.m_playerGrid, new Box(gi.X, gi.Y + hh, gi.W, gi.H - hh));
            if (_gui.m_container && _gui.m_container.gameObject.activeSelf)
            {
                One("container", _gui.m_container, ContainerIsBackpack() ? _layout.Backpack : _layout.Chest);
                if (_gui.m_containerGrid.m_elements.Count > 0 && _gui.m_containerGrid.m_elements[0]) sb.Append($" ccell0={Geo.BoxOf(_canvas, (RectTransform)_gui.m_containerGrid.m_elements[0].transform)}");
            }
            if (_packPanel) One("backpack", _packPanel, _layout.Backpack);
            Plugin.Log.LogInfo(sb.ToString());
        }

        private void ApplyPanels()
        {
            var o = _layout;
            var pad = _tokens.PanelPad * _input.SkinScale;
            // Decor hosts.
            Show(_centerHost, o.Center);
            var containerShown = _gui.m_container && _gui.m_container.gameObject.activeSelf;
            var backpackInContainer = containerShown && ContainerIsBackpack();
            Show(_chestHost, containerShown && !backpackInContainer ? o.Chest : default);
            Show(_packHost, backpackInContainer || _packPanel ? o.Backpack : default);

            // Centre: player panel, info header, grid, footer.
            Geo.Place(_canvas, _gui.m_player, o.Center);
            InfoHeader(Inset(o.Header, pad, pad / 2f));
            // Grid box = carved hotbar header strip (digits 1-8 on it) + the grid below it.
            var headerH = _tokens.HotbarHeaderH * _input.SkinScale;
            var inner = Inset(o.Grid, pad, pad);
            // Grid view down to the footer plates (Footer: o.Footer inset 4 at the top) minus the same pad as at the top:
            // the rows centre in it, so the margin over the first row equals the one under the last (no empty band).
            var gridBottom = Mathf.Max(inner.Bottom, o.Footer.Y + FooterInsetY - pad);
            var gridBox = new Box(inner.X, inner.Y + headerH, inner.W, Mathf.Max(0f, gridBottom - inner.Y - headerH));
            if (_gui.m_playerGrid) Geo.Place(_canvas, (RectTransform)_gui.m_playerGrid.transform, gridBox);
            if (_hotbarHeader && _hotbarHeader.gameObject.activeSelf != !StripHotbar) _hotbarHeader.gameObject.SetActive(!StripHotbar);
            if (_hotbarHeader && !StripHotbar)
            {
                var w = o.Player.Columns * o.Player.Pitch - _tokens.GridGap;
                var x = gridBox.X + (gridBox.W - o.Player.Columns * o.Player.Pitch) / 2f;
                Geo.Place(_canvas, _hotbarHeader, new Box(x - 10f, inner.Y, w + 20f, headerH));
            }
            Footer(Inset(o.Footer, pad, FooterInsetY));

            // Dock.
            if (containerShown)
            {
                var box = backpackInContainer ? o.Backpack : o.Chest;
                var fit = backpackInContainer ? o.BackpackGrid : o.ChestGrid;
                ArrangeContainer(_gui.m_container, _gui.m_containerGrid, box, fit, backpackInContainer ? PanelKind.Backpack : PanelKind.Container,
                    ContainerInventory(), _gui.m_containerName ? _gui.m_containerName.rectTransform : null,
                    _gui.m_containerWeight ? _gui.m_containerWeight.transform : null, _gui.m_takeAllButton, _gui.m_stackAllButton);
            }
            if (_packPanel && _packGrid)
            {
                ArrangeContainer(_packPanel, _packGrid, o.Backpack, o.BackpackGrid, PanelKind.Backpack, _packInventory,
                    Twin(_gui.m_containerName ? _gui.m_containerName.transform : null) as RectTransform,
                    Twin(_gui.m_containerWeight ? _gui.m_containerWeight.transform : null),
                    Twin(_gui.m_takeAllButton ? _gui.m_takeAllButton.transform : null)?.GetComponent<Button>(),
                    Twin(_gui.m_stackAllButton ? _gui.m_stackAllButton.transform : null)?.GetComponent<Button>());
            }

            // Crafting: position only (look = other skin until M3); scaled down, never up, to stay in its column.
            // Width fit (scaled down, never up), height = the column: one bottom edge with dock and centre.
            // With family Crafting owned, CraftingScreen.CraftingSkin lays the column out on this box itself.
            if (_gui.m_crafting && _gui.m_crafting.gameObject.activeSelf && !CraftingScreen.CraftingSkin.Instance) Geo.PlaceFitWidth(_canvas, _gui.m_crafting, o.Crafting);

            // Player panel actions in the footer, right of the stats, left of the trash plate.
            Bar(_gui.m_player, PanelKind.Player, Player.m_localPlayer.GetInventory(), null, null, FooterActionBox());
        }

        // Info header (layout-a2.png): character name on top, the info tabs (texts, skills, trophies, achievements,
        // character stats, PVP: every Selectable child of m_info) as one centred row of square tabs below it.
        private void InfoHeader(Box box)
        {
            var info = _gui.m_info;
            if (!info) return;
            Geo.Place(_canvas, info, box);
            var k = _input.SkinScale;
            var nameH = Mathf.Min(ThemeTokens.GetFloat("size.inv.name.h", 52f) * k, box.H * 0.42f);
            // Name row = the group's drag band (moves the whole docked block).
            var band = WindowMove.Handle(info, WindowMove.Inventory);
            if (band) Geo.Place(_canvas, band.rectTransform, new Box(box.X, box.Y, box.W, nameH));
            var title = info.Find("TitlePanel") as RectTransform;
            if (title)
            {
                Geo.Place(_canvas, title, new Box(box.X, box.Y, box.W, nameH));
                NamePlaque(title, new Box(box.X, box.Y, box.W, nameH));
            }
            // Index loops / cached names: this runs every frame while the inventory is open (foreach over a Transform
            // allocates an enumerator, .name a string; perf audit B2).
            var tabs = 0;
            for (var n = 0; n < info.childCount; n++)
            {
                var child = info.GetChild(n);
                if (child.gameObject.activeSelf && child.TryGetComponent<Selectable>(out _)) tabs++;
            }
            if (tabs == 0) return;
            var gap = _tokens.Gap / 2f;
            // Tab row under the name (layout-a2.png): tabs as tall as the row, a little wider than tall, never past
            // the row width; each tab's background and icon are sized with it (the vanilla Background child keeps a
            // fixed 104 px rect otherwise and ran over the name plaque, Codex M2 review).
            var h = Mathf.Max(0f, box.H - nameH - gap);
            var w = Mathf.Min(h * ThemeTokens.GetFloat("share.inv.tab.aspect", 1.25f), (box.W - (tabs - 1) * gap) / tabs);
            h = Mathf.Min(h, w);
            var x = box.X + (box.W - (tabs * w + (tabs - 1) * gap)) / 2f;
            var y = box.Y + nameH + gap;
            // One tab strip on screen (user defect 2026-10-24, RuneSkin 0.20.1: the skills window showed its own row next
            // to this one): while a docked info panel draws its InfoTabs mirror of m_info, this row is scaled to 0 - not
            // deactivated, InfoTabs collects only active m_info buttons and invokes them.
            var shown = InfoScreens.InfoMath.HeaderTabsShown(
                InfoScreens.SkillsSkin.Instance && InfoScreens.SkillsSkin.Instance.IsOpen,
                InfoScreens.TrophiesSkin.Instance && InfoScreens.TrophiesSkin.Instance.IsOpen);
            for (var n = 0; n < info.childCount; n++)
            {
                var child = info.GetChild(n);
                if (!child.gameObject.activeSelf || !child.TryGetComponent<Selectable>(out _)) continue;
                HeaderTabShown(child, shown);
                if (shown) Tab((RectTransform)child, new Box(x, y, w, h));
                x += w + gap;
            }
        }

        // Header tab scale while hidden: the button's own scale is kept and given back when the docked panel closes.
        private readonly Dictionary<Transform, Vector3> _hiddenTabScale = new Dictionary<Transform, Vector3>(8);

        private void HeaderTabShown(Transform tab, bool shown)
        {
            if (shown)
            {
                if (_hiddenTabScale.TryGetValue(tab, out var scale))
                {
                    tab.localScale = scale;
                    _hiddenTabScale.Remove(tab);
                }
                return;
            }
            if (!_hiddenTabScale.ContainsKey(tab)) _hiddenTabScale[tab] = tab.localScale;
            Geo.SetScale(tab, 0f);
        }

        // One info tab on its box: the button, its Background child over the whole box, the icon (Icon / Image /
        // Checked) in a centred square inset of it.
        private void Tab(RectTransform tab, Box b)
        {
            Geo.Place(_canvas, tab, b);
            var side = Mathf.Min(b.W, b.H) * (1f - 2f * ThemeTokens.GetFloat("share.inv.tab.icon.inset", 0.16f));
            var icon = new Box(b.X + (b.W - side) / 2f, b.Y + (b.H - side) / 2f, side, side);
            for (var n = 0; n < tab.childCount; n++)
            {
                var c = tab.GetChild(n);
                if (!(c is RectTransform rt) || !c.gameObject.activeSelf || !c.TryGetComponent<Graphic>(out _)) continue;
                var part = NameCache.Of(c);
                if (part == "Background") Geo.Place(_canvas, rt, b);
                else if (part == "Icon" || part == "Image" || part == "Checked") Geo.Place(_canvas, rt, icon);
            }
        }

        private Box _footerActions;

        private Box FooterActionBox() => _footerActions;

        // Footer (layout-a2.png): stat plates armour, coins, weight left, trash plate right, player actions in between.
        // Every plate is a plate_stat strip (glyph in the round socket, value right of it) of a token width.
        private void Footer(Box box)
        {
            var k = _input.SkinScale;
            var gap = _tokens.InnerGap * k;
            var x = box.X;
            // The plates sit under m_player or the info header depending on the prefab: the stat text's own plate
            // (its parent object, e.g. Player/Weight, Player/Armor), wherever the prefab put it.
            RectTransform PlateOf(TMP_Text text) => text && text.transform.parent is RectTransform pr && pr != _gui.m_player && pr != _gui.m_info ? pr : null;
            RectTransform Named(string name) => (_gui.m_player.Find(name) ?? (_gui.m_info ? _gui.m_info.Find(name) : null)) as RectTransform;
            void Left(RectTransform plate, string token, float width)
            {
                if (!plate || !plate.gameObject.activeSelf) return;
                var w = ThemeTokens.GetFloat(token, width) * k;
                StatPlate(plate, new Box(x, box.Y, w, box.H));
                AboveGrid(plate);
                x += w + gap;
            }
            Left(PlateOf(_gui.m_armor), "size.inv.plate.armor", 104f);
            // Coin pocket plate (CurrencyPocket CoinPocketUI) next to the armour, as in the mockup.
            Left(Named(ModCompat.CoinPocketPlate), "size.inv.plate.coins", 104f);
            Left(PlateOf(_gui.m_weight), "size.inv.plate.weight", 160f);
            var right = box.Right;
            var trash = Named(ModCompat.MtTrashPlate);
            if (trash && trash.gameObject.activeSelf)
            {
                var w = ThemeTokens.GetFloat("size.inv.plate.trash", 150f) * k;
                StatPlate(trash, new Box(right - w, box.Y, w, box.H));
                AboveGrid(trash);
                right -= w + gap;
            }
            _footerActions = new Box(x, box.Y, Mathf.Max(0f, right - x), box.H);
        }

        // Footer plates draw (and raycast) after the player grid. Vanilla sizes the grid's hit area m_gridRoot to every
        // inventory row (InventoryGrid.cs:405-406), and Reposition draws fewer rows (hotbar row in the HUD strip, EAQS
        // rows hidden), so that transparent raycast Image reaches past the visible grid over the footer. The prefab puts
        // Armor / Weight (and the MorgottTweaks MT_Trash clone, CurrencyPocket CoinPocketUI) before PlayerGrid: the hit
        // area ate every click and hover on the trash plate (no drop, no tooltip) and on the coin pocket's button.
        private void AboveGrid(RectTransform plate)
        {
            var grid = _gui.m_playerGrid ? _gui.m_playerGrid.transform : null;
            if (!plate || !grid || plate.parent != grid.parent) return;
            var g = grid.GetSiblingIndex();
            if (plate.GetSiblingIndex() < g) plate.SetSiblingIndex(g); // removal shifts the grid down: lands right after it
        }

        // One stat plate on its box at scale 1: the plate_stat strip (Weight's own image, else its "bkg" child) over the
        // whole plate, the glyph in the round socket on the left, the value text in the field right of it.
        private void StatPlate(RectTransform plate, Box box)
        {
            Geo.SetScale(plate, 1f);
            Geo.Place(_canvas, plate, box);
            var socket = box.H; // plate_stat: round socket as wide as the strip is tall (sprite border 92 of 96 px)
            // Weight carries its own plate image and a bkg child: one strip only (the bkg one, like the other plates).
            if (plate.Find("bkg") && plate.TryGetComponent<Image>(out var own) && own.enabled) own.enabled = false;
            for (var n = 0; n < plate.childCount; n++)
            {
                var child = plate.GetChild(n);
                if (!(child is RectTransform rt) || !child.gameObject.activeSelf) continue;
                var part = NameCache.Of(child);
                if (part == "bkg") Geo.Place(_canvas, rt, box);
                else if (part.EndsWith("_icon", StringComparison.Ordinal)) Geo.Place(_canvas, rt, Inset(new Box(box.X, box.Y, socket, box.H), socket * 0.2f, socket * 0.2f));
                else if (child.TryGetComponent<TMP_Text>(out var text))
                {
                    Geo.Place(_canvas, rt, new Box(box.X + socket + 4f, box.Y, Mathf.Max(1f, box.W - socket - 12f), box.H));
                    if (text.alignment != TextAlignmentOptions.MidlineLeft) text.alignment = TextAlignmentOptions.MidlineLeft;
                    if (text.textWrappingMode != TextWrappingModes.NoWrap) text.textWrappingMode = TextWrappingModes.NoWrap;
                    if (text.overflowMode != TextOverflowModes.Ellipsis) text.overflowMode = TextOverflowModes.Ellipsis;
                    // Value at ~45% of the strip height (mockup), never below the body floor.
                    var size = Mathf.Max(rt.rect.height * 0.5f, ThemeTokens.GetFloat("floor.text.body.px", 16f) / Mathf.Max(0.01f, _rootCanvas.scaleFactor));
                    if (text.enableAutoSizing) text.enableAutoSizing = false;
                    if (Mathf.Abs(text.fontSize - size) > 0.1f) text.fontSize = size;
                }
            }
        }

        private Image _namePlaque;

        // Character name on the carved header plaque (layout-a2.png "AUTOTESTCHAR"): plaque centred in the name row,
        // name in capitals between its two knot ends, auto-sized down to the floor, never clipped mid-word.
        private void NamePlaque(RectTransform title, Box box)
        {
            var k = _input.SkinScale;
            if (!_namePlaque)
            {
                _namePlaque = new GameObject("RuneSkinNamePlaque", typeof(RectTransform)).AddComponent<Image>();
                _namePlaque.rectTransform.SetParent(title, false);
                _namePlaque.rectTransform.SetAsFirstSibling();
                _namePlaque.raycastTarget = false;
                var mark = DollCell.Mark(_namePlaque, "decor.plaque", "character name plaque (header_plaque)");
                mark.Outcome = Restyler.Apply(_namePlaque, mark, ThemeTokens.Role("decor.plaque")) ? Outcome.Styled : Outcome.Degraded;
                _namePlaque.enabled = mark.Outcome == Outcome.Styled;
            }
            var width = Mathf.Min(box.W, ThemeTokens.GetFloat("size.inv.name.plaque", 560f) * k);
            var plaque = new Box(box.X + (box.W - width) / 2f, box.Y, width, box.H);
            Geo.Place(_canvas, _namePlaque.rectTransform, plaque);
            for (var n = 0; n < title.childCount; n++)
            {
                var child = title.GetChild(n);
                // The vanilla braid halves would run through the plaque: hidden while it is shown.
                if (NameCache.Of(child).StartsWith("BraidLine", StringComparison.Ordinal) && child.TryGetComponent<Image>(out var braid) && braid.enabled == _namePlaque.enabled)
                    braid.enabled = !_namePlaque.enabled;
                if (!(child is RectTransform rt) || !child.TryGetComponent<TMP_Text>(out var label)) continue;
                // Text between the knot ends (share of the plaque width each side, sprite border 230 of 620 px).
                var side = width * ThemeTokens.GetFloat("share.inv.name.knot", 0.2f);
                Geo.Place(_canvas, rt, new Box(plaque.X + side, plaque.Y, Mathf.Max(1f, width - 2f * side), plaque.H));
                if (label.alignment != TextAlignmentOptions.Center) label.alignment = TextAlignmentOptions.Center;
                if (label.textWrappingMode != TextWrappingModes.NoWrap) label.textWrappingMode = TextWrappingModes.NoWrap;
                if (label.overflowMode != TextOverflowModes.Ellipsis) label.overflowMode = TextOverflowModes.Ellipsis;
                if ((label.fontStyle & FontStyles.UpperCase) != FontStyles.UpperCase) label.fontStyle |= FontStyles.UpperCase;
                var max = rt.rect.height * 0.62f;
                var min = Mathf.Min(max, ThemeTokens.GetFloat("floor.text.body.px", 16f) / Mathf.Max(0.01f, _rootCanvas.scaleFactor));
                if (!label.enableAutoSizing || Mathf.Abs(label.fontSizeMax - max) > 0.1f || Mathf.Abs(label.fontSizeMin - min) > 0.1f)
                {
                    label.fontSizeMax = max;
                    label.fontSizeMin = min;
                    label.enableAutoSizing = true;
                }
            }
        }

        private void ArrangeContainer(RectTransform panel, InventoryGrid grid, Box box, GridFit fit, PanelKind kind, Inventory inventory,
            RectTransform name, Transform weight, Button takeAll, Button stackAll)
        {
            if (!panel || box.IsEmpty) return;
            var k = _input.SkinScale;
            var pad = _tokens.PanelPad * k;
            var headerH = _tokens.PanelHeaderH * k;
            Geo.Place(_canvas, panel, box);
            var header = new Box(box.X + pad, box.Y + pad / 2f, box.W - 2f * pad, headerH - pad / 2f);
            // Panel header = a drag band of the docked group (chest and backpack move with the inventory).
            var band = WindowMove.Handle(panel, WindowMove.Inventory);
            if (band) Geo.Place(_canvas, band.rectTransform, header);
            // Grid top-aligned under the title (view = visible rows), glyph actions in their own row at the bottom,
            // right-aligned, clear of the bottom-right corner cap (InventoryMath.ActionRow); the title keeps the header width.
            if (grid) Geo.Place(_canvas, (RectTransform)grid.transform, InventoryMath.ContainerGridView(box, fit, _tokens, k));
            if (grid) Want(grid, fit.Pitch, fit.Cell);
            Bar(panel, kind, inventory, takeAll, stackAll, InventoryMath.ActionRow(box, _tokens, k));
            var weightPlate = DirectChild(panel, weight);
            var right = header.Right;
            if (weightPlate)
            {
                // The footer's stat plate layout (glyph in the round socket, value beside it): the vanilla box layout
                // put the weight glyph above the plate_stat strip, half outside it (batch G cell rule).
                var w = ThemeTokens.GetFloat("size.inv.plate.weight.dock", 110f) * k;
                StatPlate(weightPlate, new Box(right - w - _tokens.Gap, header.Y, w, header.H));
                right -= w + _tokens.Gap;
            }
            var glyph = HeaderGlyph(panel, kind == PanelKind.Backpack ? "glyph.panel.backpack" : "glyph.panel.chest");
            var nameX = header.X;
            if (glyph && glyph.enabled)
            {
                Geo.Place(_canvas, glyph.rectTransform, new Box(header.X, header.Y, header.H, header.H));
                nameX += header.H + _tokens.Gap / 2f;
            }
            if (name)
            {
                Geo.Place(_canvas, name, new Box(nameX, header.Y, Mathf.Max(1f, right - nameX - _tokens.Gap), header.H));
                if (name.TryGetComponent<TMP_Text>(out var label))
                {
                    if (label.alignment != TextAlignmentOptions.MidlineLeft) label.alignment = TextAlignmentOptions.MidlineLeft;
                    if (label.overflowMode != TextOverflowModes.Ellipsis) label.overflowMode = TextOverflowModes.Ellipsis;
                    // Long names (Russian chests): up to two lines, auto-size down to the body floor, then ellipsis.
                    if (label.textWrappingMode != TextWrappingModes.Normal) label.textWrappingMode = TextWrappingModes.Normal;
                    if (!label.enableAutoSizing)
                    {
                        label.fontSizeMax = label.fontSize;
                        label.fontSizeMin = Mathf.Min(label.fontSize, ThemeTokens.GetFloat("floor.text.body.px", 16f) / Mathf.Max(0.01f, _rootCanvas.scaleFactor));
                        label.enableAutoSizing = true;
                    }
                }
            }
        }

        // Panel header glyph (chest / backpack), one per panel, role swapped when the panel changes kind.
        private static Image HeaderGlyph(RectTransform panel, string role)
        {
            var t = panel.Find("RuneSkinHeaderGlyph");
            Image image;
            SkinMark mark;
            if (!t)
            {
                image = new GameObject("RuneSkinHeaderGlyph", typeof(RectTransform)).AddComponent<Image>();
                image.rectTransform.SetParent(panel, false);
                image.raycastTarget = false;
                image.preserveAspect = true;
                mark = DollCell.Mark(image, role, "panel header glyph");
                mark.ThemeRevision = -1;
            }
            else
            {
                image = t.GetComponent<Image>();
                mark = t.GetComponent<SkinMark>();
            }
            if (mark && (mark.Role != role || mark.ThemeRevision != ThemeTokens.Revision))
            {
                mark.Role = role;
                mark.Outcome = Restyler.Apply(image, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
                image.enabled = mark.Outcome == Outcome.Styled;
            }
            return image;
        }

        private void Show(RectTransform host, Box box)
        {
            if (!host) return;
            var on = !box.IsEmpty;
            if (host.gameObject.activeSelf != on) host.gameObject.SetActive(on);
            if (on) Geo.Place(_canvas, host, box);
        }

        private static Box Inset(Box b, float x, float y) => new Box(b.X + x, b.Y + y, Mathf.Max(0f, b.W - 2f * x), Mathf.Max(0f, b.H - 2f * y));

        private static RectTransform DirectChild(Transform panel, Transform t)
        {
            for (var p = t; p; p = p.parent)
                if (p.parent == panel) return p as RectTransform;
            return null;
        }

        // Same relative path in the fork's clone panel as in m_container.
        private Transform Twin(Transform original)
        {
            if (!original || !_packPanel || !_gui.m_container) return null;
            // The relative path of a vanilla container part never changes: built once per part, not per frame.
            if (!_twinPaths.TryGetValue(original, out var path)) _twinPaths[original] = path = SkinPath(_gui.m_container, original);
            return path == null ? null : _packPanel.Find(path);
        }

        private readonly Dictionary<Transform, string> _twinPaths = new Dictionary<Transform, string>();

        private static string SkinPath(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var p = t; p && p != root; p = p.parent) parts.Insert(0, p.name);
            return t && t.IsChildOf(root) ? string.Join("/", parts) : null;
        }

        // ---------------------------------------------------------------- grids

        private void Want(InventoryGrid grid, float pitch, float cell)
        {
            if (!grid) return;
            if (!_grids.TryGetValue(grid, out var s)) _grids[grid] = s = new GridState();
            s.WantPitch = pitch;
            s.WantCell = cell;
            var template = grid.m_elementPrefab ? grid.m_elementPrefab.transform as RectTransform : null;
            var native = template ? template.rect.width : cell;
            s.WantScale = native > 1f ? cell / native : 1f;
        }

        // InventoryGrid.UpdateGui postfix (after EAQS and V+).
        public void AfterGrid(InventoryGrid grid)
        {
            if (!grid || !InventoryGui.IsVisible() || _layout == null) return;
            var isPlayer = grid == _gui.m_playerGrid;
            if (isPlayer) Want(grid, _layout.Player.Pitch, _layout.Player.Cell);
            if (!_grids.TryGetValue(grid, out var s) || s.WantPitch <= 0f) return;

            var rt = (RectTransform)grid.transform;
            var width = rt.rect.width;
            var visibleCount = isPlayer ? grid.GetInventory().GetWidth() * EaqsBridge.VisibleRows(grid.GetInventory().GetHeight()) : grid.m_elements.Count;
            var first = grid.m_elements.Count > 0 && grid.m_elements[0] ? grid.m_elements[0].GetInstanceID() : 0;
            var rebuilt = s.Elements != grid.m_elements.Count || s.First != first;
            s.First = first;
            if (rebuilt || !Mathf.Approximately(s.Pitch, s.WantPitch) || !Mathf.Approximately(s.Width, width) || !Mathf.Approximately(s.Scale, s.WantScale))
            {
                s.Pitch = s.WantPitch;
                s.Width = width;
                s.Scale = s.WantScale;
                s.Elements = grid.m_elements.Count;
                grid.m_elementSpace = s.Pitch;
                s.Visible = visibleCount;
                s.NeedsReposition = true; // LateUpdate, after the panel animation
                if (rebuilt)
                {
                    var owner = ScreenRegistry.RootOf(grid.transform);
                    if (owner) ScreenRegistry.Restyle(owner);
                }
            }
            for (var i = 0; i < grid.m_elements.Count; i++) CellMarks(grid.m_elements[i]);
            var inv = grid.GetInventory();
            if (rebuilt || s.OverlayRevision != InventoryRegistry.OverlayRevision || !ReferenceEquals(s.Inventory, inv) || s.ThemeRevision != ThemeTokens.Revision)
            {
                s.OverlayRevision = InventoryRegistry.OverlayRevision;
                s.Inventory = inv;
                s.ThemeRevision = ThemeTokens.Revision;
                Overlays(grid, inv, grid.m_elements.Count); // EAQS cells hold real inventory coordinates too
                if (isPlayer) HotbarPlates(grid);
            }
        }

        // The layout vanilla gives the cells (InventoryGrid.cs:267-275: row-major from the top, pitch m_elementSpace,
        // centred horizontally), measured from the live grid root so it holds for any anchors and a scrolled
        // container (the root is the ScrollRect content). Applied once per pitch / width / rebuild.
        // Markers never cover the icon (user call 2026-10-22, screenshot: a gold 4-point star hid an equipped sword). Epic
        // Loot re-dresses each cell on every UpdateGui (ItemBackgroundHelper): ApplyEquippedSprite puts its equipped sprite
        // at texture size over the cell centre (only skipped with Auga), "magicItem" = rarity glow behind the icon, "setItem"
        // = the set sprite 64 x 64 as the LAST sibling (over the icon). After it (this postfix runs last): equipped = our
        // small marker in the bottom-left corner, rarity = a rim in Epic Loot's colour on the cell edge, set = a small
        // corner gem top-right.
        private static void CellMarks(InventoryElement e)
        {
            if (!e) return;
            var cell = (RectTransform)e.transform;
            var side = Mathf.Min(cell.rect.width, cell.rect.height);
            if (!(side > 1f)) return;
            GridCellLook.Apply(e);
            var mark = side * ThemeTokens.GetFloat("share.inv.cell.mark", 0.24f);
            if (e.m_equiped && e.m_equiped.TryGetComponent<Image>(out var eq))
                Corner(eq, "sprite.marker.equipped", "color.marker.equipped", new Vector2(0f, 0f), mark);
            if (e.transform.Find("setItem") is RectTransform set && set.TryGetComponent<Image>(out var si))
                Corner(si, "sprite.gem.rarity", null, new Vector2(1f, 1f), mark);
            if (e.transform.Find("magicItem") is RectTransform magic && magic.TryGetComponent<Image>(out var mi))
            {
                var rim = ThemeTokens.GetSprite("sprite.hud.strip.rim");
                if (rim && mi.sprite != rim) { mi.sprite = rim; mi.type = Image.Type.Sliced; mi.preserveAspect = false; }
                if (magic.anchorMin != Vector2.zero || magic.anchorMax != Vector2.one)
                {
                    magic.anchorMin = Vector2.zero;
                    magic.anchorMax = Vector2.one;
                    magic.pivot = new Vector2(0.5f, 0.5f);
                    magic.offsetMin = magic.offsetMax = Vector2.zero;
                }
            }
        }

        private static void Corner(Image img, string token, string colorToken, Vector2 corner, float size)
        {
            var s = ThemeTokens.GetSprite(token);
            if (s && img.sprite != s) img.sprite = s;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            if (colorToken != null) img.color = ThemeTokens.GetColor(colorToken, img.color);
            var rt = img.rectTransform;
            var inset = new Vector2(corner.x > 0.5f ? -2f : 2f, corner.y > 0.5f ? -2f : 2f);
            if (rt.anchorMin != corner || rt.anchorMax != corner) { rt.anchorMin = rt.anchorMax = rt.pivot = corner; }
            if (rt.anchoredPosition != inset) rt.anchoredPosition = inset;
            var sz = new Vector2(size, size);
            if (rt.sizeDelta != sz) rt.sizeDelta = sz;
        }

        // Footer plates start this far under the footer box top (canvas units).
        private const float FooterInsetY = 4f;

        // RuneSkin owns the HUD strip: the inventory's hotbar row is the strip's hotbar.
        private static bool StripHotbar => PlayerHud.HudCluster.Instance;

        // Cell block centred in the grid view on both axes (InventoryMath.BlockOffset: cells only, no trailing gap; a
        // scrolling block starts at the root top). gapShare = gap / pitch.
        private void Reposition(InventoryGrid grid, int visibleCount, float scale, float gapShare)
        {
            if (!grid.m_gridRoot) return;
            var w = grid.GetInventory().GetWidth();
            var root = Geo.BoxOf(_canvas, grid.m_gridRoot);
            var view = Geo.BoxOf(_canvas, (RectTransform)grid.transform);
            var unit = Geo.WorldPerUnit(_canvas);
            var pitch = grid.m_elementSpace * grid.m_gridRoot.lossyScale.x / Mathf.Max(unit, 1e-6f); // canvas units
            var gap = pitch * gapShare;
            // Player grid under the HUD strip: row 0 lives in the strip (its hotbar), the grid starts at row 1.
            var skipRow = StripHotbar && grid == _gui.m_playerGrid ? 1 : 0;
            var count = Mathf.Min(grid.m_elements.Count, visibleCount);
            var rows = w > 0 ? Mathf.Max(0, (count + w - 1) / w - skipRow) : 0;
            var x0 = view.X + InventoryMath.BlockOffset(view.W, w, pitch, gap);
            // fits: centred in the view (whatever the root's anchors); taller: from the (scrolled) root top
            var fits = rows * pitch - gap <= view.H + 0.01f;
            var y0 = fits ? view.Y + InventoryMath.BlockOffset(view.H, rows, pitch, gap) : root.Y;
            for (var i = 0; i < grid.m_elements.Count && i < visibleCount; i++)
            {
                var e = grid.m_elements[i];
                if (!e) continue;
                var pos = e.Position;
                if (pos.y < skipRow)
                {
                    Geo.SetScale(e.transform, 0f);
                    continue;
                }
                Geo.SetScale(e.transform, scale);
                Geo.SetTopLeft((RectTransform)e.transform, Geo.World(_canvas, x0 + pos.x * pitch, y0 + (pos.y - skipRow) * pitch));
            }
        }

        private static void Overlays(InventoryGrid grid, Inventory inv, int visibleCount)
        {
            for (var i = 0; i < grid.m_elements.Count && i < visibleCount; i++)
            {
                var e = grid.m_elements[i];
                if (!e) continue;
                var token = InventoryRegistry.OverlayAt(inv, e.Position.x, e.Position.y);
                var existing = e.transform.Find("RuneSkinOverlay");
                if (token == null)
                {
                    if (existing) existing.gameObject.SetActive(false);
                    continue;
                }
                Image image;
                if (!existing)
                {
                    image = new GameObject("RuneSkinOverlay", typeof(RectTransform)).AddComponent<Image>();
                    var rt = image.rectTransform;
                    rt.SetParent(e.transform, false);
                    rt.anchorMin = new Vector2(1f, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(-2f, -2f);
                    rt.sizeDelta = new Vector2(20f, 20f);
                    image.raycastTarget = false;
                    image.preserveAspect = true;
                    DollCell.Mark(image, "content", "slot overlay (Skin.SetSlotOverlay)");
                }
                else image = existing.GetComponent<Image>();
                var sprite = ThemeTokens.GetSprite(token);
                image.sprite = sprite;
                image.enabled = sprite;
                image.gameObject.SetActive(true);
            }
        }

        // Hotbar row (grid row 1, HotkeyBar.cs:90-142): carved number plate behind the vanilla binding text.
        private static void HotbarPlates(InventoryGrid grid)
        {
            foreach (var e in grid.m_elements)
            {
                if (!e || e.Position.y != 0) continue;
                var binding = e.transform.Find("binding") as RectTransform;
                if (!binding) continue;
                // Digit on the header strip above its cell (layout-a2.png), not inside the cell.
                binding.anchorMin = binding.anchorMax = new Vector2(0.5f, 1f);
                binding.pivot = new Vector2(0.5f, 0f);
                binding.anchoredPosition = new Vector2(0f, 6f);
                if (binding.TryGetComponent<TMP_Text>(out var digit)) digit.alignment = TextAlignmentOptions.Center;
                // Digits sit directly on the header strip (art: number plate optional).
            }
        }

        // ---------------------------------------------------------------- doll

        private void RebuildEntries()
        {
            _entries.Clear();
            foreach (var s in _eaqs)
            {
                if (s.Kind == EaqsBridge.Kind.Quick) continue;
                EaqsBridge.Describe(s.Id, out var glyph, out var order, out var tooltipKey);
                _entries.Add(new DollEntry { Key = "eaqs:" + s.Id, Glyph = glyph, Name = SlotNames.Get(tooltipKey), Order = order * 1000 + s.Index, Element = s.Element });
            }
            var seen = new HashSet<string>();
            foreach (var r in InventoryRegistry.Slots)
            {
                if (r.Dead) continue;
                seen.Add(r.Key);
                // A replaced registration (same key, new instance) gets a new cell: callbacks are bound per instance.
                if (_registered.TryGetValue(r.Key, out var old) && old && (!_registeredBy.TryGetValue(r.Key, out var was) || !ReferenceEquals(was, r)))
                {
                    Destroy(old.gameObject);
                    _registered.Remove(r.Key);
                }
                if (!_registered.TryGetValue(r.Key, out var cell) || !cell)
                {
                    if (r.D.CellRect) Plugin.Log.LogWarning($"[RuneSkin] doll slot '{r.Key}': SlotDescriptor.CellRect is not supported, RuneSkin builds the cell");
                    _registeredBy[r.Key] = r;
                    var reg = r;
                    var glyphRole = !string.IsNullOrEmpty(r.D.GlyphToken) && ThemeTokens.HasRole(r.D.GlyphToken) ? r.D.GlyphToken : "glyph.slot.generic";
                    cell = DollCell.Create(_doll, r.Key, r.D.Source == SlotSource.Mirror ? "slot.mirror" : "slot.cell", glyphRole, _tooltipPrefab);
                    if (r.D.Glyph) { cell.Glyph.sprite = r.D.Glyph; cell.Glyph.enabled = true; }
                    cell.OnPrimary = c => RegisteredPrimary(reg, c);
                    cell.OnDropped = c => RegisteredPrimary(reg, c);
                    cell.OnSecondary = c => RegisteredUnequip(reg);
                    cell.SetTooltip(SlotNames.Get(r.D.TooltipKey ?? r.D.Id), "");
                    _registered[r.Key] = cell;
                }
                // Groups after the vanilla/EAQS 3x3, in registration group + order (stable ids).
                _entries.Add(new DollEntry { Key = r.Key, Glyph = null, Order = 100000 + GroupRank(r.D.Group) * 1000 + r.D.Order, Element = -1, Cell = cell });
            }
            foreach (var key in new List<string>(_registered.Keys))
                if (!seen.Contains(key))
                {
                    if (_registered[key]) Destroy(_registered[key].gameObject);
                    _registered.Remove(key);
                }
            _entries.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Key, b.Key));
            _entriesRevision = InventoryRegistry.Revision;
        }

        private static int GroupRank(string group) =>
            group == "armor" ? 0 : group == "jewelry" ? 1 : group == "utility" ? 2 : group == "light" ? 3 : 9;

        // After EAQS placed its cells this frame: storage cells on the doll grid, quick cells over the HUD quick bar.
        private void PlaceEaqsCells(InventoryGrid grid, float gridScale)
        {
            RefreshEaqs();
            if (_entriesRevision != InventoryRegistry.Revision || _entries.Count != CountDollSources()) RebuildEntries();
            var d = _layout.DollCells;
            var box = Inset(_layout.Doll, _tokens.DollPad * _input.SkinScale, _tokens.DollPad * _input.SkinScale);
            var cols = d.StorageColumns;
            // Storage grid left, portrait figure right of it, centred as one group.
            var k = _input.SkinScale;
            var strip = InventoryMath.Strip(box, d, _tokens.DollGap * k, _tokens.DollFigureGap * k, _tokens.DollFigureAspect);
            var x0 = strip.StorageX;
            var visible = Mathf.Max(1, d.VisibleRows);
            _dollScroll = Mathf.Clamp(_dollScroll, 0, Mathf.Max(0, d.Rows - visible));
            var native = grid.m_elementPrefab ? ((RectTransform)grid.m_elementPrefab.transform).rect.width : d.Cell;
            var cellScale = native > 1f ? d.Cell / native : 1f;
            // Gamepad selection on a hidden EAQS row (EAQS spatial navigation) scrolls it into view.
            if (d.Scroll && ZInput.IsGamepadActive())
                for (var i = 0; i < _entries.Count; i++)
                {
                    var en = _entries[i];
                    if (en.Element < 0 || en.Element >= grid.m_elements.Count || !grid.m_elements[en.Element] || grid.m_elements[en.Element].Position != grid.m_selected) continue;
                    var r0 = i / cols;
                    if (r0 < _dollScroll) _dollScroll = r0;
                    else if (r0 >= _dollScroll + visible) _dollScroll = r0 - visible + 1;
                }
            for (var i = 0; i < _entries.Count; i++)
            {
                var row = i / cols - _dollScroll;
                var col = i % cols;
                var shown = row >= 0 && row < visible;
                var cx = x0 + col * d.Pitch;
                var cy = box.Y + row * d.Pitch;
                var entry = _entries[i];
                if (entry.Element >= 0 && entry.Element < grid.m_elements.Count)
                {
                    var e = grid.m_elements[entry.Element];
                    if (!e) continue;
                    var rt = (RectTransform)e.transform;
                    Geo.SetScale(rt, shown ? cellScale : 0f);
                    if (shown) Geo.SetTopLeft(rt, Geo.World(_canvas, cx, cy));
                    HideLabel(e);
                    Glyph(e, entry.Glyph);
                    // Empty cell: its name in the tooltip (vanilla UpdateGui clears unused tooltips, InventoryGrid.cs:401-402).
                    if (!e.m_used && e.m_tooltip && e.m_tooltip.m_topic != entry.Name) e.m_tooltip.m_topic = entry.Name;
                }
                else if (entry.Cell)
                {
                    PlaceOwnCell(entry.Cell, cx, cy, d.Cell, shown);
                }
            }
            var figure = strip.Figure;

            if (_backdrop) Show(_backdrop, figure.W >= d.Cell ? figure : default);
            var fits = figure.W >= d.Cell;
            var live = _preview && !_preview.Failed;
            if (_preview) _preview.Place(_canvas, fits && live ? PreviewMath.Place(figure, _previewX, _previewY, _previewW, _previewH, _previewInset * k) : default);
            if (_silhouette) Show(_silhouette, fits && !live ? Inset(figure, 8f, 8f) : default);

            PlaceQuickCells(grid);
        }

        private int CountDollSources()
        {
            var n = 0;
            foreach (var s in _eaqs) if (s.Kind != EaqsBridge.Kind.Quick) n++;
            foreach (var s in InventoryRegistry.Slots) if (!s.Dead) n++;
            return n;
        }

        private void PlaceOwnCell(DollCell cell, float x, float y, float size, bool shown)
        {
            if (!cell) return;
            var rt = (RectTransform)cell.transform;
            if (cell.gameObject.activeSelf != shown) cell.gameObject.SetActive(shown);
            if (shown) Geo.Place(_canvas, rt, new Box(x, y, size, size));
        }

        private static void HideLabel(InventoryElement e)
        {
            var binding = e.transform.Find("binding");
            if (binding && binding.TryGetComponent<TMP_Text>(out var text) && text.enabled) text.enabled = false;
        }

        // Faint carved glyph of the slot type in an empty EAQS cell (no text label; name in the tooltip).
        private static void Glyph(InventoryElement e, string type)
        {
            var t = e.transform.Find("RuneSkinGlyph");
            Image image;
            if (!t)
            {
                image = new GameObject("RuneSkinGlyph", typeof(RectTransform)).AddComponent<Image>();
                var rt = image.rectTransform;
                var icon = e.m_icon.rectTransform;
                rt.SetParent(icon.parent, false);
                rt.SetSiblingIndex(icon.GetSiblingIndex());
                // Engraving, not an icon: smaller than an item picture (Codex M2 review).
                var gs = ThemeTokens.GetFloat("share.inv.glyph.scale", 0.82f);
                rt.localScale = new Vector3(gs, gs, 1f);
                image.raycastTarget = false;
                image.preserveAspect = true;
                var role = "glyph.slot." + (type ?? "generic");
                var mark = DollCell.Mark(image, role, "empty doll cell glyph");
                mark.Outcome = Restyler.Apply(image, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
                t = rt;
            }
            else image = t.GetComponent<Image>();
            // the icon's rect, centre pivot (GridCellLook anchors the icon top-left; it re-centres the glyph on relayout)
            GridCellLook.CentreOn((RectTransform)t, e.m_icon.rectTransform);
            var on = !e.m_used && image.sprite;
            if (image.enabled != on) image.enabled = on;
        }

        // EAQS quick cells sit exactly over the HUD quick-slot bar (HotkeyBar element i), so a drag in the inventory
        // targets the place the slot has on the HUD; the HUD bar itself is hidden while the inventory is open.
        private void PlaceQuickCells(InventoryGrid grid)
        {
            // The strip's own visible potion cells first, independent of the vanilla quick bar (run 20261023-194139:
            // the bar lookup returned before the strip path, the cells stayed where EAQS put them)
            var cluster0 = PlayerHud.HudCluster.Instance;
            if (cluster0 && cluster0.PotionCellRect(0))
            {
                var k = 0;
                foreach (var s in _eaqs)
                {
                    if (s.Kind != EaqsBridge.Kind.Quick || s.Element >= grid.m_elements.Count) continue;
                    var e = grid.m_elements[s.Element];
                    var cell = cluster0.PotionCellRect(k++);
                    if (e && cell) PlaceOnCell(e, cell);
                }
                return;
            }
            var bar = QuickBar();
            if (!bar || !bar.TryGetComponent<HotkeyBar>(out var hb)) return;
            var prefab = hb.m_elementPrefab ? hb.m_elementPrefab.transform as RectTransform : null;
            var q = 0;
            foreach (var s in _eaqs)
            {
                if (s.Kind != EaqsBridge.Kind.Quick || s.Element >= grid.m_elements.Count) continue;
                var e = grid.m_elements[s.Element];
                var i = q++;
                if (!e) continue;
                // HUD slot i: the live element when the bar built one, else where HotkeyBar.UpdateIcons would put it
                // (localPosition (i * m_elementSpace, 0), HotkeyBar.cs:110) at the prefab's size.
                Box b;
                var band = PlayerHud.HudCluster.Instance;
                // The strip's own visible potion cell (user 2026-10-23: a potion put into a cell slid off it - the cell
                // followed the hidden vanilla QuickSlotsHotkeyBar element, not the strip cell; SetBand has no caller).
                var stripCell = band ? band.PotionCellRect(i) : null;
                if (stripCell)
                {
                    PlaceOnCell(e, stripCell);
                    continue;
                }
                if (band && band.BandQuickCell(i, out var space, out var bl, out var bandSize))
                {
                    // Inventory band profile: the band's own quick sockets (size.hud.band.cell, mockup ratios).
                    b = Geo.ForeignRect(_canvas, space, bl, bandSize, Vector2.zero);
                    PlaceQuickCell(e, b);
                    continue;
                }
                var live = i < hb.m_elements.Count && hb.m_elements[i].m_go ? hb.m_elements[i].m_go.transform as RectTransform : null;
                if (live) b = Geo.ForeignBox(_canvas, live);
                else if (prefab)
                {
                    // No element built (empty bar): the HUD block anchor (bottom-left of slot 0, HudCluster) when known.
                    var cluster = PlayerHud.HudCluster.Instance;
                    var origin = bar.TransformPoint(new Vector3(i * hb.m_elementSpace, 0f, 0f));
                    var size = prefab.rect.size;
                    var pivot = prefab.pivot;
                    if (cluster && cluster.HasQuickAnchor)
                    {
                        // anchor = bottom-left corner of slot 0 -> pivot of slot i
                        var offset = bar.TransformVector(new Vector3(i * hb.m_elementSpace + pivot.x * size.x, pivot.y * size.y, 0f));
                        origin = cluster.QuickAnchor + offset;
                    }
                    b = Geo.ForeignRect(_canvas, bar, origin, size, pivot);
                }
                else continue;
                PlaceQuickCell(e, b);
            }
        }

        // Element exactly over a cell of another canvas (the HUD strip): through screen px, each side with its own
        // canvas camera, so the GUI scale / canvas modes never shift it (run 20261023-194139: quick cells 356 px off).
        private readonly Vector3[] _cellCorners = new Vector3[4];
        private void PlaceOnCell(InventoryElement e, RectTransform cell)
        {
            var rt = (RectTransform)e.transform;
            if (!(rt.parent is RectTransform parent)) return;
            var cellCanvas = cell.GetComponentInParent<Canvas>();
            var myCanvas = parent.GetComponentInParent<Canvas>();
            if (!cellCanvas || !myCanvas) return;
            Camera Cam(Canvas c) => c.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.rootCanvas.worldCamera;
            cell.GetWorldCorners(_cellCorners);
            var s0 = RectTransformUtility.WorldToScreenPoint(Cam(cellCanvas), _cellCorners[0]);
            var s2 = RectTransformUtility.WorldToScreenPoint(Cam(cellCanvas), _cellCorners[2]);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, s0, Cam(myCanvas), out var bl)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, s2, Cam(myCanvas), out var tr)) return;
            var native = rt.rect;
            if (native.width < 1f) return;
            var scale = (tr.x - bl.x) / native.width;
            if (Mathf.Abs(rt.localScale.x - scale) > 1e-4f) rt.localScale = new Vector3(scale, scale, 1f);
            var p = bl - native.min * scale;
            var lp = rt.localPosition;
            if (Mathf.Abs(lp.x - p.x) > 0.01f || Mathf.Abs(lp.y - p.y) > 0.01f) rt.localPosition = new Vector3(p.x, p.y, lp.z);
        }

        private void PlaceQuickCell(InventoryElement e, Box b)
        {
            if (!e) return;
            var rt = (RectTransform)e.transform;
            var native = rt.rect.width;
            var parentUnits = rt.parent ? rt.parent.lossyScale.x / Mathf.Max(Geo.WorldPerUnit(_canvas), 1e-6f) : 1f;
            if (native > 1f && parentUnits > 0f) Geo.SetScale(rt, b.W / (native * parentUnits));
            Geo.SetTopLeft(rt, Geo.World(_canvas, b.X, b.Y));
        }
        private Transform QuickBar()
        {
            if (_quickBar) return _quickBar;
            var hud = Hud.instance;
            if (!hud || !hud.m_rootObject || Time.frameCount % 60 != 0) return null;
            _quickBar = hud.m_rootObject.transform.Find("QuickSlotsHotkeyBar");
            return _quickBar;
        }

        private void HideQuickBar()
        {
            var bar = QuickBar();
            if (!bar) return;
            if (!_quickGroup) _quickGroup = bar.GetComponent<CanvasGroup>() ?? bar.gameObject.AddComponent<CanvasGroup>();
            RuneSkin.Restyle.RenderGate.Fade(_quickGroup, 0f, QuickOwner);
        }

        private void RestoreQuickBar()
        {
            // Stays hidden under the large map (LargeMap hides every HotkeyBar; this per-frame restore showed the
            // EAQS quick-slot bar over the open map, run 20261022-071214).
            // With the HUD strip the quick bar is never drawn (the strip's potion cells show its items).
            var a = Minimap.IsOpen() || PlayerHud.HudCluster.Instance ? 0f : 1f;
            if (_quickGroup) RuneSkin.Restyle.RenderGate.Fade(_quickGroup, a, QuickOwner); // at 0 not drawn
        }

        private const string QuickOwner = "inventory.quickbar";

        private void DollScrollInput()
        {
            if (_layout == null || !_layout.DollCells.Scroll) return;
            if (Screens.TooltipPlacer.CapturesWheel) return; // the hovered cell's long tooltip scrolls instead
            var wheel = ZInput.GetMouseScrollWheel();
            if (Mathf.Approximately(wheel, 0f)) return;
            var mouse = ZInput.pointerPosition;
            var tl = Geo.World(_canvas, _layout.Doll.X, _layout.Doll.Y);
            var br = Geo.World(_canvas, _layout.Doll.Right, _layout.Doll.Bottom);
            if (mouse.x < tl.x || mouse.x > br.x || mouse.y > tl.y || mouse.y < br.y) return;
            _dollScroll += wheel < 0f ? 1 : -1;
        }

        // ---------------------------------------------------------------- API-registered doll cells

        private void UpdateRegisteredSlots()
        {
            if (!Player.m_localPlayer) return;
            foreach (var r in InventoryRegistry.Slots)
            {
                if (r.Dead || r.D.GetItem == null || !_registered.TryGetValue(r.Key, out var cell) || !cell) continue;
                cell.Show(InventoryRegistry.Guard(r, r.D.GetItem, null), 0);
            }
        }

        private void RegisteredPrimary(InventoryRegistry.Slot r, DollCell c)
        {
            var item = _gui.m_dragItem;
            if (item == null || !_gui.m_dragGo || r.D.SetEquipped == null) return;
            if (r.D.Accepts == null || !InventoryRegistry.Guard(r, () => r.D.Accepts(item), false)) return;
            if (InventoryRegistry.Guard(r, () => r.D.SetEquipped(item, true), false)) _gui.SetupDragItem(null, null, 1);
        }

        private void RegisteredUnequip(InventoryRegistry.Slot r)
        {
            if (r.D.GetItem == null || r.D.SetEquipped == null) return;
            var item = InventoryRegistry.Guard(r, () => r.D.GetItem(), null);
            if (item != null) InventoryRegistry.Guard(r, () => r.D.SetEquipped(item, false), false);
        }

        // ---------------------------------------------------------------- actions

        private float Bar(RectTransform panel, PanelKind kind, Inventory inventory, Button takeAll, Button stackAll, Box box)
        {
            if (!panel) return 0f;
            if (!_bars.TryGetValue(panel, out var bar) || bar == null)
                _bars[panel] = bar = new ActionBar(panel, kind, _tooltipPrefab);
            bar.Sync(kind, inventory, takeAll, stackAll);
            var size = _tokens.FooterH * _input.SkinScale * 0.8f;
            var w = bar.Width(size, _tokens.Gap / 2f);
            if (!box.IsEmpty) bar.Place(_canvas, box, size, _tokens.Gap / 2f);
            return w;
        }

        private void UpdateActions()
        {
            var selected = UnityEngine.EventSystems.EventSystem.current ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            var typing = Chat.instance && Chat.instance.HasFocus() || global::Console.IsVisible() || TextInput.IsVisible() ||
                         _gui.m_splitDialog && _gui.m_splitDialog.IsActive || selected && (selected.GetComponent<TMP_InputField>() || selected.GetComponent<InputField>());
            // Hotkeys go to one panel: the one under the pointer, else the player panel (one consumer per press).
            ActionBar focused = null;
            Vector2 pointer = ZInput.pointerPosition;
            foreach (var pair in _bars)
                if (pair.Key && pair.Key.gameObject.activeInHierarchy && pair.Value != null && pair.Key != _gui.m_player && pair.Value.Contains(pointer))
                    focused = pair.Value;
            if (focused == null && _bars.TryGetValue(_gui.m_player, out var player)) focused = player;
            var consumed = false;
            foreach (var pair in _bars)
            {
                if (!pair.Key || !pair.Key.gameObject.activeInHierarchy || pair.Value == null) continue;
                pair.Value.Tick(!typing && pair.Value == focused, ref consumed);
            }
        }
    }
}
