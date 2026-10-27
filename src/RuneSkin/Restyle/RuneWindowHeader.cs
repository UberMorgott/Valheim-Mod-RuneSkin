using RuneSkin.InventoryScreen;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Restyle
{
    // THE window header (windows-v2: ported from the unmerged 0.20.3 windows track onto layout A): one per window, applied by the skin to every panel root it owns or
    // adopts (inventory, character, chest, backpack, crafting, skills, trophies, texts, achievements, stats, Almanac,
    // map legend, other mods' windows; VNEI's browser fills the crafting body under the crafting header). Identical on every window (user 2026-10-24: "всё по разному"):
    // - same height (size.win.header x SkinScale, canvas units), same plate art (role decor.plaque, the knotwork header
    //   plate) over the whole band;
    // - the title centred on the band, one line, rune face (font.display = Valheim-Norsebold, role window.title), one size
    //   (share.win.title of the height) - so titles of windows on one top guide share one baseline;
    // - optional extras in fixed slots: a small icon left (IconBox), an aux value right (AuxBox: weight, level, count),
    //   the title box symmetric so its centre is the band centre whatever the slots hold.
    // The title is always the header's own text: a window's own title text (TMP or legacy uGUI) is mirrored into it and
    // stops drawing itself (given back on Revert), so no owner layout can make one header differ from another.
    // It is the window's drag band too (a WindowDrag on the same object: drag moves the window, a double-click resets it,
    // positions persist per window id - WindowMove). The title is never empty (WindowHeaderMath.Title fallback).
    [DisallowMultipleComponent]
    public sealed class RuneWindowHeader : MonoBehaviour, IRevertible
    {
        public const string ObjectName = "RuneSkinWindowHeader";
        public const string TitleName = "RuneSkinWindowTitle";
        public const string PlateName = "RuneSkinWindowPlate";
        private const string Role = "window.title";

        // Window id (positions file key), the window it moves, its title text, plate, the drag band.
        public string Id;
        public Transform Window;
        public TMP_Text Title;
        public Image Plate;
        public WindowDrag Drag;

        // Slots of the last canvas placement (canvas units): small icon left, aux value right, title.
        public Box IconBox, AuxBox, TitleBox;

        private Graphic _source;
        private bool _sourceWasEnabled = true; // the adopted title's own enabled state, given back on release
        private string _lastRaw, _lastFallback, _lastShown;

        // The window's own panel stack (owners that know it set it): the header keeps inside THAT frame; null = the
        // drawn frame around the band overlapping the window most.
        public PanelDecor Frame;

        // Painted window chrome the face must draw over (a sibling of the band, e.g. the stats window's chrome quarters):
        // the face goes right after it instead of after the panel stack / scrim.
        public Transform DrawAfter;
        private string _fallback = "", _fallbackKey, _static, _staticKey, _staticLang, _staticText;
        private int _styled = -1;
        private float _stretch, _iconShare, _auxShare;
        private bool _hasIcon;

        public string TitleText => Title ? Title.text : "";

        // Last Arrange (header-local units): the title's width at the header's one size, the frame keep-outs; band width of
        // the last Place (canvas units).
        private float _natural, _keepL, _keepR, _bandW;

        // Band width (canvas units) this placed header needs to show its current title in full at the fitted title size
        // (WindowHeaderMath.BandForTitle with the keep-outs measured on it); 0 before the first placement / without a title.
        public float TitleBandNeed()
        {
            var r = ((RectTransform)transform).rect;
            if (!(_bandW > 0f) || !(r.width > 0f) || !(r.height > 0f) || !(_natural > 0f)) return 0f;
            var need = WindowHeaderMath.BandForTitle(_natural, r.height, _hasIcon, _auxShare * r.height, _keepL, _keepR);
            return need * _bandW / r.width;
        }

        // The header's own title text (fitted by Arrange only; other text fitters keep off it).
        public static bool IsTitle(TMP_Text text) => text && text.name == TitleName && text.transform.parent && text.transform.parent.name == FaceName;

        // The header of window `id` under `parent` (the window root or its title panel); `window` = the transform the
        // drag moves / whose controls keep their clicks (default: parent).
        public static RuneWindowHeader Ensure(Transform parent, string id, Transform window = null)
        {
            var drag = WindowMove.Handle(parent, id, window, ObjectName);
            if (!drag) return null;
            var h = drag.GetComponent<RuneWindowHeader>();
            if (!h)
            {
                h = drag.gameObject.AddComponent<RuneWindowHeader>();
                h.Build();
            }
            h.Id = id;
            h.Window = window ? window : parent;
            h.Drag = drag;
            return h;
        }

        // The visible header (plate + title) lives in a sibling drawn EARLY in the window (right after its panel stack), so
        // the window's own header extras (icon, weight / level plates) draw over the plate; the drag band itself stays the
        // window's last child (it takes the pointer first). The face mirrors the band's rect every frame.
        private RectTransform _face;
        public const string FaceName = "RuneSkinWindowFace";

        private void Build()
        {
            _face = (RectTransform)new GameObject(FaceName, typeof(RectTransform)).transform;
            _face.SetParent(transform.parent, false);
            _face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var plate = new GameObject(PlateName, typeof(RectTransform)).AddComponent<Image>();
            plate.rectTransform.SetParent(_face, false);
            plate.rectTransform.SetAsFirstSibling();
            plate.raycastTarget = false;
            var pm = DollCell.Mark(plate, "decor.plaque", "window header plate (RuneWindowHeader, same art on every window)");
            pm.Outcome = Restyler.Apply(plate, pm, ThemeTokens.Role("decor.plaque")) ? Outcome.Styled : Outcome.Degraded;
            plate.enabled = pm.Outcome == Outcome.Styled;
            Plate = plate;
            var tmp = InfoScreens.InfoArt.NewText(TitleName, _face, Role);
            tmp.raycastTarget = false;
            // Internal from birth: a restyle pass of the window never classifies it as an unknown graphic.
            DollCell.Mark(tmp, Role, "window header title (RuneWindowHeader, rune face)");
            Title = tmp;
            _styled = -1;
        }

        // The window's own title text (TMP) shown by the header: mirrored, the original stops drawing.
        public RuneWindowHeader Adopt(TMP_Text source, string fallback = null)
        {
            if (fallback != null) Fallback(fallback);
            Source(source);
            return this;
        }

        // The adopted title draws itself again in the state it had (a title vanilla / a mod had switched off stays off).
        private void Release()
        {
            if (_source) _source.enabled = _sourceWasEnabled;
            _source = null;
        }

        // A legacy uGUI title (mods built on UnityEngine.UI.Text, e.g. Almanac's "topic"), mirrored the same way.
        public RuneWindowHeader Mirror(Text legacy)
        {
            Source(legacy);
            return this;
        }

        private void Source(Graphic g)
        {
            if (!g || g == _source) return;
            Release();
            _source = g;
            _sourceWasEnabled = g.enabled;
            _static = null;
            Refresh();
        }

        // A RuneSkin title (windows without one of their own); `text` may be a "$key" (localized).
        public RuneWindowHeader Text(string text)
        {
            if (!_source && _static == text) return this;
            if (_source) Release();
            _static = text;
            _staticKey = null;
            Refresh();
            return this;
        }

        public RuneWindowHeader Fallback(string text)
        {
            if (text == _fallbackKey) return this;
            _fallbackKey = text;
            _fallback = Localize(text) ?? "";
            return this;
        }

        private static string Localize(string text) =>
            text != null && text.StartsWith("$", System.StringComparison.Ordinal) && Localization.instance != null ? Localization.instance.Localize(text) : text;

        // Shown text: the source's, else the static one, else the fallback; the source never draws itself.
        private void Refresh()
        {
            if (!Title) return;
            string raw;
            if (_source is TMP_Text t) raw = t.text;
            else if (_source is Text l) raw = l.text;
            else
            {
                // Localized once per key / language (Refresh runs every frame).
                var lang = Localization.instance != null ? Localization.instance.GetSelectedLanguage() : null;
                if (_staticKey != _static || !ReferenceEquals(lang, _staticLang))
                {
                    _staticKey = _static;
                    _staticLang = lang;
                    _staticText = Localize(_static);
                }
                raw = _staticText;
            }
            // Sanitised once per raw text / fallback (this runs every frame).
            if (!ReferenceEquals(raw, _lastRaw) || !ReferenceEquals(_fallback, _lastFallback) || _lastShown == null)
            {
                _lastRaw = raw;
                _lastFallback = _fallback;
                _lastShown = WindowHeaderMath.Title(raw, _fallback);
            }
            var shown = _lastShown;
            if (Title.text != shown) Title.text = shown;
            if (_source && _source.enabled) _source.enabled = false;
        }

        // Band in canvas units (owners that lay the window out): plate, centred title, optional icon (placed in the left
        // slot) and an aux slot `auxW` canvas units wide on the right (AuxBox, the owner puts its value there).
        public void Place(RectTransform canvas, Box band, Graphic icon = null, float auxW = 0f)
        {
            if (!canvas || band.IsEmpty) return;
            _stretch = 0f;
            var rt = (RectTransform)transform;
            Geo.Place(canvas, rt, band);
            // Icon / aux slots move in with the plate's frame keep-out (its own frame's top border and corner caps): an
            // icon or weight plate at the band edge lay under the top corner caps (user 2026-10-26). Own frame only, so
            // the slots never jump while the window is dragged over another one.
            FrameInsets(rt, Window, WorldRect(rt), FrameMargin * Mathf.Abs(canvas.lossyScale.x), out var kl, out var kr, out _, Frame);
            var wpu = Mathf.Max(Geo.WorldPerUnit(canvas), 1e-6f);
            var shift = WindowHeaderMath.SlotShift(Mathf.Max(kl, kr) / wpu, band.H);
            var slots = new Box(band.X + shift, band.Y, Mathf.Max(0f, band.W - 2f * shift), band.H);
            WindowHeaderMath.Slots(slots, icon, auxW, out IconBox, out AuxBox, out TitleBox);
            _hasIcon = icon;
            _iconShare = icon ? 1f : 0f;
            _auxShare = auxW > 0f && band.H > 0f ? auxW / band.H : 0f;
            _bandW = band.W;
            Arrange();
            if (icon) Geo.Place(canvas, icon.rectTransform, IconBox);
        }

        // Header of a window RuneSkin does not lay out (vanilla / mod panels): a band stretched over the top of `panel`,
        // moving `moved`; title = the window's own title text found in that top band (TMP or legacy, mirrored), else
        // `fallback`.
        public static RuneWindowHeader TopBand(RectTransform panel, string id, Transform moved, string fallback, bool adoptClose = false)
        {
            var h = Ensure(panel, id, moved ? moved : panel);
            if (!h) return null;
            h.Fallback(fallback);
            var search = ThemeTokens.GetFloat("size.window.band", 64f);
            if (!h._source && h._static == null)
            {
                var tmp = BandTitle<TMP_Text>(panel, search, h.transform, h._face);
                if (tmp) h.Adopt(tmp);
                else
                {
                    var legacy = BandTitle<Text>(panel, search, h.transform, h._face);
                    if (legacy) h.Mirror(legacy);
                    else h.Text(fallback);
                }
            }
            h.Frame = PanelDecor.Of(panel) ?? PanelDecor.Of(moved); // the window's own stack (child panel or root)
            if (adoptClose && !h.CloseButton) h.CloseButton = FindClose(moved ? moved : panel);
            h.Stretch(h.CloseButton ? WindowHeaderMath.IconShare : 0f);
            return h;
        }

        // The largest non-empty text whose centre lies in the top band of the panel (panel-local units).
        private static T BandTitle<T>(RectTransform panel, float height, Transform skip, Transform face) where T : Graphic
        {
            T best = null;
            var bestSize = 0f;
            var r = panel.rect;
            foreach (var t in panel.GetComponentsInChildren<T>(true))
            {
                if (!t || t.transform.IsChildOf(skip) || face && t.transform.IsChildOf(face)) continue; // never its own title
                string s;
                float size;
                if (t is TMP_Text tmp) { s = tmp.text; size = tmp.fontSize; }
                else if (t is Text legacy) { s = legacy.text; size = legacy.fontSize; }
                else continue;
                if (string.IsNullOrEmpty(WindowHeaderMath.StripTags(s)) || t.GetComponentInParent<Selectable>(true)) continue;
                var c = (Vector2)panel.InverseTransformPoint(t.rectTransform.TransformPoint(t.rectTransform.rect.center));
                if (c.y < r.yMax - height || c.y > r.yMax || c.x < r.xMin || c.x > r.xMax) continue;
                if (size > bestSize)
                {
                    best = t;
                    bestSize = size;
                }
            }
            return best;
        }

        // Band stretched over the top of the parent, the standard header height converted into the parent's units.
        public void Stretch() => Stretch(0f);

        // Stretched band with an aux slot `auxShare` header heights wide on the right (the owner places its control in
        // AuxLocal): the title stays centred and clear of it.
        public void Stretch(float auxShare)
        {
            _stretch = 1f;
            _hasIcon = false;
            _iconShare = 0f;
            _auxShare = Mathf.Max(0f, auxShare);
            StretchLayout();
        }

        // Aux slot of a stretched band in the header's own units (right end of the drawn plate, vertically centred,
        // IconShare of the height tall): empty before the first layout or without an aux slot.
        public Rect AuxLocal { get; private set; }

        private void StretchLayout()
        {
            var rt = (RectTransform)transform;
            var parent = rt.parent;
            var canvas = GetComponentInParent<Canvas>();
            var root = canvas ? canvas.rootCanvas.transform : null;
            var k = root && parent && Mathf.Abs(parent.lossyScale.y) > 1e-6f ? root.lossyScale.y / parent.lossyScale.y : 1f;
            var h = WindowHeaderMath.HeaderHeight(ThemeTokens.GetFloat("size.win.header", 40f), Responsive.ResponsiveContext.SkinScale) * k;
            var min = new Vector2(0f, 1f);
            if (rt.anchorMin != min) rt.anchorMin = min;
            if (rt.anchorMax != Vector2.one) rt.anchorMax = Vector2.one;
            var pivot = new Vector2(0.5f, 1f);
            if (rt.pivot != pivot) rt.pivot = pivot;
            var offMin = new Vector2(0f, -h);
            if ((rt.offsetMin - offMin).sqrMagnitude > 0.01f) rt.offsetMin = offMin;
            if (rt.offsetMax != Vector2.zero) rt.offsetMax = Vector2.zero;
            Arrange();
            PlaceClose(rt);
        }

        // The window's own close button (a mod window we do not lay out, e.g. Almanac's "Closebutton" under its frame):
        // shown in the header's aux slot like every window's close cap (user 2026-10-26: every mod panel fully skinned).
        public Button CloseButton;

        // The window's close button: an active Button named "*close*" (vanilla / mod convention), the smallest one.
        public static Button FindClose(Transform window)
        {
            if (!window) return null;
            Button best = null;
            var bestArea = float.MaxValue;
            foreach (var b in window.GetComponentsInChildren<Button>(false))
            {
                if (!b || b.name.IndexOf("close", System.StringComparison.OrdinalIgnoreCase) < 0 || !(b.transform is RectTransform r)) continue;
                var area = Mathf.Abs(r.rect.width * r.rect.height);
                // A closer spanning most of the window is a backdrop (click outside), not its close button.
                if (window is RectTransform wr && area > 0.25f * Mathf.Abs(wr.rect.width * wr.rect.height) * Sq(r.lossyScale.x / Mathf.Max(1e-6f, wr.lossyScale.x))) continue;
                if (area < bestArea) { best = b; bestArea = area; }
            }
            return best;
        }

        private static float Sq(float v) => v * v;

        private void PlaceClose(RectTransform rt)
        {
            if (!CloseButton || !(CloseButton.transform is RectTransform bt) || !(bt.parent is RectTransform bp) || AuxLocal.width <= 0f) return;
            var aux = AuxLocal;
            var s = Mathf.Min(aux.width, aux.height);
            var centre = bp.InverseTransformPoint(rt.TransformPoint(new Vector3(aux.xMax - s / 2f, aux.center.y, 0f)));
            var scale = Mathf.Abs(rt.lossyScale.y) / Mathf.Max(Mathf.Abs(bp.lossyScale.y), 1e-6f);
            var half = new Vector2(0.5f, 0.5f);
            if (bt.anchorMin != half || bt.anchorMax != half) { bt.anchorMin = half; bt.anchorMax = half; }
            if (bt.pivot != half) bt.pivot = half;
            var pos = (Vector2)centre - bp.rect.center;
            if ((bt.anchoredPosition - pos).sqrMagnitude > 0.0001f) bt.anchoredPosition = pos;
            var size = new Vector2(s, s) * scale;
            if ((bt.sizeDelta - size).sqrMagnitude > 0.0001f) bt.sizeDelta = size;
            if (bt.localScale != Vector3.one) bt.localScale = Vector3.one;
            if (bt.GetSiblingIndex() < transform.GetSiblingIndex() && bt.parent == transform.parent) bt.SetSiblingIndex(transform.GetSiblingIndex()); // over the band
        }

        // Plate and title inside the header rect (its own units), every size a share of the header height: identical on
        // every window at one height.
        private void Arrange()
        {
            var rt = (RectTransform)transform;
            var r = rt.rect;
            if (!(r.height > 0f)) return;
            SyncFace(rt);
            Style();
            WindowHeaderMath.LocalSlots(r.width, r.height, _hasIcon, _auxShare * r.height, out var plateInset, out var titleLeft, out var titleRight, out var fontSize);
            // The plate and its title inside the window frame: below the frame's top border, inside its side borders, clear
            // of its corner pieces (user 2026-10-24: the plate lay over the top border and cut the corner runes). The band
            // itself (drag area, icon / aux slots) keeps its place: only the drawn plate narrows, so nothing moves frame to
            // frame. Header-local units.
            FrameLocal(rt, out var fl, out var fr, out var ft);
            WindowHeaderMath.PlateInFrame(plateInset, titleLeft, titleRight, fl, fr, out var pl, out var pr, out var tl, out var tr);
            var plinth = ArrangePlinth(r.width, r.height - ft, pr);
            if (!plinth.IsEmpty)
            {
                // The plate ends under the plinth's left cap (its right knot replaced by the cap's terminal); the title
                // stays centred and clear of the plinth.
                pr = Mathf.Max(pr, r.width - plinth.PlateEnd);
                var clear = WindowHeaderMath.TitleClearOfPlinth(r.width, plinth, r.height * WindowHeaderMath.GapShare);
                tl = Mathf.Max(tl, clear);
                tr = Mathf.Max(tr, clear);
            }
            if (Plate)
            {
                var plateSprite = ThemeTokens.GetSprite(plinth.IsEmpty ? "sprite.plaque.header" : "sprite.plaque.header.open") ?? ThemeTokens.GetSprite("sprite.plaque.header");
                if (plateSprite && Plate.sprite != plateSprite) Plate.sprite = plateSprite;
                Inside(Plate.rectTransform, pl, pr, ft, 0f);
            }
            if (_auxShare > 0f)
            {
                var s = r.height * WindowHeaderMath.IconShare;
                var auxW = _auxShare * r.height;
                var x = r.xMax - pr - r.height * WindowHeaderMath.InsetShare + r.height * WindowHeaderMath.PlateInsetShare - auxW;
                AuxLocal = new Rect(x, r.yMin + (r.height - ft - s) / 2f, auxW, s);
            }
            else AuxLocal = default;
            if (!Title) return;
            if (Title.alignment != TextAlignmentOptions.Center) Title.alignment = TextAlignmentOptions.Center;
            if (Title.enableAutoSizing) Title.enableAutoSizing = false;
            // One size on every header; only a title wider than its room steps down (never below 60 %), after the empty
            // side's centring reserve gave up what it can (WindowHeaderMath.FreeEmptySide).
            var want = fontSize;
            _natural = 0f;
            _keepL = fl;
            _keepR = fr;
            if (!string.IsNullOrEmpty(Title.text))
            {
                var at = Title.fontSize > 0.5f ? Title.fontSize : fontSize;
                var natural = Title.GetPreferredValues(Title.text, 100000f, 100000f).x * fontSize / at;
                _natural = natural;
                WindowHeaderMath.FreeEmptySide(r.width, r.height, pl, pr, _hasIcon, _auxShare > 0f, natural, ref tl, ref tr);
                var room = Mathf.Max(0f, r.width - tl - tr);
                // a zero / negative room is still a cut title (the 3-column AB backpack before 0.25.9 had -18 units and
                // drew at full size, unmeasured): step down whenever it does not fit
                want = WindowHeaderMath.FitFont(fontSize, natural, room, WindowHeaderMath.TitleMinShare); // long chest names (run 20261026-182632: "Сундук из че…" at 75 %)
            }
            Inside(Title.rectTransform, tl, tr, ft, 0f);
            if (Mathf.Abs(Title.fontSize - want) > 0.05f) Title.fontSize = want;
            _ = _iconShare;
        }

        // ---------------------------------------------------------------- action plinth

        // Action buttons the window shows in its header (0 = none): the plate's right knot then ends in the plinth's
        // left-cap terminal and the plinth holds one cell per action (integrated header variant A, user 2026-10-27).
        public int PlinthCells { get; private set; }
        public WindowHeaderMath.Plinth PlinthLocal { get; private set; }
        private Image _capLeft, _capRight;
        private readonly System.Collections.Generic.List<Image> _cells = new System.Collections.Generic.List<Image>();

        public RuneWindowHeader Actions(int count)
        {
            PlinthCells = Mathf.Max(0, count);
            return this;
        }

        private static float Aspect(Image img) =>
            img && img.sprite && img.sprite.rect.height > 0f ? img.sprite.rect.width / img.sprite.rect.height : 0f;

        // Plinth pieces in the face (after the plate, under the title and the window's buttons); local units, origin = the
        // header rect's lower-left corner, plate = [0, h) high, right edge `width - plateRight`.
        private WindowHeaderMath.Plinth ArrangePlinth(float width, float plateH, float plateRight)
        {
            var want = PlinthCells;
            if (want > 0 && _face && !_capLeft)
            {
                _capLeft = PlinthPiece("RuneSkinPlinthCapL", "decor.plinth.cap.left");
                _capRight = PlinthPiece("RuneSkinPlinthCapR", "decor.plinth.cap.right");
            }
            while (want > _cells.Count && _face) _cells.Add(PlinthPiece("RuneSkinPlinthCell" + _cells.Count, "decor.plinth.cell"));
            var p = want > 0 && _capLeft && _capRight && _cells.Count > 0 && _capLeft.sprite && _capRight.sprite && _cells[0].sprite
                ? WindowHeaderMath.PlinthLayout(width - plateRight, 0f, plateH, want,
                    ThemeTokens.GetFloat("share.win.plinth.height", 1.29f), Aspect(_capLeft), Aspect(_cells[0]), Aspect(_capRight),
                    ThemeTokens.GetFloat("share.win.plinth.button", 0.8f), ThemeTokens.GetFloat("share.win.plinth.join", 0.3f))
                : default;
            PlinthLocal = p;
            Show(_capLeft, !p.IsEmpty, p.CapLeftX, p.Y, p.CapLeftW, p.H);
            Show(_capRight, !p.IsEmpty, p.CapRightX, p.Y, p.CapRightW, p.H);
            for (var i = 0; i < _cells.Count; i++) Show(_cells[i], !p.IsEmpty && i < p.Cells, p.CellX(i), p.Y, p.CellW, p.H);
            if (_face && _capLeft && Title && Title.transform.parent == _face) Title.transform.SetAsLastSibling();
            return p;
        }

        private Image PlinthPiece(string name, string role)
        {
            var img = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            img.rectTransform.SetParent(_face, false);
            img.raycastTarget = false;
            var m = DollCell.Mark(img, role, "window header action plinth (RuneWindowHeader)");
            m.Outcome = Restyler.Apply(img, m, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            img.enabled = false;
            if (Plate) img.transform.SetSiblingIndex(Plate.transform.GetSiblingIndex() + 1 + _cells.Count + (_capLeft ? 1 : 0));
            return img;
        }

        private static void Show(Image img, bool on, float x, float y, float w, float h)
        {
            if (!img) return;
            if (img.enabled != on) img.enabled = on;
            if (!on) return;
            var rt = img.rectTransform;
            if (rt.anchorMin != Vector2.zero) rt.anchorMin = Vector2.zero;
            if (rt.anchorMax != Vector2.zero) rt.anchorMax = Vector2.zero;
            if (rt.pivot != Vector2.zero) rt.pivot = Vector2.zero;
            var pos = new Vector2(x, y);
            if ((rt.anchoredPosition - pos).sqrMagnitude > 0.0001f) rt.anchoredPosition = pos;
            var size = new Vector2(w, h);
            if ((rt.sizeDelta - size).sqrMagnitude > 0.0001f) rt.sizeDelta = size;
            if (rt.localScale != Vector3.one) rt.localScale = Vector3.one;
        }

        // The plinth's button row in canvas units (first button's left to the last's right, button size, gap between
        // buttons), for the owner's action bar. False without a laid-out plinth.
        public bool PlinthRow(RectTransform canvas, out Box row, out float size, out float gap)
        {
            row = default;
            size = gap = 0f;
            var p = PlinthLocal;
            if (p.IsEmpty || !canvas || _cells.Count == 0 || !_cells[0] || !_cells[0].enabled || !(p.CellW > 0f)) return false;
            var cell = Geo.BoxOf(canvas, _cells[0].rectTransform);
            var k = cell.W / p.CellW;
            if (!(k > 0f)) return false;
            size = p.Button * k;
            gap = (p.CellW - p.Button) * k;
            row = new Box(cell.X + (cell.W - size) / 2f, cell.Y + (cell.H - size) / 2f, p.Cells * size + (p.Cells - 1) * gap, size);
            return true;
        }

        // Face on the band's rect (same parent), drawn right after the window's panel stack.
        private void SyncFace(RectTransform rt)
        {
            if (!_face) return;
            if (_face.parent != rt.parent) _face.SetParent(rt.parent, false);
            if (_face.anchorMin != rt.anchorMin) _face.anchorMin = rt.anchorMin;
            if (_face.anchorMax != rt.anchorMax) _face.anchorMax = rt.anchorMax;
            if (_face.pivot != rt.pivot) _face.pivot = rt.pivot;
            if (_face.sizeDelta != rt.sizeDelta) _face.sizeDelta = rt.sizeDelta;
            if (_face.anchoredPosition != rt.anchoredPosition) _face.anchoredPosition = rt.anchoredPosition;
            if (_face.localScale != rt.localScale) _face.localScale = rt.localScale;
            var stack = DrawAfter && DrawAfter.parent == rt.parent ? DrawAfter : rt.parent ? Backdrop(rt.parent) : null;
            // Right after the backdrop (an index taken while the face sits before it already counts the face).
            var want = stack ? stack.GetSiblingIndex() + (_face.GetSiblingIndex() < stack.GetSiblingIndex() ? 0 : 1) : 0;
            if (_face.GetSiblingIndex() != want) _face.SetSiblingIndex(want);
            if (_face.gameObject.activeSelf != gameObject.activeSelf) _face.gameObject.SetActive(gameObject.activeSelf);
        }

        // Gap between the header and the frame art it keeps clear of (canvas units).
        private const float FrameMargin = 2f;

        private static readonly Vector3[] W4 = new Vector3[4];

        private static Rect WorldRect(RectTransform r)
        {
            r.GetWorldCorners(W4);
            return Rect.MinMaxRect(Mathf.Min(W4[0].x, W4[2].x), Mathf.Min(W4[0].y, W4[2].y), Mathf.Max(W4[0].x, W4[2].x), Mathf.Max(W4[0].y, W4[2].y));
        }

        // Insets (world units, y up) that keep a header band inside its window's frame: below the top border band and inside
        // the side borders of that frame (PanelDecor Frame layer, its 9-slice border at the drawn pixels-per-unit), and clear
        // of the frame's own corner caps and of shared pieces (junctions) on it - an ornament left of the band centre pushes
        // the left edge, right of it the right edge. The window's frame = the frame overlapping the window rect most (the
        // smaller on a tie; the inventory windows' frames sit on hosts beside their panels), never another window's frame
        // the window was dragged over. Same root canvas only.
        public static void FrameInsets(Transform self, Transform window, Rect band, float margin, out float left, out float right, out float top, PanelDecor own = null)
        {
            left = right = top = 0f;
            var canvas = self ? self.GetComponentInParent<Canvas>() : null;
            var root = canvas ? canvas.rootCanvas : null;
            if (!root) return;
            var win = window is RectTransform wr ? WorldRect(wr) : band;
            PanelDecor bestDecor = null;
            Image best = null;
            var bestRect = default(Rect);
            var bestOverlap = 0f;
            var centre = band.center;
            // The window's own stack when the owner bound it (or one sits on the window itself): never another window's.
            if (!own && window) own = window.Find(PanelDecor.ChildName) is Transform st && st.TryGetComponent<PanelDecor>(out var onWindow) ? onWindow : null;
            var ownFrame = own ? own.Frame : null;
            if (ownFrame && ownFrame.enabled && ownFrame.sprite && ownFrame.isActiveAndEnabled && ownFrame.canvas != null && ownFrame.canvas.rootCanvas == root)
            {
                bestDecor = own;
                best = ownFrame;
                bestRect = WorldRect(ownFrame.rectTransform);
            }
            else foreach (var d in PanelDecor.All)
                {
                    var f = d ? d.Frame : null;
                    if (!f || !f.enabled || !f.sprite || !f.isActiveAndEnabled || f.canvas == null || f.canvas.rootCanvas != root) continue;
                    // Only a stack of this window (inside it, or on a host it lies in): never an unrelated window's frame.
                    var host = d.transform.parent;
                    if (window && host && !(host.IsChildOf(window) || window.IsChildOf(host))) continue;
                    var fr = WorldRect(f.rectTransform);
                    if (!fr.Contains(centre)) continue;
                    var ow = Mathf.Min(fr.xMax, win.xMax) - Mathf.Max(fr.xMin, win.xMin);
                    var oh = Mathf.Min(fr.yMax, win.yMax) - Mathf.Max(fr.yMin, win.yMin);
                    var overlap = ow > 0f && oh > 0f ? ow * oh : 0f;
                    if (overlap < bestOverlap || overlap <= 0f || overlap == bestOverlap && best && fr.width * fr.height >= bestRect.width * bestRect.height) continue;
                    bestDecor = d;
                    best = f;
                    bestRect = fr;
                    bestOverlap = overlap;
                }
            if (!best) return;
            var b = best.sprite.border; // x left, y bottom, z right, w top (sprite px)
            var ppu = Mathf.Max(best.pixelsPerUnit * best.pixelsPerUnitMultiplier, 1e-4f); // Image.multipliedPixelsPerUnit (internal)
            var s = best.rectTransform.lossyScale;
            var inner = new WindowHeaderMath.Span(bestRect.xMin + b.x / ppu * Mathf.Abs(s.x), bestRect.yMin, bestRect.xMax - b.z / ppu * Mathf.Abs(s.x), bestRect.yMax - b.w / ppu * Mathf.Abs(s.y));
            Ornaments.Clear();
            foreach (var o in FrameOrnament.All)
            {
                if (!o || !o.isActiveAndEnabled || !(o.transform is RectTransform rt)) continue;
                if (o.Owner ? o.Owner != bestDecor : false) continue; // another window's corner
                if (Mathf.Abs(rt.lossyScale.x) < 1e-4f || Mathf.Abs(rt.lossyScale.y) < 1e-4f) continue; // hidden cap (junction drawn instead)
                if (!o.TryGetComponent<Image>(out var img) || !img.enabled || !img.sprite || img.canvas == null || img.canvas.rootCanvas != root) continue;
                var r = WorldRect(rt);
                if (!o.Owner && !r.Overlaps(bestRect)) continue; // a shared piece elsewhere
                Ornaments.Add(new WindowHeaderMath.Span(r.xMin, r.yMin, r.xMax, r.yMax));
            }
            WindowHeaderMath.FrameKeepOut(new WindowHeaderMath.Span(band.xMin, band.yMin, band.xMax, band.yMax), inner, Ornaments, margin, out left, out right, out top);
        }

        private static readonly System.Collections.Generic.List<WindowHeaderMath.Span> Ornaments = new System.Collections.Generic.List<WindowHeaderMath.Span>();
        // What the face must draw over: the window's panel stack, else a screen dim leading the window (role scrim: the
        // stats window's own backdrop; at index 0 under it the face drew BELOW the 86 % dim - faint plate and title, run
        // 20261024-202818 info-CharacterStats).
        private static Transform Backdrop(Transform parent)
        {
            var stack = parent.Find(PanelDecor.ChildName);
            if (stack) return stack;
            for (var i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == FaceName) continue;
                return c.TryGetComponent<SkinMark>(out var m) && m.Role == "scrim" ? c : null;
            }
            return null;
        }

        // Frame keep-out of the header rect in its own units (FrameInsets, world -> local).
        private void FrameLocal(RectTransform rt, out float left, out float right, out float top)
        {
            left = right = top = 0f;
            var s = rt.lossyScale;
            if (Mathf.Abs(s.x) < 1e-6f || Mathf.Abs(s.y) < 1e-6f) return;
            var canvas = GetComponentInParent<Canvas>();
            var wpu = canvas ? Mathf.Abs(canvas.rootCanvas.transform.lossyScale.x) : Mathf.Abs(s.x);
            FrameInsets(rt, Window, WorldRect(rt), FrameMargin * wpu, out left, out right, out top, Frame);
            left /= Mathf.Abs(s.x);
            right /= Mathf.Abs(s.x);
            top = Mathf.Min(top / Mathf.Abs(s.y), rt.rect.height * 0.5f);
        }

        private static void Inside(RectTransform rt, float left, float right, float vertical) => Inside(rt, left, right, vertical, vertical);

        private static void Inside(RectTransform rt, float left, float right, float top, float bottom)
        {
            if (rt.anchorMin != Vector2.zero) rt.anchorMin = Vector2.zero;
            if (rt.anchorMax != Vector2.one) rt.anchorMax = Vector2.one;
            var pivot = new Vector2(0.5f, 0.5f);
            if (rt.pivot != pivot) rt.pivot = pivot;
            var a = new Vector2(left, bottom);
            var b = new Vector2(-right, -top);
            if ((rt.offsetMin - a).sqrMagnitude > 0.0001f) rt.offsetMin = a;
            if ((rt.offsetMax - b).sqrMagnitude > 0.0001f) rt.offsetMax = b;
            if (rt.localScale != Vector3.one) rt.localScale = Vector3.one;
        }

        private void LateUpdate()
        {
            Refresh();
            if (_stretch > 0f) StretchLayout();
            else Style();
        }

        // RuneSkin reverted the window's root: the mirrored title draws again, the header goes.
        public void Revert()
        {
            Release();
            if (_face) Destroy(_face.gameObject);
            if (this) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            Release();
            if (_face) Destroy(_face.gameObject);
        }

        private void OnEnable()
        {
            if (_face && !_face.gameObject.activeSelf) _face.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (_face && _face.gameObject.activeSelf) _face.gameObject.SetActive(false);
        }

        // Rune face + header colour on the title (role window.title), re-applied after a theme reload; the late-bound
        // loaded face (Valheim-Norsebold) is set directly once it exists.
        private void Style()
        {
            if (!Title) return;
            if (_styled != ThemeTokens.Revision)
            {
                _styled = ThemeTokens.Revision;
                var mark = Title.GetComponent<SkinMark>() ?? DollCell.Mark(Title, Role, "window header title (RuneWindowHeader, rune face)");
                mark.Outcome = Restyler.Apply(Title, mark, ThemeTokens.Role(Role)) ? Outcome.Styled : Outcome.Degraded;
                if (Title.textWrappingMode != TextWrappingModes.NoWrap) Title.textWrappingMode = TextWrappingModes.NoWrap;
                if (Title.overflowMode != TextOverflowModes.Ellipsis) Title.overflowMode = TextOverflowModes.Ellipsis;
            }
            var face = ThemeTokens.GetFont("font.display");
            if (face && Title.font != face)
            {
                Title.font = face;
                if (face.material && Title.fontSharedMaterial != face.material) Title.fontSharedMaterial = face.material;
            }
        }
    }
}
