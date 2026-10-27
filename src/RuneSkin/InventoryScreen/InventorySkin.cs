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
    // Family "Inventory" (windowed layout, mockup inv-layout-A, user 2026-10-26). Sole geometry owner of the InventoryGui
    // windows: the player grid window (m_player: grid locked onto the HUD strip's hotbar, side column over F), the doll
    // window (m_info name + tabs, EAQS equipment cells, live preview), chest / backpack (m_container, the fork's backpack
    // panel) and the crafting window's default PLACE (CraftingSkin lays it out). Each window has its own RuneWindowHeader
    // and user offset (WindowMove ids inventory.grid / .doll / .chest / .backpack, crafting); InventoryWindowsMath gives
    // the defaults. How it owns geometry without fighting vanilla:
    // - grid pitch: InventoryGrid.m_elementSpace, the field vanilla lays cells out with (InventoryGrid.cs:273) and
    //   sizes the grid root with every frame (:405); existing cells are moved once when pitch / width changes (the
    //   positions vanilla would give them), no destroy/rebuild (Codex M2 review #4);
    // - panel rects: written from the layout (Geo.Place, change-only); vanilla writes m_player.sizeDelta.y only in
    //   SetInventorySize (InventoryGui.cs:1723-1726), which the next frame's placement overrides;
    // - EAQS cells: EAQS re-places them every frame in its InventoryGrid.UpdateGui postfix (EquipmentPanel
    //   UpdateInventorySlots), so the doll places them right after it (AfterGrid, HarmonyAfter EAQS), no reparenting;
    // - rows / columns past a window's room scroll inside it (cells outside the shown range at scale 0, like the doll);
    // - layout math (InventoryWindowsMath) runs only when the input tuple changes.
    public sealed class InventorySkin : MonoBehaviour
    {
        public const string ScreenId = "Inventory";
        private const string RootName = "RuneSkinInventory";

        public static InventorySkin Instance { get; private set; }

        // Default crafting window box (canvas units, top-left origin; empty before the first layout). CraftingSkin adds the
        // window's own offset (WindowMove.Crafting).
        public Box CraftingBox => _layout != null && InventoryGui.IsVisible() ? _layout.Crafting : default;

        public float SkinScaleValue => _input.SkinScale;

        // Default doll window box (canvas units; empty before the first layout): the info panels' left bound.
        public Box CenterBox => _layout != null && InventoryGui.IsVisible() ? _layout.Doll : default;

        private InventoryGui _gui;
        private RectTransform _canvas;
        private Canvas _rootCanvas;
        private RectTransform _decor, _invHost, _dollHost, _chestHost, _packHost, _doll, _backdrop, _silhouette;
        private CharacterPreview _preview;
        private readonly InventoryTokens _tokens = new InventoryTokens();
        private readonly WindowsInput _input = new WindowsInput();
        // _layout = the default windows (InventoryWindowsMath.Compute); each window is shown at its default + its own user
        // offset (WindowMove), refreshed every placement pass (Shown).
        private WindowsLayout _layout;
        private int _layoutHash;
        // Shown window boxes and their offsets from the defaults (canvas units).
        private Box _invBox, _dollBox, _chestBox, _packBox;
        private Vector2 _invOff, _dollOff, _chestOff, _packOff;
        // Scroll offsets: player grid rows, chest / backpack columns (rows scroll in vanilla's ScrollRect), doll rows.
        private int _gridScroll;
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
            // Cell set (count, first cell's instance id) the last RestyleCells styled.
            public int StyledElements = -1, StyledFirst;
            public Inventory Inventory;
            public float WantPitch, WantScale, WantCell;
            public int Visible = int.MaxValue;
            public bool NeedsReposition;
            // Shown range of our own scrolling (rows of the player grid, columns of a container): first + count;
            // VisibleRows / VisibleColumns = int.MaxValue = all.
            public int RowScroll, ColScroll, VisibleRows = int.MaxValue, VisibleColumns = int.MaxValue;
            public int AppliedRowScroll = -1, AppliedColScroll = -1, AppliedRows = -1, AppliedColumns = -1;
            public Box AppliedView;
            // Grid root (the cells' parent) where the last Reposition saw it: the cells move with it.
            public Box AppliedRoot;
        }

        private struct DollEntry
        {
            public string Key, Glyph, Name;
            public int Order;
            public int Element; // EAQS element index, -1 = own cell
            public DollCell Cell;
            // Doll column (0 left, 1 right: InventoryWindowsMath.DollSide) and row in it.
            public int Side, Row;
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
            RegisterResetAction();
            foreach (var root in OwnedRoots())
            {
                var r = root;
                var id = r == _gui.m_player ? ScreenId : r == _gui.m_info ? ScreenId + ".Info" : ScreenId + ".Template";
                ScreenRegistry.Register(r, () => Context(r, id));
                ScreenRegistry.Restyle(r);
            }
            Plugin.Log.LogInfo($"[RuneSkin] inventory windows attached (EAQS {(EaqsBridge.Available ? "on" : "off")})");
        }

        private System.Action _resetAction;

        // Player window action: every inventory-screen window back on its default (the layout this class computes).
        private void RegisterResetAction()
        {
            _resetAction = InventoryRegistry.AddAction(Skin.PluginGuid, new PanelActionDescriptor
            {
                Id = "layout.reset",
                Panel = PanelKind.Player,
                GlyphToken = "glyph.action.layout",
                TooltipKey = "$runeskin_layout_reset",
                Order = 1000,
                OnInvoke = () => WindowMove.ResetMany(WindowMove.InventoryWindows)
            });
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _resetAction?.Invoke();
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
            t.Margin = F("size.inv.margin", t.Margin); t.Gap = F("size.inv.gap", t.Gap); t.StripGap = F("size.inv.strip.gap", t.StripGap);
            t.CraftW = F("size.inv.craft", t.CraftW); t.MinCraftW = F("size.inv.craft.min", t.MinCraftW);
            t.BackpackMinW = F("size.inv.backpack.min", t.BackpackMinW);
            t.InnerGap = F("size.inv.inner.gap", t.InnerGap); t.PanelActionsH = F("size.inv.panel.actions", t.PanelActionsH);
            t.HeaderH = F("size.inv.doll.header", t.HeaderH); t.FooterH = F("size.inv.footer", t.FooterH);
            t.PanelHeaderH = F("size.inv.panel.header", t.PanelHeaderH); t.PanelPad = F("size.inv.panel.pad", t.PanelPad);
            t.WinHeaderH = F("size.win.header", t.WinHeaderH);
            t.GridCell = F("size.slot", t.GridCell); t.GridGap = F("size.inv.cell.gap", t.GridGap);
            t.DollFigureGap = F("size.inv.doll.figure.gap", t.DollFigureGap); t.DollFigureAspect = F("share.inv.doll.figure", t.DollFigureAspect);
            t.PreviewMaxH = F("size.inv.preview.max", t.PreviewMaxH);
            t.ScrollbarW = F("size.inv.scrollbar", t.ScrollbarW); t.MinCellPhysPx = F("floor.cell.px", t.MinCellPhysPx);
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
            _dollHost = Host("Doll");
            _invHost = Host("Inventory");
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
                else if (t.name == "bkg" && t.parent && (t.parent.parent == _gui.m_player || t.parent.parent == _gui.m_info) && Array.IndexOf(StatPlates, t.parent.name) >= 0) ctx.Role(image, "plate.side", "side column plate (mockup inv-layout-A: trash, armour, weight, coins over F; the slot cell look without the cell fitter: CellFit re-fitted the values every few frames, run 20261026-195047)");
                else if (t.name == "bkg" && t.parent && Array.IndexOf(StatPlates, t.parent.name) >= 0) ctx.Role(image, "plate.stat", "stat plate strip (container weight)");
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
            ScrollInput();
        }

        private bool _frameReady;

        // Geometry after the Animator: InventoryGui's open/close animation writes the panels' anchored positions every
        // frame after Update (in-game run 3: m_player at (40,10) instead of (576,32) at end of frame), so every
        // placement - panels, cells, doll, quick cells - runs here, after the animation and after EAQS / vanilla
        // UpdateGui (Update phase). Change-only writes.
        private void LateUpdate()
        {
            Fade();
            if (_preview) _preview.Prewarm(); // closed: the doll's clone built ahead of the first open (CharacterPreview)
            if (_gui && _gui.m_animator) PrewarmGrid(); // closed: the player grid's cells built + styled ahead of the first open
            if (!_frameReady || _layout == null) return;
            _frameReady = false;
            if (!InventoryGui.IsVisible()) return;
            ApplyPanels();
            foreach (var pair in _grids)
            {
                var s = pair.Value;
                if (pair.Key && pair.Key != _gui.m_playerGrid && ScrollView(pair.Key)) s.NeedsReposition = true;
                if (pair.Key && RootMoved(pair.Key, s)) s.NeedsReposition = true;
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
        // transparent the panels are PARKED until InventoryGui.Show (Unpark prefix): not drawn and RuneSkin's per-frame
        // components under them idle.
        // Parking keeps the panels active, as vanilla does (InventoryGui.Awake activates m_player / m_crafting / m_info,
        // InventoryGui.cs:357-360; Hide only clears the animator's "visible", :1055-1061, and the close clip moves them
        // off screen). Deactivating them (the old park) made every open re-enable ~2000 graphics: OnEnable -> SetAllDirty
        // -> a full layout + mesh rebuild of every Image and TMP text in the open frame (rp-open-hitch run 20261027-152339:
        // Show_Prefix 2.24 ms + the unattributed canvas rebuild of frame #0). Now: a RenderGate (disabled Canvas: no
        // batching, no draw, geometry kept) and the RuneSkin behaviours with frame callbacks under the panels disabled
        // (CellFit, TextInset, PlateLabelFit, ...: exactly the OnDisable / OnEnable they got from the deactivation, so a
        // closed inventory still costs no per-frame RuneSkin work). Input stays blocked by the fade group.
        private CanvasGroup _fade;
        private float _alpha;
        private const string ParkOwner = "inventory.parked";
        private readonly List<GameObject> _parked = new List<GameObject>(4);
        private readonly List<Behaviour> _idled = new List<Behaviour>(256);
        private static readonly List<MonoBehaviour> ParkScratch = new List<MonoBehaviour>(1024);
        private static readonly Dictionary<Type, bool> FrameTypes = new Dictionary<Type, bool>();
        private static readonly int AnimVisible = Animator.StringToHash("visible");

        private void Fade()
        {
            if (!_gui || !_gui.m_animator) return;
            if (!_fade)
            {
                _fade = _gui.GetComponent<CanvasGroup>() ?? _gui.gameObject.AddComponent<CanvasGroup>();
                if (!_fade.interactable) _fade.interactable = true; // an older build's closed state
            }
            var want = _gui.m_animator.GetBool(AnimVisible);
            if (want && _parked.Count > 0) Unpark();
            var dur = Mathf.Max(0f, Plugin.PanelFade?.Value ?? 0.15f);
            var target = want ? 1f : 0f;
            _alpha = dur <= 0f ? target : Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime / dur);
            // Every CanvasGroup property write broadcasts OnCanvasGroupChanged to every component under InventoryGui,
            // inactive ones included (Unity 6000.0.75 probe: ~0.25 ms per 1000 Images per write), and Selectable's handler
            // walks its parents (Selectable.ParentGroupAllowsInteraction). Only the input block is toggled: interactable is
            // never cleared (vanilla Hide leaves its Selectables interactable, InventoryGui.cs:1055-1061; its Update takes no
            // input while hidden) - flipping it also re-ran every Selectable's state transition on each open and close.
            if (!Mathf.Approximately(_fade.alpha, _alpha)) _fade.alpha = _alpha;
            if (_fade.blocksRaycasts != want) _fade.blocksRaycasts = want;
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
                    RenderGate.Set(t.gameObject, ParkOwner, true);
                    _parked.Add(t.gameObject);
                    Idle(t);
                }
        }

        private void Idle(Transform root)
        {
            root.GetComponentsInChildren(false, ParkScratch); // active objects only: inactive ones run nothing anyway
            foreach (var b in ParkScratch)
                if (b && b.enabled && Idles(b))
                {
                    b.enabled = false;
                    _idled.Add(b);
                }
            ParkScratch.Clear();
        }

        // InventoryGui.Show prefix (and any frame the animator wants the screen): the parked panels come back before
        // vanilla touches them.
        public void Unpark()
        {
            foreach (var go in _parked)
                if (go) RenderGate.Set(go, ParkOwner, false);
            _parked.Clear();
            foreach (var b in _idled)
                if (b) b.enabled = true;
            _idled.Clear();
        }

        // A RuneSkin behaviour with a frame or state callback idles while parked (frame work stops; registries such as
        // PanelDecor.All / FrameOrnament.All see the same OnDisable / OnEnable the deactivation gave them). Plain data
        // components (SkinMark ...) are left alone; Graphics never (a disabled Graphic is a rebuild on enable); the doll's
        // preview keeps its own lifecycle (Prewarm / Close while closed).
        private static readonly string[] StateCallbacks = { "LateUpdate", "Update", "FixedUpdate", "OnEnable", "OnDisable", "OnRectTransformDimensionsChange" };

        private static bool Idles(MonoBehaviour b)
        {
            if (b is Graphic || b is CharacterPreview) return false;
            var type = b.GetType();
            if (FrameTypes.TryGetValue(type, out var idles)) return idles;
            idles = false;
            if (type.Assembly == typeof(InventorySkin).Assembly)
                for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
                    foreach (var callback in StateCallbacks)
                        if (t.GetMethod(callback, Any, null, Type.EmptyTypes, null) != null)
                        {
                            idles = true;
                            break;
                        }
                    if (idles) break;
                }
            FrameTypes[type] = idles;
            return idles;
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
                h = h * 31 + _packHeaderNeed.GetHashCode();
                DollSideCounts(out var dl, out var dr);
                h = h * 31 + dl * 97 + dr;
                h = h * 31 + SidePlateCount();
                h = h * 31 + InventoryRegistry.Revision;
                h = h * 31 + ThemeTokens.Revision;
                var cluster = PlayerHud.HudCluster.Instance;
                h = h * 31 + (cluster ? cluster.StripRevision + (cluster.ReservedScreenTop > 0f ? 1 : 0) : -1);
                return h;
            }
        }

        // The strip's hotbar row in this canvas (InventoryWindowsMath.FromStrip); invalid without the RuneSkin strip.
        private static HotbarRow StripRow(float scale)
        {
            var cluster = PlayerHud.HudCluster.Instance;
            if (!cluster || !cluster.StripRow(out var l, out var input, out var abilities, out var originX, out var ppu, out var top)) return default;
            return InventoryWindowsMath.FromStrip(l, input, abilities, originX, ppu, top, Screen.height, scale);
        }

        // The inventory m_container shows: vanilla UpdateContainer (InventoryGui.cs:703-704) activates m_container and feeds
        // m_containerGrid, and that pair is the container's state, not m_currentContainer: mods show their own inventory
        // in it with no Container (EpicLoot SocketsUI: UpdateContainer prefix fills the grid with the item's socket
        // inventory, m_currentContainer stays null). Reading m_currentContainer sized that window 0x0 (no chest box),
        // so the socket panel never appeared and its caption sat at its prefab spot over the hotbar (user 2026-10-27).
        private Inventory ContainerInventory() =>
            _gui.m_container && _gui.m_container.gameObject.activeSelf && _gui.m_containerGrid ? _gui.m_containerGrid.GetInventory() : null;

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
            i.Hotbar = StripHotbar ? StripRow(scale) : default;
            i.GridW = inv.GetWidth();
            // HUD strip: its hotbar IS row 0, so the grid shows the rows under it only (user call 2026-10-22).
            i.GridH = EaqsBridge.VisibleRows(inv.GetHeight()) - (StripHotbar && i.Hotbar.Valid ? 1 : 0);
            var chest = ContainerIsBackpack() ? null : ContainerInventory();
            var pack = ContainerIsBackpack() ? ContainerInventory() : _packInventory;
            i.ChestW = chest?.GetWidth() ?? 0;
            i.ChestH = chest?.GetHeight() ?? 0;
            i.BackpackW = pack?.GetWidth() ?? 0;
            i.BackpackH = pack?.GetHeight() ?? 0;
            i.BackpackHeaderW = pack != null ? _packHeaderNeed : 0f;
            DollSideCounts(out i.DollLeft, out i.DollRight);
            i.SidePlates = SidePlateCount();
            ReadTokens();
            _tokens.PanelCapInset = CapKeepOut(_chestHost);
            _tokens.CenterCapInset = CapKeepOut(_invHost);
            _layout = InventoryWindowsMath.Compute(i, _tokens);
            Shown();
            RequestBand(scale);
            _entriesRevision = -1; // rebuild doll order
            _logPlacement = 3;
            var o = _layout;
            Plugin.Log.LogInfo($"[RuneSkin] inventory windows: hotbar c0 {o.Hotbar.C0:0.##} pitch {o.Hotbar.Pitch:0.###} cell {o.Hotbar.Cell:0.##} F {o.Hotbar.Side:0.##} top {o.Hotbar.Top:0.#}{(i.Hotbar.Valid ? "" : " (no strip)")}; " +
                               $"grid {o.Inventory} {o.Player} doll {o.Doll} {o.DollGrid} preview {o.DollPreview} backpack {o.Backpack} {o.BackpackGrid} chest {o.Chest} {o.ChestGrid} " +
                               $"crafting {o.Crafting}{(o.CraftingTight ? " tight" : "")}{(o.DockOverflow ? " dock overflow" : "")}{(o.BackpackFolded ? " backpack folded" : "")}");
        }

        // Side column entries present (trash, armour, weight, coins).
        private int SidePlateCount()
        {
            var n = 0;
            foreach (var p in SidePlates())
                if (p && p.gameObject.activeSelf) n++;
            return n;
        }

        // HUD band flag (the HUD cluster keeps its strip; the quick cells follow its potion cells): spans the windows.
        private void RequestBand(float scale)
        {
            var cluster = PlayerHud.HudCluster.Instance;
            if (!cluster || _layout == null) return;
            float left = float.MaxValue, right = float.MinValue;
            foreach (var b in _layout.Windows)
            {
                if (b.IsEmpty) continue;
                left = Mathf.Min(left, b.X);
                right = Mathf.Max(right, b.Right);
            }
            if (left > right) return;
            var quick = 0;
            foreach (var s in _eaqs) if (s.Kind == EaqsBridge.Kind.Quick) quick++;
            cluster.SetBand(left * scale, right * scale, quick);
        }

        // Corner-cap keep-out of a host's panel stack in canvas units (PanelDecor.KeepOut: the window content inset).
        private float CapKeepOut(RectTransform host) => PanelDecor.KeepOut(host, _canvas);

        private static void ClearBand()
        {
            var cluster = PlayerHud.HudCluster.Instance;
            if (cluster) cluster.ClearBand();
        }

        // Every window at its default + its own user offset (WindowMove: drag on its header, double-click resets it),
        // clamped on screen above the HUD strip. Runs every placement pass (offsets change while dragging).
        private void Shown()
        {
            var o = _layout;
            if (o == null) return;
            _invOff = WindowMove.Applied(WindowMove.InventoryGrid, o.Inventory, _canvas);
            _dollOff = WindowMove.Applied(WindowMove.Doll, o.Doll, _canvas);
            _chestOff = o.Chest.IsEmpty ? Vector2.zero : WindowMove.Applied(WindowMove.Chest, o.Chest, _canvas);
            _packOff = o.Backpack.IsEmpty ? Vector2.zero : WindowMove.Applied(WindowMove.Backpack, o.Backpack, _canvas);
            _invBox = Move(o.Inventory, _invOff);
            _dollBox = Move(o.Doll, _dollOff);
            _chestBox = Move(o.Chest, _chestOff);
            _packBox = Move(o.Backpack, _packOff);
        }

        private static Box Move(Box b, Vector2 o) => WindowMoveMath.Shift(b, o.x, o.y);

        // Harness: default and shown window boxes (canvas units) by WindowMove id.
        public Box DefaultWindow(string id) => _layout == null ? default :
            id == WindowMove.InventoryGrid ? _layout.Inventory : id == WindowMove.Doll ? _layout.Doll :
            id == WindowMove.Chest ? _layout.Chest : id == WindowMove.Backpack ? _layout.Backpack : id == WindowMove.Crafting ? _layout.Crafting : default;

        public Box ShownWindow(string id) =>
            id == WindowMove.InventoryGrid ? _invBox : id == WindowMove.Doll ? _dollBox :
            id == WindowMove.Chest ? _chestBox : id == WindowMove.Backpack ? _packBox : default;

        // Harness: the computed layout (hotbar row, grids) of the open screen.
        public WindowsLayout Layout => _layout;

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
            sb.Append($" canvasMode={_rootCanvas.renderMode}");
            if (_gui.m_playerGrid)
                foreach (var e in _gui.m_playerGrid.m_elements)
                    if (e && e.Position.x == 0 && e.Position.y == (StripHotbar && _input.Hotbar.Valid ? 1 : 0))
                    {
                        // first shown cell vs the hotbar column 0 (same x centre, same side)
                        sb.Append($" cell0={Geo.BoxOf(_canvas, (RectTransform)e.transform)} want x {_layout.PlayerCellX(0) + _invOff.x:0.#} side {_layout.Player.Cell:0.#}");
                        break;
                    }
            One("player", _gui.m_player, _invBox);
            One("grid", _gui.m_playerGrid, Move(_layout.PlayerView, _invOff));
            if (_gui.m_info) One("doll", _gui.m_info, Move(_layout.DollHeader, _dollOff));
            if (_gui.m_container && _gui.m_container.gameObject.activeSelf)
            {
                One("container", _gui.m_container, ContainerIsBackpack() ? _packBox : _chestBox);
                if (_gui.m_containerGrid.m_elements.Count > 0 && _gui.m_containerGrid.m_elements[0]) sb.Append($" ccell0={Geo.BoxOf(_canvas, (RectTransform)_gui.m_containerGrid.m_elements[0].transform)}");
            }
            if (_packPanel) One("backpack", _packPanel, _packBox);
            Plugin.Log.LogInfo(sb.ToString());
        }

        private void ApplyPanels()
        {
            var o = _layout;
            Shown();
            _colViews.Clear();
            var containerShown = _gui.m_container && _gui.m_container.gameObject.activeSelf;
            var backpackInContainer = containerShown && ContainerIsBackpack();
            // Window frames (panel stacks).
            Show(_invHost, _invBox);
            Show(_dollHost, _dollBox);
            Show(_chestHost, containerShown && !backpackInContainer ? _chestBox : default);
            Show(_packHost, backpackInContainer || _packPanel ? _packBox : default);

            // Player grid window: m_player on its box, the grid on its cell block (columns over the hotbar cells), the
            // side column over F, the header with the player actions.
            Geo.Place(_canvas, _gui.m_player, _invBox);
            if (_gui.m_playerGrid)
            {
                var view = Move(o.PlayerView, _invOff);
                Geo.Place(_canvas, (RectTransform)_gui.m_playerGrid.transform, view);
                Range(_gui.m_playerGrid, o.Player, true, _gridScroll, 0, view);
            }
            SideColumn(o);
            GridHeader(Move(o.InventoryHeader, _invOff));

            // Doll window: name + info tab row (m_info); cells, alcove and preview after EAQS (PlaceEaqsCells).
            InfoHeader(new Box(_dollBox.X, _dollBox.Y, _dollBox.W, o.DollHeader.H));

            // Chest / backpack.
            if (containerShown)
            {
                ArrangeContainer(_gui.m_container, _gui.m_containerGrid, backpackInContainer ? _packBox : _chestBox, backpackInContainer ? o.BackpackGrid : o.ChestGrid,
                    backpackInContainer ? PanelKind.Backpack : PanelKind.Container, ContainerInventory(), _gui.m_containerName,
                    _gui.m_containerWeight ? _gui.m_containerWeight.transform : null, _gui.m_takeAllButton, _gui.m_stackAllButton);
            }
            if (_packPanel && _packGrid)
            {
                ArrangeContainer(_packPanel, _packGrid, _packBox, o.BackpackGrid, PanelKind.Backpack, _packInventory,
                    Twin(_gui.m_containerName ? _gui.m_containerName.transform : null)?.GetComponent<TMP_Text>(),
                    Twin(_gui.m_containerWeight ? _gui.m_containerWeight.transform : null),
                    Twin(_gui.m_takeAllButton ? _gui.m_takeAllButton.transform : null)?.GetComponent<Button>(),
                    Twin(_gui.m_stackAllButton ? _gui.m_stackAllButton.transform : null)?.GetComponent<Button>());
            }

            // Crafting: position only when the crafting family is not owned (CraftingSkin lays the window out on
            // CraftingBox + its own offset); scaled down, never up, to stay in its box.
            if (_gui.m_crafting && _gui.m_crafting.gameObject.activeSelf && !CraftingScreen.CraftingSkin.Instance)
                Geo.PlaceFitWidth(_canvas, _gui.m_crafting, WindowMove.Place(WindowMove.Crafting, o.Crafting, _canvas));
        }

        // Player grid window header: "Inventory" centred, the player panel's glyph actions (sort, reset layout, mods'
        // actions) in its aux slot; drag moves the grid window.
        private void GridHeader(Box band)
        {
            var head = RuneWindowHeader.Ensure(_gui.m_player, WindowMove.InventoryGrid);
            if (!head) return;
            head.Frame = PanelDecor.Of(_invHost);
            head.Text("$runeskin_win_inventory");
            var k = _input.SkinScale;
            var size = Mathf.Min(_tokens.FooterH * k * 0.8f, band.H);
            var inv = Player.m_localPlayer.GetInventory();
            Bar(_gui.m_player, PanelKind.Player, inv, null, null, default, size);
            // The actions sit in the header's stone plinth (integrated header variant A, user 2026-10-27: buttons pasted
            // over the plate's right knot looked stuck on): the plate's knot ends in the plinth's terminal, one plinth
            // cell per action, the variant-B button art in each.
            var bar = _bars[_gui.m_player];
            head.Actions(bar.Count);
            head.Place(_canvas, band, null, 0f);
            if (head.PlinthRow(_canvas, out var row, out var bs, out var gap))
            {
                bar.ButtonRole(ActionBar.HeaderRole); // no-op once set (Codex review: no per-frame role flip)
                Bar(_gui.m_player, PanelKind.Player, inv, null, null, row, bs, gap);
            }
            else
            {
                // No plinth art (theme without it): the old aux-slot row.
                bar.ButtonRole(ActionBar.DefaultRole);
                head.Actions(0);
                head.Place(_canvas, band, null, bar.Width(size, _tokens.Gap / 2f));
                Bar(_gui.m_player, PanelKind.Player, inv, null, null, head.AuxBox, size);
            }
        }

        // Doll window header band (m_info): the character name in the window header on top, the info tabs (texts, skills,
        // trophies, achievements, character stats, PVP: every Selectable child of m_info) as one centred row below it.
        private void InfoHeader(Box box)
        {
            var info = _gui.m_info;
            if (!info) return;
            Geo.Place(_canvas, info, box);
            var k = _input.SkinScale;
            var nameH = Mathf.Min(ThemeTokens.GetFloat("size.inv.name.h", 52f) * k, box.H * 0.42f);
            // Name row = the window's one header (RuneWindowHeader: same height / plate / centred rune title as every
            // window; drag moves the doll window, double-click resets it): the character name (TitlePanel's text,
            // mirrored), centred in the name row.
            var title = info.Find("TitlePanel") as RectTransform;
            TMP_Text nameText = null;
            if (title)
            {
                Geo.Place(_canvas, title, new Box(box.X, box.Y, box.W, nameH));
                for (var n = 0; n < title.childCount; n++)
                {
                    var child = title.GetChild(n);
                    // The vanilla braid halves under the name: the header plate replaces them.
                    if (NameCache.Of(child).StartsWith("BraidLine", StringComparison.Ordinal) && child.TryGetComponent<Image>(out var braid) && braid.enabled) braid.enabled = false;
                    if (!nameText && child.TryGetComponent<TMP_Text>(out var label)) nameText = label;
                }
            }
            var head = RuneWindowHeader.Ensure(info, WindowMove.Doll);
            if (head)
            {
                head.Frame = PanelDecor.Of(_dollHost); // the doll window's frame, never an overlapping window's
                var playerName = Player.m_localPlayer ? Player.m_localPlayer.GetPlayerName() : "$runeskin_win_character";
                if (nameText) head.Adopt(nameText, playerName);
                else head.Fallback(playerName).Text(playerName);
                var hh = Mathf.Min(WindowHeaderMath.HeaderHeight(ThemeTokens.GetFloat("size.win.header", 40f), k), nameH);
                head.Place(_canvas, new Box(box.X, box.Y, box.W, hh)); // top-aligned: one top / baseline with the other headers (0.24.0)
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

        // Side column entries top to bottom (mockup inv-layout-A over F): trash (MorgottTweaks MT_Trash), armour, weight,
        // coins (CurrencyPocket CoinPocketUI). The plates sit under m_player or the info header depending on the prefab:
        // the stat text's own plate (its parent object, e.g. Player/Weight, Player/Armor), wherever the prefab put it.
        private readonly RectTransform[] _sidePlates = new RectTransform[4];

        private RectTransform[] SidePlates()
        {
            RectTransform PlateOf(TMP_Text text) => text && text.transform.parent is RectTransform pr && pr != _gui.m_player && pr != _gui.m_info ? pr : null;
            RectTransform Named(string name) => (_gui.m_player.Find(name) ?? (_gui.m_info ? _gui.m_info.Find(name) : null)) as RectTransform;
            _sidePlates[0] = Named(ModCompat.MtTrashPlate);
            _sidePlates[1] = PlateOf(_gui.m_armor);
            _sidePlates[2] = PlateOf(_gui.m_weight);
            _sidePlates[3] = Named(ModCompat.CoinPocketPlate);
            return _sidePlates;
        }

        private void SideColumn(WindowsLayout o)
        {
            var n = 0;
            foreach (var plate in SidePlates())
            {
                if (!plate || !plate.gameObject.activeSelf) continue;
                CellPlate(plate, Move(o.SidePlate(n++), _invOff));
                AboveGrid(plate);
            }
        }

        // Side column plates draw (and raycast) after the player grid. Vanilla sizes the grid's hit area m_gridRoot to every
        // inventory row (InventoryGrid.cs:405-406), and Reposition draws fewer rows (hotbar row in the HUD strip, EAQS
        // rows hidden), so that transparent raycast Image reaches past the visible grid. The prefab puts Armor / Weight
        // (and the MorgottTweaks MT_Trash clone, CurrencyPocket CoinPocketUI) before PlayerGrid: the hit area ate every
        // click and hover on the trash plate (no drop, no tooltip) and on the coin pocket's button.
        private void AboveGrid(RectTransform plate)
        {
            var grid = _gui.m_playerGrid ? _gui.m_playerGrid.transform : null;
            if (!plate || !grid || plate.parent != grid.parent) return;
            var g = grid.GetSiblingIndex();
            if (plate.GetSiblingIndex() < g) plate.SetSiblingIndex(g); // removal shifts the grid down: lands right after it
        }

        // One side column plate as a cell (slot.cell look, mockup inv-layout-A): its "bkg" over the box, the glyph centred in
        // the upper part, the value centred under it inside the plate, a mod's own control on the plate (CurrencyPocket
        // ExtractCoinsButton) as a small square in the top-right corner (InventoryWindowsMath.SidePlateParts: nothing
        // overlaps). The value has one fixed size per text / box (FitSize, no auto-size) and is claimed from the generic
        // fitters (OwnFit): user 2026-10-26 - values under the plates, the coin value hidden under the extract button,
        // numbers blinking.
        private readonly List<RectTransform> _plateExtras = new List<RectTransform>(2);
        private readonly Dictionary<TMP_Text, (string Text, float W, float H, float Size)> _plateFit = new Dictionary<TMP_Text, (string, float, float, float)>();

        private void CellPlate(RectTransform plate, Box box)
        {
            Geo.SetScale(plate, 1f);
            Geo.Place(_canvas, plate, box);
            if (plate.Find("bkg") && plate.TryGetComponent<Image>(out var own) && own.enabled) own.enabled = false;
            RectTransform bkg = null, icon = null;
            TMP_Text value = null;
            _plateExtras.Clear();
            for (var n = 0; n < plate.childCount; n++)
            {
                var child = plate.GetChild(n);
                if (!(child is RectTransform rt) || !child.gameObject.activeSelf) continue;
                var part = NameCache.Of(child);
                if (part == "bkg") bkg = rt;
                else if (part.EndsWith("_icon", StringComparison.Ordinal)) icon = rt;
                else if (!value && child.TryGetComponent<TMP_Text>(out var label)) value = label;
                else if (child.TryGetComponent<Selectable>(out _)) _plateExtras.Add(rt);
            }
            var hasValue = value && plate != _sidePlates[0] && !string.IsNullOrEmpty(value.text);
            InventoryWindowsMath.SidePlateParts(box, hasValue, _plateExtras.Count, ThemeTokens.GetFloat("share.inv.side.icon", 0.9f), ThemeTokens.GetFloat("share.inv.side.value", 0.32f),
                out var iconBox, out var valueBox, out var extra0, out var step);
            if (bkg) Geo.Place(_canvas, bkg, box);
            if (icon) Geo.Place(_canvas, icon, iconBox);
            for (var n = 0; n < _plateExtras.Count; n++)
            {
                Geo.SetScale(_plateExtras[n], 1f);
                Geo.Place(_canvas, _plateExtras[n], new Box(extra0.X, extra0.Y + n * step, extra0.W, extra0.H));
            }
            if (!value) return;
            // the trash plate's own label: the glyph says it (its tooltip names it)
            if (value.enabled != hasValue) value.enabled = hasValue;
            if (!hasValue) return;
            OwnFit.Claim(value);
            Geo.Place(_canvas, value.rectTransform, valueBox);
            if (value.alignment != TextAlignmentOptions.Center) value.alignment = TextAlignmentOptions.Center;
            if (value.textWrappingMode != TextWrappingModes.NoWrap) value.textWrappingMode = TextWrappingModes.NoWrap;
            if (value.overflowMode != TextOverflowModes.Overflow) value.overflowMode = TextOverflowModes.Overflow;
            if (value.enableAutoSizing) value.enableAutoSizing = false;
            if (value.margin != Vector4.zero) value.margin = Vector4.zero;
            // one size per text / box: measured only when either changed (vanilla rewrites the text every frame, same value)
            var txt = value.text;
            if (!_plateFit.TryGetValue(value, out var fit) || fit.Text != txt || Mathf.Abs(fit.W - valueBox.W) > 0.01f || Mathf.Abs(fit.H - valueBox.H) > 0.01f)
            {
                var want = valueBox.H * 0.9f;
                var at = value.fontSize > 0.5f ? value.fontSize : want;
                var width = value.GetPreferredValues(txt, 100000f, 100000f).x * want / at;
                var floor = ThemeTokens.GetFloat("floor.text.number.px", 14f) / Mathf.Max(0.01f, _rootCanvas.scaleFactor);
                fit = (txt, valueBox.W, valueBox.H, InventoryWindowsMath.FitSize(want, width, valueBox.W, floor));
                _plateFit[value] = fit;
            }
            if (Mathf.Abs(value.fontSize - fit.Size) > 0.01f) value.fontSize = fit.Size;
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
                    Geo.Place(_canvas, rt, new Box(box.X + socket + 4f, box.Y, InventoryMath.StatValueBoxW(box.W, socket), box.H));
                    if (text.alignment != TextAlignmentOptions.MidlineLeft) text.alignment = TextAlignmentOptions.MidlineLeft;
                    if (text.textWrappingMode != TextWrappingModes.NoWrap) text.textWrappingMode = TextWrappingModes.NoWrap;
                    if (text.overflowMode != TextOverflowModes.Ellipsis) text.overflowMode = TextOverflowModes.Ellipsis;
                    var size = StatValueSize(rt.rect.height);
                    if (text.enableAutoSizing) text.enableAutoSizing = false;
                    if (Mathf.Abs(text.fontSize - size) > 0.1f) text.fontSize = size;
                }
            }
        }

        // Value font size of a stat plate: ~half the strip height (mockup), never below the body floor (screen px).
        private float StatValueSize(float plateH) =>
            Mathf.Max(plateH * 0.5f, ThemeTokens.GetFloat("floor.text.body.px", 16f) / Mathf.Max(0.01f, _rootCanvas.scaleFactor));

        // Width the plate's value text needs at StatValueSize(plateH), measured on its widest digits ("0" for every digit,
        // so a weight change in play does not outgrow the plate between layout passes).
        private float StatValueWidth(Transform plate, float plateH)
        {
            var text = plate ? plate.GetComponentInChildren<TMP_Text>(true) : null;
            if (!text || string.IsNullOrEmpty(text.text)) return 0f;
            var probe = System.Text.RegularExpressions.Regex.Replace(text.text, "[0-9]", "0");
            var size = StatValueSize(plateH);
            var w = text.GetPreferredValues(probe, float.PositiveInfinity, float.PositiveInfinity).x;
            return text.fontSize > 0.01f ? w * size / text.fontSize : w;
        }

        // Chest / backpack window: the window's one header (RuneWindowHeader: glyph in the icon slot, the container name
        // centred in the rune face (mirrored) over the plate's whole width; drag moves this window), grid top-aligned under
        // it (view = visible rows; columns past the room scroll sideways), the action row at the bottom: weight plate left,
        // glyph actions right-aligned, clear of the bottom corner caps (InventoryMath.ActionRow).
        private void ArrangeContainer(RectTransform panel, InventoryGrid grid, Box box, WindowGrid g, PanelKind kind, Inventory inventory,
            TMP_Text name, Transform weight, Button takeAll, Button stackAll)
        {
            if (!panel || box.IsEmpty) return;
            var k = _input.SkinScale;
            var pad = _tokens.PanelPad * k;
            var headerH = _tokens.PanelHeaderH * k;
            var fit = g.Fit;
            Geo.Place(_canvas, panel, box);
            var row = new Box(box.X + pad, box.Y + pad / 2f, box.W - 2f * pad, headerH - pad / 2f);
            if (grid)
            {
                var view = InventoryMath.ContainerGridView(box, fit, _tokens, k);
                Geo.Place(_canvas, (RectTransform)grid.transform, view);
                Want(grid, fit.Pitch, fit.Cell);
                var s = State(grid);
                Range(grid, g, false, 0, s.ColScroll, view);
                if (g.ScrollH) _colViews.Add((grid, view, g));
                ColumnBar(panel, view, g, s.ColScroll);
            }
            var actions = InventoryMath.ActionRow(box, _tokens, k);
            var actionsW = Bar(panel, kind, inventory, takeAll, stackAll, actions);
            var backpack = kind == PanelKind.Backpack;
            var glyph = HeaderGlyph(panel, backpack ? "glyph.panel.backpack" : "glyph.panel.chest");
            // The weight plate sits at the left of the action row (glyph actions right-aligned): the header keeps only the
            // glyph, so the title gets the plate's whole width and truncates only when it really does not fit (user
            // 2026-10-26: "Сундук из…" cut while the header had room - the aux slot narrowed both title sides).
            var weightPlate = DirectChild(panel, weight);
            if (weightPlate)
            {
                var ph = Mathf.Min(actions.H, ThemeTokens.GetFloat("size.win.header", 40f) * k * WindowHeaderMath.IconShare);
                var tokenW = ThemeTokens.GetFloat("size.inv.plate.weight.dock", 110f) * k;
                var room = Mathf.Max(0f, actions.W - actionsW - _tokens.Gap * k);
                var textW = StatValueWidth(weightPlate, ph);
                var pw = InventoryMath.StatPlateWidth(tokenW, ph, textW, room);
                StatPlate(weightPlate, new Box(actions.X, actions.Y + (actions.H - ph) / 2f, pw, ph));
            }
            var head = RuneWindowHeader.Ensure(panel, backpack ? WindowMove.Backpack : WindowMove.Chest);
            if (!head) return;
            head.Frame = PanelDecor.Of(backpack ? _packHost : _chestHost);
            var fallback = backpack ? "$runeskin_win_backpack" : "$runeskin_win_chest";
            if (name) head.Adopt(name, fallback);
            else head.Fallback(fallback).Text(fallback);
            var hh = Mathf.Min(WindowHeaderMath.HeaderHeight(ThemeTokens.GetFloat("size.win.header", 40f), k), row.H);
            head.Place(_canvas, new Box(row.X, row.Y, row.W, hh), glyph && glyph.enabled ? glyph : null); // top-aligned (name row too)
            // The backpack window is as wide as its header needs for the full title (next layout, InputHash): the AB names
            // ("Ранец Уровень 1" .. "Небывалый рюкзак исследователя Уровень 5") were cut to "Ранец Уро…" in the 3-column
            // window (run 20261027-145423). Half-unit steps keep the layout from recomputing on float noise.
            if (backpack)
            {
                var band = head.TitleBandNeed();
                _packHeaderNeed = band > 0f ? Mathf.Ceil((band + 2f * pad) * 2f) / 2f : 0f;
            }
        }

        // Backpack window width its header needs (ArrangeContainer -> WindowsInput.BackpackHeaderW), canvas units.
        private float _packHeaderNeed;

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

        private GridState State(InventoryGrid grid)
        {
            if (!_grids.TryGetValue(grid, out var s)) _grids[grid] = s = new GridState();
            return s;
        }

        private void Want(InventoryGrid grid, float pitch, float cell)
        {
            if (!grid) return;
            var s = State(grid);
            s.WantPitch = pitch;
            s.WantCell = cell;
            var template = grid.m_elementPrefab ? grid.m_elementPrefab.transform as RectTransform : null;
            var native = template ? template.rect.width : cell;
            s.WantScale = native > 1f ? cell / native : 1f;
        }

        // Shown range of a grid in its window: our own row scroll (player grid) and column scroll (containers past the
        // room); rows of a container scroll in vanilla's ScrollRect. Re-places the cells when the range changed.
        private void Range(InventoryGrid grid, WindowGrid g, bool ownRows, int rowScroll, int colScroll, Box view)
        {
            var s = State(grid);
            // A moved / resized view re-places its cells (they are placed in canvas coordinates, not parented to the view
            // layout): run 20261026-195047 inv-rows-chest-small: a chest window placed after its cells kept them 6 px over
            // its viewport top.
            if (s.AppliedView.X != view.X || s.AppliedView.Y != view.Y || s.AppliedView.W != view.W || s.AppliedView.H != view.H)
            {
                s.AppliedView = view;
                s.NeedsReposition = true;
            }
            s.VisibleRows = ownRows && g.ScrollV ? Mathf.Max(1, g.VisibleRows) : int.MaxValue;
            s.VisibleColumns = g.ScrollH ? Mathf.Max(1, g.VisibleColumns) : int.MaxValue;
            s.RowScroll = ownRows && g.ScrollV ? InventoryWindowsMath.ClampScroll(rowScroll, g.Rows, g.VisibleRows) : 0;
            s.ColScroll = g.ScrollH ? InventoryWindowsMath.ClampScroll(colScroll, g.Columns, g.VisibleColumns) : 0;
            if (s.AppliedRowScroll != s.RowScroll || s.AppliedColScroll != s.ColScroll || s.AppliedRows != s.VisibleRows || s.AppliedColumns != s.VisibleColumns)
            {
                s.AppliedRowScroll = s.RowScroll;
                s.AppliedColScroll = s.ColScroll;
                s.AppliedRows = s.VisibleRows;
                s.AppliedColumns = s.VisibleColumns;
                s.NeedsReposition = true;
            }
        }

        // Cells vanilla (re)built: InventoryGrid.UpdateGui destroys and re-instantiates every cell when the inventory size
        // changes (InventoryGrid.cs:258-274: the first open, every chest / backpack of another size). Only those cells get
        // the root's classification pass (same context, same exclusions as ScreenRegistry.Restyle(root)): the full pass over
        // m_player (run 20261027-152339: "diag Inventory: graphics=804" in the first open frame) restyled the doll, footer,
        // container and headers again although nothing but the cells was new. Same roles, same look.
        private static void RestyleCells(InventoryGrid grid, GridState s)
        {
            var first = grid.m_elements.Count > 0 && grid.m_elements[0] ? grid.m_elements[0].GetInstanceID() : 0;
            if (s.StyledElements == grid.m_elements.Count && s.StyledFirst == first) return;
            var owner = ScreenRegistry.RootOf(grid.transform);
            var ctx = ScreenRegistry.ContextOf(owner);
            if (ctx == null) return;
            s.StyledElements = grid.m_elements.Count;
            s.StyledFirst = first;
            Restyler.RestyleSubtree(grid.transform, owner, ctx);
        }

        // Closed, first frame with a live local player (behind the spawn's loading screen, like the doll's preview clone,
        // CharacterPreview.Prewarm): the player grid's cells built and styled ahead of the first open. The build is
        // vanilla's own first-open call, InventoryGui.UpdateInventory -> m_playerGrid.UpdateInventory(inventory, player,
        // dragItem) (InventoryGui.cs:688-691; no drag exists while closed), which instantiates the cells when the size
        // changed (InventoryGrid.cs:258-274) with every UpdateGui patch (EAQS, V+) as usual; the first Tab then only
        // re-places them (AfterGrid sees the styled cell set). Once per inventory: a no-op afterwards.
        private void PrewarmGrid()
        {
            var grid = _gui.m_playerGrid;
            var player = Player.m_localPlayer;
            if (!grid || !player || player.IsDead() || _gui.m_animator.GetBool(AnimVisible)) return;
            var inv = player.GetInventory();
            if (inv == null || grid.GetInventory() == inv) return;
            grid.UpdateInventory(inv, player, null);
            RestyleCells(grid, State(grid));
            if (_parked.Count > 0) Idle(grid.transform); // the new cells' frame components idle like the rest
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
                if (rebuilt) RestyleCells(grid, s);
            }
            // Gamepad: the selected cell scrolls into the shown range (vanilla moves m_selected over the whole grid).
            if (ZInput.IsGamepadActive() && grid.m_selected.x >= 0)
            {
                if (s.VisibleColumns != int.MaxValue)
                {
                    var c = InventoryWindowsMath.ScrollTo(s.ColScroll, grid.m_selected.x, s.VisibleColumns);
                    if (c != s.ColScroll) s.ColScroll = c;
                }
                if (isPlayer && s.VisibleRows != int.MaxValue && grid.m_selected.y < EaqsBridge.VisibleRows(grid.GetInventory().GetHeight()))
                    _gridScroll = InventoryWindowsMath.ScrollTo(_gridScroll, grid.m_selected.y - SkipRows(grid), s.VisibleRows);
            }
            // Column-scrolled container: the root's hit area (vanilla sizes it to every column, :405) kept to the shown
            // columns, so it never reaches over the next window's cells.
            if (s.VisibleColumns != int.MaxValue && grid.m_gridRoot)
            {
                var want = s.VisibleColumns * grid.m_elementSpace;
                var sd = grid.m_gridRoot.sizeDelta;
                if (Mathf.Abs(sd.x - want) > 0.01f) grid.m_gridRoot.sizeDelta = new Vector2(want, sd.y);
            }
            for (var i = 0; i < grid.m_elements.Count; i++) CellMarks(grid.m_elements[i]);
            Magazines(grid);
            // EAQS cells right after EAQS put them back on its own panel spots (its UpdateGui postfix, every frame): placed
            // only in LateUpdate they sat over the crafting window for the rest of the Update phase, where the input module
            // and InventoryGrid.GetHoveredElement raycast (run 20261026-230323 tips-eaqs-utility: Megingjord's Utility
            // cell measured under Recipes, no tooltip). LateUpdate places them again after a relayout.
            if (isPlayer) PlaceEaqsCells(grid, s.Scale, cellsOnly: true);
            var inv = grid.GetInventory();
            if (rebuilt || s.OverlayRevision != InventoryRegistry.OverlayRevision || !ReferenceEquals(s.Inventory, inv) || s.ThemeRevision != ThemeTokens.Revision)
            {
                s.OverlayRevision = InventoryRegistry.OverlayRevision;
                s.Inventory = inv;
                s.ThemeRevision = ThemeTokens.Revision;
                Overlays(grid, inv, grid.m_elements.Count); // EAQS cells hold real inventory coordinates too
            }
        }

        // Vanilla's scroll view rule (InventoryGrid.ResetView, run once on the first container update): a root taller than
        // the view scrolls from its top (pivot y 1), a shorter one is centred (pivot 0.5), anchoredPosition 0. That first
        // run sees the prefab view and the vanilla pitch; once this layout resizes the view / pitch the decision went stale
        // (user 2026-10-26: 4th chest row cut by the viewport, empty frame below). Re-applied whenever it differs.
        private static bool ScrollView(InventoryGrid grid)
        {
            if (!grid.m_gridRoot) return false;
            var view = ((RectTransform)grid.transform).rect.height;
            var want = grid.m_gridRoot.rect.height > view + 0.5f ? 1f : 0.5f;
            if (Mathf.Abs(grid.m_gridRoot.pivot.y - want) < 0.001f) return false;
            grid.ResetView();
            return true;
        }

        // The cells are children of the grid root (a container's ScrollRect content) but placed against the view in canvas
        // coordinates, so a root that moves after a Reposition drags them along: ResetView puts the root's pivot at
        // anchoredPosition 0 and the ScrollRect then pulls the content into its viewport over the next frames (run
        // 20261026-200900 inv-rows-chest-small-2560x1440: the top row 5 px over the viewport top, one gap off the centred
        // block, with an unchanged view). A moved root re-places its cells.
        private bool RootMoved(InventoryGrid grid, GridState s)
        {
            if (!grid.m_gridRoot) return false;
            var root = Geo.BoxOf(_canvas, grid.m_gridRoot);
            var a = s.AppliedRoot;
            return Mathf.Abs(root.X - a.X) > 0.01f || Mathf.Abs(root.Y - a.Y) > 0.01f || Mathf.Abs(root.W - a.W) > 0.01f || Mathf.Abs(root.H - a.H) > 0.01f;
        }

        // Rows of a grid that live elsewhere: the player grid's row 0 is the HUD strip's hotbar.
        private int SkipRows(InventoryGrid grid) => StripHotbar && _input.Hotbar.Valid && grid == _gui.m_playerGrid ? 1 : 0;

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

        // Crossbow magazine dots (PlayerHud.MagazinePips) on every cell of this grid that holds a magazine crossbow: player
        // grid incl. its EAQS cells (real inventory coordinates), containers, backpacks. The grid's items are scanned once
        // (usually none or one crossbow); every other cell only hides pips it may still show.
        private static readonly List<ItemDrop.ItemData> MagazineItems = new List<ItemDrop.ItemData>();

        private static void Magazines(InventoryGrid grid)
        {
            var inv = grid.GetInventory();
            MagazineItems.Clear();
            if (inv != null)
                foreach (var item in inv.GetAllItems())
                    if (PlayerHud.MagazinePips.IsCandidate(item)) MagazineItems.Add(item);
            var tokens = GridCellLook.Tokens;
            for (var i = 0; i < grid.m_elements.Count; i++)
            {
                var e = grid.m_elements[i];
                if (!e) continue;
                ItemDrop.ItemData item = null;
                foreach (var m in MagazineItems)
                    if (m.m_gridPos == e.Position) { item = m; break; }
                if (item == null)
                {
                    PlayerHud.MagazinePips.Hide(e);
                    continue;
                }
                var cell = (RectTransform)e.transform;
                var dur = InventoryMath.Cell(cell.rect.width, cell.rect.height, tokens).Durability;
                PlayerHud.MagazinePips.Apply(e, item, dur.X, dur.Y, Mathf.Abs(cell.lossyScale.y));
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

        // RuneSkin owns the HUD strip: the inventory's hotbar row is the strip's hotbar.
        private static bool StripHotbar => PlayerHud.HudCluster.Instance;

        // Cell block centred in the grid view on both axes (InventoryMath.BlockOffset: cells only, no trailing gap; a
        // scrolling block starts at the root top), only the shown range (GridState rows / columns) drawn, the rest at
        // scale 0. gapShare = gap / pitch.
        private void Reposition(InventoryGrid grid, int visibleCount, float scale, float gapShare)
        {
            if (!grid.m_gridRoot) return;
            var s = State(grid);
            var w = grid.GetInventory().GetWidth();
            var root = Geo.BoxOf(_canvas, grid.m_gridRoot);
            s.AppliedRoot = root;
            var view = Geo.BoxOf(_canvas, (RectTransform)grid.transform);
            var unit = Geo.WorldPerUnit(_canvas);
            var pitch = grid.m_elementSpace * grid.m_gridRoot.lossyScale.x / Mathf.Max(unit, 1e-6f); // canvas units
            var gap = pitch * gapShare;
            // Player grid under the HUD strip: row 0 lives in the strip (its hotbar), the grid starts at row 1.
            var skipRow = SkipRows(grid);
            var count = Mathf.Min(grid.m_elements.Count, visibleCount);
            var rows = w > 0 ? Mathf.Max(0, (count + w - 1) / w - skipRow) : 0;
            var cols = Mathf.Min(w, s.VisibleColumns);
            var shownRows = Mathf.Min(rows, s.VisibleRows);
            var x0 = view.X + InventoryMath.BlockOffset(view.W, cols, pitch, gap);
            // fits: centred in the view (whatever the root's anchors); taller: from the (scrolled) root top
            var fits = shownRows * pitch - gap <= view.H + 0.01f;
            var y0 = fits ? view.Y + InventoryMath.BlockOffset(view.H, shownRows, pitch, gap) : root.Y;
            for (var i = 0; i < grid.m_elements.Count && i < visibleCount; i++)
            {
                var e = grid.m_elements[i];
                if (!e) continue;
                var pos = e.Position;
                var c = pos.x - s.ColScroll;
                var r = pos.y - skipRow - s.RowScroll;
                if (pos.y < skipRow || c < 0 || c >= cols || r < 0 || r >= shownRows)
                {
                    // hidden (hotbar row in the strip, scrolled out): scale 0 at the block origin, never left where vanilla
                    // put it (run 20261026-193647 strip-clear-player-inv: the scale-0 hotbar row sat 500 px under the screen)
                    Geo.SetScale(e.transform, 0f);
                    Geo.SetTopLeft((RectTransform)e.transform, Geo.World(_canvas, x0, y0));
                    continue;
                }
                Geo.SetScale(e.transform, scale);
                Geo.SetTopLeft((RectTransform)e.transform, Geo.World(_canvas, x0 + c * pitch, y0 + r * pitch));
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

        // ---------------------------------------------------------------- doll

        private void RebuildEntries()
        {
            _entries.Clear();
            foreach (var s in _eaqs)
            {
                if (s.Kind == EaqsBridge.Kind.Quick) continue;
                EaqsBridge.Describe(s.Id, out var glyph, out var order, out var tooltipKey);
                _entries.Add(new DollEntry { Key = "eaqs:" + s.Id, Glyph = glyph, Name = SlotNames.Get(tooltipKey), Side = InventoryWindowsMath.DollSide(glyph), Order = InventoryWindowsMath.DollRank(glyph) * 10000 + order * 100 + s.Index, Element = s.Element });
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
                // After the vanilla / EAQS cells of their side, in registration group + order (stable ids).
                _entries.Add(new DollEntry { Key = r.Key, Glyph = null, Side = RegisteredSide(r), Order = 10000000 + GroupRank(r.D.Group) * 1000 + r.D.Order, Element = -1, Cell = cell });
            }
            foreach (var key in new List<string>(_registered.Keys))
                if (!seen.Contains(key))
                {
                    if (_registered[key]) Destroy(_registered[key].gameObject);
                    _registered.Remove(key);
                }
            _entries.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Key, b.Key));
            int left = 0, right = 0;
            for (var i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                e.Row = e.Side == 0 ? left++ : right++;
                _entries[i] = e;
            }
            _entriesRevision = InventoryRegistry.Revision;
        }

        private static int GroupRank(string group) =>
            group == "armor" ? 0 : group == "jewelry" ? 1 : group == "utility" ? 2 : group == "light" ? 3 : 9;

        // A registered slot's doll side: armour with the body equipment (left), everything else right.
        private static int RegisteredSide(InventoryRegistry.Slot r) => r.D.Group == "armor" ? 0 : 1;

        // Equipment cells per doll side (the layout's DollLeft / DollRight), same rules as RebuildEntries.
        private void DollSideCounts(out int left, out int right)
        {
            RefreshEaqs();
            left = right = 0;
            foreach (var s in _eaqs)
            {
                if (s.Kind == EaqsBridge.Kind.Quick) continue;
                EaqsBridge.Describe(s.Id, out var glyph, out _, out _);
                if (InventoryWindowsMath.DollSide(glyph) == 0) left++;
                else right++;
            }
            foreach (var r in InventoryRegistry.Slots)
            {
                if (r.Dead) continue;
                if (RegisteredSide(r) == 0) left++;
                else right++;
            }
        }

        // After EAQS placed its cells this frame: storage cells in the doll window's two columns (body left, jewellery /
        // light / mods right, rows scrolling together past the room), the alcove and the live preview between them; quick
        // cells over the HUD strip's potion cells.
        private void PlaceEaqsCells(InventoryGrid grid, float gridScale, bool cellsOnly = false)
        {
            RefreshEaqs();
            if (_entriesRevision != InventoryRegistry.Revision || _entries.Count != CountDollSources()) RebuildEntries();
            var o = _layout;
            var d = o.DollGrid;
            var dx = _dollOff.x;
            var dy = _dollOff.y;
            var visible = Mathf.Max(1, d.VisibleRows);
            _dollScroll = InventoryWindowsMath.ClampScroll(_dollScroll, d.Rows, visible);
            var native = grid.m_elementPrefab ? ((RectTransform)grid.m_elementPrefab.transform).rect.width : d.Cell;
            var cellScale = native > 1f ? d.Cell / native : 1f;
            // Gamepad selection on a hidden EAQS row (EAQS spatial navigation) scrolls it into view.
            if (d.ScrollV && ZInput.IsGamepadActive())
                foreach (var en in _entries)
                {
                    if (en.Element < 0 || en.Element >= grid.m_elements.Count || !grid.m_elements[en.Element] || grid.m_elements[en.Element].Position != grid.m_selected) continue;
                    _dollScroll = InventoryWindowsMath.ScrollTo(_dollScroll, en.Row, visible);
                }
            foreach (var entry in _entries)
            {
                var row = entry.Row - _dollScroll;
                var shown = row >= 0 && row < visible;
                var cx = (entry.Side == 0 ? o.DollLeftX : o.DollRightX) + dx;
                var cy = o.DollCellsY + dy + row * d.Pitch;
                if (entry.Element >= 0 && entry.Element < grid.m_elements.Count)
                {
                    var e = grid.m_elements[entry.Element];
                    if (!e) continue;
                    var rt = (RectTransform)e.transform;
                    Geo.SetScale(rt, shown ? cellScale : 0f);
                    Geo.SetTopLeft(rt, Geo.World(_canvas, cx, shown ? cy : o.DollCellsY + dy)); // hidden: in the doll, scale 0
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
            if (cellsOnly)
            {
                PlaceQuickCells(grid);
                return;
            }
            var figure = Move(o.DollFigure, _dollOff);
            var fits = figure.W >= d.Cell;
            if (_backdrop) Show(_backdrop, fits ? figure : default);
            var live = _preview && !_preview.Failed;
            // live preview: the texture covers the whole alcove (weapons / shield / cape never cut at a narrow image
            // edge), the body framed to the figure box's height inside it
            if (_preview) _preview.Place(_canvas, fits && live ? figure : default, Move(o.DollPreview, _dollOff));
            if (_silhouette) Show(_silhouette, fits && !live ? Move(o.DollPreview, _dollOff) : default);

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

        // Container grids that scroll sideways (columns past the window's room) with their view, refreshed each placement.
        private readonly List<(InventoryGrid Grid, Box View, WindowGrid Fit)> _colViews = new List<(InventoryGrid, Box, WindowGrid)>(2);
        private float _dragScrollAt;

        // Wheel over a window scrolls what overflows in it: doll rows, player grid rows, container columns (rows of a
        // container are vanilla's ScrollRect; with both, Shift + wheel scrolls the columns). An item dragged to the edge
        // of a sideways-scrolling container steps its columns (Codex review: drag must reach hidden columns).
        private void ScrollInput()
        {
            if (_layout == null || !_canvas) return;
            var o = _layout;
            var scale = _rootCanvas && _rootCanvas.scaleFactor > 0f ? _rootCanvas.scaleFactor : 1f;
            var p = ZInput.pointerPosition;
            var pt = new Vector2(p.x / scale, (Screen.height - p.y) / scale);
            if (_gui.m_dragGo && Time.unscaledTime >= _dragScrollAt)
                foreach (var v in _colViews)
                {
                    if (!v.Grid || pt.y < v.View.Y || pt.y > v.View.Bottom) continue;
                    var edge = v.Fit.Cell * 0.5f;
                    var step = pt.x >= v.View.X - edge && pt.x < v.View.X + edge ? -1 : pt.x > v.View.Right - edge && pt.x <= v.View.Right + edge ? 1 : 0;
                    if (step == 0) continue;
                    State(v.Grid).ColScroll += step;
                    _dragScrollAt = Time.unscaledTime + ThemeTokens.GetFloat("time.inv.drag.scroll", 0.35f);
                }
            if (Screens.TooltipPlacer.CapturesWheel) return; // the hovered cell's long tooltip scrolls instead
            var wheel = ZInput.GetMouseScrollWheel();
            if (Mathf.Approximately(wheel, 0f)) return;
            var dir = wheel < 0f ? 1 : -1;
            if (o.DollGrid.ScrollV && Inside(_dollBox, pt))
            {
                _dollScroll += dir;
                return;
            }
            if (o.Player.ScrollV && Inside(_invBox, pt))
            {
                _gridScroll = InventoryWindowsMath.ClampScroll(_gridScroll + dir, o.Player.Rows, o.Player.VisibleRows);
                return;
            }
            var shift = ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift);
            foreach (var v in _colViews)
                if (v.Grid && Inside(v.View, pt) && (!v.Fit.ScrollV || shift))
                {
                    State(v.Grid).ColScroll += dir;
                    return;
                }
        }

        private static bool Inside(Box b, Vector2 pt) => !b.IsEmpty && pt.x >= b.X && pt.x <= b.Right && pt.y >= b.Y && pt.y <= b.Bottom;

        // Column scroll indicator of a sideways-scrolling container (Codex review: show that columns are hidden): a thin
        // track under the shown cells, the handle = the shown share at the scroll position. Hidden without column scroll.
        private void ColumnBar(RectTransform panel, Box view, WindowGrid g, int scroll)
        {
            var track = OwnImage(panel, "RuneSkinColumnTrack", "scrollbar.track", "container column scroll track");
            var handle = OwnImage(panel, "RuneSkinColumnHandle", "scrollbar.handle", "container column scroll handle");
            if (!g.ScrollH || g.Columns <= 0)
            {
                if (track.gameObject.activeSelf) track.gameObject.SetActive(false);
                if (handle.gameObject.activeSelf) handle.gameObject.SetActive(false);
                return;
            }
            if (!track.gameObject.activeSelf) track.gameObject.SetActive(true);
            if (!handle.gameObject.activeSelf) handle.gameObject.SetActive(true);
            var k = _input.SkinScale;
            var h = ThemeTokens.GetFloat("size.inv.colbar", 5f) * k;
            var x = view.X + InventoryMath.BlockOffset(view.W, g.VisibleColumns, g.Pitch, g.Pitch - g.Cell);
            var shownH = Mathf.Min(view.H, g.ViewH);
            var b = new Box(x, view.Y + shownH + Mathf.Max(1f, (g.Pitch - g.Cell - h) / 2f), g.ViewW, h);
            Geo.Place(_canvas, track.rectTransform, b);
            Geo.Place(_canvas, handle.rectTransform, new Box(b.X + b.W * scroll / g.Columns, b.Y, b.W * g.VisibleColumns / g.Columns, h));
        }

        private static Image OwnImage(Transform parent, string name, string role, string reason)
        {
            var t = parent.Find(name);
            if (t && t.TryGetComponent<Image>(out var existing)) return existing;
            var image = InfoScreens.InfoArt.OwnImage(parent, name, role, reason);
            image.preserveAspect = false;
            return image;
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

        private float Bar(RectTransform panel, PanelKind kind, Inventory inventory, Button takeAll, Button stackAll, Box box, float size = 0f, float gap = -1f)
        {
            if (!panel) return 0f;
            if (!_bars.TryGetValue(panel, out var bar) || bar == null)
                _bars[panel] = bar = new ActionBar(panel, kind, _tooltipPrefab);
            bar.Sync(kind, inventory, takeAll, stackAll);
            if (!(size > 0f)) size = _tokens.FooterH * _input.SkinScale * 0.8f;
            if (!(gap >= 0f)) gap = _tokens.Gap / 2f;
            var w = bar.Width(size, gap);
            if (!box.IsEmpty) bar.Place(_canvas, box, size, gap);
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
