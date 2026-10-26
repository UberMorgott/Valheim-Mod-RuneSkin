using System;
using System.Collections.Generic;
using System.Text;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.InventoryScreen;
using RuneSkin.Ownership;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuneSkin.InfoScreens
{
    // Family "CharacterStats" (batch P, stats-full-b.png): the character stats window, opened by a 5th button in the
    // inventory's info row (a clone of the Achievements button, like the vanilla tabs) or the Hud.StatsKey shortcut.
    // A modal screen (FullWindow: the inventory panels hide while it is open): the mockup's 1920x1080 design frame
    // mapped onto the canvas (StatsMath.Map), the user-approved painted chrome (frame, plaque, card frames, close
    // button) in four quarters, the title on the rune face, a 4x2 grid of cards (resources, resistances, movement and
    // weight, defense, food, rest, battle enchantments, ward); the active effects stay on the compendium page. Every value comes from the live game objects (StatsReader, cited per row); Epic Loot and
    // MorgottTweaks through their public APIs (StatsSources, soft). A card whose rows do not fit scrolls (mouse wheel,
    // slim handle). Values refresh every Hud.StatsRefresh seconds while open (pooled rows, StringBuilder -> TMP, no
    // per-frame allocation); layout runs on open and on canvas size change.
    public sealed class StatsWindow : MonoBehaviour
    {
        public const string ScreenId = "CharacterStats";
        public const string ButtonName = "CharacterStats";
        public const string RootName = "RuneSkinCharacterStats";
        public static StatsWindow Instance { get; private set; }

        internal enum Kind { Pair, PairSub, Bar, Cell, Dish, Note, Divider, Tall, TallSub, Wide, Name }

        internal sealed class Row
        {
            public Kind Kind;
            public RectTransform Rt;
            public Image Plate, Icon, Trough, Fill, Icon2, Plate2;
            public TMP_Text Label, Value, Sub, Label2, Value2;
            public readonly StringBuilder L = new StringBuilder(48), V = new StringBuilder(64), S = new StringBuilder(64), L2 = new StringBuilder(32), V2 = new StringBuilder(32);
            public string IconToken, Icon2Token, BarToken;
            public Sprite IconSprite;
            public Color IconColor = Color.white, Icon2Color = Color.white;
            public int LastValueLength = -1;
            public Kind LastShape = (Kind)(-1);
            public float FillShare;
            public float H;
            // Breakdown shown while the plate is hovered (Explain2 / Tip2: the right half of a resistance cell).
            public Explain Explain, Explain2;
            public int Arg, Arg2;
            public UITooltip Tip, Tip2;
            public Color PlateColor;
        }

        internal sealed class Card
        {
            public int Index;
            public Image HeaderIcon, Divider, Handle;
            public TMP_Text Title;
            public string LongestWord = "";
            public RectTransform View;
            public readonly List<Row> Rows = new List<Row>(16);
            public int Used;
            public float Scroll, ContentH, ViewH;
            public Box Box;
        }

        private static readonly string[] CardIcons = { "res", "resist", "move", "defense", "food", "rest", "combat", "mt" };
        private static readonly string[] CardTitles =
        {
            "$runeskin_stats_resources", "$runeskin_stats_resists", "$runeskin_stats_movement", "$runeskin_stats_defense",
            "$runeskin_stats_food", "$runeskin_stats_rest", "$runeskin_stats_combat", "$runeskin_stats_mt",
        };

        private InventoryGui _gui;
        private RectTransform _canvas, _root;
        private Canvas _canvasComp;
        private Image _scrim, _closeImage;

        // Frame and title plaque pieces (Codex 4x redraws of the stats-full-b parts): bands / rails / plaque field tile,
        // knot corners and plaque ends sit at their design rects (size.stats.piece.<name>.x/y/w/h).
        private sealed class Piece
        {
            public string Name;
            public Image Image;
            public float X, Y, W, H;
        }

        private static readonly (string name, string role, float x, float y, float w, float h)[] PieceDefs =
        {
            ("ChromeTL", "stats.chrome.tl", 0f, 0f, 960f, 540f),
            ("ChromeTR", "stats.chrome.tr", 960f, 0f, 960f, 540f),
            ("ChromeBL", "stats.chrome.bl", 0f, 540f, 960f, 540f),
            ("ChromeBR", "stats.chrome.br", 960f, 540f, 960f, 540f),
        };

        private readonly List<Piece> _pieces = new List<Piece>(12);
        private TMP_Text _title;
        private Button _close, _button;
        private readonly Card[] _cards = new Card[8];
        private float _nextRefresh, _scale;
        private Vector2 _laidOut;
        private bool _layoutDirty = true;
        private readonly StatsReader _reader = new StatsReader();
        private readonly float[] _tops = new float[32];
        private readonly float[] _heights = new float[32];

        public bool IsOpen => _root && _root.gameObject.activeSelf;

        public static void Attach(InventoryGui gui)
        {
            if (!gui || gui.GetComponent<StatsWindow>()) return;
            gui.gameObject.AddComponent<StatsWindow>().Init(gui);
        }

        private void Init(InventoryGui gui)
        {
            Instance = this;
            _gui = gui;
            StatsSources.Bind();
            _canvasComp = gui.GetComponentInParent<Canvas>();
            _canvas = _canvasComp ? (RectTransform)_canvasComp.rootCanvas.transform : null;
            _canvasComp = _canvasComp ? _canvasComp.rootCanvas : null;
            var go = new GameObject(RootName, typeof(RectTransform));
            _root = (RectTransform)go.transform;
            _root.SetParent(gui.transform, false);
            go.SetActive(false);
            Build();
            // Trilinear hi-res atlas (4x redraws): a slightly sharp mip bias keeps 1080p crisp.
            var hiSprite = ThemeTokens.GetSprite("sprite.stats.chrome.tl");
            if (hiSprite && hiSprite.texture) hiSprite.texture.mipMapBias = T("size.stats.mip.bias", -0.7f);
            _button = CreateButton(gui);
            WindowMove.OnMoved(WindowMove.Stats, () => _layoutDirty = true);
            ScreenRegistry.Register(_root, Context);
            Plugin.Log.LogInfo($"[RuneSkin] character stats window attached (button={(bool)_button})");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_root) ScreenRegistry.Unregister(_root);
        }

        private ScreenContext Context() => new ScreenContext(ScreenId, OwnershipRegistry.CharacterStats);

        // ---------------------------------------------------------------- build

        private Image Img(Transform parent, string name, string role, string reason)
        {
            var image = InfoArt.OwnImage(parent, name, role, reason);
            image.enabled = true;
            return image;
        }

        private static Material _dense;

        // Body texts of the window share one material copy of their face: slight dilate + dark outline (the mockup's
        // dense, outlined PT Sans). Made once, never per text.
        private static void Dense(TMP_Text text)
        {
            if (!text.font) return;
            if (!_dense || _dense.mainTexture != text.font.material.mainTexture)
            {
                _dense = new Material(text.font.material) { name = "RuneSkin stats body (dense)" };
                _dense.SetFloat(ShaderUtilities.ID_FaceDilate, ThemeTokens.GetFloat("size.stats.text.dilate", 0.1f));
                _dense.SetFloat(ShaderUtilities.ID_OutlineWidth, ThemeTokens.GetFloat("size.stats.text.outline", 0.12f));
                _dense.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.85f));
            }
            text.fontSharedMaterial = _dense;
        }

        private static TMP_Text Txt(Transform parent, string name, string role, TextAlignmentOptions align)
        {
            var text = InfoArt.RowText(parent, name, role, align);
            if (role == "stats.label" || role == "stats.value" || role == "stats.sub") Dense(text);
            text.richText = true;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.enableAutoSizing = false;
            return text;
        }

        private void Build()
        {
            _scrim = Img(_root, "Scrim", "scrim", "modal dim behind the stats window (click closes)");
            _scrim.raycastTarget = true;
            _scrim.sprite = null; // a plain full-screen dim (the role has no sprite)
            _scrim.color = ThemeTokens.GetColor("color.stats.backdrop", new Color(0.016f, 0.027f, 0.04f, 0.86f));
            var scrimButton = _scrim.gameObject.AddComponent<Button>();
            scrimButton.transition = Selectable.Transition.None;
            scrimButton.navigation = new Navigation { mode = Navigation.Mode.None };
            scrimButton.onClick.AddListener(Close);
            // The painted chrome (frame, plaque, card frames and glass, close button) under everything; its quarters
            // catch clicks inside the window so they do not reach the scrim.
            foreach (var d in PieceDefs)
            {
                var img = Img(_root, d.name, d.role, "painted window chrome quarter (user-approved Codex edit of stats-full-b)");
                img.raycastTarget = true;
                _pieces.Add(new Piece { Name = d.name, Image = img, X = d.x, Y = d.y, W = d.w, H = d.h });
            }
            for (var i = 0; i < _cards.Length; i++) _cards[i] = MakeCard(i);
            _title = Txt(_root, "Title", "stats.title", TextAlignmentOptions.Center);
            _title.SetText(Words.Get("$runeskin_stats_title"));
            _closeImage = Img(_root, "Close", "stats.hit", "click area of the painted close button (the chrome draws it)");
            _closeImage.raycastTarget = true;
            _close = _closeImage.gameObject.AddComponent<Button>();
            _close.targetGraphic = _closeImage;
            _close.navigation = new Navigation { mode = Navigation.Mode.None };
            _close.onClick.AddListener(Close);
        }

        private Card MakeCard(int i)
        {
            var c = new Card { Index = i };
            var view = new GameObject("Card" + i + "View", typeof(RectTransform));
            c.View = (RectTransform)view.transform;
            c.View.SetParent(_root, false);
            view.AddComponent<RectMask2D>();
            c.HeaderIcon = Img(_root, "Card" + i + "Icon", "stats.icon", "card category icon (stats_head_*)");
            c.HeaderIcon.sprite = ThemeTokens.GetSprite("sprite.stats.head." + CardIcons[i]);
            c.HeaderIcon.enabled = c.HeaderIcon.sprite;
            c.Title = Txt(_root, "Card" + i + "Title", "stats.header", TextAlignmentOptions.MidlineLeft);
            c.Title.textWrappingMode = TextWrappingModes.Normal;
            c.Title.overflowMode = TextOverflowModes.Overflow;
            c.Title.SetText(Words.Get(CardTitles[i]));
            foreach (var word in Words.Get(CardTitles[i]).Split(' '))
                if (word.Length > c.LongestWord.Length) c.LongestWord = word.ToUpperInvariant();
            c.Title.fontStyle |= FontStyles.UpperCase; // serif small caps (mockup headers)
            c.Divider = Img(_root, "Card" + i + "Divider", "stats.divider", "gold header divider (stats_divider)");
            c.Divider.preserveAspect = false; // stretched across the card (the sprite fades at both ends)
            c.Handle = Img(_root, "Card" + i + "Scroll", "scrollbar.handle", "card scroll handle (shown only when the rows overflow)");
            c.Handle.gameObject.SetActive(false);
            return c;
        }

        private Row MakeRow(Card c, Kind kind)
        {
            var r = new Row { Kind = kind };
            var go = new GameObject("Row" + c.Rows.Count, typeof(RectTransform));
            r.Rt = (RectTransform)go.transform;
            r.Rt.SetParent(c.View, false);
            var p = r.Rt;
            switch (kind)
            {
                case Kind.Divider:
                    r.Plate = Img(p, "Line", "stats.divider", "section divider (stats_divider)");
                    r.Plate.preserveAspect = false;
                    break;
                case Kind.Bar:
                    r.Trough = Img(p, "Trough", "stats.trough", "pool bar trough (stats_bar_trough)");
                    r.Fill = Img(p, "Fill", "stats.fill", "pool bar fill (stats_bar_*)");
                    break;
                case Kind.Cell:
                    r.Plate = Img(p, "PlateL", "stats.plate", "resistance cell plate (stats_plate)");
                    r.Plate2 = Img(p, "PlateR", "stats.plate", "resistance cell plate (stats_plate)");
                    r.Icon = Img(p, "IconL", "stats.icon", "damage type icon (stats_icon_*)");
                    r.Icon2 = Img(p, "IconR", "stats.icon", "damage type icon (stats_icon_*)");
                    r.Label = Txt(p, "LabelL", "stats.label", TextAlignmentOptions.MidlineLeft);
                    r.Value = Txt(p, "ValueL", "stats.value", TextAlignmentOptions.MidlineRight);
                    r.Label2 = Txt(p, "LabelR", "stats.label", TextAlignmentOptions.MidlineLeft);
                    r.Value2 = Txt(p, "ValueR", "stats.value", TextAlignmentOptions.MidlineRight);
                    break;
                default:
                    if (kind != Kind.Note && kind != Kind.Name) r.Plate = Img(p, "Plate", "stats.plate", "row plate (stats_plate)");
                    r.Icon = Img(p, "Icon", "stats.icon", "row icon (stats_icon_* / item icon)");
                    r.Label = Txt(p, "Label", kind == Kind.Note ? "stats.sub" : "stats.label", TextAlignmentOptions.MidlineLeft);
                    r.Value = Txt(p, "Value", "stats.value", TextAlignmentOptions.MidlineRight);
                    if (kind == Kind.PairSub || kind == Kind.TallSub || kind == Kind.Dish) r.Sub = Txt(p, "Sub", kind == Kind.Dish ? "stats.label" : "stats.sub", kind == Kind.Dish ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
                    if (kind == Kind.Dish) r.Icon2 = Img(p, "Hourglass", "stats.icon", "food timer glyph (stats_icon_hourglass)");
                    if (kind == Kind.Wide) { r.Value.textWrappingMode = TextWrappingModes.Normal; r.Value.overflowMode = TextOverflowModes.Overflow; }
                    break;
            }
            if (r.Plate && kind != Kind.Divider) r.Tip = Hoverable(r, r.Plate, 0);
            if (r.Plate2) r.Tip2 = Hoverable(r, r.Plate2, 1);
            c.Rows.Add(r);
            return r;
        }

        // ---------------------------------------------------------------- breakdown tooltip

        private readonly StatBreakdown _bd = new StatBreakdown();
        private readonly StringBuilder _tip = new StringBuilder(1024);
        private Row _hoverRow;
        private int _hoverHalf;
        private GameObject _tooltipPrefab;

        // A row plate catches the pointer: StatsRowHover first (fills the text), then the vanilla UITooltip (shows it
        // after its delay with the game's tooltip prefab, which RuneSkin's Tooltips family skins).
        private UITooltip Hoverable(Row r, Image plate, int half)
        {
            plate.raycastTarget = true;
            r.PlateColor = plate.color;
            var hover = plate.gameObject.AddComponent<StatsRowHover>();
            hover.Window = this;
            hover.Row = r;
            hover.Half = half;
            var tip = plate.gameObject.AddComponent<UITooltip>();
            tip.m_tooltipPrefab = TooltipPrefab();
            tip.m_text = "";
            tip.m_topic = "";
            return tip;
        }

        private GameObject TooltipPrefab()
        {
            if (_tooltipPrefab) return _tooltipPrefab;
            if (_buttonGo && _buttonGo.TryGetComponent<UITooltip>(out var own) && own.m_tooltipPrefab) return _tooltipPrefab = own.m_tooltipPrefab;
            foreach (var t in _gui.GetComponentsInChildren<UITooltip>(true))
                if (t.m_tooltipPrefab) return _tooltipPrefab = t.m_tooltipPrefab;
            return null;
        }

        internal void Hover(Row r, int half, bool enter)
        {
            var plate = half == 0 ? r.Plate : r.Plate2;
            if (enter)
            {
                // One line per hover start (diagnostics: the breakdown runs other mods' status-effect hooks every frame).
                if (_hoverRow != r || _hoverHalf != half) Plugin.Log.LogInfo($"[RuneSkin] stats hover {(half == 0 ? r.Explain : r.Explain2)} arg {(half == 0 ? r.Arg : r.Arg2)}");
                _hoverRow = r;
                _hoverHalf = half;
                if (plate) plate.color = ThemeTokens.GetColor("color.stats.plate.hover", r.PlateColor * 1.35f);
                UpdateTip();
                return;
            }
            if (plate) plate.color = r.PlateColor;
            if (_hoverRow == r && _hoverHalf == half) _hoverRow = null;
        }

        // The hovered row's breakdown into its tooltip; the string is rebuilt only when the text changed.
        private void UpdateTip()
        {
            var r = _hoverRow;
            if (r == null || !r.Rt) return;
            var tip = _hoverHalf == 0 ? r.Tip : r.Tip2;
            if (!tip) return;
            var id = _hoverHalf == 0 ? r.Explain : r.Explain2;
            var player = Player.m_localPlayer;
            if (id == Explain.None || !player)
            {
                tip.Set("", "");
                return;
            }
            _reader.ExplainInto(player, id, _hoverHalf == 0 ? r.Arg : r.Arg2, _bd);
            _tip.Clear();
            _bd.Render(_tip, _reader.Comma, _reader.GoodHex, _reader.BadHex, _reader.SubHex, _reader.ElementHex());
            if (Same(_tip, tip.m_text) && tip.m_topic == _bd.Title) return;
            tip.Set(_bd.Title, _tip.ToString());
        }

        private static bool Same(StringBuilder sb, string s)
        {
            if (s == null || sb.Length != s.Length) return false;
            for (var i = 0; i < s.Length; i++)
                if (sb[i] != s[i]) return false;
            return true;
        }

        // Breakdown text of a row, for the harness (null when the row has none).
        internal string ExplainText(int card, int row, int half)
        {
            if (card < 0 || card >= _cards.Length || row < 0 || row >= _cards[card].Used || !Player.m_localPlayer) return null;
            var r = _cards[card].Rows[row];
            var id = half == 0 ? r.Explain : r.Explain2;
            if (id == Explain.None) return null;
            _reader.ExplainInto(Player.m_localPlayer, id, half == 0 ? r.Arg : r.Arg2, _bd);
            var sb = new StringBuilder(512);
            _bd.Render(sb, _reader.Comma, _reader.GoodHex, _reader.BadHex, _reader.SubHex, _reader.ElementHex());
            return _bd.Title + "\n" + sb;
        }

        // ---------------------------------------------------------------- button

        private GameObject _buttonGo;

        // Clone of the Achievements info button (next free slot of the info row: the vanilla buttons sit 100 px apart),
        // own click event, the window's helmet medallion as its icon; InventorySkin lays out every Selectable of m_info.
        private Button CreateButton(InventoryGui gui)
        {
            var info = gui.m_info ? gui.m_info : gui.m_infoPanel as RectTransform;
            var achievements = info ? info.Find("Achievements") as RectTransform : null;
            var trophies = info ? info.Find("Trophies") as RectTransform : null;
            if (!achievements || !trophies)
            {
                Plugin.Log.LogWarning($"[RuneSkin] character stats: no Achievements / Trophies button in the info row (Achievements={(bool)achievements} Trophies={(bool)trophies}); key only");
                return null;
            }
            _buttonGo = Instantiate(achievements.gameObject, achievements.parent, false);
            _buttonGo.name = ButtonName;
            var rt = (RectTransform)_buttonGo.transform;
            rt.anchoredPosition = achievements.anchoredPosition + (achievements.anchoredPosition - trophies.anchoredPosition);
            rt.SetSiblingIndex(achievements.GetSiblingIndex() + 1);
            var button = _buttonGo.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(Toggle);
            if (_buttonGo.TryGetComponent<UITooltip>(out var tip))
            {
                tip.m_topic = "";
                tip.m_text = "$runeskin_stats_title";
            }
            var icon = (_buttonGo.transform.Find("Image") ?? _buttonGo.transform.Find("Icon"))?.GetComponent<Image>();
            var sprite = ThemeTokens.GetSprite("sprite.stats.button");
            if (icon && sprite)
            {
                icon.sprite = sprite;
                icon.color = Color.white;
                icon.preserveAspect = true;
            }
            _buttonGo.SetActive(true);
            return button;
        }

        // ---------------------------------------------------------------- open / close

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        // InventoryGui.OnOpenSkills (InventoryGui.cs:2325-2333): the Info group gets the gamepad focus, then the window.
        public void Open()
        {
            if (!Player.m_localPlayer || !_gui) return;
            _shownInventory = false;
            if (_gui.m_uiGroups != null && _gui.m_uiGroups.Length > 2) _gui.SetActiveGroup(_gui.m_uiGroups[2]);
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            FullWindow.Enter(_root, OpaqueRect); // modal: the inventory panels hide (not drawn) until it closes
            _layoutDirty = true;
            _nextRefresh = 0f;
            // Every card opens at its top (the offset of the last visit is no rest position for new content).
            foreach (var card in _cards) if (card != null) card.Scroll = 0f;
            Refresh();
            LayoutAll();
        }

        // Back to where it was opened from, like vanilla's info dialogs (SkillsDialog.OnClose / TextsDialog.OnClose only
        // deactivate themselves, InventoryGui.Update :535-542): opened from the inventory, the inventory stays; opened by
        // StatsKey outside it (ToggleFromKey showed the inventory for it), the game.
        public void Close()
        {
            var back = _shownInventory;
            _shownInventory = false;
            if (_root && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
            if (_root) FullWindow.Exit(_root);
            if (back && _gui && InventoryGui.IsVisible()) _gui.Hide();
        }

        private bool _shownInventory;

        // The chrome's opaque interior on screen (px): the four quarters' union less their transparent rim. Measured on
        // stats_chrome_*.png (3840x2160 composed, alpha >= 250 everywhere inside 88 / 104 / 88 / 56 px = 44 / 52 / 44 /
        // 28 design px), plus 4 design px of slack. The UI drawn before the window inside it is gated (CoverCull).
        private Rect OpaqueRect()
        {
            if (_pieces.Count == 0 || !_canvasComp) return default;
            var cam = _canvasComp.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvasComp.worldCamera;
            var corners = new Vector3[4];
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in _pieces)
            {
                if (!p.Image || !p.Image.isActiveAndEnabled || !p.Image.sprite) return default; // a missing quarter: no claim
                p.Image.rectTransform.GetWorldCorners(corners);
                foreach (var c in corners)
                {
                    var s = RectTransformUtility.WorldToScreenPoint(cam, c);
                    x0 = Mathf.Min(x0, s.x); y0 = Mathf.Min(y0, s.y);
                    x1 = Mathf.Max(x1, s.x); y1 = Mathf.Max(y1, s.y);
                }
            }
            RuneSkin.Restyle.RenderGateMath.Interior(x0, y0, x1, y1, 1920f, 1080f,
                T("size.stats.opaque.left", 48f), T("size.stats.opaque.top", 56f), T("size.stats.opaque.right", 48f), T("size.stats.opaque.bottom", 32f),
                out var innerLeft, out var innerBottom, out var innerRight, out var innerTop);
            return Rect.MinMaxRect(innerLeft, innerBottom, innerRight, innerTop);
        }
        // ---------------------------------------------------------------- update

        private void Update()
        {
            var key = Plugin.StatsKey;
            if (key != null && key.Value.MainKey != KeyCode.None && key.Value.IsDown() && CanToggleByKey()) ToggleFromKey();
            if (!IsOpen) return;
            if (Time.unscaledTime >= _nextRefresh) Refresh();
            var size = _canvas ? _canvas.rect.size : Vector2.zero;
            if (_layoutDirty || (size - _laidOut).sqrMagnitude > 0.25f) LayoutAll();
            Wheel();
        }

        private static bool CanToggleByKey()
        {
            var p = Player.m_localPlayer;
            if (!p || p.IsDead() || p.InCutscene() || p.IsTeleporting()) return false;
            if ((Chat.instance && Chat.instance.HasFocus()) || global::Console.IsVisible() || Menu.IsVisible() || TextInput.IsVisible() || Minimap.IsOpen()) return false;
            return !(TextViewer.instance && TextViewer.instance.IsVisible());
        }

        // The key opens the inventory first (the window lives in it), then the window; again closes both.
        private void ToggleFromKey()
        {
            if (IsOpen)
            {
                Close();
                return;
            }
            var shown = !InventoryGui.IsVisible();
            if (shown) _gui.Show(null);
            Open();
            _shownInventory = shown && IsOpen;
        }

        private void Refresh()
        {
            _nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, Plugin.StatsRefresh != null ? Plugin.StatsRefresh.Value : 0.25f);
            var player = Player.m_localPlayer;
            if (!player || player.IsDead()) return;
            _reader.Begin(this, player);
            for (var i = 0; i < _cards.Length; i++)
            {
                var c = _cards[i];
                c.Used = 0;
                _reader.Fill(i, c);
                if (EndCard(c)) _layoutDirty = true;
            }
            ApplyValues();
            if (_hoverRow != null) UpdateTip();
        }

        // Next pooled row of `kind` (a pooled row of another kind at this index is replaced in place).
        internal Row Next(Card c, Kind kind)
        {
            Row r;
            if (c.Used < c.Rows.Count && c.Rows[c.Used].Kind == kind) r = c.Rows[c.Used];
            else
            {
                if (c.Used < c.Rows.Count)
                {
                    var old = c.Rows[c.Used];
                    Destroy(old.Rt.gameObject);
                    c.Rows.RemoveAt(c.Used);
                    _layoutDirty = true;
                }
                r = MakeRow(c, kind);
                if (c.Rows.Count - 1 != c.Used)
                {
                    c.Rows.RemoveAt(c.Rows.Count - 1);
                    c.Rows.Insert(c.Used, r);
                }
                _layoutDirty = true;
            }
            if (!r.Rt.gameObject.activeSelf)
            {
                r.Rt.gameObject.SetActive(true);
                _layoutDirty = true;
            }
            c.Used++;
            r.L.Clear(); r.V.Clear(); r.S.Clear(); r.L2.Clear(); r.V2.Clear();
            r.IconSprite = null;
            r.IconColor = r.Icon2Color = Color.white;
            r.IconToken = r.Icon2Token = r.BarToken = null;
            r.Explain = r.Explain2 = Explain.None;
            r.Arg = r.Arg2 = 0;
            return r;
        }

        // Hides the rows past Used; true when the visible set changed.
        private static bool EndCard(Card c)
        {
            var changed = false;
            for (var i = c.Used; i < c.Rows.Count; i++)
                if (c.Rows[i].Rt.gameObject.activeSelf)
                {
                    c.Rows[i].Rt.gameObject.SetActive(false);
                    changed = true;
                }
            return changed;
        }

        private void ApplyValues()
        {
            foreach (var c in _cards)
                for (var i = 0; i < c.Used; i++)
                {
                    var r = c.Rows[i];
                    if (r.Label) r.Label.SetText(r.L);
                    if (r.Value) r.Value.SetText(r.V);
                    if (r.Sub) r.Sub.SetText(r.S);
                    if (r.Label2) r.Label2.SetText(r.L2);
                    if (r.Value2) r.Value2.SetText(r.V2);
                    SetIcon(r.Icon, r.IconSprite ? r.IconSprite : ThemeTokens.GetSprite(r.IconToken));
                    if (r.Icon && r.Icon.color != r.IconColor) r.Icon.color = r.IconColor;
                    // The label / value split follows the value's width: re-lay out when its length changes.
                    if (r.V.Length != r.LastValueLength)
                    {
                        r.LastValueLength = r.V.Length;
                        _layoutDirty = true;
                    }
                    // A sub-line appearing / emptying changes the row's shape and height (Shape).
                    var shape = Shape(r);
                    if (shape != r.LastShape)
                    {
                        r.LastShape = shape;
                        _layoutDirty = true;
                    }
                    if (r.Icon2)
                    {
                        SetIcon(r.Icon2, ThemeTokens.GetSprite(r.Icon2Token));
                        if (r.Icon2.color != r.Icon2Color) r.Icon2.color = r.Icon2Color;
                    }
                    if (r.Fill)
                    {
                        var s = ThemeTokens.GetSprite(r.BarToken);
                        if (s && r.Fill.sprite != s) r.Fill.sprite = s;
                        if (r.Trough) PlaceFill(r);
                    }
                }
        }

        private static void SetIcon(Image image, Sprite sprite)
        {
            if (!image) return;
            if (image.sprite != sprite) image.sprite = sprite;
            if (image.enabled != (bool)sprite) image.enabled = sprite;
        }

        // ---------------------------------------------------------------- layout

        private float T(string token, float fallback) => ThemeTokens.GetFloat(token, fallback);

        private Box D(float x, float y, float w, float h)
        {
            var r = _canvas.rect;
            var b = StatsMath.Map(r.width, r.height, _scale, x, y, w, h);
            return new Box(b.X + _moved.x, b.Y + _moved.y, b.W, b.H);
        }

        // User offset of the window (WindowMove, canvas units), applied to every design rect; the scrim stays full screen.
        private Vector2 _moved;
        private WindowDrag _drag;

        private void Place(RectTransform rt, Box b) => Geo.Place(_canvas, rt, b);

        // Sliced / tiled art drawn at design px x scale: the art is authored at size.stats.art.scale sprite px per design
        // px (4x redraws), so borders and tiles keep the mockup's proportions at any canvas size.
        private void Ppu(Image image)
        {
            if (!image || !_canvasComp) return;
            var m = _canvasComp.referencePixelsPerUnit * T("size.stats.art.scale", 4f) / (200f * Mathf.Max(_scale, 0.05f));
            if (Mathf.Abs(image.pixelsPerUnitMultiplier - m) > 0.0001f) image.pixelsPerUnitMultiplier = m;
        }

        private void Font(TMP_Text text, float designSize)
        {
            var size = designSize * _scale;
            if (Mathf.Abs(text.fontSize - size) > 0.05f) text.fontSize = size;
        }

        private void LayoutAll()
        {
            if (!_canvas) return;
            _layoutDirty = false;
            var rect = _canvas.rect;
            _laidOut = rect.size;
            _scale = StatsMath.FitScale(rect.width, rect.height);
            if (_scale <= 0f) return;
            Place(_root, new Box(0f, 0f, rect.width, rect.height));
            Place(_scrim.rectTransform, new Box(0f, 0f, rect.width, rect.height));
            _moved = Vector2.zero;
            _moved = WindowMove.Applied(WindowMove.Stats, D(0f, 0f, StatsMath.DesignW, StatsMath.DesignH), _canvas);
            // The window's one header (RuneWindowHeader: drag, double-click reset) on the painted title plaque of the chrome
            // (size.stats.title.*: centred on the window's top strip, inside the frame, left of the painted close cap),
            // standard height, its plate drawn over the painted plaque (face right after the chrome pieces), the chrome's
            // title text mirrored into it. User 2026-10-26: a header across the whole drag area with the strip clipped away
            // read as a detached full-width bar above the frame, the close button outside it.
            var header = RuneWindowHeader.Ensure(_root, WindowMove.Stats);
            if (header)
            {
                header.Adopt(_title, Words.Get("$runeskin_stats_title"));
                header.DrawAfter = _pieces.Count > 0 ? _pieces[_pieces.Count - 1].Image.transform : null;
                _drag = header.Drag;
                var row = D(T("size.stats.title.x", 760f), T("size.stats.title.y", 34f), T("size.stats.title.w", 522f), T("size.stats.title.h", 62f));
                var hh = Mathf.Min(row.H, WindowHeaderMath.HeaderHeight(ThemeTokens.GetFloat("size.win.header", 40f), Responsive.ResponsiveContext.SkinScale));
                header.Place(_canvas, new Box(row.X, row.Y + (row.H - hh) / 2f, row.W, hh));
            }
            foreach (var p in _pieces)
            {
                var k = "size.stats.piece." + p.Name;
                Place(p.Image.rectTransform, D(T(k + ".x", p.X), T(k + ".y", p.Y), T(k + ".w", p.W), T(k + ".h", p.H)));
            }
            Place(_title.rectTransform, D(T("size.stats.title.x", 760f), T("size.stats.title.y", 34f), T("size.stats.title.w", 522f), T("size.stats.title.h", 62f)));
            Font(_title, T("size.stats.font.title", 52f));
            _title.enableAutoSizing = false;
            _title.characterSpacing = T("size.stats.title.spacing", 6f);
            Place(_closeImage.rectTransform, D(T("size.stats.close.x", 1804f), T("size.stats.close.y", 44f), T("size.stats.close.w", 78f), T("size.stats.close.h", 78f)));
            for (var i = 0; i < _cards.Length; i++) LayoutCard(_cards[i]);
        }

        private void LayoutCard(Card c)
        {
            var col = c.Index % 4;
            var row = c.Index / 4;
            var b = StatsMath.Card(col, row, T("size.stats.card.x0", 68f), T("size.stats.card.y0", 133f), T("size.stats.card.w", 439f), T("size.stats.card.h", 421f),
                T("size.stats.card.px", 448.3f), T("size.stats.card.py", 435f));
            c.Box = b;
            var pad = T("size.stats.card.pad", 20f);
            var headH = T("size.stats.card.header", 88f);
            // Category icon at its native size (cut 1:1 from the mockup: 1 sprite px = 1 design px), capped.
            var iconH = T("size.stats.card.icon", 66f);
            var iconW = iconH;
            if (c.HeaderIcon.sprite)
            {
                var sr = c.HeaderIcon.sprite.rect;
                var f = Mathf.Min(1f, Mathf.Min(T("size.stats.card.icon.max", 150f) / Mathf.Max(1f, sr.width), T("size.stats.card.icon.maxh", 92f) / Mathf.Max(1f, sr.height)));
                iconW = sr.width * f;
                iconH = sr.height * f;
            }
            var tx = b.X + pad + iconW + T("size.stats.card.title.gap", 16f);
            var tw = b.Right - pad - tx;
            c.Title.lineSpacing = T("size.stats.header.line", -4f);
            c.Title.characterSpacing = T("size.stats.header.spacing", 1.5f);
            // Up to two lines: a title that needs three at the full size is set smaller (down to the min) if that
            // makes two (MorgottTweaks), else keeps the full size on three lines and pushes the divider down (combat).
            var full = T("size.stats.font.header", 27.5f);
            var size = full;
            var titleH = 0f;
            for (var fs = full; fs >= T("size.stats.font.header.min", 22f) - 0.01f; fs -= 0.5f)
            {
                Font(c.Title, fs);
                titleH = c.Title.GetPreferredValues(tw * _scale, 0f).y / Mathf.Max(_scale, 0.05f);
                var wordFits = c.Title.GetPreferredValues(c.LongestWord).x <= tw * _scale + 0.5f;
                if (wordFits && titleH <= 2.5f * fs * 1.21f * (1f + T("size.stats.header.line", -4f) / 100f)) { size = fs; break; }
                size = -1f;
            }
            if (size < 0f)
            {
                Font(c.Title, full);
                titleH = c.Title.GetPreferredValues(tw * _scale, 0f).y / Mathf.Max(_scale, 0.05f);
            }
            headH = Mathf.Max(headH, titleH + T("size.stats.card.header.pad", 16f));
            Place(c.HeaderIcon.rectTransform, D(b.X + pad, b.Y + (headH - iconH) / 2f + 2f, iconW, iconH));
            Place(c.Title.rectTransform, D(tx, b.Y + T("size.stats.card.title.top", 10f), tw, headH - 8f));
            var dh = T("size.stats.divider.h", 8f);
            Place(c.Divider.rectTransform, D(b.X + pad, b.Y + headH - dh / 2f, b.W - 2f * pad, dh));
            var viewTop = b.Y + headH + (c.Index == 0 ? T("size.stats.card.gap.res", 6f) : T("size.stats.card.gap", 14f));
            var view = new Box(b.X + pad - 4f, viewTop, b.W - 2f * pad + 8f, b.Bottom - T("size.stats.card.bottom", 16f) - viewTop);
            Place(c.View, D(view.X, view.Y, view.W, view.H));
            c.ViewH = view.H;
            var n = Mathf.Min(c.Used, _heights.Length);
            for (var i = 0; i < n; i++) { _kinds[i] = Shape(c.Rows[i]); _heights[i] = RowHeight(c.Index, _kinds[i]); }
            // Food: 0..N dishes from the live slot count; when they do not fit, every dish row shrinks evenly (down to
            // the min), only then the card scrolls. Never clipped.
            if (c.Index == 4) CompressDishes(n, view.H);
            else FitRows(n, view.H);
            c.ContentH = StatsMath.Stack(_heights, n, 0f, _tops);
            c.Scroll = StatsMath.SnapScroll(c.Scroll, _tops, n, c.ContentH, c.ViewH);
            for (var i = 0; i < n; i++)
            {
                var r = c.Rows[i];
                r.H = _heights[i];
                LayoutRow(r, new Box(view.X + 4f, view.Y + _tops[i] - c.Scroll, view.W - 8f, r.H));
            }
            if (c.Index == 1) EqualizeCells(c, n);
            var over = c.ContentH > c.ViewH + 0.5f;
            if (c.Handle.gameObject.activeSelf != over) c.Handle.gameObject.SetActive(over);
            if (over)
            {
                var hh = Mathf.Max(24f, c.ViewH * c.ViewH / c.ContentH);
                var hy = view.Y + (c.ViewH - hh) * (c.Scroll / Mathf.Max(1f, c.ContentH - c.ViewH));
                Place(c.Handle.rectTransform, D(b.Right - pad + 2f, hy, 6f, hh));
                Ppu(c.Handle);
            }
        }

        // Any other card: when its rows overflow the view, every row (not bars / dividers) shrinks by one factor so the
        // card fits, down to size.stats.row.fit.min of its pitch; only past that it scrolls. No half-cut last row.
        private void FitRows(int n, float viewH)
        {
            float flex = 0f, fixedH = 0f;
            for (var i = 0; i < n; i++)
            {
                if (_kinds[i] == Kind.Bar || _kinds[i] == Kind.Divider) fixedH += _heights[i];
                else flex += _heights[i];
            }
            if (flex <= 0f || flex + fixedH <= viewH) return;
            var f = Mathf.Max(T("size.stats.row.fit.min", 0.85f), (viewH - fixedH) / flex);
            for (var i = 0; i < n; i++)
                if (_kinds[i] != Kind.Bar && _kinds[i] != Kind.Divider) _heights[i] *= f;
        }

        private readonly Kind[] _kinds = new Kind[32];
        // Shrinks the dish rows of the food card evenly so all rows fit the view (StatsMath.Compress), not below
        // size.stats.row.dish.min.
        private void CompressDishes(int n, float viewH)
        {
            var dishes = 0;
            var total = 0f;
            for (var i = 0; i < n; i++)
            {
                total += _heights[i];
                if (_cards[4].Rows[i].Kind == Kind.Dish) dishes++;
            }
            var h = StatsMath.Compress(total, viewH, dishes, T("size.stats.row.dish", 76f), T("size.stats.row.dish.min", 54f));
            for (var i = 0; i < n; i++)
                if (_cards[4].Rows[i].Kind == Kind.Dish) _heights[i] = h;
        }

        // Row heights (design px) per card: the mockup's pitches (resources 32 / 26 / 48, resistances 58, movement 38,
        // defense 90, food 76 / 37, rest 90, combat 31.5, MorgottTweaks 42).
        private float RowHeight(int card, Kind kind)
        {
            switch (kind)
            {
                case Kind.Bar: return T("size.stats.row.bar", 26f);
                case Kind.Divider: return T("size.stats.row.divider", 14f);
                case Kind.Note: return T("size.stats.row.note", 27f);
                case Kind.Cell: return T("size.stats.row.cell", 72f);
                case Kind.Dish: return T("size.stats.row.dish", 76f);
                case Kind.Tall: return T("size.stats.row.tall", 90f);
                case Kind.TallSub: return T("size.stats.row.tallsub", 94f);
                case Kind.Wide: return T("size.stats.row.wide", 46f);
                case Kind.Name: return T("size.stats.row.name", 34f);
                case Kind.PairSub: return card == 0 ? T("size.stats.row.res.sub", 48f) : T("size.stats.row.pairsub", 58f);
                default:
                    switch (card)
                    {
                        case 0: return T("size.stats.row.res", 32f);
                        case 4: return T("size.stats.row.slots", 37f);
                        case 6: return T("size.stats.row.combat", 31.5f);
                        case 7: return T("size.stats.row.mt", 42f);
                        default: return T("size.stats.row.pair", 38f);
                    }
            }
        }

        private void LayoutRow(Row r, Box b)
        {
            Place(r.Rt, D(b.X, b.Y, b.W, b.H));
            var label = T("size.stats.font.label", 21f);
            var value = T("size.stats.font.value", 22f);
            var sub = T("size.stats.font.sub", 16.5f);
            var iconW = T("size.stats.icon", 30f);
            var textX = b.X + T("size.stats.label.x", 50f);
            var right = b.Right - T("size.stats.value.pad", 10f);
            switch (r.Kind)
            {
                case Kind.Divider:
                    Place(r.Plate.rectTransform, D(b.X, b.Y + b.H / 2f - 1.5f, b.W, 3f));
                    return;
                case Kind.Bar:
                    {
                        var bh = T("size.stats.bar.h", 22f);
                        var bb = new Box(textX - 6f, b.Y + (b.H - bh) / 2f - 2f, right - textX + 6f, bh);
                        Place(r.Trough.rectTransform, D(bb.X, bb.Y, bb.W, bb.H));
                        Ppu(r.Trough);
                        Ppu(r.Fill);
                        PlaceFill(r);
                        return;
                    }
                case Kind.Cell:
                    {
                        var half = (b.W - 6f) / 2f;
                        CellHalf(r.Plate, r.Icon, r.Label, r.Value, new Box(b.X, b.Y + 2f, half, b.H - 6f), label, value);
                        CellHalf(r.Plate2, r.Icon2, r.Label2, r.Value2, new Box(b.X + half + 6f, b.Y + 2f, half, b.H - 6f), label, value);
                        return;
                    }
                case Kind.Dish:
                    {
                        if (r.Plate) { Place(r.Plate.rectTransform, D(b.X, b.Y + 2f, b.W, b.H - 4f)); Ppu(r.Plate); }
                        var ih = b.H - 14f;
                        Place(r.Icon.rectTransform, D(b.X + 6f, b.Y + 7f, ih * 1.25f, ih));
                        var nx = b.X + T("size.stats.dish.text.x", 84f);
                        var timerW = T("size.stats.dish.timer.w", 58f);
                        var glass = T("size.stats.dish.glass", 22f);
                        Place(r.Label.rectTransform, D(nx, b.Y + 6f, right - timerW - glass - 8f - nx, b.H * 0.45f));
                        Place(r.Sub.rectTransform, D(nx, b.Y + b.H * 0.48f, right - timerW - glass - 8f - nx, b.H * 0.42f));
                        Place(r.Icon2.rectTransform, D(right - timerW - glass - 2f, b.Y + (b.H - glass) / 2f, glass, glass));
                        Place(r.Value.rectTransform, D(right - timerW, b.Y, timerW, b.H));
                        Font(r.Label, label);
                        Font(r.Sub, T("size.stats.font.dish.sub", 17.5f));
                        Font(r.Value, value);
                        return;
                    }
                case Kind.Note:
                    {
                        var ni = T("size.stats.icon.note", 22f);
                        Place(r.Icon.rectTransform, D(b.X + 14f, b.Y + (b.H - ni) / 2f, ni, ni));
                        Place(r.Label.rectTransform, D(b.X + 56f, b.Y, right - b.X - 56f, b.H));
                        Place(r.Value.rectTransform, D(right - 1f, b.Y, 1f, b.H));
                        Font(r.Label, T("size.stats.font.note", 17.5f));
                        return;
                    }
            }
            // Pair / PairSub / Tall / TallSub / Wide / Name
            var tall = r.Kind == Kind.Tall || r.Kind == Kind.TallSub;
            if (r.Plate) { Place(r.Plate.rectTransform, D(b.X, b.Y + 2f, b.W, b.H - 4f)); Ppu(r.Plate); }
            var iw = tall ? T("size.stats.icon.tall", 42f) : iconW;
            if (tall) textX = b.X + T("size.stats.label.x.tall", 74f);
            var shape = Shape(r);
            var lineH = shape == Kind.PairSub ? b.H * 0.56f : shape == Kind.TallSub ? b.H * 0.68f : b.H;
            var iy = shape == Kind.PairSub ? b.Y + (lineH - iw) / 2f : b.Y + (b.H - iw) / 2f;
            Place(r.Icon.rectTransform, D(b.X + 6f, iy, iw, iw));
            // Value column as wide as its text (+ a gap), the label gets the rest: long labels ("Delay after break")
            // and long values ("+15.0/s after 3.0 s") both fit without shrinking below the readable floor.
            var span = right - textX;
            var valueW = r.Kind == Kind.Wide ? span * 0.66f : Mathf.Clamp(r.Value.preferredWidth / Mathf.Max(_scale, 0.05f) + 8f, 36f, span * 0.72f);
            if (r.Kind == Kind.Name) valueW = 1f;
            Place(r.Label.rectTransform, D(textX, b.Y, right - textX - valueW - 6f, lineH));
            Place(r.Value.rectTransform, D(right - valueW, b.Y, valueW, lineH));
            Font(r.Label, r.Kind == Kind.Name ? T("size.stats.font.name", 20f) : label);
            Font(r.Value, value);
            if (r.Kind == Kind.Wide)
            {
                r.Value.enableAutoSizing = true;
                r.Value.fontSizeMax = value * _scale;
                r.Value.fontSizeMin = T("size.stats.font.min", 16f) * _scale;
            }
            if (r.Sub)
            {
                // An empty sub-line takes no space: its row has the one-line shape (Shape) and the sub-line object is off
                // (a zero-height box is not placed, Geo.Place, so it kept its two-line rect; run 20261026-171058).
                var shown = shape == Kind.PairSub || shape == Kind.TallSub;
                if (r.Sub.gameObject.activeSelf != shown) r.Sub.gameObject.SetActive(shown);
                if (shown)
                {
                    var sy = shape == Kind.TallSub ? b.Y + b.H * 0.56f : b.Y + lineH - 6f;
                    var sh = shape == Kind.TallSub ? b.H * 0.32f : b.Bottom - sy;
                    Place(r.Sub.rectTransform, D(textX, sy, right - textX, sh));
                    Font(r.Sub, sub);
                }
            }
        }

        // Drawn shape of a row: a sub-line row (PairSub / TallSub) whose sub-line is empty is laid out and sized as its
        // one-line kind. The weight row ("Переносимый вес") is PairSub for its carry bonus line and stood double height
        // with nothing under it (0.23.0 live report).
        internal static Kind Shape(Row r) =>
            r.S.Length > 0 ? r.Kind : r.Kind == Kind.PairSub ? Kind.Pair : r.Kind == Kind.TallSub ? Kind.Tall : r.Kind;

        private void CellHalf(Image plate, Image icon, TMP_Text label, TMP_Text value, Box b, float labelSize, float valueSize)
        {
            Place(plate.rectTransform, D(b.X, b.Y, b.W, b.H));
            Ppu(plate);
            var iw = T("size.stats.icon.cell", 28f);
            Place(icon.rectTransform, D(b.X + 6f, b.Y + (b.H - iw) / 2f, iw, iw));
            var lx = b.X + 6f + iw + T("size.stats.cell.label.gap", 6f);
            var vw = T("size.stats.cell.value.w", 40f);
            Place(label.rectTransform, D(lx, b.Y, b.Right - vw - 6f - lx, b.H));
            Place(value.rectTransform, D(b.Right - vw - 4f, b.Y, vw, b.H));
            Font(label, labelSize);
            Font(value, valueSize);
            label.enableAutoSizing = false;
        }

        // Resistance grid: one label size for every cell (the largest that fits the longest name, not below the
        // readable floor), so no single name ("Рассекающий") is drawn smaller than its siblings.
        private void EqualizeCells(Card c, int n)
        {
            var full = T("size.stats.font.label", 21f);
            var size = full;
            for (var i = 0; i < n; i++)
            {
                var r = c.Rows[i];
                if (r.Kind != Kind.Cell) continue;
                size = Mathf.Min(size, FitSize(r.Label, full));
                size = Mathf.Min(size, FitSize(r.Label2, full));
            }
            size = Mathf.Max(size, T("size.stats.font.min", 16f));
            for (var i = 0; i < n; i++)
            {
                var r = c.Rows[i];
                if (r.Kind != Kind.Cell) continue;
                Font(r.Label, size);
                Font(r.Label2, size);
            }
        }

        // Largest design size <= full at which the text fits its box width.
        private float FitSize(TMP_Text t, float full)
        {
            Font(t, full);
            var w = t.rectTransform.rect.width;
            var pw = t.preferredWidth;
            return pw > w && pw > 0f ? full * w / pw : full;
        }

        // Fill width = share of the trough's inner window (inset by the trough's rim).
        private void PlaceFill(Row r)
        {
            if (!r.Trough || _scale <= 0f) return;
            var tb = Geo.BoxOf(_canvas, r.Trough.rectTransform);
            var inset = T("size.stats.bar.inset", 4f) * _scale;
            var w = (tb.W - 2f * inset) * StatsMath.Clamp01(r.FillShare);
            var show = w > 0.5f;
            if (r.Fill.enabled != show) r.Fill.enabled = show;
            if (show) Place(r.Fill.rectTransform, new Box(tb.X + inset, tb.Y + inset, w, tb.H - 2f * inset));
        }


        // ---------------------------------------------------------------- scroll

        private void Wheel()
        {
            var wheel = ZInput.GetMouseScrollWheel();
            if (Mathf.Abs(wheel) < 0.01f) return;
            var cam = _canvasComp && _canvasComp.renderMode != RenderMode.ScreenSpaceOverlay ? _canvasComp.worldCamera : null;
            Vector2 mouse = ZInput.pointerPosition;
            foreach (var c in _cards)
            {
                if (c.ContentH <= c.ViewH + 0.5f) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(c.View, mouse, cam)) continue;
                c.Scroll = StatsMath.ClampScroll(c.Scroll - Mathf.Sign(wheel) * T("size.stats.scroll.step", 40f), c.ContentH, c.ViewH);
                LayoutCard(c);
                return;
            }
        }

        // Diagnostics / harness: rows per card, overflow, open state.
        public string Describe()
        {
            var sb = new StringBuilder(256);
            sb.Append("open=").Append(IsOpen).Append(" scale=").Append(_scale.ToString("0.###"));
            foreach (var c in _cards) sb.Append(" card").Append(c.Index).Append('=').Append(c.Used).Append(c.ContentH > c.ViewH + 0.5f ? "+scroll" : "");
            return sb.ToString();
        }

        // Escape / B: InventoryGui.Update prefix closes the window first (the inventory stays).
        internal static bool HandleBack(InventoryGui gui)
        {
            var w = Instance;
            if (!w || !w.IsOpen) return true;
            var p = Player.m_localPlayer;
            if (!p || p.IsDead() || p.InCutscene() || p.IsTeleporting()) return true;
            if (gui.m_craftTimer >= 0f || (Chat.instance && Chat.instance.HasFocus()) || global::Console.IsVisible() || Menu.IsVisible() ||
                (TextViewer.instance && TextViewer.instance.IsVisible()) || GameCamera.InFreeFly() || Minimap.IsOpen()) return true;
            if (!ZInput.GetButtonDown("JoyButtonB") && !ZInput.GetKeyDown(KeyCode.Escape)) return true;
            w.Close();
            // One press closes one layer: the button is consumed as vanilla consumes it when it hides the inventory
            // (InventoryGui.Update :556-559), so no later reader this frame sees it.
            ZInput.ResetButtonStatus("JoyButtonB");
            return false;
        }

        internal Card CardAt(int i) => _cards[i];
        internal bool HasButton => _button;
    }
}
