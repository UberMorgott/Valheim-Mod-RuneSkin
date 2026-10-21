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

        private InventoryGui _gui;
        private RectTransform _canvas;
        private Canvas _rootCanvas;
        private RectTransform _decor, _centerHost, _chestHost, _packHost, _doll, _focus, _hotbarHeader, _backdrop, _silhouette;
        private readonly InventoryTokens _tokens = new InventoryTokens();
        private readonly InventoryInput _input = new InventoryInput();
        private InventoryLayout _layout;
        private int _layoutHash;
        private GameObject _tooltipPrefab;

        // Doll
        private DollCell _right, _left, _ammo;
        private readonly Dictionary<string, DollCell> _registered = new Dictionary<string, DollCell>();
        private readonly Dictionary<string, InventoryRegistry.Slot> _registeredBy = new Dictionary<string, InventoryRegistry.Slot>();
        private readonly List<EaqsBridge.SlotInfo> _eaqs = new List<EaqsBridge.SlotInfo>();
        private readonly List<DollEntry> _entries = new List<DollEntry>();
        private int _eaqsReadFrame = -1000, _entriesRevision = -1, _dollScroll;
        private float _focusUntil;

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
            public float WantPitch, WantScale;
            public int Visible = int.MaxValue;
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
            t.HudBand = F("size.inv.hud.band", t.HudBand); t.HeaderH = F("size.inv.header", t.HeaderH); t.FooterH = F("size.inv.footer", t.FooterH);
            t.PanelHeaderH = F("size.inv.panel.header", t.PanelHeaderH); t.PanelPad = F("size.inv.panel.pad", t.PanelPad);
            t.GridCell = F("size.inv.cell", t.GridCell); t.GridGap = F("size.inv.cell.gap", t.GridGap); t.HotbarHeaderH = F("size.inv.hotbar.header", t.HotbarHeaderH);
            t.ContainerCell = F("size.inv.container.cell", t.ContainerCell); t.ContainerGap = F("size.inv.container.gap", t.ContainerGap);
            t.DollCell = F("size.inv.doll.cell", t.DollCell); t.DollGap = F("size.inv.doll.gap", t.DollGap);
            t.DollColumns = F("size.inv.doll.columns", t.DollColumns); t.DollMirrorGap = F("size.inv.doll.mirror.gap", t.DollMirrorGap);
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
            _silhouette = Decor("RuneSkinDollFigure", "doll.silhouette", "carved warrior silhouette (no 3D preview yet)");
            _right = Mirror("right", "weapon", "$runeskin_slot_right");
            _left = Mirror("left", "offhand", "$runeskin_slot_left");
            _ammo = Mirror("ammo", "ammo", "$runeskin_slot_ammo");

            _focus = (RectTransform)new GameObject("RuneSkinFocus", typeof(RectTransform)).transform;
            _focus.SetParent(_gui.m_player, false);
            var ring = _focus.gameObject.AddComponent<Image>();
            ring.raycastTarget = false;
            var fm = DollCell.Mark(ring, "slot.focus", "source-cell highlight of a hand mirror");
            fm.Outcome = Restyler.Apply(ring, fm, ThemeTokens.Role("slot.focus")) ? Outcome.Styled : Outcome.Degraded;
            if (fm.Outcome != Outcome.Styled) ring.color = new Color(0.31f, 0.88f, 0.9f, 0.5f);
            _focus.gameObject.SetActive(false);
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

        private DollCell Mirror(string id, string glyph, string key)
        {
            var cell = DollCell.Create(_doll, id, "slot.mirror", "glyph.slot." + glyph, _tooltipPrefab);
            cell.OnPrimary = c => MirrorPrimary(c);
            cell.OnDropped = c => DropOnMirror(c);
            cell.OnSecondary = c => MirrorUnequip(c);
            cell.SetTooltip(SlotNames.Get(key), SlotNames.Get("$runeskin_mirror_hint"));
            return cell;
        }

        // Vanilla panel backgrounds under owned roots: replaced by RuneSkin's panel stacks (hidden, not destroyed).
        private void HideVanillaBackgrounds()
        {
            foreach (var panel in new[] { _gui.m_player, _gui.m_container, _gui.m_info })
            {
                if (!panel) continue;
                if (panel.TryGetComponent<Image>(out var own)) own.enabled = false;
                foreach (Transform child in panel)
                    if (IsVanillaBackground(child) && child.TryGetComponent<Image>(out var image))
                        image.enabled = false;
            }
        }

        private static bool IsVanillaBackground(Transform t) =>
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
                else if (t.name == "Weight" && t.parent && (t.parent == _gui.m_player || t.parent.GetComponentInChildren<InventoryGrid>(true))) ctx.Role(image, "plate.stat", "weight plate (InventoryGui.m_weight / m_containerWeight)");
                else if (t.name == "weight_icon") ctx.Role(image, "glyph.stat.weight", "weight plate icon");
                else if (t.name == "armor_icon" && t.parent && t.parent.name == "Armor") ctx.Role(image, "glyph.stat.armor", "armor plate icon (InventoryGui.m_armor)");
                else if (t.name == "armor_icon" && t.parent && t.parent.name == "MT_Trash") ctx.Role(image, "glyph.trash", "MorgottTweaks trash plate icon");
                else if (t.name == "armor_icon" && t.parent && t.parent.name == "CoinPocketUI") ctx.Role(image, "content", "MorgottTweaks coin pocket: item icon");
                else if (t.name == "Background" && t.parent && t.parent.parent == _gui.m_info) ctx.Role(image, "tab.background", "info tab button background (Info/<tab>/Background)");
                else if (t.name == "Checked" && t.parent && t.parent.parent == _gui.m_info) ctx.Role(image, "content", "PVP toggle state icon");
                else if (sprite.StartsWith("BraidLine", StringComparison.Ordinal)) ctx.Role(image, "divider", "vanilla braid divider (" + sprite + ")");
            }
            foreach (var panel in new[] { _gui.m_player, _gui.m_container, _gui.m_info, _packPanel })
            {
                if (!panel) continue;
                if (panel.TryGetComponent<Image>(out var own)) ctx.Role(own, "skip", "vanilla panel background (replaced by RuneSkin panel stack)");
                foreach (Transform child in panel)
                    if (IsVanillaBackground(child) && child.TryGetComponent<Image>(out var image))
                        ctx.Role(image, "skip", "vanilla / EAQS panel background (replaced by RuneSkin panel stack)");
            }
            foreach (var b in new[] { _gui.m_takeAllButton, _gui.m_stackAllButton })
                if (b && b.targetGraphic) ctx.Role(b.targetGraphic, "action.button", "vanilla Take all / Stack all as glyph action (InventoryGui.cs:408-409)");
            if (_packPanel)
                foreach (var b in _packPanel.GetComponentsInChildren<Button>(true))
                    if (b.targetGraphic) ctx.Role(b.targetGraphic, "action.button", "backpack panel action button");
            // The crafting panel is not ours until M3.
            if (_gui.m_crafting) ctx.Exclude(_gui.m_crafting, "crafting panel: placed by RuneSkin, styled by the other skin until M3");
            return ctx;
        }

        private static void ElementRoles(ScreenContext ctx, InventoryElement e, bool hotbar, bool doll = false)
        {
            if (e.m_button && e.m_button.targetGraphic)
                ctx.Role(e.m_button.targetGraphic, doll ? "slot.recess" : hotbar ? "slot.hotbar" : "slot.cell", doll ? "EAQS doll cell (recessed)" : "InventoryElement.m_button target (slot cell)");
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
            ctx.Role(e.m_amount, "text.body", "InventoryElement.m_amount");
            ctx.Role(e.m_quality, "text.body", "InventoryElement.m_quality");
            var binding = e.transform.Find("binding");
            if (binding && binding.TryGetComponent<TMP_Text>(out var key)) ctx.Role(key, "text.header", "hotbar binding number (InventoryGrid.cs:286-294)");
        }

        // ---------------------------------------------------------------- per frame

        // InventoryGui.Update postfix, after EAQS and AugaSkin.
        public void Frame()
        {
            if (!InventoryGui.IsVisible() || !_canvas || !Player.m_localPlayer)
            {
                RestoreQuickBar();
                return;
            }
            FindBackpack();
            var hash = InputHash();
            if (hash != _layoutHash || _layout == null)
            {
                _layoutHash = hash;
                Compute();
            }
            ApplyPanels();
            if (_logPlacement > 0 && --_logPlacement == 0) LogPlacement();
            UpdateMirrors();
            UpdateActions();
            HideQuickBar();
            if (_focus && _focus.gameObject.activeSelf && Time.unscaledTime > _focusUntil) _focus.gameObject.SetActive(false);
            DollScrollInput();
        }

        public void OnHide()
        {
            RestoreQuickBar();
            if (_focus) _focus.gameObject.SetActive(false);
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
                h = h * 31 + inv.GetWidth() * 101 + EaqsBridge.VisibleRows(inv.GetHeight());
                var c = ContainerInventory();
                h = h * 31 + (c != null ? c.GetHashCode() ^ (c.GetWidth() * 101 + c.GetHeight()) : 0);
                h = h * 31 + (ContainerIsBackpack() ? 7 : 0);
                h = h * 31 + (_packInventory != null ? _packInventory.GetHashCode() ^ (_packInventory.GetWidth() * 101 + _packInventory.GetHeight()) : 0);
                h = h * 31 + DollStorageCount();
                h = h * 31 + InventoryRegistry.Revision;
                h = h * 31 + ThemeTokens.Revision;
                return h;
            }
        }

        private Inventory ContainerInventory() => _gui.m_currentContainer ? _gui.m_currentContainer.GetInventory() : null;

        // AdventureBackpacks opens a worn backpack without a chest as InventoryGui.Show(player's own Container)
        // (fork PlayerExtensions.OpenBackpack): m_container then shows the backpack.
        private bool ContainerIsBackpack() =>
            _gui.m_currentContainer && Player.m_localPlayer && _gui.m_currentContainer.gameObject == Player.m_localPlayer.gameObject;

        // Backpack beside a chest: the fork's third panel (registered "inventory.backpack", or its clone
        // "AB_BackpackPanel" under the crafting panel's parent before the fork registers it).
        private void FindBackpack()
        {
            var panel = InventoryRegistry.Panel("inventory.backpack");
            if (!panel) panel = AbApi.Panel();
            if (!panel && _gui.m_crafting && _gui.m_crafting.parent)
                panel = _gui.m_crafting.parent.Find("AB_BackpackPanel") as RectTransform;
            if (panel && !panel.gameObject.activeInHierarchy) panel = null;
            if (panel != _packPanel)
            {
                _packPanel = panel;
                _packGrid = panel ? panel.GetComponentInChildren<InventoryGrid>(true) : null;
                if (panel && ScreenRegistry.RootOf(panel) != panel)
                {
                    var p = (Transform)panel;
                    if (panel.TryGetComponent<Image>(out var own)) own.enabled = false;
                    foreach (Transform child in panel)
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
            i.GridH = EaqsBridge.VisibleRows(inv.GetHeight());
            var chest = ContainerIsBackpack() ? null : ContainerInventory();
            var pack = ContainerIsBackpack() ? ContainerInventory() : _packInventory;
            i.ChestW = chest?.GetWidth() ?? 0;
            i.ChestH = chest?.GetHeight() ?? 0;
            i.BackpackW = pack?.GetWidth() ?? 0;
            i.BackpackH = pack?.GetHeight() ?? 0;
            i.DollStorage = DollStorageCount();
            i.DollMirrors = 3;
            ReadTokens();
            _layout = InventoryMath.Compute(i, _tokens);
            _entriesRevision = -1; // rebuild doll order
            _logPlacement = 3;
            Plugin.Log.LogInfo($"[RuneSkin] inventory layout {_layout.Mode}: dock {_layout.Dock} centre {_layout.Center} crafting {_layout.Crafting} " +
                               $"grid {_layout.Player} chest {_layout.ChestGrid} backpack {_layout.BackpackGrid} doll {_layout.DollCells.Rows}r/{_layout.DollCells.VisibleRows}v" +
                               (_layout.DockOverflow ? " dock overflow" : ""));
        }

        // ---------------------------------------------------------------- panels

        private int _logPlacement;

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
            One("player", _gui.m_player, _layout.Center);
            One("grid", _gui.m_playerGrid, Inset(_layout.Grid, _tokens.PanelPad * _input.SkinScale, _tokens.PanelPad * _input.SkinScale));
            if (_gui.m_playerGrid) One("gridRoot", _gui.m_playerGrid.m_gridRoot, Inset(_layout.Grid, _tokens.PanelPad * _input.SkinScale, _tokens.PanelPad * _input.SkinScale));
            if (_gui.m_container && _gui.m_container.gameObject.activeSelf)
            {
                One("container", _gui.m_container, ContainerIsBackpack() ? _layout.Backpack : _layout.Chest);
                One("containerGrid", _gui.m_containerGrid, ContainerIsBackpack() ? _layout.Backpack : _layout.Chest);
                One("containerName", _gui.m_containerName, ContainerIsBackpack() ? _layout.Backpack : _layout.Chest);
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
            if (_gui.m_info) Geo.PlaceFit(_canvas, _gui.m_info, Inset(o.Header, pad, pad / 2f));
            // Grid box = carved hotbar header strip (digits 1-8 on it) + the grid below it.
            var headerH = _tokens.HotbarHeaderH * _input.SkinScale;
            var inner = Inset(o.Grid, pad, pad);
            var gridBox = new Box(inner.X, inner.Y + headerH, inner.W, Mathf.Max(0f, inner.H - headerH));
            if (_gui.m_playerGrid) Geo.Place(_canvas, (RectTransform)_gui.m_playerGrid.transform, gridBox);
            if (_hotbarHeader)
            {
                var w = o.Player.Columns * o.Player.Pitch - _tokens.GridGap;
                var x = gridBox.X + (gridBox.W - o.Player.Columns * o.Player.Pitch) / 2f;
                Geo.Place(_canvas, _hotbarHeader, new Box(x - 10f, inner.Y, w + 20f, headerH));
            }
            Footer(Inset(o.Footer, pad, 4f));

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
            if (_gui.m_crafting && _gui.m_crafting.gameObject.activeSelf) Geo.PlaceFit(_canvas, _gui.m_crafting, o.Crafting);

            // Player panel actions in the footer, right of the stats, left of the trash plate.
            Bar(_gui.m_player, PanelKind.Player, Player.m_localPlayer.GetInventory(), null, null, FooterActionBox());
        }

        private Box _footerActions;

        private Box FooterActionBox() => _footerActions;

        // Footer: armour, weight (vanilla plates) left, MorgottTweaks trash plate right; player actions in between.
        private void Footer(Box box)
        {
            var x = box.X;
            foreach (var text in new TMP_Text[] { _gui.m_armor, _gui.m_weight })
            {
                var plate = DirectChild(_gui.m_player, text ? text.transform : null);
                if (!plate) continue;
                var w = Geo.CanvasWidth(_canvas, plate);
                Geo.PlaceFit(_canvas, plate, new Box(x, box.Y, Mathf.Max(w, 1f), box.H));
                x += Geo.CanvasWidth(_canvas, plate) + _tokens.Gap;
            }
            var right = box.Right;
            var trash = _gui.m_player.Find("MT_Trash") as RectTransform;
            if (trash && trash.gameObject.activeSelf)
            {
                var w = Geo.CanvasWidth(_canvas, trash);
                Geo.PlaceFit(_canvas, trash, new Box(right - w, box.Y, w, box.H));
                right -= Geo.CanvasWidth(_canvas, trash) + _tokens.Gap;
            }
            _footerActions = new Box(x, box.Y, Mathf.Max(0f, right - x), box.H);
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
            if (grid) Geo.Place(_canvas, (RectTransform)grid.transform, new Box(box.X + pad, box.Y + headerH + pad, box.W - 2f * pad, box.H - headerH - 2f * pad));
            if (grid) Want(grid, fit.Pitch, fit.Cell);
            var actionsW = Bar(panel, kind, inventory, takeAll, stackAll, default);
            var weightPlate = DirectChild(panel, weight);
            var right = header.Right - actionsW;
            if (weightPlate)
            {
                var w = Geo.CanvasWidth(_canvas, weightPlate);
                Geo.PlaceFit(_canvas, weightPlate, new Box(right - w - _tokens.Gap, header.Y, w, header.H));
                right -= w + _tokens.Gap;
            }
            var glyph = HeaderGlyph(panel, kind == PanelKind.Backpack ? "glyph.panel.backpack" : "glyph.panel.chest");
            var nameX = header.X;
            if (glyph && glyph.enabled)
            {
                Geo.Place(_canvas, glyph.rectTransform, new Box(header.X, header.Y, header.H, header.H));
                nameX += header.H + _tokens.Gap / 2f;
            }
            if (name) Geo.Place(_canvas, name, new Box(nameX, header.Y, Mathf.Max(1f, right - nameX - _tokens.Gap), header.H));
            Bar(panel, kind, inventory, takeAll, stackAll, new Box(header.Right - actionsW, header.Y, actionsW, header.H));
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
            var path = SkinPath(_gui.m_container, original);
            return path == null ? null : _packPanel.Find(path);
        }

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
                Reposition(grid, visibleCount, s.Scale);
                if (rebuilt)
                {
                    var owner = ScreenRegistry.RootOf(grid.transform);
                    if (owner) ScreenRegistry.Restyle(owner);
                }
            }
            var inv = grid.GetInventory();
            if (rebuilt || s.OverlayRevision != InventoryRegistry.OverlayRevision || !ReferenceEquals(s.Inventory, inv) || s.ThemeRevision != ThemeTokens.Revision)
            {
                s.OverlayRevision = InventoryRegistry.OverlayRevision;
                s.Inventory = inv;
                s.ThemeRevision = ThemeTokens.Revision;
                Overlays(grid, inv, grid.m_elements.Count); // EAQS cells hold real inventory coordinates too
                if (isPlayer) HotbarPlates(grid);
            }
            if (isPlayer) PlaceEaqsCells(grid, s.Scale);
        }

        // Positions vanilla would give the cells with this pitch (InventoryGrid.cs:267-275), applied to the live cells.
        private static void Reposition(InventoryGrid grid, int visibleCount, float scale)
        {
            var w = grid.GetInventory().GetWidth();
            var widget = new Vector2(w * grid.m_elementSpace, 0f);
            var origin = new Vector2(((RectTransform)grid.transform).rect.width / 2f, 0f) - widget * 0.5f;
            for (var i = 0; i < grid.m_elements.Count && i < visibleCount; i++)
            {
                var e = grid.m_elements[i];
                if (!e) continue;
                var pos = e.Position;
                ((RectTransform)e.transform).anchoredPosition = origin + new Vector2(pos.x * grid.m_elementSpace, -pos.y * grid.m_elementSpace);
                Geo.SetScale(e.transform, scale);
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
            var box = Inset(_layout.Doll, _tokens.PanelPad * _input.SkinScale, _tokens.PanelPad * _input.SkinScale);
            var cols = d.StorageColumns;
            // Storage grid left, mirrors column right, carved silhouette between (layout-a2.png).
            var x0 = box.X;
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
            var mx = Mathf.Max(x0 + cols * d.Pitch - _tokens.DollGap + _tokens.DollMirrorGap, box.Right - d.Cell);
            var figureX = x0 + cols * d.Pitch - _tokens.DollGap + _tokens.DollMirrorGap;
            var figure = new Box(figureX, box.Y, Mathf.Max(0f, mx - _tokens.DollMirrorGap - figureX), Mathf.Min(box.H, Mathf.Max(3, d.Rows) * d.Pitch - _tokens.DollGap));
            if (_backdrop) Show(_backdrop, figure.W >= d.Cell ? figure : default);
            if (_silhouette) Show(_silhouette, figure.W >= d.Cell ? Inset(figure, 8f, 8f) : default);
            // Mirrors scroll with the doll rows (never over the grid when fewer rows fit).
            for (var m = 0; m < 3; m++)
            {
                var row = m - _dollScroll;
                PlaceOwnCell(m == 0 ? _right : m == 1 ? _left : _ammo, mx, box.Y + row * d.Pitch, d.Cell, row >= 0 && row < visible);
            }
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
                rt.anchorMin = icon.anchorMin;
                rt.anchorMax = icon.anchorMax;
                rt.pivot = icon.pivot;
                rt.anchoredPosition = icon.anchoredPosition;
                rt.sizeDelta = icon.sizeDelta;
                image.raycastTarget = false;
                image.preserveAspect = true;
                var role = "glyph.slot." + (type ?? "generic");
                var mark = DollCell.Mark(image, role, "empty doll cell glyph");
                mark.Outcome = Restyler.Apply(image, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
                t = rt;
            }
            else image = t.GetComponent<Image>();
            var on = !e.m_used && image.sprite;
            if (image.enabled != on) image.enabled = on;
        }

        // EAQS quick cells sit exactly over the HUD quick-slot bar (HotkeyBar element i), so a drag in the inventory
        // targets the place the slot has on the HUD; the HUD bar itself is hidden while the inventory is open.
        private void PlaceQuickCells(InventoryGrid grid)
        {
            var bar = QuickBar();
            if (!bar) return;
            var q = 0;
            foreach (var s in _eaqs)
            {
                if (s.Kind != EaqsBridge.Kind.Quick || s.Element >= grid.m_elements.Count) continue;
                var e = grid.m_elements[s.Element];
                if (!e || q >= bar.childCount) { q++; continue; }
                var hud = bar.GetChild(q++) as RectTransform;
                if (!hud) continue;
                var rt = (RectTransform)e.transform;
                var hudW = Geo.CanvasWidth(_canvas, hud);
                var native = rt.rect.width;
                var parentUnits = rt.parent ? rt.parent.lossyScale.x / Mathf.Max(Geo.WorldPerUnit(_canvas), 1e-6f) : 1f;
                if (native > 1f && parentUnits > 0f) Geo.SetScale(rt, hudW / (native * parentUnits));
                var b = Geo.BoxOf(_canvas, hud);
                Geo.SetTopLeft(rt, Geo.World(_canvas, b.X, b.Y));
            }
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
            if (_quickGroup.alpha != 0f) _quickGroup.alpha = 0f;
        }

        private void RestoreQuickBar()
        {
            if (_quickGroup && _quickGroup.alpha != 1f) _quickGroup.alpha = 1f;
        }

        private void DollScrollInput()
        {
            if (_layout == null || !_layout.DollCells.Scroll) return;
            var wheel = ZInput.GetMouseScrollWheel();
            if (Mathf.Approximately(wheel, 0f)) return;
            var mouse = ZInput.pointerPosition;
            var tl = Geo.World(_canvas, _layout.Doll.X, _layout.Doll.Y);
            var br = Geo.World(_canvas, _layout.Doll.Right, _layout.Doll.Bottom);
            if (mouse.x < tl.x || mouse.x > br.x || mouse.y > tl.y || mouse.y < br.y) return;
            _dollScroll += wheel < 0f ? 1 : -1;
        }

        // ---------------------------------------------------------------- mirrors

        private void UpdateMirrors()
        {
            var p = Player.m_localPlayer;
            if (!p || !_right) return;
            var right = p.m_rightItem;
            var left = p.m_leftItem;
            var twoRight = right != null && right.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon;
            var twoLeft = left != null && left.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft;
            // Two-handed: both mirrors show the weapon, the hand that does not hold it dimmed; both link frames lit.
            _right.Show(twoLeft ? left : right, twoLeft, 0);
            _left.Show(twoRight ? right : left, twoRight, 0);
            if (twoRight && _right.Link.sprite) _right.Link.enabled = true;
            if (twoLeft && _left.Link.sprite) _left.Link.enabled = true;
            var ammo = p.m_ammoItem;
            _ammo.Show(ammo, false, ammo != null ? p.GetInventory().CountItems(ammo.m_shared.m_name) : 0);
            foreach (var r in InventoryRegistry.Slots)
            {
                if (r.Dead || r.D.GetItem == null || !_registered.TryGetValue(r.Key, out var cell) || !cell) continue;
                var reg = r;
                cell.Show(InventoryRegistry.Guard(reg, () => reg.D.GetItem(), null), false, 0);
            }
        }

        private ItemDrop.ItemData MirrorItem(DollCell c)
        {
            var p = Player.m_localPlayer;
            if (!p) return null;
            if (c == _ammo) return p.m_ammoItem;
            var right = p.m_rightItem;
            var left = p.m_leftItem;
            if (c == _right) return right ?? (left != null && left.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft ? left : null);
            if (c == _left) return left ?? (right != null && right.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ? right : null);
            return null;
        }

        // Click: drag held -> drop on the mirror (equip); otherwise highlight + reveal the source cell.
        private void MirrorPrimary(DollCell c)
        {
            if (_gui.m_dragGo) { DropOnMirror(c); return; }
            var item = MirrorItem(c);
            if (item == null) return;
            var grid = _gui.m_playerGrid;
            foreach (var e in grid.m_elements)
            {
                if (!e || e.Position != item.m_gridPos) continue;
                var rt = (RectTransform)e.transform;
                _focus.gameObject.SetActive(true);
                _focus.SetAsLastSibling();
                var b = Geo.BoxOf(_canvas, rt);
                Geo.Place(_canvas, _focus, new Box(b.X - 3f, b.Y - 3f, b.W + 6f, b.H + 6f));
                _focusUntil = Time.unscaledTime + 1.5f;
                break;
            }
        }

        // Drop: the held grid item goes through the normal equip path (Humanoid.UseItem -> Player.ToggleEquipped,
        // equip queue / duration, Player.cs:7265); the item keeps its grid position. Drag cleared only when accepted.
        private void DropOnMirror(DollCell c)
        {
            var p = Player.m_localPlayer;
            var item = _gui.m_dragItem;
            if (!p || item == null || !_gui.m_dragGo) return;
            var inv = p.GetInventory();
            if (_gui.m_dragInventory != inv || !inv.ContainsItem(item)) return;
            if (!FitsMirror(c, item)) return;
            if (!item.m_equipped && !p.IsEquipActionQueued(item)) p.UseItem(inv, item, true);
            if (item.m_equipped || p.IsEquipActionQueued(item)) _gui.SetupDragItem(null, null, 1);
        }

        private bool FitsMirror(DollCell c, ItemDrop.ItemData item)
        {
            if (!item.IsEquipable()) return false;
            var t = item.m_shared.m_itemType;
            if (c == _ammo) return t == ItemDrop.ItemData.ItemType.Ammo || t == ItemDrop.ItemData.ItemType.AmmoNonEquipable;
            if (c == _left) return t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Torch || t == ItemDrop.ItemData.ItemType.Bow ||
                                   t == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || t == ItemDrop.ItemData.ItemType.TwoHandedWeapon;
            return t == ItemDrop.ItemData.ItemType.OneHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeapon || t == ItemDrop.ItemData.ItemType.Tool ||
                   t == ItemDrop.ItemData.ItemType.Torch || t == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || t == ItemDrop.ItemData.ItemType.Bow;
        }

        // Right click / gamepad: unequip through the same queue; the item stays where it is in the grid.
        private void MirrorUnequip(DollCell c)
        {
            var p = Player.m_localPlayer;
            var item = MirrorItem(c);
            if (!p || item == null || !item.m_equipped) return;
            p.UseItem(p.GetInventory(), item, true);
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
