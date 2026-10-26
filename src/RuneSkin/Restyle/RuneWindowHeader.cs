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
        private string _fallback = "", _fallbackKey, _static, _staticKey, _staticLang, _staticText;
        private int _styled = -1;
        private float _stretch, _iconShare, _auxShare;
        private bool _hasIcon;

        public string TitleText => Title ? Title.text : "";

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

        // A legacy uGUI title (mods built on UnityEngine.UI.Text, e.g. Almanac's "topic"), mirrored the same way.
        public RuneWindowHeader Mirror(Text legacy)
        {
            Source(legacy);
            return this;
        }

        private void Source(Graphic g)
        {
            if (!g || g == _source) return;
            if (_source) _source.enabled = true;
            _source = g;
            _static = null;
            Refresh();
        }

        // A RuneSkin title (windows without one of their own); `text` may be a "$key" (localized).
        public RuneWindowHeader Text(string text)
        {
            if (!_source && _static == text) return this;
            if (_source) { _source.enabled = true; _source = null; }
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
            var shown = WindowHeaderMath.Title(raw, _fallback);
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
            FrameInsets(rt, Window, WorldRect(rt), FrameMargin * Mathf.Abs(canvas.lossyScale.x), out var kl, out var kr, out _);
            var wpu = Mathf.Max(Geo.WorldPerUnit(canvas), 1e-6f);
            var shift = WindowHeaderMath.SlotShift(Mathf.Max(kl, kr) / wpu, band.H);
            var slots = new Box(band.X + shift, band.Y, Mathf.Max(0f, band.W - 2f * shift), band.H);
            WindowHeaderMath.Slots(slots, icon, auxW, out IconBox, out AuxBox, out TitleBox);
            _hasIcon = icon;
            _iconShare = icon ? 1f : 0f;
            _auxShare = auxW > 0f && band.H > 0f ? auxW / band.H : 0f;
            Arrange();
            if (icon) Geo.Place(canvas, icon.rectTransform, IconBox);
        }

        // Header of a window RuneSkin does not lay out (vanilla / mod panels): a band stretched over the top of `panel`,
        // moving `moved`; title = the window's own title text found in that top band (TMP or legacy, mirrored), else
        // `fallback`.
        public static RuneWindowHeader TopBand(RectTransform panel, string id, Transform moved, string fallback)
        {
            var h = Ensure(panel, id, moved ? moved : panel);
            if (!h) return null;
            h.Fallback(fallback);
            var search = ThemeTokens.GetFloat("size.window.band", 64f);
            if (!h._source && h._static == null)
            {
                var tmp = BandTitle<TMP_Text>(panel, search, h.transform);
                if (tmp) h.Adopt(tmp);
                else
                {
                    var legacy = BandTitle<Text>(panel, search, h.transform);
                    if (legacy) h.Mirror(legacy);
                    else h.Text(fallback);
                }
            }
            h.Stretch();
            return h;
        }

        // The largest non-empty text whose centre lies in the top band of the panel (panel-local units).
        private static T BandTitle<T>(RectTransform panel, float height, Transform skip) where T : Graphic
        {
            T best = null;
            var bestSize = 0f;
            var r = panel.rect;
            foreach (var t in panel.GetComponentsInChildren<T>(true))
            {
                if (!t || t.transform.IsChildOf(skip)) continue;
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
        public void Stretch()
        {
            _stretch = 1f;
            _hasIcon = false;
            _iconShare = _auxShare = 0f;
            StretchLayout();
        }

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
            if (Plate) Inside(Plate.rectTransform, pl, pr, ft, 0f);
            if (!Title) return;
            Inside(Title.rectTransform, tl, tr, ft, 0f);
            if (Title.alignment != TextAlignmentOptions.Center) Title.alignment = TextAlignmentOptions.Center;
            if (Title.enableAutoSizing) Title.enableAutoSizing = false;
            // One size on every header; only a title wider than its narrowed plate steps down (never below 75 %).
            var room = Mathf.Max(0f, r.width - tl - tr);
            var want = fontSize;
            if (room > 0f && !string.IsNullOrEmpty(Title.text))
            {
                var at = Title.fontSize > 0.5f ? Title.fontSize : fontSize;
                var natural = Title.GetPreferredValues(Title.text, 100000f, 100000f).x * fontSize / at;
                want = WindowHeaderMath.FitFont(fontSize, natural, room, 0.75f);
            }
            if (Mathf.Abs(Title.fontSize - want) > 0.05f) Title.fontSize = want;
            _ = _iconShare;
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
            var stack = rt.parent ? Backdrop(rt.parent) : null;
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
        public static void FrameInsets(Transform self, Transform window, Rect band, float margin, out float left, out float right, out float top)
        {
            left = right = top = 0f;
            var own = self ? self.GetComponentInParent<Canvas>() : null;
            var root = own ? own.rootCanvas : null;
            if (!root) return;
            var win = window is RectTransform wr ? WorldRect(wr) : band;
            PanelDecor bestDecor = null;
            Image best = null;
            var bestRect = default(Rect);
            var bestOverlap = 0f;
            var centre = band.center;
            foreach (var d in PanelDecor.All)
            {
                var f = d ? d.Frame : null;
                if (!f || !f.enabled || !f.sprite || !f.isActiveAndEnabled || f.canvas == null || f.canvas.rootCanvas != root) continue;
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
            WindowHeaderMath.FrameKeepOut(new WindowHeaderMath.Span(band.xMin, band.yMin, band.xMax, band.yMax), inner, Ornaments.ToArray(), margin, out left, out right, out top);
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
            FrameInsets(rt, Window, WorldRect(rt), FrameMargin * wpu, out left, out right, out top);
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
            if (_source) _source.enabled = true;
            if (_face) Destroy(_face.gameObject);
            if (this) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_source) _source.enabled = true;
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
