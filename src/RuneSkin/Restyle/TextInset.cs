using System.Collections.Generic;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Restyle
{
    // Global text placement rule (batch G) for a restyled TMP text without a plate fit (PlateLabelFit owns centred
    // plate labels), decided from its wiring every layout pass:
    // - field: value / placeholder of a TMP_InputField -> left-aligned, its text viewport spans the box (the input's
    //   target graphic) inset size.field.inset from each edge, or from a glyph inside the box (search magnifier);
    // - dropdown: caption of a TMP_Dropdown -> left-aligned, area from the box edge + size.field.inset to the arrow;
    // - row: a left-aligned label in a Button with a visible plate (list rows) -> starts size.row.pad after the row
    //   edge or the icon beside it;
    // - toggle: the label beside a checkbox -> size.row.pad from the box on its side;
    // - any: a text that still truncates (Ellipsis / Truncate) auto-sizes down to size.text.fit.min.
    // Writes are journaled (margin, alignment, viewport offsets, auto-size) and reverted while they still hold
    // RuneSkin's value. Cells (square slot / socket buttons) are CellFit's.
    [DisallowMultipleComponent]
    public sealed class TextInset : MonoBehaviour, IRevertible
    {
        private enum Mode { None, Field, Dropdown, Row, Toggle }

        private static readonly List<Image> Images = new List<Image>(32);
        private static readonly List<LayoutMath.Span> Obstacles = new List<LayoutMath.Span>(8);

        private TMP_Text _text;
        private bool _dirty = true;
        private int _next;
        private bool _hasMargin, _hasAlign, _hasViewport, _hasAuto;
        private Vector4 _origMargin, _setMargin;
        private HorizontalAlignmentOptions _origAlign, _setAlign;
        private RectTransform _viewport;
        private Vector2 _origMin, _origMax, _setMin, _setMax;
        private bool _origAuto;
        private float _origSizeMin, _origSizeMax;

        public static void Ensure(TMP_Text text)
        {
            // A plate fit being reverted this frame (Destroy is deferred) no longer owns the label.
            if (!text || text.TryGetComponent<PlateLabelFit>(out var fit) && !fit.Reverted) return;
            if (!text.TryGetComponent<TextInset>(out var inset))
            {
                inset = text.gameObject.AddComponent<TextInset>();
                inset.hideFlags = HideFlags.HideInInspector;
            }
            inset._dirty = true;
        }

        private void OnEnable() => _dirty = true;

        private void OnRectTransformDimensionsChange() => _dirty = true;

        private void LateUpdate()
        {
            if (_reverted) return;
            // Screen code moves rows / icons after the restyle without touching this rect: re-measure now and then.
            if (!_dirty && Time.frameCount < _next) return;
            _dirty = false;
            _next = Time.frameCount + 10 + (GetInstanceID() & 7);
            Apply();
        }

        private void Apply()
        {
            if (!_text) _text = GetComponent<TMP_Text>();
            if (!_text || !_text.enabled) return;
            var sel = GetComponentInParent<Selectable>();
            if (sel is TMP_InputField input) Field(input);
            else if (sel is TMP_Dropdown dropdown) Caption(dropdown);
            else if (sel is Toggle toggle && !GetComponentInParent<TMP_Dropdown>()) Beside(toggle);
            else if (sel is Button button) Row(button);
            FitTruncated();
        }

        // ------------------------------------------------------------------ modes

        private void Field(TMP_InputField input)
        {
            var box = input.targetGraphic ? input.targetGraphic.rectTransform : (RectTransform)input.transform;
            var pad = ThemeTokens.GetFloat("size.field.inset", 12f);
            Align(HorizontalAlignmentOptions.Left);
            var vp = input.textViewport;
            // One owner per viewport: the value text moves it (the placeholder shares it).
            if (vp && _text != input.textComponent && input.textComponent && input.textComponent.transform.IsChildOf(vp)) return;
            var space = box;
            var br = box.rect;
            var area = AreaIn(space);
            var span = Free(box, br, area, input.targetGraphic, box.parent ? box.parent : box);
            if (vp && _text.transform.IsChildOf(vp) && vp != box)
            {
                // Viewport edges move by the error of the text area (the text stretches inside it; RectMask2D clips there).
                var dl = span.Min + pad - area.xMin;
                var dr = span.Max - pad - area.xMax;
                if (Mathf.Abs(dl) < 0.05f && Mathf.Abs(dr) < 0.05f) return;
                if (!_hasViewport || _viewport != vp)
                {
                    _viewport = vp;
                    _origMin = vp.offsetMin;
                    _origMax = vp.offsetMax;
                    _hasViewport = true;
                }
                var k = vp.parent ? ScaleX(vp.parent, space) : 1f;
                vp.offsetMin = _setMin = vp.offsetMin + new Vector2(dl / k, 0f);
                vp.offsetMax = _setMax = vp.offsetMax + new Vector2(dr / k, 0f);
                return;
            }
            Margins(span.Min, span.Max, pad, pad, space, right: true);
        }

        private void Caption(TMP_Dropdown dropdown)
        {
            if (dropdown.captionText != _text) return;
            var box = dropdown.targetGraphic ? dropdown.targetGraphic.rectTransform : (RectTransform)dropdown.transform;
            var pad = ThemeTokens.GetFloat("size.field.inset", 12f);
            Align(HorizontalAlignmentOptions.Left);
            var area = AreaIn(box);
            var span = Free(box, box.rect, area, dropdown.targetGraphic, box.parent ? box.parent : box);
            Margins(span.Min, span.Max, pad, pad, box, right: true);
        }

        private void Row(Button button)
        {
            if (!(button.targetGraphic is Image plate) || !plate.sprite || !plate.enabled) return;
            var box = (RectTransform)button.transform;
            var br = box.rect;
            if (LayoutMath.IsCellShape(br.width, br.height)) return; // cell: CellFit
            var h = _text.horizontalAlignment;
            if (h != HorizontalAlignmentOptions.Left && h != HorizontalAlignmentOptions.Justified && h != HorizontalAlignmentOptions.Flush) return;
            var area = AreaIn(box);
            if (!br.Contains(area.center)) return;
            var span = Free(box, br, area, plate, box);
            Margins(span.Min, span.Max, ThemeTokens.GetFloat("size.row.pad", 12f), 0f, box, right: false);
        }

        private void Beside(Toggle toggle)
        {
            if (!(toggle.targetGraphic is Image check) || !check.enabled) return;
            var space = check.rectTransform;
            var b = Drawn(check, space);
            var area = AreaIn(space);
            if (b.Contains(area.center)) return;
            var pad = ThemeTokens.GetFloat("size.row.pad", 12f);
            var h = _text.horizontalAlignment;
            var rect = RectIn(_text.rectTransform, _text.rectTransform.rect, space);
            var k = ScaleX(_text.rectTransform, space);
            var m = Margin0();
            if (area.center.x >= b.center.x && h == HorizontalAlignmentOptions.Left)
                m.x = LayoutMath.StartMargin(rect.xMin, b.xMax, pad) / k;
            else if (area.center.x < b.center.x && h == HorizontalAlignmentOptions.Right)
                m.z = LayoutMath.EndMargin(rect.xMax, b.xMin, pad) / k;
            else return;
            SetMargin(m);
        }

        // Label that still does not fit (Ellipsis / Truncate) shrinks to the fit floor instead of losing letters.
        private void FitTruncated()
        {
            if (_text.overflowMode != TextOverflowModes.Ellipsis && _text.overflowMode != TextOverflowModes.Truncate) return;
            if (_text.enableAutoSizing && _text.fontSizeMin <= ThemeTokens.GetFloat("size.text.fit.min", 12f) + 0.01f) return;
            if (!_text.isTextTruncated) return;
            if (!_hasAuto)
            {
                _origAuto = _text.enableAutoSizing;
                _origSizeMin = _text.fontSizeMin;
                _origSizeMax = _text.fontSizeMax;
                _hasAuto = true;
            }
            var max = _text.enableAutoSizing ? _text.fontSizeMax : _text.fontSize;
            _text.fontSizeMax = max;
            _text.fontSizeMin = Mathf.Min(max, ThemeTokens.GetFloat("size.text.fit.min", 12f));
            _text.enableAutoSizing = true;
        }

        // ------------------------------------------------------------------ helpers

        // Margins (in the text's own units) that put the text area at [min + startPad, max - endPad] of space.
        private void Margins(float min, float max, float startPad, float endPad, RectTransform space, bool right)
        {
            var rect = RectIn(_text.rectTransform, _text.rectTransform.rect, space);
            var k = ScaleX(_text.rectTransform, space);
            var m = Margin0();
            m.x = LayoutMath.StartMargin(rect.xMin, min, startPad) / k;
            if (right) m.z = LayoutMath.EndMargin(rect.xMax, max, endPad) / k;
            // Never squeeze the area below a third of the rect (a wrong measurement must not erase the label).
            var width = _text.rectTransform.rect.width;
            if (width - m.x - m.z < width / 3f) return;
            SetMargin(m);
        }

        private Vector4 Margin0() => _hasMargin && _text.margin == _setMargin ? _setMargin : _text.margin;

        private void SetMargin(Vector4 m)
        {
            if (!_hasMargin || _text.margin != _setMargin) { _origMargin = _text.margin; _hasMargin = true; }
            if ((m - _text.margin).sqrMagnitude < 0.0025f) { _setMargin = _text.margin; return; }
            _text.margin = _setMargin = m;
        }

        private void Align(HorizontalAlignmentOptions a)
        {
            if (_text.horizontalAlignment == a) return;
            if (!_hasAlign || _text.horizontalAlignment != _setAlign) { _origAlign = _text.horizontalAlignment; _hasAlign = true; }
            _text.horizontalAlignment = _setAlign = a;
        }

        // Current text area (rect minus margins) in space.
        private Rect AreaIn(RectTransform space)
        {
            var r = _text.rectTransform.rect;
            var m = _text.margin;
            var area = Rect.MinMaxRect(r.xMin + m.x, r.yMin + m.w, Mathf.Max(r.xMin + m.x, r.xMax - m.z), Mathf.Max(r.yMin + m.w, r.yMax - m.y));
            return RectIn(_text.rectTransform, area, space);
        }

        // Free span of the box beside the text area (icons inside the box, left / right of the area).
        private LayoutMath.Span Free(RectTransform box, Rect br, Rect area, Graphic skip, Transform scope)
        {
            Obstacles.Clear();
            Images.Clear();
            scope.GetComponentsInChildren(false, Images);
            foreach (var img in Images)
            {
                if (!img || img == skip || !img.sprite || !img.enabled || img.type == Image.Type.Filled) continue;
                if (img.transform.IsChildOf(transform) || transform.IsChildOf(img.transform)) continue;
                if (Ornament(img)) continue;
                var r = Drawn(img, box);
                if (!br.Contains(r.center) || r.width >= br.width * 0.6f || r.yMax < area.yMin || r.yMin > area.yMax) continue;
                Obstacles.Add(new LayoutMath.Span(r.xMin, r.xMax));
            }
            Images.Clear();
            return LayoutMath.FreeSpan(br.xMin, br.xMax, area.xMin, area.xMax, Obstacles);
        }

        // Units of space per local unit of from (scales between them; 1 in a flat hierarchy).
        private static float ScaleX(Transform from, Transform space)
        {
            var a = space.InverseTransformVector(from.TransformVector(Vector3.right));
            var k = Mathf.Abs(a.x);
            return k > 0.0001f ? k : 1f;
        }

        public static Rect RectIn(RectTransform rt, Rect local, Transform space)
        {
            var a = space.InverseTransformPoint(rt.TransformPoint(local.min));
            var b = space.InverseTransformPoint(rt.TransformPoint(local.max));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        // Drawn rect of an image in space (preserveAspect: the sprite fitted by the pivot, as Image draws it).
        public static Rect Drawn(Image image, Transform space)
        {
            var rt = image.rectTransform;
            var r = rt.rect;
            if (image.preserveAspect && image.sprite && (image.type == Image.Type.Simple || image.type == Image.Type.Filled))
            {
                var size = image.sprite.rect.size;
                if (size.y > 0f && r.height > 0f && r.width > 0f)
                {
                    var spriteRatio = size.x / size.y;
                    if (spriteRatio > r.width / r.height)
                    {
                        var h = r.width / spriteRatio;
                        r.y += (r.height - h) * rt.pivot.y;
                        r.height = h;
                    }
                    else
                    {
                        var w = r.height * spriteRatio;
                        r.x += (r.width - w) * rt.pivot.x;
                        r.width = w;
                    }
                }
            }
            return RectIn(rt, r, space);
        }

        // Plate decor (knots, caps) frames a label; it is not an icon beside it.
        // Name read through NameCache (a string per read otherwise; this runs on every periodic re-measure).
        internal static bool Ornament(Image img) =>
            img.TryGetComponent<SkinMark>(out var mark) && mark.Role != null &&
            (mark.Role.StartsWith("ornament", System.StringComparison.Ordinal) || mark.Role.StartsWith("decor", System.StringComparison.Ordinal)) ||
            NameCache.Of(img.gameObject).EndsWith("Knot", System.StringComparison.Ordinal);

        private bool _reverted;

        public void Revert()
        {
            _reverted = true;
            if (_text)
            {
                if (_hasMargin && _text.margin == _setMargin) _text.margin = _origMargin;
                if (_hasAlign && _text.horizontalAlignment == _setAlign) _text.horizontalAlignment = _origAlign;
                if (_hasAuto)
                {
                    _text.enableAutoSizing = _origAuto;
                    _text.fontSizeMin = _origSizeMin;
                    _text.fontSizeMax = _origSizeMax;
                }
            }
            if (_hasViewport && _viewport && _viewport.offsetMin == _setMin && _viewport.offsetMax == _setMax)
            {
                _viewport.offsetMin = _origMin;
                _viewport.offsetMax = _origMax;
            }
            Destroy(this);
        }
    }
}
