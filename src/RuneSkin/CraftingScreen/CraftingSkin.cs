using System;
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

namespace RuneSkin.CraftingScreen
{
    // Family "Crafting" (M3, mockup-1 crafting column = right column of layout A). Owns the look and the geometry of
    // InventoryGui.m_crafting inside the box InventoryMath gives (same top / bottom / gutters as dock and centre).
    // Reposition, never rebuild: every vanilla child keeps its parent and name, because mods find them by path -
    // VNEI (Crafting/RecipeList, Crafting/Decription, its tab clone under TabsButtons), Epic Loot (row "icon" +
    // sibling "magicItemIconBG", RepairButton/Glow), ValheimPlus CraftFromChest (SetupRequirement prefix: res_icon /
    // res_name / res_amount), MorgottTweaks CraftingFilter (MT_CraftFilter under RecipeList) and RecipeChains
    // (MT_ChainTree docks above the highest active requirement cell). Geometry runs in LateUpdate, after the
    // InventoryGui animator and every Update-phase writer, change-only (Geo.Place).
    public sealed class CraftingSkin : MonoBehaviour
    {
        public const string ScreenId = "Crafting";
        private const string Prefix = "RuneSkinCraft";

        public static CraftingSkin Instance { get; private set; }

        private InventoryGui _gui;
        private RectTransform _crafting, _canvas, _host;
        private Canvas _rootCanvas;
        private Image _listWell, _detailWell, _craftGlyph;
        private RowArrows _tabArrows, _catArrows;
        private readonly CraftingTokens _t = new CraftingTokens();
        private readonly CraftingInput _in = new CraftingInput();
        private CraftingLayout _layout;
        private int _hash;
        private int _tabOffset, _catOffset;
        private bool _frameReady;
        private int _rowCount = -1, _rowFirst, _rowsTheme = -1;
        private float _rowPitch = -1f;
        private int _reqCells = -1;
        private readonly List<RectTransform> _tabs = new List<RectTransform>(8);
        private readonly List<RectTransform> _cats = new List<RectTransform>(16);
        private readonly List<int> _reqActive = new List<int>(8);
        private readonly Dictionary<GameObject, float> _redAt = new Dictionary<GameObject, float>();
        private readonly Dictionary<GameObject, bool> _missingShown = new Dictionary<GameObject, bool>();

        public static void Attach(InventoryGui gui)
        {
            if (!gui || !gui.m_crafting || gui.GetComponent<CraftingSkin>()) return;
            gui.gameObject.AddComponent<CraftingSkin>().Init(gui);
        }

        private void Init(InventoryGui gui)
        {
            Instance = this;
            _gui = gui;
            _crafting = gui.m_crafting;
            _rootCanvas = gui.GetComponentInParent<Canvas>();
            if (_rootCanvas) _rootCanvas = _rootCanvas.rootCanvas;
            _canvas = _rootCanvas ? (RectTransform)_rootCanvas.transform : null;
            HideVanilla();
            BuildOwn();
            ScreenRegistry.Register(_crafting, Context);
            ScreenRegistry.Restyle(_crafting);
            Plugin.Log.LogInfo("[RuneSkin] crafting column attached");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- build

        // Vanilla backgrounds, frames and dividers of the crafting panel: replaced by the panel stack and the two wells.
        private void HideVanilla()
        {
            if (_crafting.TryGetComponent<Image>(out var own)) own.enabled = false;
            foreach (Transform child in _crafting)
                if (IsVanillaBackground(child))
                    foreach (var image in child.GetComponentsInChildren<Image>(true))
                        image.enabled = false;
            var tabs = TabsRoot();
            var border = tabs ? tabs.Find("TabBorder") : null;
            if (border && border.TryGetComponent<Image>(out var b)) b.enabled = false;
            foreach (var t in new[] { _gui.m_repairPanel, _gui.m_repairPanelSelection })
                if (t && t.TryGetComponent<Image>(out var r)) r.enabled = false;
            // Wells replace the vanilla text backdrops; the scroll view keeps its raycast (drag / wheel) but draws nothing.
            var detail = _crafting.Find("Decription");
            if (detail && detail.TryGetComponent<Image>(out var di)) di.enabled = false;
            var req = Requirements();
            if (req && req.TryGetComponent<Image>(out var ri)) ri.enabled = false;
            var view = ListView();
            if (view && view.TryGetComponent<Image>(out var vi)) vi.color = Color.clear;
            var repairText = _gui.m_repairButton ? _gui.m_repairButton.transform.Find("Text") : null;
            if (repairText) repairText.gameObject.SetActive(false);
        }

        private static bool IsVanillaBackground(Transform t) =>
            t.name == "Bkg" || t.name == "Darken" || t.name == "selected_frame" || t.name == "TitlePanel" ||
            t.name.StartsWith("BraidLineHorisontalMedium", StringComparison.Ordinal);

        private void BuildOwn()
        {
            // Panel stack host (glass + carved frame + corner caps) beside the inventory's hosts, behind every panel.
            var decor = _gui.m_inventoryRoot ? _gui.m_inventoryRoot.Find("RuneSkinInventory") as RectTransform : null;
            _host = (RectTransform)new GameObject("RuneSkinInventory_Crafting", typeof(RectTransform)).transform;
            _host.SetParent(decor ? decor : _gui.m_inventoryRoot, false);
            if (!decor) _host.SetAsFirstSibling();
            PanelDecor.Ensure(_host, "size.panel.glass.inset", glass: true, plaque: false);
            _host.gameObject.SetActive(false);
            var detail = _crafting.Find("Decription");
            _listWell = OwnImage(Prefix + "ListWell", "craft.well", "recipe list well (frame_inset)", 0);
            _detailWell = OwnImage(Prefix + "DetailWell", "craft.well", "detail well (frame_inset)", detail ? detail.GetSiblingIndex() : 1);
            // Craft / upgrade glyph left of the craft button's label (art: glyph_act_craft / _upgrade on button_*).
            if (_gui.m_craftButton)
            {
                _craftGlyph = new GameObject(Prefix + "ButtonGlyph", typeof(RectTransform)).AddComponent<Image>();
                _craftGlyph.rectTransform.SetParent(_gui.m_craftButton.transform, false);
                _craftGlyph.raycastTarget = false;
                _craftGlyph.preserveAspect = true;
                var mark = DollCell.Mark(_craftGlyph, "craft.glyph.craft", "craft button glyph");
                mark.Outcome = Restyler.Apply(_craftGlyph, mark, ThemeTokens.Role(mark.Role)) ? Outcome.Styled : Outcome.Degraded;
                _craftGlyph.enabled = mark.Outcome == Outcome.Styled;
            }
            _tabArrows = new RowArrows(_crafting, Prefix + "Tab", () => _tabOffset--, () => _tabOffset++);
            _catArrows = new RowArrows(_crafting, Prefix + "Category", () => _catOffset--, () => _catOffset++);
        }

        private Image OwnImage(string name, string role, string reason, int sibling)
        {
            var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            image.rectTransform.SetParent(_crafting, false);
            image.rectTransform.SetSiblingIndex(Mathf.Clamp(sibling, 0, _crafting.childCount - 1));
            image.raycastTarget = false;
            var mark = DollCell.Mark(image, role, reason);
            mark.Outcome = Restyler.Apply(image, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            image.enabled = mark.Outcome == Outcome.Styled;
            return image;
        }

        // Prev / next arrows of a scrolled row (pooled pair, own glyph buttons).
        private sealed class RowArrows
        {
            public readonly RectTransform Prev, Next;

            public RowArrows(RectTransform parent, string name, Action prev, Action next)
            {
                Prev = Make(parent, name + "Prev", "craft.arrow.left", prev);
                Next = Make(parent, name + "Next", "craft.arrow.right", next);
            }

            private static RectTransform Make(RectTransform parent, string name, string role, Action click)
            {
                var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
                image.rectTransform.SetParent(parent, false);
                image.preserveAspect = true;
                var mark = DollCell.Mark(image, role, "row scroll arrow");
                mark.Outcome = Restyler.Apply(image, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
                if (mark.Outcome != Outcome.Styled) image.color = new Color(0.89f, 0.9f, 0.91f, 0.8f); // degraded: plain hit area
                var button = image.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.onClick.AddListener(() => click());
                image.gameObject.SetActive(false);
                return image.rectTransform;
            }

            public void Show(RectTransform canvas, RowFit f)
            {
                Set(canvas, Prev, f.Scroll ? f.Prev : default, f.Scroll && f.First > 0);
                Set(canvas, Next, f.Scroll ? f.Next : default, f.Scroll && f.First + f.Visible < f.Cells.Length);
            }

            private static void Set(RectTransform canvas, RectTransform rt, Box b, bool enabled)
            {
                var on = !b.IsEmpty;
                if (rt.gameObject.activeSelf != on) rt.gameObject.SetActive(on);
                if (!on) return;
                Geo.Place(canvas, rt, b);
                var button = rt.GetComponent<Button>();
                if (button.interactable != enabled) button.interactable = enabled;
                var image = rt.GetComponent<Image>();
                var a = enabled ? 1f : 0.3f;
                if (!Mathf.Approximately(image.canvasRenderer.GetAlpha(), a)) image.canvasRenderer.SetAlpha(a);
            }
        }

        // ---------------------------------------------------------------- lookups

        private bool RepairShown() => _gui.m_repairButton && _gui.m_repairButton.gameObject.activeSelf && _gui.m_repairButton.interactable;

        private RectTransform TabsRoot() => _gui.m_tabCraft ? _gui.m_tabCraft.transform.parent as RectTransform : null;
        private RectTransform ListView() => _gui.m_recipeListScroll ? FindScrollRect() : null;
        private RectTransform RecipeList() => _crafting.Find("RecipeList") as RectTransform;
        private RectTransform Requirements() =>
            _gui.m_recipeRequirementList != null && _gui.m_recipeRequirementList.Length > 0 && _gui.m_recipeRequirementList[0]
                ? _gui.m_recipeRequirementList[0].transform.parent as RectTransform : null;

        private RectTransform FindScrollRect()
        {
            var sr = _gui.m_recipeListRoot ? _gui.m_recipeListRoot.GetComponentInParent<ScrollRect>(true) : null;
            return sr ? (RectTransform)sr.transform : null;
        }

        private RectTransform Filter()
        {
            var list = RecipeList();
            var f = list ? list.Find("MT_CraftFilter") as RectTransform : null;
            return f && f.gameObject.activeInHierarchy ? f : null;
        }

        // ---------------------------------------------------------------- classification

        private ScreenContext Context()
        {
            var ctx = new ScreenContext(ScreenId, OwnershipRegistry.Crafting);
            var vnei = _crafting.Find("VNEI(Clone)");
            if (vnei) ctx.Exclude(vnei, "VNEI item browser panel: mod panel (M7), placed over the body by RuneSkin");
            foreach (var image in _crafting.GetComponentsInChildren<Image>(true))
            {
                var t = image.transform;
                if (image.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                if (t == _crafting || IsUnder(t, IsVanillaBackground)) ctx.Role(image, "skip", "vanilla crafting background / frame (replaced by the panel stack)");
                else if (t.name == "TabBorder") ctx.Role(image, "skip", "vanilla tab divider (hidden)");
                else if (_gui.m_repairPanel && t == _gui.m_repairPanel || _gui.m_repairPanelSelection && t == _gui.m_repairPanelSelection) ctx.Role(image, "skip", "vanilla repair plate (hidden, round repair button)");
                else if (t.name == "Decription" && t.parent == _crafting) ctx.Role(image, "skip", "vanilla detail backdrop (replaced by the detail well)");
                else if (t.name == "requirements" && t.parent && t.parent.name == "Decription") ctx.Role(image, "skip", "vanilla requirement row backdrop (hidden)");
                else if (t.name == "MT_ChainTree") ctx.Role(image, "craft.missing", "RecipeChains 'missing' box (MorgottTweaks; RuneSkin adds its tree scroll)");
                else if (image.GetComponent<ScrollRect>()) ctx.Role(image, "skip", "recipe scroll view hit area (drawn clear, list well behind)");
                else if (_gui.m_craftingStationLevelRoot && t == _gui.m_craftingStationLevelRoot) ctx.Role(image, "craft.badge", "station level badge (InventoryGui.m_craftingStationLevelRoot)");
                else if (_gui.m_craftingStationIcon && image == _gui.m_craftingStationIcon) ctx.Role(image, "content", "station icon (CraftingStation.m_icon)");
                else if (_gui.m_craftButton && image == _gui.m_craftButton.targetGraphic) ctx.Role(image, "craft.button", "craft / upgrade button (button_craft_*, falls back to button_*)");
                else if (t.name == DescView && t.parent && t.parent.name == "Decription") ctx.Role(image, "skip", "description scroll view hit area (RuneSkin, clear)");
                else if (_gui.m_repairButton && image == _gui.m_repairButton.targetGraphic) ctx.Role(image, "craft.repair", "repair button (round)");
                else if (_gui.m_repairButton && t.parent == _gui.m_repairButton.transform && t.name == "Image") ctx.Role(image, "craft.repair.glyph", "repair button hammer glyph");
                else if (_gui.m_repairButtonGlow && image == _gui.m_repairButtonGlow) ctx.Role(image, "content", "repair glow (vanilla pulses it; Epic Loot finds RepairButton/Glow)");
                else if (IsTab(t, out var tabTarget)) ctx.Role(image, tabTarget ? (IsCategory(t) ? "craft.category" : "craft.tab") : t.name == "Selected" ? "skip" : "content",
                    tabTarget ? "crafting tab button (selected = interactable off: sprite disabled state)" : "tab child");
                else if (t.name == "MT_CraftSearch" && image.GetComponent<Selectable>()) ctx.Role(image, "craft.search", "CraftingFilter search field");
                else if (Row(t, out var row))
                {
                    if (t.name == "bkg" && t.parent == row) ctx.Role(image, CanCraft(row) ? "craft.row" : "craft.row.unavailable", "recipe row strip (canCraft from m_availableRecipes)");
                    else if (t.name == "selected" && t.parent == row) ctx.Role(image, "craft.row.selected", "recipe row selected rim (SetRecipe toggles it)");
                    else if (image.GetComponentInParent<Button>(true) && image.GetComponentInParent<Button>(true).targetGraphic == image) ctx.Role(image, "craft.row", "recipe row button");
                    else ctx.Role(image, "content", "recipe row icon / durability / magic background");
                }
                else if (IsRequirementCell(t)) ctx.Role(image, MissingShown(t.gameObject) ? "craft.socket.missing" : "craft.socket", "requirement socket (m_recipeRequirementList cell)");
                else if (_gui.m_minStationLevelIcon && t == _gui.m_minStationLevelIcon.transform.parent && t.parent == Requirements()) ctx.Role(image, "craft.socket", "required station level: its own requirement socket (level, parent of MinLevel)");
                else if (_gui.m_minStationLevelIcon && t == _gui.m_minStationLevelIcon.transform) ctx.Role(image, "content", "required station level icon (MinLevel, shows the station's icon)");
                else if (t.name == "res_icon" || t.parent && t.parent.name == "level" || t.name == "MinLevel") ctx.Role(image, "content", "requirement icon / min station level glyph");
                else if (t.name == "MT_ChainTree") ctx.Role(image, "craft.missing", "RecipeChains 'missing' box (MorgottTweaks)");
                else if (_gui.m_recipeIcon && image == _gui.m_recipeIcon || t.name == "magicItemIconBG" || t.name == "MT_Icon") ctx.Role(image, "content", "item icon / magic background / category icon");
                else if (Leftover(image)) ctx.Role(image, "content", "vanilla crafting graphic kept (no theme role): " + (image.sprite ? image.sprite.name : "no sprite"));
            }
            foreach (var g in _crafting.GetComponentsInChildren<Graphic>(true))
                if (!(g is Image) && !(g is TMP_Text) && !(g is TMP_SubMeshUI)) ctx.Role(g, "content", g.GetType().Name + " (input caret / mod graphic, kept)");
            foreach (var text in _crafting.GetComponentsInChildren<TMP_Text>(true))
            {
                var t = text.transform;
                if (_gui.m_recipeName && text == _gui.m_recipeName) ctx.Role(text, "craft.title", "recipe title (gold, mockup)");
                else if (_gui.m_craftingStationName && text == _gui.m_craftingStationName) ctx.Role(text, "text.header", "station title (serif, no plaque)");
                else if (_gui.m_craftingStationLevel && text == _gui.m_craftingStationLevel) ctx.Role(text, "craft.level", "station level digit on the star badge (text_active)");
                else if (IsTab(t, out _)) ctx.Role(text, "craft.tab.label", "tab label");
                else if (Row(t, out _)) ctx.Role(text, "text.body", "recipe row name / quality (body sans, mockup-1)");
            }
            return ctx;
        }

        // t is (inside) a direct child of m_crafting that passes `test`.
        private bool IsUnder(Transform t, Func<Transform, bool> test)
        {
            for (var p = t; p && p != _crafting; p = p.parent)
                if (p.parent == _crafting && test(p)) return true;
            return false;
        }

        // Leftover vanilla graphics without a theme role (progress bar back, upgrade panel parts, mod decor): kept as
        // they are, explained instead of "unknown". Never overrides component semantics (buttons, scrollbars, masks,
        // GuiBar fills) or sprite roles.
        private static bool Leftover(Image image)
        {
            var bar = image.GetComponentInParent<GuiBar>(true);
            if (image.GetComponentInParent<Scrollbar>(true) || image.GetComponent<Mask>() || image.GetComponent<RectMask2D>() || bar && bar.m_bar && bar.m_bar.gameObject == image.gameObject) return false;
            var sel = image.GetComponentInParent<Selectable>(true);
            if (sel && sel.targetGraphic == image) return false;
            var sprite = image.sprite ? image.sprite.name : null;
            return string.IsNullOrEmpty(sprite) || Classifier.BySprite(sprite, OwnershipRegistry.Crafting) == null;
        }

        // A tab button (mode tab under TabsButtons, or a CraftingFilter category tab) or one of its children.
        private bool IsTab(Transform t, out bool target)
        {
            target = false;
            var tabs = TabsRoot();
            for (var p = t; p && p != _crafting; p = p.parent)
            {
                if (p.parent && (p.parent == tabs || p.parent.name == "Tabs" && p.parent.parent && p.parent.parent.name == "MT_CraftFilter") && p.GetComponent<Button>())
                {
                    target = p == t && p.GetComponent<Button>().targetGraphic && p.GetComponent<Button>().targetGraphic.transform == t;
                    return true;
                }
            }
            return false;
        }

        private static bool IsCategory(Transform t) => t.parent && t.parent.name == "Tabs" && t.parent.parent && t.parent.parent.name == "MT_CraftFilter";

        private bool Row(Transform t, out Transform row)
        {
            row = null;
            var listRoot = _gui.m_recipeListRoot;
            var template = _gui.m_recipeElementPrefab ? _gui.m_recipeElementPrefab.transform : null;
            for (var p = t; p && p != _crafting; p = p.parent)
            {
                if (p == template || listRoot && p.parent == listRoot)
                {
                    row = p;
                    return true;
                }
            }
            return false;
        }

        private bool CanCraft(Transform row)
        {
            foreach (var r in _gui.m_availableRecipes)
                if (r.InterfaceElement && r.InterfaceElement.transform == row) return r.CanCraft;
            return true;
        }

        private bool IsRequirementCell(Transform t)
        {
            var list = _gui.m_recipeRequirementList;
            if (list == null) return false;
            foreach (var go in list)
                if (go && go.transform == t) return true;
            return false;
        }

        private bool MissingShown(GameObject cell) => _missingShown.TryGetValue(cell, out var m) && m;

        // ---------------------------------------------------------------- requirement cells (no vanilla paging)

        // SetupRequirementList prefix: at least as many cells as the recipe has resources, so vanilla's paging
        // (InventoryGui.cs:1665-1668: num3 = ceil(count / cells)) is always one page. Cells are clones of the first
        // (same children names: ValheimPlus' SetupRequirement prefix and vanilla Find res_icon / res_name / res_amount).
        public void EnsureRequirementCells(int needed)
        {
            var list = _gui.m_recipeRequirementList;
            if (list == null || list.Length == 0 || !list[0] || list.Length >= needed) return;
            var grown = new GameObject[needed];
            Array.Copy(list, grown, list.Length);
            var source = list[list.Length - 1] ? list[list.Length - 1] : list[0];
            for (var i = list.Length; i < needed; i++)
            {
                var clone = Instantiate(source, source.transform.parent, false);
                clone.name = "res_bkg (" + i + ")";
                clone.transform.SetSiblingIndex(grown[i - 1].transform.GetSiblingIndex() + 1);
                InventoryGui.HideRequirement(clone.transform);
                grown[i] = clone;
            }
            _gui.m_recipeRequirementList = grown;
            ScreenRegistry.Restyle(_crafting);
        }

        // ---------------------------------------------------------------- per frame

        // InventoryGui.Update postfix: state only; geometry in LateUpdate.
        public void Frame()
        {
            if (!InventoryGui.IsVisible() || !_crafting.gameObject.activeInHierarchy)
            {
                if (_host && _host.gameObject.activeSelf) _host.gameObject.SetActive(false);
                return;
            }
            _frameReady = true;
        }

        private void LateUpdate()
        {
            if (!_frameReady) return;
            _frameReady = false;
            var inv = InventorySkin.Instance;
            if (!inv || !_canvas || !InventoryGui.IsVisible()) return;
            var box = inv.CraftingBox;
            if (box.IsEmpty) return;
            Collect();
            ReadTokens();
            var i = _in;
            i.Panel = box;
            i.Scale = inv.SkinScaleValue;
            i.CapInset = CapInset();
            var hash = Hash(i);
            if (_layout == null || hash != _hash)
            {
                _hash = hash;
                _layout = CraftingMath.Compute(i, _t);
                _tabOffset = _layout.Tabs.First;
                _catOffset = _layout.Categories.First;
            }
            Apply(_layout, box);
        }

        private void Collect()
        {
            _tabs.Clear();
            var tabs = TabsRoot();
            if (tabs)
                foreach (Transform c in tabs)
                    if (c.gameObject.activeSelf && c.GetComponent<Button>()) _tabs.Add((RectTransform)c);
            _cats.Clear();
            var filter = Filter();
            var catRoot = filter ? filter.Find("Tabs") : null;
            if (catRoot)
                foreach (Transform c in catRoot)
                    if (c.gameObject.activeSelf && c.GetComponent<Button>()) _cats.Add((RectTransform)c);
            _reqActive.Clear();
            var list = _gui.m_recipeRequirementList;
            if (list != null)
                for (var n = 0; n < list.Length; n++)
                {
                    var icon = list[n] ? list[n].transform.Find("res_icon") : null;
                    if (icon && icon.gameObject.activeSelf) _reqActive.Add(n);
                }
            var i = _in;
            i.Tabs = _tabs.Count;
            i.Categories = _cats.Count;
            i.Search = filter && filter.Find("Search/MT_CraftSearch");
            i.Requirements = _reqActive.Count + (_gui.m_minStationLevelIcon && _gui.m_minStationLevelIcon.gameObject.activeSelf ? 1 : 0);
            // Round repair button only while something can be repaired (vanilla UpdateRepair: interactable); otherwise
            // the craft button takes the whole row (mockup-1: no empty socket beside it).
            i.Repair = RepairShown();
            i.CraftType = _gui.m_itemCraftType && _gui.m_itemCraftType.gameObject.activeSelf;
            i.Quality = _gui.m_qualityPanel && _gui.m_qualityPanel.gameObject.activeSelf;
            i.TabOffset = _tabOffset;
            i.CategoryOffset = _catOffset;
        }

        private static int Hash(CraftingInput i)
        {
            unchecked
            {
                var h = i.Panel.X.GetHashCode() * 31 + i.Panel.Y.GetHashCode();
                h = h * 31 + i.Panel.W.GetHashCode();
                h = h * 31 + i.Panel.H.GetHashCode();
                h = h * 31 + i.Scale.GetHashCode();
                h = h * 31 + i.CapInset.GetHashCode();
                h = h * 31 + i.Tabs;
                h = h * 31 + i.Categories * 7 + i.Requirements * 101;
                h = h * 31 + (i.Search ? 1 : 0) + (i.Repair ? 2 : 0) + (i.CraftType ? 4 : 0) + (i.Quality ? 8 : 0);
                h = h * 31 + i.TabOffset * 13 + i.CategoryOffset;
                h = h * 31 + ThemeTokens.Revision;
                return h;
            }
        }

        private float CapInset()
        {
            var stack = _host ? _host.Find(PanelDecor.ChildName) : null;
            if (!stack || !stack.TryGetComponent<PanelDecor>(out var decor)) return 0f;
            return decor.ContentInset() * _host.lossyScale.x / Mathf.Max(Geo.WorldPerUnit(_canvas), 1e-6f);
        }

        private void ReadTokens()
        {
            float F(string token, float fallback) => ThemeTokens.GetFloat(token, fallback);
            var t = _t;
            t.Pad = F("size.craft.pad", t.Pad); t.Gap = F("size.craft.gap", t.Gap); t.HeaderH = F("size.craft.header", t.HeaderH);
            t.TabH = F("size.craft.tab.h", t.TabH); t.TabW = F("size.craft.tab.w", t.TabW); t.TabMinW = F("size.craft.tab.min", t.TabMinW);
            t.ArrowW = F("size.craft.arrow", t.ArrowW); t.CategoryH = F("size.craft.category", t.CategoryH); t.SearchH = F("size.craft.search", t.SearchH);
            t.ListShare = F("share.craft.list", t.ListShare); t.RowH = F("size.craft.row", t.RowH); t.ScrollbarW = F("size.craft.scrollbar", t.ScrollbarW);
            t.TitleH = F("size.craft.title", t.TitleH); t.CraftTypeH = F("size.craft.crafttype", t.CraftTypeH); t.IconH = F("size.craft.icon", t.IconH);
            t.QualityH = F("size.craft.quality", t.QualityH); t.DescriptionMinH = F("size.craft.description.min", t.DescriptionMinH);
            t.ReqW = F("size.craft.req.w", t.ReqW); t.ReqH = F("size.craft.req.h", t.ReqH); t.ReqMinW = F("size.craft.req.min", t.ReqMinW);
            t.ReqMaxRows = F("size.craft.req.rows", t.ReqMaxRows); t.ButtonH = F("size.craft.button", t.ButtonH); t.WellPad = F("size.craft.well.pad", t.WellPad);
        }

        // ---------------------------------------------------------------- placement

        private void Apply(CraftingLayout o, Box box)
        {
            if (!_host.gameObject.activeSelf) _host.gameObject.SetActive(true);
            Geo.Place(_canvas, _host, box);
            Geo.SetScale(_crafting, 1f); // M2 fitted the vanilla panel by scale; the column now has its own layout
            Geo.Place(_canvas, _crafting, box);

            Header(o);
            Tabs(o);
            Categories(o);
            List(o);
            Detail(o);
            Buttons(o);
            var vnei = _crafting.Find("VNEI(Clone)") as RectTransform;
            if (vnei && vnei.gameObject.activeSelf)
            {
                var top = o.Search.IsEmpty ? o.List.Y : o.Search.Y;
                var area = new Box(o.List.X, top, o.Detail.Right - o.List.X, o.ButtonRow.Bottom - top);
                Geo.Place(_canvas, vnei, area);
                VneiFit(vnei, area);
            }
        }

        // VNEI's item browser (VNEI.UI.BaseUI) sizes its grid in whole 50-unit cells: root = X*50+10 by Y*50+100
        // (BaseUI.RebuildSize); attached to crafting it takes the vanilla panel's size once (MainVneiHandler: SetSize
        // from Crafting.sizeDelta). Here the cell counts follow the body box (BaseUI.SetSize(false, x, y), only on change)
        // and the grid root is centred in it. Reflection: VNEI is optional and not referenced.
        private static Type _vneiType;
        private static System.Reflection.MethodInfo _vneiSetSize;
        private static System.Reflection.FieldInfo _vneiRoot;
        private static System.Reflection.PropertyInfo _vneiX, _vneiY;
        private static bool _vneiLooked;

        private void VneiFit(RectTransform vnei, Box area)
        {
            if (!_vneiLooked)
            {
                _vneiLooked = true;
                _vneiType = HarmonyLib.AccessTools.TypeByName("VNEI.UI.BaseUI");
                if (_vneiType != null)
                {
                    _vneiSetSize = HarmonyLib.AccessTools.Method(_vneiType, "SetSize", new[] { typeof(bool), typeof(int), typeof(int) });
                    _vneiRoot = HarmonyLib.AccessTools.Field(_vneiType, "root");
                    _vneiX = HarmonyLib.AccessTools.Property(_vneiType, "ItemSizeX");
                    _vneiY = HarmonyLib.AccessTools.Property(_vneiType, "ItemSizeY");
                }
                if (_vneiSetSize == null || _vneiRoot == null || _vneiX == null || _vneiY == null)
                    Plugin.Log.LogInfo("[RuneSkin] VNEI BaseUI.SetSize/root not found: its grid keeps its own size");
            }
            if (_vneiSetSize == null) return;
            var ui = vnei.GetComponent(_vneiType);
            if (!ui) return;
            // Canvas units per VNEI local unit (its own scale chain).
            var u = vnei.lossyScale.x / Mathf.Max(Geo.WorldPerUnit(_canvas), 1e-6f);
            if (u <= 0f) return;
            var (x, y) = CraftingMath.VneiCells(area.W, area.H, u);
            try
            {
                if ((int)_vneiX.GetValue(ui, null) != x || (int)_vneiY.GetValue(ui, null) != y) _vneiSetSize.Invoke(ui, new object[] { false, x, y });
                if (_vneiRoot.GetValue(ui) is RectTransform root && root != vnei)
                {
                    var w = (x * 50f + 10f) * u;
                    var h = (y * 50f + 100f) * u;
                    Geo.Place(_canvas, root, new Box(area.X + (area.W - w) / 2f, area.Y, w, h));
                }
            }
            catch (Exception e)
            {
                _vneiSetSize = null;
                Plugin.Log.LogWarning($"[RuneSkin] VNEI grid fit disabled: {e.GetBaseException().Message}");
            }
        }

        private void Header(CraftingLayout o)
        {
            if (_gui.m_craftingStationIcon) PlaceKeep(_gui.m_craftingStationIcon.rectTransform, o.StationIcon, true);
            if (_gui.m_craftingStationName)
            {
                // Station title: plain serif capitals centred between icon and badge (art README: no plaque).
                var station = _gui.m_craftingStationName;
                Geo.Place(_canvas, station.rectTransform, o.Title);
                Label(station, TextAlignmentOptions.Center, o.Title.H * 0.46f, 14f, upper: true);
            }
            var level = _gui.m_craftingStationLevelRoot;
            if (level)
            {
                Geo.Place(_canvas, level, o.Level);
                if (_gui.m_craftingStationLevel)
                {
                    // Digit in the star's round recess (badge_level_star: centre (76,75) of 150x144 = the middle).
                    Geo.Place(_canvas, _gui.m_craftingStationLevel.rectTransform, CraftingMath.Inset(o.Level, o.Level.W * 0.27f, o.Level.H * 0.27f));
                    Label(_gui.m_craftingStationLevel, TextAlignmentOptions.Center, o.Level.H * 0.42f, 12f);
                }
            }
        }

        private void Tabs(CraftingLayout o)
        {
            var root = TabsRoot();
            if (root) Geo.Place(_canvas, root, o.TabRow.IsEmpty ? new Box(o.Content.X, o.Content.Y, o.Content.W, 1f) : o.TabRow);
            var size = ThemeTokens.GetFloat("size.craft.font.tab", 18f) * _in.Scale;
            for (var n = 0; n < _tabs.Count; n++)
            {
                var tab = _tabs[n];
                var cell = n < o.Tabs.Cells.Length ? o.Tabs.Cells[n] : default;
                if (!Visible(tab, cell)) continue;
                Geo.Place(_canvas, tab, cell);
                foreach (Transform c in tab)
                {
                    if (c.name == "Text" && c is RectTransform tr && c.TryGetComponent<TMP_Text>(out var label))
                    {
                        Geo.Place(_canvas, tr, cell);
                        Label(label, TextAlignmentOptions.Center, size, 12f, upper: true);
                    }
                    else if (c.name == "Selected" && c.TryGetComponent<Image>(out var sel) && sel.enabled) sel.enabled = false;
                }
            }
            _tabArrows.Show(_canvas, o.Tabs);
        }

        private void Categories(CraftingLayout o)
        {
            var filter = Filter();
            if (!filter) { _catArrows.Show(_canvas, new RowFit()); return; }
            var tabsRoot = filter.Find("Tabs") as RectTransform;
            if (tabsRoot && !o.CategoryRow.IsEmpty) Geo.Place(_canvas, tabsRoot, o.CategoryRow);
            for (var n = 0; n < _cats.Count; n++)
            {
                var tab = _cats[n];
                var cell = n < o.Categories.Cells.Length ? o.Categories.Cells[n] : default;
                if (!Visible(tab, cell)) continue;
                Geo.Place(_canvas, tab, cell);
                var inner = CraftingMath.Inset(cell, cell.W * 0.19f, cell.H * 0.19f);
                var glyph = CategoryGlyph(tab, inner);
                foreach (Transform c in tab)
                {
                    if (!(c is RectTransform rt)) continue;
                    if (glyph && (c.name == "MT_Icon" || c.name == "Text"))
                    {
                        // The kit glyph replaces CraftingFilter's item icon / "All" text (mockup: one glyph family).
                        if (c.TryGetComponent<Graphic>(out var g) && g.enabled) g.enabled = false;
                        continue;
                    }
                    if (c.name == "MT_Icon") Geo.Place(_canvas, rt, inner);
                    else if (c.name == "Text" && c.TryGetComponent<TMP_Text>(out var label))
                    {
                        Geo.Place(_canvas, rt, cell);
                        if (label.margin != Vector4.zero) label.margin = Vector4.zero;
                        Label(label, TextAlignmentOptions.Center, cell.H * 0.36f, 10f);
                    }
                    else if (c.name == "Selected" && c.TryGetComponent<Image>(out var sel) && sel.enabled) sel.enabled = false;
                }
            }
            _catArrows.Show(_canvas, o.Categories);
            var search = filter.Find("Search") as RectTransform;
            var field = search ? search.Find("MT_CraftSearch") as RectTransform : null;
            if (search && !o.Search.IsEmpty) Geo.Place(_canvas, search, o.Search);
            if (field && !o.Search.IsEmpty)
            {
                Geo.Place(_canvas, field, o.Search);
                // Text area starts right of the magnifier recess (field_search: text from x 100 of 184 at height 76).
                SearchGlyph(field, new Box(o.Search.X + o.Search.H * 0.22f, o.Search.Y + o.Search.H * 0.2f, o.Search.H * 0.6f, o.Search.H * 0.6f));
                var area = field.Find("Text Area") as RectTransform;
                if (area) Geo.Place(_canvas, area, new Box(o.Search.X + o.Search.H * 1.25f, o.Search.Y + 4f, Mathf.Max(1f, o.Search.W - o.Search.H * 1.25f - 8f), Mathf.Max(1f, o.Search.H - 8f)));
            }
        }

        private Image _searchGlyph;

        // Magnifier in the search field's left recess (field_search + glyph_search), own image under the field.
        private void SearchGlyph(RectTransform field, Box b)
        {
            if (!_searchGlyph)
            {
                _searchGlyph = new GameObject(Prefix + "SearchGlyph", typeof(RectTransform)).AddComponent<Image>();
                _searchGlyph.rectTransform.SetParent(field, false);
                _searchGlyph.raycastTarget = false;
                var mark = DollCell.Mark(_searchGlyph, "craft.glyph.search", "search field magnifier");
                mark.Outcome = Restyler.Apply(_searchGlyph, mark, ThemeTokens.Role(mark.Role)) ? Outcome.Styled : Outcome.Degraded;
                _searchGlyph.enabled = mark.Outcome == Outcome.Styled;
            }
            Geo.Place(_canvas, _searchGlyph.rectTransform, b);
        }

        // CraftingFilter category (MT_CraftTab_<Category>) -> kit glyph glyph_cat_*; categories without a kit glyph keep
        // CraftingFilter's item icon. Tint: selected (vanilla-style interactable off) text_active, else text_body.
        private static readonly Dictionary<string, string> CategoryGlyphs = new Dictionary<string, string>
        {
            ["All"] = "all",
            ["Tools"] = "tools",
            ["Ammo"] = "ammo",
            ["Melee"] = "weapons",
            ["Ranged"] = "ranged",
            ["Magic"] = "magic",
            ["Armor"] = "armor",
            ["Accessories"] = "utility",
            ["Food"] = "food",
            ["Meads"] = "meads",
            ["Materials"] = "materials",
        };

        private Image CategoryGlyph(RectTransform tab, Box inner)
        {
            const string prefix = "MT_CraftTab_";
            if (!tab.name.StartsWith(prefix, StringComparison.Ordinal) || !CategoryGlyphs.TryGetValue(tab.name.Substring(prefix.Length), out var key)) return null;
            var t = tab.Find(Prefix + "Glyph");
            Image image;
            if (!t)
            {
                image = new GameObject(Prefix + "Glyph", typeof(RectTransform)).AddComponent<Image>();
                image.rectTransform.SetParent(tab, false);
                image.raycastTarget = false;
                var mark = DollCell.Mark(image, "craft.glyph.cat." + key, "CraftingFilter category glyph");
                mark.Outcome = Restyler.Apply(image, mark, ThemeTokens.Role(mark.Role)) ? Outcome.Styled : Outcome.Degraded;
                image.enabled = mark.Outcome == Outcome.Styled;
            }
            else image = t.GetComponent<Image>();
            if (!image.enabled) return null;
            Geo.Place(_canvas, image.rectTransform, inner);
            var button = tab.GetComponent<Button>();
            var want = ThemeTokens.GetColor(button && !button.interactable ? "color.text.active" : "color.text.body", Color.white);
            if (image.color != want) image.color = want;
            return image;
        }

        // Shown cells at scale 1; scrolled-out cells collapse to scale 0 (stay active: mods keep their references).
        private static bool Visible(RectTransform rt, Box cell)
        {
            var on = !cell.IsEmpty;
            Geo.SetScale(rt, on ? 1f : 0f);
            return on;
        }

        private void List(CraftingLayout o)
        {
            var list = RecipeList();
            if (!list) return;
            Geo.Place(_canvas, list, o.List);
            Geo.Place(_canvas, _listWell.rectTransform, o.List);
            var view = ListView();
            if (view) Geo.Place(_canvas, view, o.ListView);
            if (_gui.m_recipeListScroll) Geo.Place(_canvas, (RectTransform)_gui.m_recipeListScroll.transform, o.ListScrollbar);
            Rows(o);
        }

        // Recipe rows: pitch = size.craft.row via the field AddRecipeToList lays rows out with (m_recipeListSpace,
        // InventoryGui.cs:1341); the template carries the row geometry so every new row inherits it; existing rows are
        // moved once when the pitch or the row set changes; the scroll content keeps at least the view height
        // (m_recipeListBaseSize, :1212) so a short list never scrolls.
        private void Rows(CraftingLayout o)
        {
            var pitch = o.RowPitch;
            var rows = _gui.m_availableRecipes;
            var first = rows.Count > 0 && rows[0].InterfaceElement ? rows[0].InterfaceElement.GetInstanceID() : 0;
            var changed = !Mathf.Approximately(pitch, _rowPitch) || rows.Count != _rowCount || first != _rowFirst || _rowsTheme != ThemeTokens.Revision;
            if (!changed) return;
            var rebuilt = rows.Count != _rowCount || first != _rowFirst;
            _rowPitch = pitch;
            _rowCount = rows.Count;
            _rowFirst = first;
            _rowsTheme = ThemeTokens.Revision;
            _gui.m_recipeListSpace = pitch;
            var view = ListView();
            if (view) _gui.m_recipeListBaseSize = view.rect.height;
            if (_gui.m_recipeElementPrefab) StyleRow((RectTransform)_gui.m_recipeElementPrefab.transform, pitch);
            for (var n = 0; n < rows.Count; n++)
            {
                var e = rows[n].InterfaceElement;
                if (!e) continue;
                var rt = (RectTransform)e.transform;
                StyleRow(rt, pitch);
                rt.anchoredPosition = new Vector2(0f, -n * pitch);
            }
            if (_gui.m_recipeListRoot)
                _gui.m_recipeListRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(_gui.m_recipeListBaseSize, rows.Count * pitch));
            if (rebuilt) ScreenRegistry.Restyle(_crafting); // per-row role (craftable / unavailable)
            // Content bounds changed after vanilla SetRecipe may have centred the selection: reveal it once more.
            var sel = _gui.GetSelectedRecipeIndex();
            if (_gui.m_recipeEnsureVisible && sel >= 0 && sel < rows.Count && rows[sel].InterfaceElement)
                _gui.m_recipeEnsureVisible.CenterOnItem((RectTransform)rows[sel].InterfaceElement.transform);
        }

        private void StyleRow(RectTransform row, float pitch)
        {
            var h = pitch - 2f;
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, h);
            var icon = h - 10f;
            var font = ThemeTokens.GetFloat("size.craft.font.row", 16f) * _in.Scale;
            foreach (Transform c in row)
            {
                if (!(c is RectTransform rt)) continue;
                switch (c.name)
                {
                    case "bkg":
                    case "selected":
                        Anchors(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                        break;
                    case "icon":
                    case "magicItemIconBG":
                        Anchors(rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, -icon / 2f), new Vector2(8f + icon, icon / 2f));
                        break;
                    case "name":
                        Anchors(rt, Vector2.zero, Vector2.one, new Vector2(icon + 16f, 2f), new Vector2(-30f, -2f));
                        if (c.TryGetComponent<TMP_Text>(out var name))
                        {
                            if (name.alignment != TextAlignmentOptions.MidlineLeft) name.alignment = TextAlignmentOptions.MidlineLeft;
                            if (name.textWrappingMode != TextWrappingModes.Normal) name.textWrappingMode = TextWrappingModes.Normal;
                            if (name.overflowMode != TextOverflowModes.Ellipsis) name.overflowMode = TextOverflowModes.Ellipsis;
                            name.enableAutoSizing = true;
                            name.fontSizeMax = font;
                            name.fontSizeMin = Mathf.Min(font, 12f);
                        }
                        break;
                    case "QualityLevel":
                        Anchors(rt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-28f, -12f), new Vector2(-6f, 12f));
                        break;
                    case "Durability":
                        Anchors(rt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(icon + 16f, 4f), new Vector2(-30f, 8f));
                        break;
                }
            }
        }

        private static void Anchors(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            if (rt.anchorMin != min) rt.anchorMin = min;
            if (rt.anchorMax != max) rt.anchorMax = max;
            if (rt.offsetMin != offMin) rt.offsetMin = offMin;
            if (rt.offsetMax != offMax) rt.offsetMax = offMax;
        }

        private void Detail(CraftingLayout o)
        {
            var detail = _crafting.Find("Decription") as RectTransform;
            if (!detail) return;
            Geo.Place(_canvas, detail, o.Detail);
            Geo.Place(_canvas, _detailWell.rectTransform, o.Detail);
            var k = _in.Scale;
            if (_gui.m_recipeName)
            {
                Geo.Place(_canvas, _gui.m_recipeName.rectTransform, o.DetailTitle);
                Label(_gui.m_recipeName, TextAlignmentOptions.MidlineLeft, ThemeTokens.GetFloat("size.craft.font.title", 22f) * k, 14f, upper: true);
            }
            if (_gui.m_itemCraftType && !o.CraftType.IsEmpty)
            {
                Geo.Place(_canvas, _gui.m_itemCraftType.rectTransform, o.CraftType);
                Label(_gui.m_itemCraftType, TextAlignmentOptions.MidlineLeft, ThemeTokens.GetFloat("size.craft.font.body", 16f) * k, 11f);
            }
            if (_gui.m_recipeIcon)
            {
                var icon = _gui.m_recipeIcon.rectTransform;
                PlaceKeep(_gui.m_recipeIcon, o.Icon, true);
                var bg = icon.parent ? icon.parent.Find("magicItemIconBG") as RectTransform : null;
                if (bg) Geo.Place(_canvas, bg, o.Icon);
            }
            if (_gui.m_variantButton && _gui.m_variantButton.gameObject.activeSelf)
            {
                var w = 96f * k;
                Geo.Place(_canvas, (RectTransform)_gui.m_variantButton.transform, new Box(o.Detail.Right - w - 10f * k, o.Icon.Y, w, 28f * k));
            }
            if (_gui.m_qualityPanel && !o.Quality.IsEmpty) Geo.Place(_canvas, (RectTransform)_gui.m_qualityPanel, o.Quality);
            RequirementGrid(o);
            var description = o.Description;
            var chain = Chain(o);
            if (!chain.IsEmpty) description = new Box(description.X, description.Y, description.W, Mathf.Max(0f, chain.Y - _t.Gap * k * 0.5f - description.Y));
            DescriptionView(description, k);
        }

        // ---------------------------------------------------------------- description scroll

        private const string DescView = Prefix + "DescView";
        private RectTransform _descView;
        private ScrollRect _descScroll;
        private string _descText;
        private float _descWidth = -1f, _descSize = -1f;

        // Long descriptions (Epic Loot, mod items) scroll inside their box instead of shrinking to micro text or being
        // cut: the description text (m_recipeDecription, same object and name) moves into an own clipped view with a
        // vertical ScrollRect; its height = its preferred height at the view width. Nothing finds it by path
        // (RecipeChains reads its corners for x only: the view keeps the detail column's x).
        private void DescriptionView(Box b, float k)
        {
            var d = _gui.m_recipeDecription;
            if (!d) return;
            var detail = _crafting.Find("Decription");
            if (!_descView && detail)
            {
                var go = new GameObject(DescView, typeof(RectTransform));
                _descView = (RectTransform)go.transform;
                _descView.SetParent(detail, false);
                _descView.SetSiblingIndex(d.transform.GetSiblingIndex());
                var hit = go.AddComponent<Image>();
                hit.color = Color.clear; // wheel / drag hit area, draws nothing
                go.AddComponent<RectMask2D>();
                _descScroll = go.AddComponent<ScrollRect>();
                _descScroll.horizontal = false;
                _descScroll.vertical = true;
                _descScroll.movementType = ScrollRect.MovementType.Clamped;
                _descScroll.inertia = false;
                _descScroll.scrollSensitivity = 24f;
                _descScroll.viewport = _descView;
                d.rectTransform.SetParent(_descView, false);
                _descScroll.content = d.rectTransform;
                var rt = d.rectTransform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(0f, 10f);
                rt.localScale = Vector3.one;
            }
            if (!_descView) { Geo.Place(_canvas, d.rectTransform, b); return; }
            if (b.H < 1f) b = new Box(b.X, b.Y, b.W, 1f);
            Geo.Place(_canvas, _descView, b);
            var size = ThemeTokens.GetFloat("size.craft.font.body", 16f) * k;
            if (d.enableAutoSizing) d.enableAutoSizing = false;
            if (Mathf.Abs(d.fontSize - size) > 0.1f) d.fontSize = size;
            if (d.alignment != TextAlignmentOptions.TopLeft) d.alignment = TextAlignmentOptions.TopLeft;
            if (d.textWrappingMode != TextWrappingModes.Normal) d.textWrappingMode = TextWrappingModes.Normal;
            if (d.overflowMode != TextOverflowModes.Overflow) d.overflowMode = TextOverflowModes.Overflow;
            var width = _descView.rect.width;
            if (!ReferenceEquals(d.text, _descText) || Mathf.Abs(width - _descWidth) > 0.5f || Mathf.Abs(size - _descSize) > 0.1f)
            {
                var newText = !ReferenceEquals(d.text, _descText);
                _descText = d.text;
                _descWidth = width;
                _descSize = size;
                var h = Mathf.Max(d.GetPreferredValues(d.text ?? "", width, 0f).y + 4f, 1f);
                d.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
                if (newText) _descScroll.verticalNormalizedPosition = 1f; // a new recipe starts at the top
            }
        }

        // ---------------------------------------------------------------- RecipeChains box

        // MorgottTweaks RecipeChains "missing" box (MT_ChainTree): it docks itself above the highest requirement cell
        // at the description's x / width and sizes its height to the whole tree (RecipeChainsUi.DockInDetail). Here:
        // its height is capped (size.craft.chain.max, and never over the description's minimum), the tree text scrolls
        // inside it (ScrollRect on the box, viewport = the text area above its chain button), and the description ends
        // above it. Returns the box's canvas rect (empty when it is hidden).
        private RectTransform _chain;
        private RectTransform _chainView;

        private Box Chain(CraftingLayout o)
        {
            if (!_chain) _chain = _crafting.Find("MT_ChainTree") as RectTransform;
            if (!_chain || !_chain.gameObject.activeInHierarchy) return default;
            var k = _in.Scale;
            var box = Geo.BoxOf(_canvas, _chain);
            var top = (o.Icon.IsEmpty ? o.Description.Y : o.Icon.Bottom) + ThemeTokens.GetFloat("size.craft.description.min", 48f) * k;
            var h = CraftingMath.ChainHeight(box.H, box.Bottom, top, ThemeTokens.GetFloat("size.craft.chain.max", 150f) * k, ThemeTokens.GetFloat("size.craft.chain.min", 72f) * k);
            if (box.H > h + 0.5f)
            {
                var unit = Geo.WorldPerUnit(_canvas) / Mathf.Max(_chain.lossyScale.y, 1e-6f);
                _chain.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h * unit);
                box = Geo.BoxOf(_canvas, _chain);
            }
            ChainScroll(box);
            return box;
        }

        private void ChainScroll(Box box)
        {
            var text = _chain.Find("Text") as RectTransform;
            if (!text) return;
            if (!_chainView)
            {
                _chainView = (RectTransform)new GameObject(Prefix + "ChainView", typeof(RectTransform)).transform;
                _chainView.SetParent(_chain, false);
                _chainView.SetAsFirstSibling();
                if (!_chain.GetComponent<RectMask2D>()) _chain.gameObject.AddComponent<RectMask2D>();
                var sr = _chain.GetComponent<ScrollRect>() ?? _chain.gameObject.AddComponent<ScrollRect>();
                sr.horizontal = false;
                sr.vertical = true;
                sr.movementType = ScrollRect.MovementType.Clamped;
                sr.inertia = false;
                sr.scrollSensitivity = 24f;
                sr.viewport = _chainView;
                sr.content = text;
            }
            // Viewport = the text area: from the box top down to the chain button (or the box bottom).
            var button = _chain.Find("MT_ChainCraftButton") as RectTransform;
            var bottom = box.Bottom;
            if (button && button.gameObject.activeSelf) bottom = Geo.BoxOf(_canvas, button).Y;
            Geo.Place(_canvas, _chainView, new Box(box.X, box.Y, box.W, Mathf.Max(1f, bottom - box.Y)));
        }

        private void RequirementGrid(CraftingLayout o)
        {
            var parent = Requirements();
            if (!parent) return;
            if (!o.Requirements.IsEmpty) Geo.Place(_canvas, parent, o.Requirements);
            var list = _gui.m_recipeRequirementList;
            if (list.Length != _reqCells)
            {
                _reqCells = list.Length;
                foreach (var go in list) if (go) StyleSocket((RectTransform)go.transform);
            }
            var slot = 0;
            for (var n = 0; n < list.Length; n++)
            {
                var go = list[n];
                if (!go) continue;
                var shown = _reqActive.Contains(n);
                // Unused cells leave the hierarchy (RecipeChains docks above the highest ACTIVE cell).
                if (go.activeSelf != shown) go.SetActive(shown);
                if (!shown || slot >= o.RequirementCells.Length) continue;
                // The socket art fills the whole cell (label strip, icon face, count band), so RecipeChains, which
                // docks above the highest active cell, never covers a label.
                Geo.Place(_canvas, (RectTransform)go.transform, o.RequirementCells[slot++]);
                SocketState(go);
            }
            var min = _gui.m_minStationLevelIcon;
            if (min && min.gameObject.activeSelf && slot < o.RequirementCells.Length)
            {
                // Station level requirement: a socket like the materials (its "level" parent), the station's own icon
                // (MinLevel) in the face, the level digit (level_text) in the count band.
                var cell = o.RequirementCells[slot];
                var socket = min.transform.parent as RectTransform;
                if (socket && socket != Requirements()) Geo.Place(_canvas, socket, cell);
                if (_gui.m_craftingStationIcon && _gui.m_craftingStationIcon.sprite && min.sprite != _gui.m_craftingStationIcon.sprite) min.sprite = _gui.m_craftingStationIcon.sprite;
                if (min.type != Image.Type.Simple) min.type = Image.Type.Simple;
                if (!min.preserveAspect) min.preserveAspect = true;
                Geo.Place(_canvas, min.rectTransform, new Box(cell.X + cell.W * 0.2f, cell.Y + cell.H * 0.12f, cell.W * 0.6f, cell.H * 0.54f));
                if (_gui.m_minStationLevelText)
                {
                    Geo.Place(_canvas, _gui.m_minStationLevelText.rectTransform, new Box(cell.X, cell.Y + cell.H * 0.72f, cell.W, cell.H * 0.26f));
                    Label(_gui.m_minStationLevelText, TextAlignmentOptions.Center, ThemeTokens.GetFloat("size.craft.font.amount", 16f) * _in.Scale, 10f);
                }
            }
        }
        // Socket internals (socket_req art: clear glass face, count band along the bottom, 9-sliced to the cell): label
        // along the top of the face, icon under it, amount centred in the band. Anchored to the cell, so clones inherit it.
        private void StyleSocket(RectTransform cell)
        {
            var k = _in.Scale;
            foreach (Transform c in cell)
            {
                if (!(c is RectTransform rt)) continue;
                switch (c.name)
                {
                    case "res_name":
                        Anchors(rt, new Vector2(0.03f, 0.67f), new Vector2(0.97f, 0.98f), Vector2.zero, Vector2.zero);
                        if (c.TryGetComponent<TMP_Text>(out var n))
                        {
                            Label(n, TextAlignmentOptions.Center, ThemeTokens.GetFloat("size.craft.font.label", 13f) * k, 9f);
                            // Long names (Кожаные обрывки) wrap to two lines instead of shrinking to micro text.
                            if (n.textWrappingMode != TextWrappingModes.Normal) n.textWrappingMode = TextWrappingModes.Normal;
                            if (n.maxVisibleLines != 2) n.maxVisibleLines = 2;
                        }
                        break;
                    case "res_icon":
                        Anchors(rt, new Vector2(0.15f, 0.26f), new Vector2(0.85f, 0.69f), Vector2.zero, Vector2.zero); // icon ~18% larger (Codex r2)
                        if (c.TryGetComponent<Image>(out var i) && !i.preserveAspect) i.preserveAspect = true;
                        break;
                    case "res_amount":
                        Anchors(rt, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.27f), Vector2.zero, Vector2.zero);
                        if (c.TryGetComponent<TMP_Text>(out var a)) Label(a, TextAlignmentOptions.Center, ThemeTokens.GetFloat("size.craft.font.amount", 16f) * k, 10f);
                        break;
                }
            }
        }

        // Missing material. ValheimPlus' SetupRequirement replacement (CraftFromChest: counts nearby chests) writes
        // "have/need": parsed. Vanilla writes only "need" and blinks it red while the stock is short
        // (InventoryGui.cs:1751-1753): the socket keeps the missing state for one blink period after the last red frame,
        // so the style never flickers. Never recounted here (the player inventory alone would contradict CraftFromChest).
        // The amount then gets the steady theme colour instead of the blink.
        private void SocketState(GameObject cell)
        {
            var amount = cell.transform.Find("res_amount");
            if (!amount || !amount.TryGetComponent<TMP_Text>(out var text)) return;
            var c = text.color;
            var now = Time.unscaledTime;
            bool missing;
            var s = text.text;
            var slash = s != null ? s.IndexOf('/') : -1;
            if (slash > 0 && int.TryParse(s.Substring(0, slash).Trim(), out var have) && int.TryParse(s.Substring(slash + 1).Trim(), out var need))
                missing = have < need && !(ZoneSystem.instance && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost));
            else
            {
                if (c.r > 0.9f && c.g < 0.2f && c.b < 0.2f) _redAt[cell] = now;
                missing = _redAt.TryGetValue(cell, out var at) && now - at < 0.7f;
            }
            var want = ThemeTokens.GetColor(missing ? "color.text.missing" : "color.text.ok", c);
            if (text.color != want) text.color = want;
            if (MissingShown(cell) == missing) return;
            _missingShown[cell] = missing;
            if (!cell.TryGetComponent<Image>(out var socket)) return;
            var role = missing ? "craft.socket.missing" : "craft.socket";
            var mark = socket.GetComponent<SkinMark>();
            if (!mark) return;
            mark.Role = role;
            mark.Outcome = Restyler.Apply(socket, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
        }

        private void Buttons(CraftingLayout o)
        {
            var panel = _gui.m_craftButton ? _gui.m_craftButton.transform.parent as RectTransform : null;
            if (panel && panel != _crafting)
            {
                Geo.Place(_canvas, panel, o.Craft);
                Geo.Place(_canvas, (RectTransform)_gui.m_craftButton.transform, o.Craft);
                if (_gui.m_craftProgressPanel) Geo.Place(_canvas, (RectTransform)_gui.m_craftProgressPanel, o.Craft);
                var label = _gui.m_craftButton.GetComponentInChildren<TMP_Text>(true);
                if (label)
                {
                    Geo.Place(_canvas, label.rectTransform, o.Craft);
                    Label(label, TextAlignmentOptions.Center, o.Craft.H * 0.42f, 14f, upper: true);
                    CraftGlyph(label, o.Craft);
                }
            }
            var repair = _gui.m_repairButton;
            if (repair && repair.gameObject.activeSelf) Geo.SetScale(repair.transform, o.Repair.IsEmpty ? 0f : 1f);
            if (repair && repair.gameObject.activeSelf && !o.Repair.IsEmpty)
            {
                var rt = (RectTransform)repair.transform;
                Geo.Place(_canvas, rt, o.Repair);
                foreach (Transform c in rt)
                {
                    if (!(c is RectTransform cr)) continue;
                    if (c.name == "Image")
                    {
                        Geo.Place(_canvas, cr, CraftingMath.Inset(o.Repair, o.Repair.W * 0.27f, o.Repair.H * 0.27f));
                        if (c.TryGetComponent<Image>(out var gi))
                        {
                            if (!gi.enabled) gi.enabled = true;
                            if (!Mathf.Approximately(gi.canvasRenderer.GetAlpha(), 1f)) gi.canvasRenderer.SetAlpha(1f);
                        }
                    }
                    else if (c.name == "Glow") Geo.Place(_canvas, cr, CraftingMath.Inset(o.Repair, -o.Repair.W * 0.08f, -o.Repair.H * 0.08f));
                }
            }
        }

        // Glyph left of the centred label: craft in the craft tab, upgrade otherwise.
        private void CraftGlyph(TMP_Text label, Box b)
        {
            if (!_craftGlyph) return;
            var mark = _craftGlyph.GetComponent<SkinMark>();
            var role = _gui.InCraftTab() ? "craft.glyph.craft" : "craft.glyph.upgrade";
            if (mark && mark.Role != role)
            {
                mark.Role = role;
                mark.Outcome = Restyler.Apply(_craftGlyph, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
                _craftGlyph.enabled = mark.Outcome == Outcome.Styled;
            }
            if (!_craftGlyph.enabled) return;
            var g = b.H * 0.5f;
            var textW = Mathf.Min(b.W * 0.7f, label.preferredWidth);
            var x = b.X + (b.W - textW) / 2f - g - b.H * 0.15f;
            Geo.Place(_canvas, _craftGlyph.rectTransform, new Box(Mathf.Max(b.X + b.H * 0.3f, x), b.Y + (b.H - g) / 2f, g, g));
        }

        // Places a graphic keeping its aspect (icons).
        private void PlaceKeep(Image image, Box b, bool keep)
        {
            if (!image) return;
            if (keep && !image.preserveAspect) image.preserveAspect = true;
            Geo.Place(_canvas, image.rectTransform, b);
        }

        private void PlaceKeep(RectTransform rt, Box b, bool keep)
        {
            if (rt.TryGetComponent<Image>(out var image)) PlaceKeep(image, b, keep);
            else Geo.Place(_canvas, rt, b);
        }

        // One label: alignment, single line with ellipsis, auto-size between the floor and `size` (canvas units).
        private void Label(TMP_Text label, TextAlignmentOptions align, float size, float min, bool upper = false)
        {
            if (label.alignment != align) label.alignment = align;
            if (label.textWrappingMode != TextWrappingModes.NoWrap) label.textWrappingMode = TextWrappingModes.NoWrap;
            if (label.overflowMode != TextOverflowModes.Ellipsis) label.overflowMode = TextOverflowModes.Ellipsis;
            if (upper && (label.fontStyle & FontStyles.UpperCase) != FontStyles.UpperCase) label.fontStyle |= FontStyles.UpperCase;
            var floor = Mathf.Min(size, min);
            if (!label.enableAutoSizing || Mathf.Abs(label.fontSizeMax - size) > 0.1f || Mathf.Abs(label.fontSizeMin - floor) > 0.1f)
            {
                label.fontSizeMax = size;
                label.fontSizeMin = floor;
                label.enableAutoSizing = true;
            }
        }
    }
}
