using System.Collections.Generic;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.InventoryScreen;
using RuneSkin.Ownership;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.InfoScreens
{
    // Family "Trophies" (trophies-a.png): the vanilla trophies panel (root/Trophies/TrophiesFrame) as a RuneSkin panel on
    // the right of layout A: title + "unlocked / total" count, a boss strip, then one wrapping grid of every trophy item
    // (unlocked bright, locked as a dark icon). Vanilla lists only unlocked trophies at hand-placed m_trophyPos spots
    // (InventoryGui.cs:2381-2423); the grid keeps that order (m_trophyPos y, x) for every trophy in ObjectDB, so modded
    // trophies appear too. Unlocked trophies keep their vanilla element (repositioned); locked ones are pooled own cells.
    // Bosses are data-driven: trophies dropped by a prefab whose Character.m_boss is set (any count, rows wrap).
    public sealed class TrophiesSkin : MonoBehaviour
    {
        public const string ScreenId = "Trophies";
        public static TrophiesSkin Instance { get; private set; }

        // Open = the panel is shown; its InfoTabs row then replaces the inventory header tab row (InfoMath.HeaderTabsShown).
        public bool IsOpen => _root && _root.gameObject.activeInHierarchy;

        private InventoryGui _gui;
        private RectTransform _root, _frame, _canvas, _strip;
        private Image _stripWell, _knotL, _knotR;
        private TMP_Text _count;
        private InfoTabs _tabs;
        private GameObject _tooltipPrefab;
        private readonly List<ItemDrop> _all = new List<ItemDrop>();
        private readonly HashSet<string> _bosses = new HashSet<string>();
        private readonly List<Cell> _locked = new List<Cell>();
        private readonly List<Cell> _bossCells = new List<Cell>();
        private readonly List<(ItemDrop Item, RectTransform Element, Cell Own)> _grid = new List<(ItemDrop, RectTransform, Cell)>();
        private readonly List<(ItemDrop Item, bool Unlocked)> _bossList = new List<(ItemDrop, bool)>();
        private bool _catalogued;

        private sealed class Cell
        {
            public RectTransform Root;
            public Image Back, Icon, Badge;
            public TMP_Text Label;
            public UITooltip Tooltip;
        }

        public static void Attach(InventoryGui gui)
        {
            if (!gui || !gui.m_trophiesPanel || gui.GetComponent<TrophiesSkin>()) return;
            gui.gameObject.AddComponent<TrophiesSkin>().Init(gui);
        }

        private void Init(InventoryGui gui)
        {
            Instance = this;
            _gui = gui;
            _root = (RectTransform)gui.m_trophiesPanel.transform;
            _frame = _root.Find("TrophiesFrame") as RectTransform ?? _root;
            var canvas = gui.GetComponentInParent<Canvas>();
            _canvas = canvas ? (RectTransform)canvas.rootCanvas.transform : null;
            var element = gui.m_playerGrid ? gui.m_playerGrid.m_elementPrefab : null;
            var tip = element ? element.GetComponent<InventoryElement>() : null;
            _tooltipPrefab = tip && tip.m_tooltip ? tip.m_tooltip.m_tooltipPrefab : null;
            foreach (var c in Children.Of(_frame))
                if (c.name.StartsWith("border", System.StringComparison.Ordinal) && c.TryGetComponent<Image>(out var b)) b.enabled = false;
            PanelDecor.Ensure(_frame, "size.panel.glass.inset", glass: true, plaque: false);
            _strip = (RectTransform)new GameObject("RuneSkinBossStrip", typeof(RectTransform)).transform;
            _strip.SetParent(_frame, false);
            _stripWell = InfoArt.OwnImage(_strip, "Well", "craft.well", "boss strip well (frame_inset)");
            _stripWell.preserveAspect = false;
            _count = InfoArt.NewText("RuneSkinTrophyCount", _frame, "text.body");
            _count.raycastTarget = false;
            var mark = DollCell.Mark(_count, "text.body", "unlocked / total trophies");
            mark.Outcome = Restyler.Apply(_count, mark, ThemeTokens.Role("text.body")) ? Outcome.Styled : Outcome.Degraded;
            _knotL = InfoArt.OwnImage(_frame, "RuneSkinTrophiesKnotL", "ornament.knot", "title ornament (knot)");
            _knotR = InfoArt.OwnImage(_frame, "RuneSkinTrophiesKnotR", "ornament.knot", "title ornament (knot)");
            _tabs = new InfoTabs(gui, _frame, "OnOpenTrophies", () => _gui.OnCloseTrophies());
            ScreenRegistry.Register(_root, Context);
            ScreenRegistry.Restyle(_root);
            Plugin.Log.LogInfo("[RuneSkin] trophies panel attached");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private ScreenContext Context()
        {
            var ctx = new ScreenContext(ScreenId, OwnershipRegistry.Trophies);
            foreach (var image in _root.GetComponentsInChildren<Image>(true))
            {
                var t = image.transform;
                if (image.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                if (t.parent == _frame && t.name.StartsWith("border", System.StringComparison.Ordinal)) ctx.Role(image, "skip", "vanilla trophies frame (replaced by the panel stack)");
                else if (t.name == "darken" || t.name == "blur") ctx.Role(image, "skip", "vanilla modal dim / blur (kept)");
                else if (t.name == "icon_bkg") ctx.Role(image, "slot.recess", "trophy cell (slot_cell_recess)");
                else if (t.name == "bkg" && t.parent && t.parent.name.StartsWith("TrophyElement", System.StringComparison.Ordinal)) ctx.Role(image, "skip", "vanilla trophy card strip (grid cells only, trophies-a)");
                else if (t.name == "icon") ctx.Role(image, "content", "trophy icon");
                else if (!image.sprite) ctx.Role(image, "skip", "sprite-less vanilla container image (list area), kept");
            }
            foreach (var text in _root.GetComponentsInChildren<TMP_Text>(true))
                if (text.transform.name == "topic" && text.transform.parent == _frame) ctx.Role(text, "info.title", "panel title (serif)");
            return ctx;
        }

        // Catalogue once per session: every trophy item, the boss set (data-driven from boss prefabs' drops).
        private void Catalogue()
        {
            if (_catalogued || !ObjectDB.instance || !ZNetScene.instance) return;
            _catalogued = true;
            foreach (var go in ObjectDB.instance.m_items)
            {
                var drop = go ? go.GetComponent<ItemDrop>() : null;
                if (drop && drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy) _all.Add(drop);
            }
            _all.Sort((a, b) =>
            {
                var pa = a.m_itemData.m_shared.m_trophyPos;
                var pb = b.m_itemData.m_shared.m_trophyPos;
                return pa.y != pb.y ? pa.y.CompareTo(pb.y) : pa.x != pb.x ? pa.x.CompareTo(pb.x) : string.CompareOrdinal(a.name, b.name);
            });
            foreach (var go in ZNetScene.instance.m_prefabs)
            {
                if (!go || !go.TryGetComponent<Character>(out var ch) || !ch.m_boss || !go.TryGetComponent<CharacterDrop>(out var drops)) continue;
                foreach (var d in drops.m_drops)
                    if (d.m_prefab && d.m_prefab.TryGetComponent<ItemDrop>(out var item) && item.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy)
                        _bosses.Add(d.m_prefab.name);
            }
            Plugin.Log.LogInfo($"[RuneSkin] trophies: {_all.Count} trophy items, {_bosses.Count} boss trophies ({string.Join(",", _bosses)})");
        }

        // InventoryGui.UpdateTrophyList postfix: map vanilla elements to trophies, build locked + boss cells.
        public void AfterUpdateList()
        {
            Catalogue();
            var player = Player.m_localPlayer;
            if (!player) return;
            // Same walk as vanilla (:2392-2417): element i belongs to the i-th trophy whose prefab exists.
            var byName = new Dictionary<string, RectTransform>();
            var names = player.GetTrophies();
            var n = 0;
            foreach (var trophy in names)
            {
                if (!ObjectDB.instance.GetItemPrefab(trophy)) continue;
                if (n < _gui.m_trophyList.Count && _gui.m_trophyList[n]) byName[trophy] = (RectTransform)_gui.m_trophyList[n].transform;
                n++;
            }
            _grid.Clear();
            _bossList.Clear();
            var lockedUsed = 0;
            foreach (var item in _all)
            {
                byName.TryGetValue(item.name, out var element);
                if (_bosses.Contains(item.name))
                {
                    _bossList.Add((item, element != null));
                    if (element) Geo.SetScale(element, 0f); // shown in the strip
                    continue;
                }
                Cell own = null;
                if (!element)
                {
                    if (lockedUsed == _locked.Count) _locked.Add(MakeCell(_gui.m_trophieListRoot, "RuneSkinTrophyLocked" + lockedUsed, false));
                    own = _locked[lockedUsed++];
                    Fill(own, item, false);
                }
                else Tooltip(element, item, true);
                _grid.Add((item, element, own));
            }
            for (var i = 0; i < _locked.Count; i++)
                if (_locked[i].Root.gameObject.activeSelf != i < lockedUsed) _locked[i].Root.gameObject.SetActive(i < lockedUsed);
            for (var i = 0; i < _bossList.Count; i++)
            {
                if (i == _bossCells.Count) _bossCells.Add(MakeCell(_strip, "RuneSkinBoss" + i, true));
                Fill(_bossCells[i], _bossList[i].Item, _bossList[i].Unlocked);
            }
            for (var i = 0; i < _bossCells.Count; i++)
                if (_bossCells[i].Root.gameObject.activeSelf != i < _bossList.Count) _bossCells[i].Root.gameObject.SetActive(i < _bossList.Count);
            var unlocked = 0;
            foreach (var item in _all) if (byName.ContainsKey(item.name)) unlocked++;
            var title = _frame.Find("topic")?.GetComponent<TMP_Text>();
            _count.text = $"{(title ? title.text : "")}: {unlocked} / {_all.Count}";
            ScreenRegistry.Restyle(_root);
            Layout();
        }

        private Cell MakeCell(Transform parent, string name, bool boss)
        {
            var c = new Cell { Root = (RectTransform)new GameObject(name, typeof(RectTransform)).transform };
            c.Root.SetParent(parent, false);
            c.Back = InfoArt.OwnImage(c.Root, "Back", "slot.recess", "trophy cell");
            c.Back.preserveAspect = false;
            c.Back.raycastTarget = true;
            c.Icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<Image>();
            c.Icon.rectTransform.SetParent(c.Root, false);
            c.Icon.raycastTarget = false;
            c.Icon.preserveAspect = true;
            DollCell.Mark(c.Icon, "content", "trophy icon");
            if (boss)
            {
                c.Badge = InfoArt.OwnImage(c.Root, "Badge", "info.glyph.boss", "boss trophy badge (glyph_boss)");
                c.Label = InfoArt.RowText(c.Root, "Name", "text.body", TextAlignmentOptions.Center);
            }
            if (_tooltipPrefab)
            {
                c.Tooltip = c.Root.gameObject.AddComponent<UITooltip>();
                c.Tooltip.m_tooltipPrefab = _tooltipPrefab;
            }
            return c;
        }

        private static string Name(ItemDrop item)
        {
            var text = Localization.instance.Localize(item.m_itemData.m_shared.m_name);
            // Vanilla drops the English " trophy" suffix (InventoryGui.cs:2410); other languages prefix it ("Трофей: X").
            if (text.EndsWith(" trophy", System.StringComparison.Ordinal)) return text.Substring(0, text.Length - 7);
            var colon = text.IndexOf(": ", System.StringComparison.Ordinal);
            return colon > 0 && colon < text.Length - 2 ? text.Substring(colon + 2) : text;
        }

        private void Fill(Cell c, ItemDrop item, bool unlocked)
        {
            c.Icon.sprite = item.m_itemData.GetIcon();
            // Locked: the trophy's own icon as a dark silhouette (no per-trophy silhouette art, art README).
            c.Icon.color = unlocked ? Color.white : new Color(0.3f, 0.34f, 0.38f, 0.8f);
            if (c.Badge) c.Badge.enabled = unlocked && c.Badge.sprite;
            if (c.Back.GetComponent<SkinMark>() is SkinMark m)
            {
                var role = c.Badge && unlocked ? "trophy.boss" : "slot.recess";
                if (m.Role != role)
                {
                    m.Role = role;
                    m.Outcome = Restyler.Apply(c.Back, m, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
                }
            }
            if (c.Label) c.Label.text = unlocked ? Name(item) : "???";
            if (c.Tooltip) c.Tooltip.Set(unlocked ? Name(item) : "???", unlocked ? Localization.instance.Localize(item.m_itemData.m_shared.m_name + "_lore") : "");
        }

        // Unlocked vanilla element: name / lore move into a tooltip (grid shows icons only, trophies-a).
        private void Tooltip(RectTransform element, ItemDrop item, bool unlocked)
        {
            foreach (var t in element.GetComponentsInChildren<TMP_Text>(true))
                if (t.enabled) t.enabled = false;
            var strip = element.Find("bkg");
            if (strip && strip.TryGetComponent<Image>(out var si) && si.enabled) si.enabled = false;
            if (!_tooltipPrefab) return;
            var tip = element.GetComponent<UITooltip>() ?? element.gameObject.AddComponent<UITooltip>();
            if (!tip.m_tooltipPrefab) tip.m_tooltipPrefab = _tooltipPrefab;
            tip.Set(Name(item), unlocked ? Localization.instance.Localize(item.m_itemData.m_shared.m_name + "_lore") : "");
            var bg = element.Find("icon_bkg");
            if (bg && bg.TryGetComponent<Image>(out var img) && !img.raycastTarget) img.raycastTarget = true;
        }

        private void LateUpdate()
        {
            if (!_root.gameObject.activeInHierarchy) return;
            Layout();
        }

        private void Layout()
        {
            var inv = InventorySkin.Instance;
            if (!inv || !_canvas) return;
            var k = inv.SkinScaleValue;
            var box = InfoArt.RightPanel(inv, ThemeTokens.GetFloat("size.info.trophies.w", 1000f) * k);
            if (box.IsEmpty) return;
            Geo.SetScale(_frame, 1f);
            Geo.Place(_canvas, _frame, box);
            var pad = ThemeTokens.GetFloat("size.info.pad", 22f) * k;
            var inner = new Box(box.X + pad, box.Y + pad, box.W - 2f * pad, box.H - 2f * pad);
            var headerH = ThemeTokens.GetFloat("size.info.header", 64f) * k;
            var gap = ThemeTokens.GetFloat("size.info.trophy.gap", 8f) * k;
            // Title band = a drag band of the docked inventory group (this panel docks over its columns).
            var band = WindowMove.Handle(_frame, WindowMove.Inventory);
            if (band) Geo.Place(_canvas, band.rectTransform, new Box(inner.X, inner.Y, inner.W, headerH));
            foreach (var c in Children.Of(_frame))
            {
                if (!(c is RectTransform rt) || !c.gameObject.activeSelf) continue;
                if (c.name == "topic" && c.TryGetComponent<TMP_Text>(out var title))
                {
                    Geo.Place(_canvas, rt, new Box(inner.X, inner.Y, inner.W, headerH));
                    InfoArt.Label(title, TextAlignmentOptions.Center, headerH * 0.55f, 16f, true);
                    InfoArt.TitleKnots(_canvas, title, new Box(inner.X, inner.Y, inner.W, headerH), _knotL, _knotR);
                }
                else if (c.name == "Closebutton") Geo.SetScale(rt, 0f); // the selected tab closes (mockup: no close button)
            }
            Geo.Place(_canvas, _count.rectTransform, new Box(inner.Right - inner.W * 0.26f - 8f * k, inner.Y, inner.W * 0.26f, headerH));
            InfoArt.Label(_count, TextAlignmentOptions.MidlineRight, headerH * 0.34f, 13f);
            var y = inner.Y + headerH;
            var tabsH = ThemeTokens.GetFloat("size.info.tabs", 70f) * k;
            _tabs.Layout(_canvas, new Box(inner.X, y, inner.W, tabsH), k);
            y += tabsH + gap;

            // Boss strip: cells + name labels, wrapping rows (any boss count).
            var bc = ThemeTokens.GetFloat("size.info.boss.cell", 112f) * k;
            var bl = ThemeTokens.GetFloat("size.info.boss.label", 26f) * k;
            var shown = 0;
            foreach (var c in _bossCells) if (c.Root.gameObject.activeSelf) shown++;
            if (shown > 0)
            {
                var sp = 10f * k;
                // One row while the cells can shrink to 70% (trophies-a: one strip); past that the strip wraps.
                var fit = (inner.W - 2f * sp - (shown - 1) * gap) / shown;
                if (fit < bc) bc = Mathf.Max(bc * 0.7f, fit);
                var cols = Mathf.Max(1, Mathf.Min(shown, Mathf.FloorToInt((inner.W - 2f * sp + gap) / (bc + gap))));
                var rows = (shown + cols - 1) / cols;
                var stripH = rows * (bc + bl + gap) - gap + 2f * sp;
                Geo.Place(_canvas, _strip, new Box(inner.X, y, inner.W, stripH));
                InfoArt.Rect(_stripWell.rectTransform, 0f, 0f, inner.W, stripH, stripH);
                var rowW = cols * (bc + gap) - gap;
                for (var i = 0; i < shown; i++)
                {
                    var c = _bossCells[i];
                    var col = i % cols;
                    var row = i / cols;
                    var inRow = Mathf.Min(cols, shown - row * cols);
                    var x0 = (inner.W - (inRow * (bc + gap) - gap)) / 2f;
                    _ = rowW;
                    InfoArt.Rect(c.Root, x0 + col * (bc + gap), sp + row * (bc + bl + gap), bc, bc + bl, stripH);
                    InfoArt.Rect(c.Back.rectTransform, 0f, 0f, bc, bc, bc + bl);
                    InfoArt.Rect(c.Icon.rectTransform, bc * 0.05f, bc * 0.05f, bc * 0.9f, bc * 0.9f, bc + bl);
                    if (c.Badge) InfoArt.Rect(c.Badge.rectTransform, bc * 0.72f, bc * 0.04f, bc * 0.24f, bc * 0.24f, bc + bl);
                    if (c.Label)
                    {
                        InfoArt.Rect(c.Label.rectTransform, -gap / 2f, bc, bc + gap, bl, bc + bl);
                        InfoArt.Label(c.Label, TextAlignmentOptions.Center, bl * 0.62f, 11f);
                    }
                }
                y += stripH + gap;
            }
            else if (_strip.gameObject.activeSelf) Geo.Place(_canvas, _strip, new Box(inner.X, y, inner.W, 0f));

            // Grid: the scroll view fills the rest; cells fill its width (columns from the cell token).
            var view = _gui.m_trophieListRoot.GetComponentInParent<ScrollRect>(true);
            var vr = view ? (RectTransform)view.transform : null;
            var bar = _gui.m_trophyListScroll ? (RectTransform)_gui.m_trophyListScroll.transform : null;
            var barW = 16f * k;
            var grid = new Box(inner.X, y, inner.W, inner.Bottom - y);
            if (vr) Geo.Place(_canvas, vr, new Box(grid.X, grid.Y, grid.W - barW - 6f * k, grid.H));
            if (bar) Geo.Place(_canvas, bar, new Box(grid.Right - barW, grid.Y, barW, grid.H));
            var viewW = vr ? vr.rect.width : grid.W;
            var cell = ThemeTokens.GetFloat("size.info.trophy.cell", 96f) * k;
            var columns = Mathf.Max(1, Mathf.FloorToInt((viewW + gap) / (cell + gap)));
            cell = (viewW - (columns - 1) * gap) / columns;
            for (var i = 0; i < _grid.Count; i++)
            {
                var (item, element, own) = _grid[i];
                var x = i % columns * (cell + gap);
                var yy = i / columns * (cell + gap);
                if (element)
                {
                    InfoArt.Rect(element, x, yy, cell, cell, cell);
                    foreach (var ch in Children.Of(element))
                        if (ch is RectTransform cr && ch.name == "icon_bkg")
                        {
                            InfoArt.Rect(cr, 0f, 0f, cell, cell, cell);
                            if (cr.Find("icon") is RectTransform ic) InfoArt.Rect(ic, cell * 0.1f, cell * 0.1f, cell * 0.8f, cell * 0.8f, cell);
                        }
                }
                else if (own != null)
                {
                    InfoArt.Rect(own.Root, x, yy, cell, cell, cell);
                    InfoArt.Rect(own.Back.rectTransform, 0f, 0f, cell, cell, cell);
                    InfoArt.Rect(own.Icon.rectTransform, cell * 0.1f, cell * 0.1f, cell * 0.8f, cell * 0.8f, cell);
                }
                _ = item;
            }
            var rowsN = (_grid.Count + columns - 1) / columns;
            var height = Mathf.Max(vr ? vr.rect.height : 0f, rowsN * (cell + gap));
            if (Mathf.Abs(_gui.m_trophieListRoot.rect.height - height) > 0.5f)
                _gui.m_trophieListRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
    }
}
