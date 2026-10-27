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
using RuneSkin.Compat;

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
        private Image _listWell, _detailWell, _vneiWell, _craftGlyph, _progressFrame;
        private Button _minus, _plus;
        private TMP_InputField _amountField;
        private TMP_Text _amountText;
        // Stepper amount (1 = single craft, vanilla Shift still multi-crafts), its max for the selected recipe (cached
        // 0.25 s: HaveRequirements also counts ValheimPlus CraftFromChest chests), vanilla's m_multiCraftAmount to restore.
        private int _amount = 1, _max = 1, _vanillaMulti;
        private bool _amountOwned;
        private Recipe _maxRecipe;
        private float _maxAt;
        private Box _troughBox;
        private RowArrows _tabArrows, _catArrows, _subArrows;
        private readonly CraftingTokens _t = new CraftingTokens();
        private readonly CraftingInput _in = new CraftingInput();
        private CraftingLayout _layout;
        private int _hash;
        private int _tabOffset, _catOffset, _subOffset;
        private bool _frameReady;
        private int _rowCount = -1, _rowFirst, _rowsTheme = -1;
        private float _rowPitch = -1f;
        private int _reqCells = -1;
        private readonly List<RectTransform> _tabs = new List<RectTransform>(8);
        private readonly List<RectTransform> _cats = new List<RectTransform>(16);
        private readonly List<RectTransform> _subs = new List<RectTransform>(16);
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
            Compat.FadePark.Attach(gui); // VNEI's hidden browser out of the inventory's fade group
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
            foreach (var child in Children.Of(_crafting))
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
            // craft_button_panel's own black plate (alpha 0.56, prefab) = the black box behind the progress bar.
            if (ButtonPanel() is RectTransform bp && bp.TryGetComponent<Image>(out var bpi)) bpi.enabled = false;
        }

        // Vanilla craft_button_panel: parent of m_craftButton and m_craftProgressPanel.
        private RectTransform ButtonPanel() => _gui.m_craftButton ? _gui.m_craftButton.transform.parent as RectTransform : null;

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
            _vneiWell = OwnImage(Prefix + "VneiWell", "craft.well", "VNEI browser well (frame_inset)", 0);
            _vneiWell.enabled = false;
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
            BuildAmount();
            BuildProgress();
            _tabArrows = new RowArrows(_crafting, Prefix + "Tab", () => _tabOffset--, () => _tabOffset++);
            _catArrows = new RowArrows(_crafting, Prefix + "Category", () => _catOffset--, () => _catOffset++);
            _subArrows = new RowArrows(_crafting, Prefix + "SubCategory", () => _subOffset--, () => _subOffset++);
        }

        // Amount stepper [-][N][+] left of the craft button (craft tab only). It drives vanilla's own multi-craft path:
        // m_multiCraftAmount (InventoryGui.cs:97) + the touch arm m_touchMultiCrafting (read by UpdateRecipe :1498 for the
        // label / requirement check and by OnCraftPressed :2059 for the craft), set in their prefixes (Arm). Vanilla has
        // no amount control on desktop (the touch path is a long press, :2041-2047), so the stepper is ours; Shift
        // (AltPlace) stays vanilla.
        private void BuildAmount()
        {
            var panel = ButtonPanel();
            if (!panel) return;
            _minus = StepButton(panel, Prefix + "AmountMinus", "craft.amount.glyph.minus", () => SetAmount(_amount - 1));
            _plus = StepButton(panel, Prefix + "AmountPlus", "craft.amount.glyph.plus", () => SetAmount(_amount + 1));
            var field = new GameObject(Prefix + "AmountField", typeof(RectTransform)).AddComponent<Image>();
            field.rectTransform.SetParent(panel, false);
            var mark = DollCell.Mark(field, "craft.amount.field", "craft amount field (recessed well)");
            mark.Outcome = Restyler.Apply(field, mark, ThemeTokens.Role(mark.Role)) ? Outcome.Styled : Outcome.Degraded;
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            area.SetParent(field.rectTransform, false);
            Stretch(area);
            var text = NewText("Text", area, "1");
            _amountField = field.gameObject.AddComponent<TMP_InputField>();
            _amountField.textViewport = area;
            _amountField.textComponent = text;
            _amountField.targetGraphic = field;
            _amountField.transition = Selectable.Transition.None;
            _amountField.navigation = new Navigation { mode = Navigation.Mode.None };
            _amountField.contentType = TMP_InputField.ContentType.IntegerNumber;
            _amountField.characterLimit = 3;
            _amountField.SetTextWithoutNotify("1");
            _amountField.onEndEdit.AddListener(s => SetAmount(CraftingMath.ParseAmount(s, _amount, MaxAmount())));
            _amountText = text;
            _vanillaMulti = _gui.m_multiCraftAmount;
            ShowAmount(false);
        }

        // Step glyph = one sprite pair of the same stroke (glyph_minus / glyph_plus, 48 px, 12 px bars) in one colour
        // (roles craft.amount.glyph.*): the old TMP en dash "–" drew a hairline beside the bold "+" and was dimmed to
        // alpha 0.35 at amount 1 (0.25.0 user report: [-] barely visible). The disabled step keeps its plate only.
        private Button StepButton(RectTransform parent, string name, string glyphRole, Action click)
        {
            var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            image.rectTransform.SetParent(parent, false);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            // Styled after the Button exists: the role's sprite states go on its targetGraphic.
            var mark = DollCell.Mark(image, "craft.amount.step", "craft amount step button (square glyph plate)");
            mark.Outcome = Restyler.Apply(image, mark, ThemeTokens.Role(mark.Role)) ? Outcome.Styled : Outcome.Degraded;
            button.onClick.AddListener(() => click());
            var glyph = new GameObject("Glyph", typeof(RectTransform)).AddComponent<Image>();
            glyph.rectTransform.SetParent(image.rectTransform, false);
            glyph.raycastTarget = false;
            glyph.preserveAspect = true;
            var gm = DollCell.Mark(glyph, glyphRole, "craft amount step glyph (" + glyphRole + ")");
            gm.Outcome = Restyler.Apply(glyph, gm, ThemeTokens.Role(glyphRole)) ? Outcome.Styled : Outcome.Degraded;
            glyph.enabled = gm.Outcome == Outcome.Styled;
            return button;
        }

        private TMP_Text NewText(string name, RectTransform parent, string value)
        {
            var t = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            t.rectTransform.SetParent(parent, false);
            Stretch(t.rectTransform);
            var src = CraftLabel();
            if (src)
            {
                t.font = src.font;
                t.fontSharedMaterial = src.fontSharedMaterial;
                t.fontStyle = src.fontStyle;
            }
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.color = ThemeTokens.GetColor("color.text.body", Color.white);
            t.text = value;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private TMP_Text CraftLabel() => _gui.m_craftButton ? _gui.m_craftButton.GetComponentInChildren<TMP_Text>(true) : null;

        // Progress state: vanilla progressbar (GuiBar m_craftProgressBar: bkg trough + bar fill) restyled to the craft row kit
        // (craft_bar_trough / craft_bar_fill, roles craft.progress.*) inside craft_bar_frame (own image), approved mockup craft-progress.png.
        private void BuildProgress()
        {
            var panel = _gui.m_craftProgressPanel;
            var bar = _gui.m_craftProgressBar;
            if (!panel || !bar) return;
            _progressFrame = new GameObject(Prefix + "ProgressFrame", typeof(RectTransform)).AddComponent<Image>();
            _progressFrame.rectTransform.SetParent(panel, false);
            _progressFrame.rectTransform.SetSiblingIndex(bar.transform.GetSiblingIndex() + 1);
            _progressFrame.raycastTarget = false;
            var mark = DollCell.Mark(_progressFrame, "craft.progress.frame", "craft progress frame (craft_bar_frame)");
            mark.Outcome = Restyler.Apply(_progressFrame, mark, ThemeTokens.Role(mark.Role)) ? Outcome.Styled : Outcome.Degraded;
            _progressFrame.enabled = mark.Outcome == Outcome.Styled;
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
            var f = list ? list.Find(ModCompat.MtCraftFilter) as RectTransform : null;
            return f && f.gameObject.activeInHierarchy ? f : null;
        }

        // ---------------------------------------------------------------- classification

        private ScreenContext Context()
        {
            var ctx = new ScreenContext(ScreenId, OwnershipRegistry.Crafting);
            var vnei = _crafting.Find(ModCompat.VneiPanel);
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
                else if (t.name == ModCompat.MtChainTree) ctx.Role(image, "craft.missing", "RecipeChains 'missing' box (MorgottTweaks; RuneSkin adds its tree scroll)");
                else if (image.GetComponent<ScrollRect>()) ctx.Role(image, "skip", "recipe scroll view hit area (drawn clear, list well behind)");
                else if (_gui.m_craftingStationLevelRoot && t == _gui.m_craftingStationLevelRoot) ctx.Role(image, "craft.badge", "station level badge (InventoryGui.m_craftingStationLevelRoot)");
                else if (_gui.m_craftingStationIcon && image == _gui.m_craftingStationIcon) ctx.Role(image, "content", "station icon (CraftingStation.m_icon)");
                // Vanilla m_craftCancelButton (craft_button_panel/Progress/CraftCancelButton, a sprite-less alpha-0 hit area
                // stretched over the bar in the prefab): moved beside the bar (ProgressCue) and plated as the craft button,
                // so its own onClick -> OnCraftCancelPressed (:2008, wired in Awake :406) stays the only cancel logic.
                else if (_gui.m_craftCancelButton && t == _gui.m_craftCancelButton.transform) ctx.Role(image, "craft.button", "craft cancel button (vanilla m_craftCancelButton beside the bar, craft button plate)");
                else if (ButtonPanel() && t == ButtonPanel()) ctx.Role(image, "skip", "craft_button_panel black plate (hidden: the black box behind the bar)");
                else if (_gui.m_craftProgressBar && t.parent == _gui.m_craftProgressBar.transform && t.name == "bkg") ctx.Role(image, "craft.progress.trough", "craft progress trough (craft_bar_trough)");
                else if (_gui.m_craftProgressBar && _gui.m_craftProgressBar.m_bar && t == _gui.m_craftProgressBar.m_bar) ctx.Role(image, "craft.progress.fill", "craft progress fill (craft_bar_fill, teal)");
                else if (_gui.m_craftButton && image == _gui.m_craftButton.targetGraphic) ctx.Role(image, "craft.button", "craft / upgrade button (button_craft_*, falls back to button_*)");
                else if (t.name == DescView && t.parent && t.parent.name == "Decription") ctx.Role(image, "skip", "description scroll view hit area (RuneSkin, clear)");
                else if (_gui.m_repairButton && image == _gui.m_repairButton.targetGraphic) ctx.Role(image, "craft.repair", "repair button (round)");
                else if (_gui.m_repairButton && t.parent == _gui.m_repairButton.transform && t.name == "Image") ctx.Role(image, "craft.repair.glyph", "repair button hammer glyph");
                else if (_gui.m_repairButtonGlow && image == _gui.m_repairButtonGlow) ctx.Role(image, "content", "repair glow (vanilla pulses it; Epic Loot finds RepairButton/Glow)");
                else if (IsTab(t, out var tabTarget)) ctx.Role(image, tabTarget ? (IsCategory(t) ? "craft.category" : "craft.tab") : t.name == "Selected" ? "skip" : "content",
                    tabTarget ? "crafting tab button (selected = interactable off: sprite disabled state)" : "tab child");
                else if (t.name == ModCompat.MtCraftSearch && image.GetComponent<Selectable>()) ctx.Role(image, "craft.search", "CraftingFilter search field");
                else if (Row(t, out var row)) RowImage(ctx, image, row);
                else if (IsRequirementCell(t)) ctx.Role(image, MissingShown(t.gameObject) ? "craft.socket.missing" : "craft.socket", "requirement socket (m_recipeRequirementList cell)");
                else if (_gui.m_minStationLevelIcon && t == _gui.m_minStationLevelIcon.transform.parent && t.parent == Requirements()) ctx.Role(image, "craft.socket", "required station level: its own requirement socket (level, parent of MinLevel)");
                else if (_gui.m_minStationLevelIcon && t == _gui.m_minStationLevelIcon.transform) ctx.Role(image, "content", "required station level icon (MinLevel, shows the station's icon)");
                else if (t.name == "res_icon" || t.parent && t.parent.name == "level" || t.name == "MinLevel") ctx.Role(image, "content", "requirement icon / min station level glyph");
                else if (t.name == ModCompat.MtChainTree) ctx.Role(image, "craft.missing", "RecipeChains 'missing' box (MorgottTweaks)");
                else if (_gui.m_recipeIcon && image == _gui.m_recipeIcon || t.name == "magicItemIconBG" || t.name == ModCompat.MtCategoryIcon) ctx.Role(image, "content", "item icon / magic background / category icon");
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
                else if (Row(t, out _)) ctx.Role(text, CraftingMath.RowTextRole, RowTextReason);
                else if (CancelLabel() == text) ctx.Role(text, "skip", "craft cancel label: the craft label's face (ProgressCue)");
                else if (_gui.m_craftCancelButton && t.IsChildOf(_gui.m_craftCancelButton.transform)) ctx.Role(text, "skip", "craft cancel gamepad hint (vanilla, kept as is)");
                else if (_gui.m_craftProgressPanel && t.parent == _gui.m_craftProgressPanel) ctx.Role(text, "skip", "craft progress state text: craft label face + outline (ProgressCue)");
                else if (IsAmountPart(t)) ctx.Role(text, "skip", "craft amount stepper text: craft label face (BuildAmount)");
            }
            return ctx;
        }

        private const string RowTextReason = "recipe row name / quality (body sans, mockup-1)";

        // One recipe row image's role (CraftingMath.RowImageRole): the same rule for the full pass and the per-row pass.
        private void RowImage(ScreenContext ctx, Image image, Transform row)
        {
            var t = image.transform;
            var child = t.parent == row;
            var button = image.GetComponentInParent<Button>(true);
            var target = button && button.targetGraphic == image;
            var n = t.name;
            var canCraft = !child || n != "bkg" || CanCraft(row); // m_availableRecipes scan for the strip only
            ctx.Role(image, CraftingMath.RowImageRole(n, child, target, canCraft), CraftingMath.RowImageReason(n, child, target));
        }

        // The rows' own context: exactly the roles Context() gives a row's graphics (rows are leaves of the crafting root:
        // no earlier Context() rule matches inside a row), without rebuilding the whole crafting root's context - that
        // walked every graphic of m_crafting (~1500 with VNEI's pool) with name / parent tests on each open.
        private readonly List<Graphic> _rowGraphics = new List<Graphic>(64);

        private ScreenContext RowContext(List<InventoryGui.RecipeDataPair> rows)
        {
            var ctx = new ScreenContext(ScreenId, OwnershipRegistry.Crafting);
            for (var n = 0; n < rows.Count; n++)
            {
                var e = rows[n].InterfaceElement;
                if (!e) continue;
                var row = e.transform;
                _rowGraphics.Clear();
                row.GetComponentsInChildren(true, _rowGraphics);
                foreach (var g in _rowGraphics)
                {
                    if (!g) continue;
                    if (g is Image image)
                    {
                        if (image.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                        RowImage(ctx, image, row);
                    }
                    else if (g is TMP_Text text) ctx.Role(text, CraftingMath.RowTextRole, RowTextReason);
                    else if (!(g is TMP_SubMeshUI)) ctx.Role(g, "content", g.GetType().Name + " (input caret / mod graphic, kept)");
                }
            }
            _rowGraphics.Clear();
            return ctx;
        }

        private bool IsAmountPart(Transform t) =>
            _minus && t.IsChildOf(_minus.transform) || _plus && t.IsChildOf(_plus.transform) || _amountField && t.IsChildOf(_amountField.transform);

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
                if (p.parent && (p.parent == tabs || IsFilterRow(p.parent)) && p.GetComponent<Button>())
                {
                    target = p == t && p.GetComponent<Button>().targetGraphic && p.GetComponent<Button>().targetGraphic.transform == t;
                    return true;
                }
            }
            return false;
        }

        private static bool IsCategory(Transform t) => t.parent && IsFilterRow(t.parent);

        // CraftingFilter's category row (Tabs) or its sub-group row (Subs).
        private static bool IsFilterRow(Transform row) =>
            (row.name == "Tabs" || row.name == ModCompat.MtCraftSubRow) && row.parent && row.parent.name == ModCompat.MtCraftFilter;

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
                // Closed: the host stays active, fades out with the screen and is parked with it (InventorySkin.Park gates
                // RuneSkinInventory, its parent). Deactivating it here dropped the crafting frame the frame Hide ran (before
                // the 0.15 s fade) and re-enabled its graphics in the next open frame (Apply). Only a crafting panel that
                // left the hierarchy takes its frame with it.
                if (_host && _host.gameObject.activeSelf && !_crafting.gameObject.activeInHierarchy) _host.gameObject.SetActive(false);
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
            // The docked crafting column (follows the inventory block) plus the window's own offset (its RuneWindowHeader).
            var box = inv.CraftingBox;
            if (box.IsEmpty) return;
            box = Restyle.WindowMove.Place(Restyle.WindowMove.Crafting, box, _canvas);
            Collect();
            ReadTokens();
            var i = _in;
            i.Panel = box;
            i.Scale = inv.SkinScaleValue;
            i.CapInset = CapInset();
            i.WindowHeaderH = Restyle.WindowHeaderMath.HeaderHeight(ThemeTokens.GetFloat("size.win.header", 40f), inv.SkinScaleValue);
            var hash = Hash(i);
            if (_layout == null || hash != _hash)
            {
                _hash = hash;
                _layout = CraftingMath.Compute(i, _t);
                _tabOffset = _layout.Tabs.First;
                _catOffset = _layout.Categories.First;
                _subOffset = _layout.SubCategories.First;
            }
            Apply(_layout, box);
        }

        private void Collect()
        {
            _tabs.Clear();
            var tabs = TabsRoot();
            // Index loops: foreach over a Transform allocates an enumerator per call (per frame here).
            if (tabs)
                for (var n = 0; n < tabs.childCount; n++)
                {
                    var c = tabs.GetChild(n);
                    if (c.gameObject.activeSelf && c.TryGetComponent<Button>(out _)) _tabs.Add((RectTransform)c);
                }
            _cats.Clear();
            var filter = Filter();
            var catRoot = filter ? filter.Find("Tabs") : null;
            if (catRoot)
                for (var n = 0; n < catRoot.childCount; n++)
                {
                    var c = catRoot.GetChild(n);
                    if (c.gameObject.activeSelf && c.TryGetComponent<Button>(out _)) _cats.Add((RectTransform)c);
                }
            _subs.Clear();
            var subRoot = filter ? filter.Find(ModCompat.MtCraftSubRow) : null;
            if (subRoot && subRoot.gameObject.activeSelf)
                for (var n = 0; n < subRoot.childCount; n++)
                {
                    var c = subRoot.GetChild(n);
                    if (c.gameObject.activeSelf && c.TryGetComponent<Button>(out _)) _subs.Add((RectTransform)c);
                }
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
            i.SubCategories = _subs.Count;
            i.Search = filter && filter.Find("Search/MT_CraftSearch");
            i.Requirements = _reqActive.Count + (_gui.m_minStationLevelIcon && _gui.m_minStationLevelIcon.gameObject.activeSelf ? 1 : 0);
            // Round repair button only while something can be repaired (vanilla UpdateRepair: interactable); otherwise
            // the craft button takes the whole row (mockup-1: no empty socket beside it).
            i.Repair = RepairShown();
            i.CraftType = _gui.m_itemCraftType && _gui.m_itemCraftType.gameObject.activeSelf;
            i.Quality = _gui.m_qualityPanel && _gui.m_qualityPanel.gameObject.activeSelf;
            i.TabOffset = _tabOffset;
            i.CategoryOffset = _catOffset;
            i.SubCategoryOffset = _subOffset;
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
                h = h * 31 + i.WindowHeaderH.GetHashCode();
                h = h * 31 + i.Tabs;
                h = h * 31 + i.Categories * 7 + i.Requirements * 101;
                h = h * 31 + (i.Search ? 1 : 0) + (i.Repair ? 2 : 0) + (i.CraftType ? 4 : 0) + (i.Quality ? 8 : 0);
                h = h * 31 + i.TabOffset * 13 + i.CategoryOffset;
                h = h * 31 + i.SubCategories * 17 + i.SubCategoryOffset;
                h = h * 31 + ThemeTokens.Revision;
                return h;
            }
        }

        private float CapInset() => PanelDecor.KeepOut(_host, _canvas);

        private int _tokensRevision = -1;

        // Theme sizes change only with the theme (runeskin_reload / watcher): read once per revision, not per frame.
        private void ReadTokens()
        {
            if (_tokensRevision == ThemeTokens.Revision) return;
            _tokensRevision = ThemeTokens.Revision;
            float F(string token, float fallback) => ThemeTokens.GetFloat(token, fallback);
            var t = _t;
            t.Pad = F("size.craft.pad", t.Pad); t.Gap = F("size.craft.gap", t.Gap); t.HeaderH = F("size.craft.header", t.HeaderH);
            t.TabH = F("size.craft.tab.h", t.TabH); t.TabW = F("size.craft.tab.w", t.TabW); t.TabMinW = F("size.craft.tab.min", t.TabMinW);
            t.ArrowW = F("size.craft.arrow", t.ArrowW); t.CategoryH = F("size.craft.category", t.CategoryH); t.SubCategoryH = F("size.craft.subcategory", t.SubCategoryH); t.SearchH = F("size.craft.search", t.SearchH);
            t.ListShare = F("share.craft.list", t.ListShare); t.ListMinW = F("size.craft.list.min", t.ListMinW); t.RowH = F("size.craft.row", t.RowH); t.ScrollbarW = F("size.craft.scrollbar", t.ScrollbarW);
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
            // VNEI's tab hides Crafting/RecipeList and Crafting/Decription while its browser is up (MainVneiHandler
            // SetTabActive / SetTabInactive): the wells behind them follow, so neither well nor their column seam shows
            // under VNEI's grid.
            var recipeList = RecipeList();
            var detailRoot = _crafting.Find("Decription");
            WellShown(_listWell, !recipeList || recipeList.gameObject.activeSelf);
            WellShown(_detailWell, !detailRoot || detailRoot.gameObject.activeSelf);
            var vnei = _crafting.Find(ModCompat.VneiPanel) as RectTransform;
            var vneiUp = vnei && vnei.gameObject.activeSelf;
            // Docked, VNEI switches its own woodpanel off (MainVneiHandler.CreateBaseUI) and the two wells are hidden
            // above, so only the panel glass (fill_glass, alpha 0.9) was left behind its grid: the world showed through.
            // One opaque well spans VNEI's whole body instead, like the list/detail wells on the craft tab.
            WellShown(_vneiWell, vneiUp);
            if (!vneiUp) return;
            var top = o.Search.IsEmpty ? o.List.Y : o.Search.Y;
            var area = new Box(o.List.X, top, o.Detail.Right - o.List.X, o.ButtonRow.Bottom - top);
            if (_vneiWell) Geo.Place(_canvas, _vneiWell.rectTransform, area);
            Geo.Place(_canvas, vnei, area);
            VneiFit(vnei, area);
        }

        private static void WellShown(Image well, bool on)
        {
            if (!well) return;
            var want = on && well.GetComponent<SkinMark>() is SkinMark m && m.Outcome == Outcome.Styled;
            if (well.enabled != want) well.enabled = want;
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
        private Component _vneiUi;
        private (int x, int y) _vneiCells;

        private void VneiFit(RectTransform vnei, Box area)
        {
            if (!_vneiLooked)
            {
                _vneiLooked = true;
                _vneiType = HarmonyLib.AccessTools.TypeByName(ModCompat.VneiBaseUi);
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
            // VNEI(Clone) itself stays active on every tab; its browser is `root`, shown and hidden by BaseUI.SetVisibility
            // (MainVneiHandler.SetTabActive / SetTabInactive). Hidden (or parked out of the fade group, FadePark) its grid
            // size is not seen: fitted when the browser shows. Fitting it every crafting frame re-ran SetSize -> RebuildSize
            // -> SearchUI.RebuildCells (destroys and instantiates all X x Y item cells) whenever the open frames gave the
            // body another whole-cell count.
            var browser = _vneiRoot.GetValue(ui) as RectTransform;
            if (!browser || !browser.gameObject.activeInHierarchy) return;
            // Canvas units per VNEI local unit (its own scale chain).
            var u = vnei.lossyScale.x / Mathf.Max(Geo.WorldPerUnit(_canvas), 1e-6f);
            if (u <= 0f) return;
            var (x, y) = CraftingMath.VneiCells(area.W, area.H, u);
            try
            {
                // Reflection reads box their ints: asked only when the wanted cell counts or the VNEI instance changed.
                if (ui != _vneiUi || x != _vneiCells.x || y != _vneiCells.y)
                {
                    if ((int)_vneiX.GetValue(ui, null) != x || (int)_vneiY.GetValue(ui, null) != y) _vneiSetSize.Invoke(ui, new object[] { false, x, y });
                    _vneiUi = ui;
                    _vneiCells = (x, y);
                }
                if (browser != vnei)
                {
                    var w = (x * 50f + 10f) * u;
                    var h = (y * 50f + 100f) * u;
                    Geo.Place(_canvas, browser, new Box(area.X + (area.W - w) / 2f, area.Y, w, h));
                }
            }
            catch (Exception e)
            {
                _vneiSetSize = null;
                Plugin.Log.LogWarning($"[RuneSkin] VNEI grid fit disabled: {e.GetBaseException().Message}");
            }
        }

        // The window's one header (RuneWindowHeader: drag / double-click reset, position persisted): station icon in the
        // icon slot, the station name centred (rune face, mirrored from m_craftingStationName), the level star in the aux
        // slot - the same band, plate and title as every other window.
        private void Header(CraftingLayout o)
        {
            var header = Restyle.RuneWindowHeader.Ensure(_crafting, Restyle.WindowMove.Crafting);
            if (!header) return;
            header.Frame = PanelDecor.Of(_host);
            if (_gui.m_craftingStationName) header.Adopt(_gui.m_craftingStationName, "$runeskin_win_crafting");
            else header.Fallback("$runeskin_win_crafting").Text("$runeskin_win_crafting");
            var icon = _gui.m_craftingStationIcon && _gui.m_craftingStationIcon.gameObject.activeInHierarchy && _gui.m_craftingStationIcon.sprite ? _gui.m_craftingStationIcon : null;
            var level = _gui.m_craftingStationLevelRoot;
            var levelOn = level && level.gameObject.activeInHierarchy;
            // Star at least big enough for a readable digit (floor.text.number.px on screen): the shared header's aux slot
            // alone (0.8 x size.win.header) left a ~8 px digit box at 720p, shorter than one line at the auto-size floor,
            // and TMP's Ellipsis overflow dropped the only line - the star showed no level.
            var floor = ThemeTokens.GetFloat("floor.text.number.px", 14f) / Mathf.Max(0.01f, _rootCanvas ? _rootCanvas.scaleFactor : 1f);
            header.Place(_canvas, o.Header, icon, levelOn ? CraftingMath.LevelSide(o.Header.H * Restyle.WindowHeaderMath.IconShare, floor) : 0f);
            if (icon) icon.preserveAspect = true;
            if (levelOn)
            {
                var b = CraftingMath.LevelBadge(header.AuxBox, floor, out var size);
                Geo.Place(_canvas, level, b);
                if (_gui.m_craftingStationLevel) LevelDigit(_gui.m_craftingStationLevel, b, size);
            }
        }

        // Digit centred on the star (its round recess: centre (76,75) of 150x144 = the middle), a fixed size that never
        // auto-shrinks and an overflow mode that never drops the line (vanilla sets only the text, InventoryGui.cs:1480).
        private void LevelDigit(TMP_Text digit, Box star, float size)
        {
            Geo.Place(_canvas, digit.rectTransform, star);
            if (digit.alignment != TextAlignmentOptions.Center) digit.alignment = TextAlignmentOptions.Center;
            if (digit.textWrappingMode != TextWrappingModes.NoWrap) digit.textWrappingMode = TextWrappingModes.NoWrap;
            if (digit.overflowMode != TextOverflowModes.Overflow) digit.overflowMode = TextOverflowModes.Overflow;
            if (digit.enableAutoSizing) digit.enableAutoSizing = false;
            if (Mathf.Abs(digit.fontSize - size) > 0.1f) digit.fontSize = size;
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
                foreach (var c in Children.Of(tab))
                {
                    if (NameCache.Of(c) == "Text" && c is RectTransform tr && c.TryGetComponent<TMP_Text>(out var label))
                    {
                        Geo.Place(_canvas, tr, cell);
                        Label(label, TextAlignmentOptions.Center, size, 12f, upper: true);
                    }
                    else if (NameCache.Of(c) == "Selected" && c.TryGetComponent<Image>(out var sel) && sel.enabled) sel.enabled = false;
                }
            }
            _tabArrows.Show(_canvas, o.Tabs);
        }

        private void Categories(CraftingLayout o)
        {
            var filter = Filter();
            if (!filter)
            {
                _catArrows.Show(_canvas, new RowFit());
                _subArrows.Show(_canvas, new RowFit());
                return;
            }
            var tabsRoot = filter.Find("Tabs") as RectTransform;
            if (tabsRoot && !o.CategoryRow.IsEmpty) Geo.Place(_canvas, tabsRoot, o.CategoryRow);
            FilterTabs(_cats, o.Categories, _catArrows);
            // Sub-group row (MT_CraftSub_<group>: skill / slot / ammo icon from CraftingFilter, "All" gets the kit glyph).
            var subRoot = filter.Find(ModCompat.MtCraftSubRow) as RectTransform;
            if (subRoot && !o.SubCategoryRow.IsEmpty) Geo.Place(_canvas, subRoot, o.SubCategoryRow);
            FilterTabs(_subs, o.SubCategories, _subArrows);
            SearchField(o, filter.Find("Search") as RectTransform);
        }

        // One CraftingFilter tab row: square cells, kit glyph or the filter's own icon / label, scroll arrows.
        private void FilterTabs(List<RectTransform> tabs, RowFit fit, RowArrows arrows)
        {
            for (var n = 0; n < tabs.Count; n++)
            {
                var tab = tabs[n];
                var cell = n < fit.Cells.Length ? fit.Cells[n] : default;
                if (!Visible(tab, cell)) continue;
                Geo.Place(_canvas, tab, cell);
                var inner = CraftingMath.Inset(cell, cell.W * 0.19f, cell.H * 0.19f);
                var glyph = CategoryGlyph(tab, inner);
                foreach (var c in Children.Of(tab))
                {
                    if (!(c is RectTransform rt)) continue;
                    if (glyph && (NameCache.Of(c) == ModCompat.MtCategoryIcon || NameCache.Of(c) == "Text"))
                    {
                        // The kit glyph replaces CraftingFilter's item icon / "All" text (mockup: one glyph family).
                        if (c.TryGetComponent<Graphic>(out var g) && g.enabled) g.enabled = false;
                        continue;
                    }
                    if (NameCache.Of(c) == ModCompat.MtCategoryIcon) Geo.Place(_canvas, rt, inner);
                    else if (NameCache.Of(c) == "Text" && c.TryGetComponent<TMP_Text>(out var label))
                    {
                        Geo.Place(_canvas, rt, cell);
                        if (label.margin != Vector4.zero) label.margin = Vector4.zero;
                        Label(label, TextAlignmentOptions.Center, cell.H * 0.36f, 10f);
                    }
                    else if (NameCache.Of(c) == "Selected" && c.TryGetComponent<Image>(out var sel) && sel.enabled) sel.enabled = false;
                }
            }
            arrows.Show(_canvas, fit);
        }

        private void SearchField(CraftingLayout o, RectTransform search)
        {
            var field = search ? search.Find(ModCompat.MtCraftSearch) as RectTransform : null;
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

        private readonly Dictionary<RectTransform, string> _catKeys = new Dictionary<RectTransform, string>();

        private Image CategoryGlyph(RectTransform tab, Box inner)
        {
            // Glyph role suffix per tab object, cached (name + Substring allocated two strings per tab per frame; CraftingFilter
            // keeps one tab object per group, never renamed). Categories: CategoryGlyphs. Sub groups: "All" = the category
            // glyph, MT_CraftSub_<Group> = glyph_sub_<group> when the theme has it (vanilla kinds); others (modded skills)
            // keep CraftingFilter's skill / item icon.
            if (!_catKeys.TryGetValue(tab, out var key))
            {
                var tabName = NameCache.Of(tab);
                if (tabName.StartsWith(ModCompat.MtCraftTabPrefix, StringComparison.Ordinal))
                    key = CategoryGlyphs.TryGetValue(tabName.Substring(ModCompat.MtCraftTabPrefix.Length), out var k) ? "cat." + k : null;
                else if (tabName.StartsWith(ModCompat.MtCraftSubPrefix, StringComparison.Ordinal))
                {
                    var group = tabName.Substring(ModCompat.MtCraftSubPrefix.Length);
                    key = group == "All" ? "cat.all" : "sub." + group.ToLowerInvariant();
                    if (ThemeTokens.Role("craft.glyph." + key) == null) key = null;
                }
                _catKeys[tab] = key;
            }
            if (key == null) return null;
            var t = tab.Find(Prefix + "Glyph");
            Image image;
            if (!t)
            {
                image = new GameObject(Prefix + "Glyph", typeof(RectTransform)).AddComponent<Image>();
                image.rectTransform.SetParent(tab, false);
                image.raycastTarget = false;
                var mark = DollCell.Mark(image, "craft.glyph." + key, "CraftingFilter category / sub-group glyph");
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
            // Per-row role (craftable / unavailable) on the new rows only: vanilla re-instantiates every row on each Show
            // (InventoryGui.UpdateRecipeList), and a full pass over the crafting root (~870 graphics, run 20261027-151010:
            // 22 full passes logged over the open / close cycles) ran in the open frame.
            if (rebuilt) RestyleRows(rows);
            // Content bounds changed after vanilla SetRecipe may have centred the selection: reveal it once more.
            var sel = _gui.GetSelectedRecipeIndex();
            if (_gui.m_recipeEnsureVisible && sel >= 0 && sel < rows.Count && rows[sel].InterfaceElement)
                _gui.m_recipeEnsureVisible.CenterOnItem((RectTransform)rows[sel].InterfaceElement.transform);
        }

        // InventoryGui.UpdateRecipeList (InventoryGui.cs:1150-1157) destroys and re-instantiates every row from
        // m_recipeElementPrefab, which the root pass styled at attach: each clone carries the template's marks (Style keeps
        // a same-role, same-revision mark and only ensures its layout components), so the per-row state left is the strip's
        // craftable / unavailable role. Classified with the rows' own context (RowContext), not the whole root's.
        private void RestyleRows(List<InventoryGui.RecipeDataPair> rows)
        {
            var ctx = RowContext(rows);
            if (ctx == null) return;
            for (var n = 0; n < rows.Count; n++)
                if (rows[n].InterfaceElement) Restyler.RestyleSubtree(rows[n].InterfaceElement.transform, _crafting, ctx);
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
            foreach (var c in Children.Of(row))
            {
                if (!(c is RectTransform rt)) continue;
                switch (NameCache.Of(c))
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
                        // Right column only while the quality level shows (upgrade tab); else the name takes the row
                        // (long names were cut at the fit floor, batch G).
                        var quality = row.Find("QualityLevel");
                        Anchors(rt, Vector2.zero, Vector2.one, new Vector2(icon + 16f, 2f), new Vector2(quality && quality.gameObject.activeSelf ? -30f : -6f, -2f));
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
            if (!_chain) _chain = _crafting.Find(ModCompat.MtChainTree) as RectTransform;
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
            var button = _chain.Find(ModCompat.MtChainCraftButton) as RectTransform;
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
                FitName(go.transform, o.RequirementCells[slot - 1]);
            }
            var min = _gui.m_minStationLevelIcon;
            var levelSocket = min ? min.transform.parent as RectTransform : null;
            if (levelSocket == Requirements()) levelSocket = null;
            var levelShown = min && min.gameObject.activeSelf && slot < o.RequirementCells.Length;
            // The "level" socket image (vanilla parent of MinLevel, restyled craft.socket) stays active when vanilla
            // hides only MinLevel (recipe without a station level, InventoryGui.cs:1554): unplaced, it showed as an
            // empty tile at its old spot over the first material cell (user screenshot 2026-10-22). Hidden with it.
            if (levelSocket) Geo.SetScale(levelSocket, levelShown ? 1f : 0f);
            if (levelShown)
            {
                // Station level requirement: a socket like the materials (its "level" parent), the station's own icon
                // (MinLevel) in the face, the level digit (level_text) in the count band.
                var cell = o.RequirementCells[slot];
                var socket = levelSocket;
                if (socket) Geo.Place(_canvas, socket, cell);
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
            foreach (var c in Children.Of(cell))
            {
                if (!(c is RectTransform rt)) continue;
                switch (NameCache.Of(c))
                {
                    case "res_name":
                        Anchors(rt, new Vector2(0.06f, 0.67f), new Vector2(0.94f, 0.98f), Vector2.zero, Vector2.zero);
                        if (c.TryGetComponent<TMP_Text>(out var n))
                        {
                            // Size and lines from FitName (one line shrinking to the min token, then two lines).
                            if (n.alignment != TextAlignmentOptions.Center) n.alignment = TextAlignmentOptions.Center;
                            if (n.overflowMode != TextOverflowModes.Ellipsis) n.overflowMode = TextOverflowModes.Ellipsis;
                            if (n.enableAutoSizing) n.enableAutoSizing = false;
                            if (n.maxVisibleLines != 2) n.maxVisibleLines = 2;
                            _nameFit.Remove(n);
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

        private struct NameFitState
        {
            public string Text;
            public float W, H, Max;
        }

        private readonly Dictionary<TMP_Text, NameFitState> _nameFit = new Dictionary<TMP_Text, NameFitState>();

        // Material name above the icon (0.10.2, user screenshot 2026-10-22: "...анина" / "Одуванчи..." clipped): one
        // line auto-sized from size.craft.font.label down to size.craft.font.label.min, then two lines at the best
        // word split (LabelFit.NameSize), always inside the name box of the socket. Measured only when the text or the
        // box changes (vanilla rewrites the same text every frame).
        private void FitName(Transform cell, Box box)
        {
            var t = cell.Find("res_name");
            if (box.IsEmpty || !t || !t.TryGetComponent<TMP_Text>(out var n) || !n.gameObject.activeInHierarchy) return;
            var text = n.text;
            var r = n.rectTransform.rect;
            var w = r.width - n.margin.x - n.margin.z;
            var h = r.height - n.margin.y - n.margin.w;
            var max = ThemeTokens.GetFloat("size.craft.font.label", 13f) * _in.Scale;
            if (_nameFit.TryGetValue(n, out var was) && string.Equals(was.Text, text, StringComparison.Ordinal) &&
                Mathf.Abs(was.W - w) < 0.1f && Mathf.Abs(was.H - h) < 0.1f && Mathf.Abs(was.Max - max) < 0.01f) return;
            _nameFit[n] = new NameFitState { Text = text, W = w, H = h, Max = max };
            if (string.IsNullOrEmpty(text) || w <= 1f || h <= 1f) return;
            var min = ThemeTokens.GetFloat("size.craft.font.label.min", 10f) * _in.Scale;
            if (n.enableAutoSizing) n.enableAutoSizing = false;
            var unit = Mathf.Max(0.01f, n.fontSize);
            var one = n.GetPreferredValues(text, float.PositiveInfinity, float.PositiveInfinity);
            var lineH = one.y / unit;
            var oneW = one.x / unit;
            var split = LabelFit.BestSplit(text, (i, j) => n.GetPreferredValues(text.Substring(i, j - i), float.PositiveInfinity, float.PositiveInfinity).x / unit, out var twoW);
            // Two-line names set tight (theme size.craft.label.spacing, TMP em/100): the socket's name band is short.
            var spacing = split > 0 ? ThemeTokens.GetFloat("size.craft.label.spacing", -22f) : 0f;
            if (!Mathf.Approximately(n.lineSpacing, spacing)) n.lineSpacing = spacing;
            var twoH = split > 0 ? n.GetPreferredValues(text.Substring(0, split) + "\n" + text.Substring(split + 1), float.PositiveInfinity, float.PositiveInfinity).y / unit * 1.04f : 0f;
            var size = LabelFit.NameSize(max, min, w, h, lineH, oneW, twoW, out var lines, twoH);
            n.fontSize = size;
            if (lines == 1 && !Mathf.Approximately(n.lineSpacing, 0f)) n.lineSpacing = 0f;
            var wrap = lines == 2 ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            if (n.textWrappingMode != wrap) n.textWrappingMode = wrap;
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
            if (CraftingMath.TryParseHaveNeed(text.text, out var have, out var need))
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
                var progress = _gui.m_craftProgressPanel && _gui.m_craftProgressPanel.gameObject.activeSelf;
                var stepper = !progress && AmountShown();
                var row = CraftingMath.ActionRow(o.Craft, stepper, o.Craft.H * 0.1f,
                    innerShare: ThemeTokens.GetFloat("share.craft.step.gap", 0.8f));
                Geo.Place(_canvas, panel, o.Craft);
                Geo.Place(_canvas, (RectTransform)_gui.m_craftButton.transform, row.Button);
                if (_gui.m_craftProgressPanel) Geo.Place(_canvas, (RectTransform)_gui.m_craftProgressPanel, o.Craft);
                var label = CraftLabel();
                if (label)
                {
                    Geo.Place(_canvas, label.rectTransform, row.Button);
                    Label(label, TextAlignmentOptions.Center, o.Craft.H * 0.42f, ThemeTokens.GetFloat("size.craft.button.font.min", 11f), upper: true);
                    Condense(label, squeeze: false); // PlateLabelFit sets its margins in the label's local units: no x-scale; fit = font step-down
                    CraftGlyph(label, row.Button);
                }
                Amount(row, stepper);
                ProgressCue(row);
            }
            var repair = _gui.m_repairButton;
            if (repair && repair.gameObject.activeSelf) Geo.SetScale(repair.transform, o.Repair.IsEmpty ? 0f : 1f);
            if (repair && repair.gameObject.activeSelf && !o.Repair.IsEmpty)
            {
                var rt = (RectTransform)repair.transform;
                Geo.Place(_canvas, rt, o.Repair);
                foreach (var c in Children.Of(rt))
                {
                    if (!(c is RectTransform cr)) continue;
                    if (NameCache.Of(c) == "Image")
                    {
                        Geo.Place(_canvas, cr, CraftingMath.Inset(o.Repair, o.Repair.W * 0.27f, o.Repair.H * 0.27f));
                        if (c.TryGetComponent<Image>(out var gi))
                        {
                            if (!gi.enabled) gi.enabled = true;
                            if (!Mathf.Approximately(gi.canvasRenderer.GetAlpha(), 1f)) gi.canvasRenderer.SetAlpha(1f);
                        }
                    }
                    else if (NameCache.Of(c) == "Glow") Geo.Place(_canvas, cr, CraftingMath.Inset(o.Repair, -o.Repair.W * 0.08f, -o.Repair.H * 0.08f));
                }
            }
        }

        // Vanilla CraftCancelButton/Text (prefab text 'Button'): the cancel cue.
        private TMP_Text CancelLabel()
        {
            var cancel = _gui.m_craftCancelButton;
            var t = cancel ? cancel.transform.Find("Text") : null;
            return t ? t.GetComponent<TMP_Text>() : null;
        }

        // Craft progress state (approved mockup craft-progress.png, right). UpdateRecipe (InventoryGui.cs:1611-1625) swaps
        // m_craftButton for m_craftProgressPanel while m_craftTimer >= 0. Bar = the vanilla GuiBar m_craftProgressBar in the
        // craft row kit (bkg -> craft_bar_trough, bar -> teal craft_bar_fill) inside craft_bar_frame, its width given back to GuiBar
        // through its own SetWidth (GuiBar.cs, m_width cached in Awake). State text centred on the bar: the panel's own text
        // (vanilla token from Localization.textMeshStrings, Localization.cs:303-311) + " x N" while m_multiCrafting (the
        // craft button's pattern, :1572-1575; DoCrafting makes all m_multiCraftAmount at once, :1787 / :1897), bold body face
        // with the theme outline. Cancel: the vanilla m_craftCancelButton itself, moved beside the bar and plated like the
        // craft button (role craft.button); its onClick stays vanilla's OnCraftCancelPressed (:2008).
        private int _stateRevision = -1;

        private void ProgressCue(CraftRow r)
        {
            var panel = _gui.m_craftProgressPanel;
            if (!panel || !panel.gameObject.activeInHierarchy) return;
            var frameBox = r.Bar;
            var share = Mathf.Clamp(ThemeTokens.GetFloat("share.craft.progress.bar", 0.9f), 0.3f, 1f);
            frameBox = new Box(frameBox.X, frameBox.Y + frameBox.H * (1f - share) / 2f, frameBox.W, frameBox.H * share);
            var trough = frameBox;
            var capW = 0f;
            if (_progressFrame && _progressFrame.enabled)
            {
                Geo.Place(_canvas, _progressFrame.rectTransform, frameBox);
                trough = FrameInset(frameBox, out capW);
            }
            var bar = _gui.m_craftProgressBar;
            if (bar)
            {
                var root = (RectTransform)bar.transform;
                Geo.Place(_canvas, root, trough);
                var bkg = root.Find("bkg") as RectTransform;
                if (bkg) Geo.Place(_canvas, bkg, trough);
                if (bar.m_bar && (Mathf.Abs(trough.X - _troughBox.X) > 0.05f || Mathf.Abs(trough.W - _troughBox.W) > 0.05f ||
                                  Mathf.Abs(trough.Y - _troughBox.Y) > 0.05f || Mathf.Abs(trough.H - _troughBox.H) > 0.05f))
                {
                    _troughBox = trough;
                    var fill = bar.m_bar;
                    fill.pivot = new Vector2(0f, 0.5f);
                    // A defined strip inside the trough (approved mockup): inset share.craft.progress.fill.inset of its height.
                    var fin = trough.H * Mathf.Clamp(ThemeTokens.GetFloat("share.craft.progress.fill.inset", 0.08f), 0f, 0.4f);
                    var strip = new Box(trough.X + fin * 0.5f, trough.Y + fin, Mathf.Max(0f, trough.W - fin), Mathf.Max(0f, trough.H - 2f * fin));
                    Geo.Place(_canvas, fill, strip);
                    bar.SetWidth(fill.sizeDelta.x); // GuiBar scales this width by the progress in its LateUpdate
                    if (fill.TryGetComponent<Image>(out var fi)) bar.m_originalColor = fi.color;
                }
            }
            TMP_Text state = null;
            for (var n = 0; n < panel.childCount; n++)
                if (panel.GetChild(n).TryGetComponent<TMP_Text>(out var s)) { state = s; break; }
            if (state)
            {
                if (_stateRevision != ThemeTokens.Revision && WorldHud.TextLook.Apply(state, "font.body.bold", "craftstate"))
                {
                    _stateRevision = ThemeTokens.Revision;
                    state.color = ThemeTokens.GetColor("color.text.body", state.color);
                }
                if (state.raycastTarget) state.raycastTarget = false;
                Geo.Place(_canvas, state.rectTransform, new Box(frameBox.X + capW, frameBox.Y, Mathf.Max(0f, frameBox.W - 2f * capW), frameBox.H));
                state.margin = Vector4.zero;
                Label(state, TextAlignmentOptions.Center, r.Bar.H * 0.28f, 12f, upper: true);
                Condense(state);

                if (Localization.instance.textMeshStrings.TryGetValue(state, out var token))
                {
                    var want = Localization.instance.Localize(token) + (_gui.m_multiCrafting ? " x " + _gui.m_multiCraftAmount : "");
                    if (state.text != want) state.text = want;
                }
            }
            var cancel = _gui.m_craftCancelButton;
            if (!cancel) return;
            Geo.Place(_canvas, (RectTransform)cancel.transform, r.Cancel);
            var cancelLabel = CancelLabel();
            if (!cancelLabel) return;
            if (!cancelLabel.gameObject.activeSelf) cancelLabel.gameObject.SetActive(true);
            if (!cancelLabel.enabled) cancelLabel.enabled = true;
            var cancelText = Localization.instance.Localize("$menu_cancel");
            if (cancelLabel.text != cancelText) cancelLabel.text = cancelText;
            if (cancelLabel.raycastTarget) cancelLabel.raycastTarget = false;
            SameFace(cancelLabel);
            var body = ThemeTokens.GetColor("color.text.body", cancelLabel.color);
            if (cancelLabel.color != body) cancelLabel.color = body;
            Geo.Place(_canvas, cancelLabel.rectTransform, r.Cancel);
            cancelLabel.margin = Vector4.zero;
            Label(cancelLabel, TextAlignmentOptions.Center, r.Cancel.H * 0.3f, 11f, upper: true);
            Condense(cancelLabel);
        }

        // Trough box inside the frame (craft_bar_frame: T/B border = the full sprite height, so it is drawn at its own
        // height; the pixelsPerUnitMultiplier makes the sprite exactly the frame's height). The trough fills the window
        // (share.craft.progress.inset of the height from top / bottom) and runs on under the end ornaments to
        // share.craft.progress.cap x height; capW = the ornament (border.x) for the text span.
        private Box FrameInset(Box frame, out float capW)
        {
            var insetY = frame.H * Mathf.Clamp(ThemeTokens.GetFloat("share.craft.progress.inset", 0.19f), 0.02f, 0.45f);
            var insetX = Mathf.Min(frame.H * Mathf.Clamp(ThemeTokens.GetFloat("share.craft.progress.cap", 0.35f), 0f, 2f), frame.W * 0.3f);
            var sprite = _progressFrame.sprite;
            var spriteH = sprite ? sprite.rect.height : 0f;
            capW = sprite && spriteH > 0f ? Mathf.Min(frame.H * Mathf.Max(sprite.border.x, sprite.border.z) / spriteH, frame.W * 0.3f) : insetX;
            var localH = _progressFrame.rectTransform.rect.height;
            if (sprite && spriteH > 0f && localH > 0f && _rootCanvas)
            {
                var mult = spriteH * _rootCanvas.referencePixelsPerUnit / (sprite.pixelsPerUnit * localH);
                if (Mathf.Abs(_progressFrame.pixelsPerUnitMultiplier - mult) > 0.001f) _progressFrame.pixelsPerUnitMultiplier = mult;
            }
            return CraftingMath.Inset(frame, insetX, insetY);
        }
        // Craft-row labels condensed like the approved mockup's caps: tighter tracking (size.craft.label.tracking, TMP
        // em/100) and, on the centred margin-less labels (state text, cancel), a horizontal glyph scale
        // (share.craft.label.condense on the label's localScale.x; Geo.Place sizes the rect in that scale). The craft
        // label keeps scale 1: PlateLabelFit writes its margins (span right of the anvil) in the label's local units, so a
        // scaled label would sit off-centre in that span (layout check button-skew).
        private static void Condense(TMP_Text t, bool squeeze = true)
        {
            if (!t) return;
            var spacing = ThemeTokens.GetFloat("size.craft.label.tracking", -1f);
            if (!Mathf.Approximately(t.characterSpacing, spacing)) t.characterSpacing = spacing;
            var sx = squeeze ? Mathf.Clamp(ThemeTokens.GetFloat("share.craft.label.condense", 0.88f), 0.6f, 1f) : 1f;
            var s = t.rectTransform.localScale;
            if (!Mathf.Approximately(s.x, sx)) t.rectTransform.localScale = new Vector3(sx, s.y, s.z);
        }
        // The craft button label's face (font asset + material + style) on another label: one face for every craft-row text.
        private void SameFace(TMP_Text t)
        {
            var src = CraftLabel();
            if (!src || !t) return;
            if (t.font != src.font) t.font = src.font;
            if (t.fontSharedMaterial != src.fontSharedMaterial) t.fontSharedMaterial = src.fontSharedMaterial;
            if (t.fontStyle != src.fontStyle) t.fontStyle = src.fontStyle;
        }

        // ---------------------------------------------------------------- craft amount

        private bool AmountShown() =>
            _minus && _gui.m_selectedRecipe.Recipe && _gui.m_selectedRecipe.ItemData == null && _gui.InCraftTab();

        private void ShowAmount(bool on)
        {
            foreach (var go in new[] { _minus ? _minus.gameObject : null, _plus ? _plus.gameObject : null, _amountField ? _amountField.gameObject : null })
                if (go && go.activeSelf != on) go.SetActive(on);
        }

        private void StepGlyph(Button b, Box box, float side)
        {
            var t = b.transform.Find("Glyph");
            if (!t || !t.TryGetComponent<Image>(out var glyph) || !glyph.enabled) return;
            Geo.Place(_canvas, glyph.rectTransform, new Box(box.X + (box.W - side) / 2f, box.Y + (box.H - side) / 2f, side, side));
        }

        private void Amount(CraftRow r, bool shown)
        {
            if (!_minus || !_plus || !_amountField) return;
            ShowAmount(shown);
            if (!shown) return;
            var max = MaxAmount();
            _amount = CraftingMath.ClampAmount(_amount, max);
            Geo.Place(_canvas, (RectTransform)_minus.transform, r.Minus);
            Geo.Place(_canvas, (RectTransform)_plus.transform, r.Plus);
            Geo.Place(_canvas, (RectTransform)_amountField.transform, r.Field);
            if (_minus.interactable != _amount > 1) _minus.interactable = _amount > 1;
            if (_plus.interactable != _amount < max) _plus.interactable = _amount < max;
            // Step glyphs: one side (share.craft.step.glyph of the step height, mockup: the bars ~0.28 h), centred.
            var side = r.Minus.H * Mathf.Clamp(ThemeTokens.GetFloat("share.craft.step.glyph", 0.4f), 0.1f, 0.9f);
            StepGlyph(_minus, r.Minus, side);
            StepGlyph(_plus, r.Plus, side);
            if (_amountText)
            {
                SameFace(_amountText);
                Label(_amountText, TextAlignmentOptions.Center, r.Button.H * 0.3f, 10f);
            }
            if (!_amountField.isFocused)
            {
                var s = _amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (_amountField.text != s) _amountField.SetTextWithoutNotify(s);
            }
        }

        private void SetAmount(int n)
        {
            _amount = CraftingMath.ClampAmount(n, MaxAmount());
            if (_amountField) _amountField.SetTextWithoutNotify(_amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Arm();
        }

        // Most the player can craft of the selected recipe now: requirements (Player.HaveRequirements with the batch
        // multiplier, InventoryGui.cs:1556; ValheimPlus CraftFromChest patches it to count chests) and room for the
        // result (OnCraftPressed :2062-2064: Recipe.GetAmount + Inventory.CanAddItem), at most size.craft.amount.max.
        // Free crafting (NoCostCheat / NoCraftCost, :1559) skips the requirement half.
        public int MaxAmount()
        {
            var recipe = _gui.m_selectedRecipe.Recipe;
            var player = Player.m_localPlayer;
            if (!recipe || !player) return 1;
            if (recipe == _maxRecipe && Time.unscaledTime < _maxAt) return _max;
            _maxRecipe = recipe;
            _maxAt = Time.unscaledTime + 0.25f;
            var cap = Mathf.Max(1, Mathf.RoundToInt(ThemeTokens.GetFloat("size.craft.amount.max", 99f)));
            var free = player.NoCostCheat() || ZoneSystem.instance && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost);
            var inv = player.GetInventory();
            var prefab = recipe.m_item ? recipe.m_item.gameObject : null;
            _max = CraftingMath.LargestOk(cap, n =>
                (free || player.HaveRequirements(recipe, false, 1, n)) &&
                (!prefab || inv == null || inv.CanAddItem(prefab, recipe.GetAmount(1, out _, out _, n))));
            return _max;
        }

        // UpdateRecipe / OnCraftPressed prefix: a stepper amount > 1 arms vanilla's own multi-craft (m_multiCraftAmount +
        // m_touchMultiCrafting, the touch long-press arm, :2045), so the label, the requirement check and the craft all take
        // N through vanilla code. Amount 1 / upgrade / other tabs: vanilla's own m_multiCraftAmount back (Shift = vanilla).
        public void Arm()
        {
            if (_amount > 1 && AmountShown())
            {
                if (_gui.m_multiCraftAmount != _amount) _gui.m_multiCraftAmount = _amount;
                _gui.m_touchMultiCrafting = true;
                _amountOwned = true;
            }
            else if (_amountOwned)
            {
                _amountOwned = false;
                if (_vanillaMulti > 0) _gui.m_multiCraftAmount = _vanillaMulti;
                _gui.m_touchMultiCrafting = false;
            }
        }

        // Glyph at the button's left (craft in the craft tab, upgrade otherwise) and the label in the span right of it
        // (approved mockup craft-progress.png, idle: pad 0.18 h | anvil 0.58 h | 0.21 h | text | 0.21 h).
        // Root cause of 0.24.0 / 0.25.0 "anvil over the first letter": the glyph was placed from the label's
        // preferredWidth (clamped to the left edge for a wide label) while PlateLabelFit decided the glyph's side from
        // the drawn text bounds; a text already drawn under the glyph's centre made it no obstacle, so the label kept
        // centring over the whole plate (a stable overlap once d8a8cfc dropped the 0.88 condense). Now the glyph box is
        // fixed by the button alone and handed to PlateLabelFit as its lead: the text area starts gap after the glyph,
        // one line, auto-size steps the font down to fit (no x-scale: margins stay in the label's local units).
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
            var lead = CraftingMath.LeadGlyph(b, ThemeTokens.GetFloat("share.craft.glyph.pad", 0.18f), ThemeTokens.GetFloat("share.craft.glyph.size", 0.58f));
            if (_craftGlyph.enabled) Geo.Place(_canvas, _craftGlyph.rectTransform, lead);
            // One pad on both sides of the text (share.craft.label.pad x h): centred in the span right of the glyph
            // (layout check button-skew measures the text's gaps to that span; the mockup's 0.16 / 0.26 averaged).
            var pad = ThemeTokens.GetFloat("share.craft.label.pad", 0.21f);
            if (label.TryGetComponent<PlateLabelFit>(out var fit) && !fit.Reverted)
                fit.Lead(_craftGlyph.enabled ? _craftGlyph : null, pad, pad, singleLine: true);
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
