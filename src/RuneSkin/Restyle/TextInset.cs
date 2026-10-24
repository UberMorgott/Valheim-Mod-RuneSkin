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
    // - any: a text that still truncates (Ellipsis / Truncate) auto-sizes down to size.text.fit.min - never an input
    //   field's text: its overflow is Overflow and its viewport grows to hold one line (0.20.3 track F, the blinking
    //   new-world inputs; see LayoutMath.MayShrinkToFit).
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
        private bool _toggleLabel, _hasOverflow;
        private TextOverflowModes _origOverflow;
        private int _columns = -1; // 1 = column text of a multi-text row button, 0 = not, -1 = undecided
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
            var field = sel is TMP_InputField;
            if (sel is TMP_InputField input) Field(input);
            else if (sel is TMP_Dropdown dropdown) Caption(dropdown);
            else if (sel is Toggle toggle && !GetComponentInParent<TMP_Dropdown>()) { Beside(toggle); _toggleLabel = true; }
            else if (sel is Button button)
            {
                Row(button);
                if (_columns < 0) _columns = RuneSkin.Classify.Classifier.RowTexts(button) > 1 ? 1 : 0;
            }
            // Input texts are never shrunk (LayoutMath.MayShrinkToFit: the auto-size here fought the readable floor and
            // blinked the new-world inputs); the field holds their line instead (FieldLine).
            if (LayoutMath.MayShrinkToFit(field)) FitTruncated();
        }

        // Value / placeholder of an input field: never truncated (TMP_InputField scrolls the text inside its RectMask2D
        // viewport; vanilla's Truncate dropped the whole line once the readable floor made it taller than the 25-unit
        // text area). Journaled like the toggle labels' overflow.
        private void FieldText()
        {
            if (_text.overflowMode != TextOverflowModes.Truncate && _text.overflowMode != TextOverflowModes.Ellipsis) return;
            if (!_hasOverflow) { _origOverflow = _text.overflowMode; _hasOverflow = true; }
            _text.overflowMode = TextOverflowModes.Overflow;
        }

        // Height of one line of the field's value / placeholder (current size and faces, fallback glyphs included), in
        // the viewport parent's units.
        private static float FieldLine(TMP_InputField input, Transform space)
        {
            var h = 0f;
            foreach (var t in new[] { input.textComponent, input.placeholder as TMP_Text })
            {
                if (!t) continue;
                var line = t.GetPreferredValues((t.text ?? "") + "Hg", float.PositiveInfinity, float.PositiveInfinity).y + t.margin.y + t.margin.w;
                var k = Mathf.Abs(space.InverseTransformVector(t.rectTransform.TransformVector(Vector3.up)).y);
                h = Mathf.Max(h, line * (k > 0.0001f ? k : 1f));
            }
            return h;
        }

        // ------------------------------------------------------------------ modes

        private void Field(TMP_InputField input)
        {
            var box = input.targetGraphic ? input.targetGraphic.rectTransform : (RectTransform)input.transform;
            var pad = ThemeTokens.GetFloat("size.field.inset", 12f);
            Align(HorizontalAlignmentOptions.Left);
            FieldText();
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
                if (Mathf.Abs(dl) < 0.05f) dl = 0f;
                if (Mathf.Abs(dr) < 0.05f) dr = 0f;
                // Vertically the viewport holds one line of the text at its current (readable-floor) size, inside the box.
                var host = vp.parent ? vp.parent : (Transform)box;
                var grow = LayoutMath.FieldViewportGrow(RectIn(vp, vp.rect, host).height, RectIn(box, br, host).height, FieldLine(input, host));
                if (grow < 0.05f) grow = 0f;
                if (dl == 0f && dr == 0f && grow == 0f) return;
                if (!_hasViewport || _viewport != vp)
                {
                    _viewport = vp;
                    _origMin = vp.offsetMin;
                    _origMax = vp.offsetMax;
                    _hasViewport = true;
                }
                var k = vp.parent ? ScaleX(vp.parent, space) : 1f;
                vp.offsetMin = _setMin = vp.offsetMin + new Vector2(dl / k, -grow);
                vp.offsetMax = _setMax = vp.offsetMax + new Vector2(dr / k, grow);
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
        // A column text of a multi-text row (server / save rows) that runs past its own rect shrinks the same way: an
        // overflowing column drew over the next one.
        private void FitTruncated()
        {
            // Checkbox labels keep the readable size and run past their (narrow vanilla) box instead of shrinking or
            // cutting: the character-creation sex toggles shrank to 15.5 px fighting the readable floor (runs
            // 20261023-012017 / -032312), a one-word label has no second line to take.
            if (_toggleLabel && (_text.overflowMode == TextOverflowModes.Ellipsis || _text.overflowMode == TextOverflowModes.Truncate) && _text.isTextTruncated)
            {
                if (!_hasOverflow) { _origOverflow = _text.overflowMode; _hasOverflow = true; }
                _text.overflowMode = TextOverflowModes.Overflow;
                return;
            }
            if (_toggleLabel && _text.enableAutoSizing && _text.fontSizeMin < ThemeTokens.GetFloat("size.text.readable", 16f)) return;
            var column = _columns == 1 && _text.overflowMode == TextOverflowModes.Overflow;
            if (!column && _text.overflowMode != TextOverflowModes.Ellipsis && _text.overflowMode != TextOverflowModes.Truncate) return;
            // Column texts of a list row keep the readable size (16) and end in their ellipsis below it (m5-text-min,
            // run 20261022-210451: server names shrank to 12-14 px).
            var floor = _columns == 1 ? ThemeTokens.GetFloat("size.text.readable", 16f) : ThemeTokens.GetFloat("size.text.fit.min", 12f);
            if (_text.enableAutoSizing && _text.fontSizeMin <= floor + 0.01f) return;
            if (column ? !ColumnOverflows() : !_text.isTextTruncated) return;
            if (!_hasAuto)
            {
                _origAuto = _text.enableAutoSizing;
                _origSizeMin = _text.fontSizeMin;
                _origSizeMax = _text.fontSizeMax;
                _hasAuto = true;
            }
            var max = _text.enableAutoSizing ? _text.fontSizeMax : _text.fontSize;
            _text.fontSizeMax = max;
            _text.fontSizeMin = Mathf.Min(max, floor);
            _text.enableAutoSizing = true;
        }

        private bool ColumnOverflows()
        {
            var r = _text.rectTransform.rect;
            var m = _text.margin;
            var w = r.width - m.x - m.z;
            if (w <= 1f || string.IsNullOrEmpty(_text.text)) return false;
            var size = _text.GetPreferredValues(_text.text, _text.textWrappingMode == TextWrappingModes.NoWrap ? float.PositiveInfinity : w, float.PositiveInfinity);
            return size.x > w + 0.5f || size.y > r.height - m.y - m.w + 0.5f;
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
                if (_hasOverflow && _text.overflowMode == TextOverflowModes.Overflow) _text.overflowMode = _origOverflow;
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
