using System.Collections.Generic;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Restyle
{
    // Text area of a plated button label = the button rect inset (x: plate cap inset, scaled down on small buttons;
    // y: small pad), whatever the vanilla label rect is (theme role fitInset, Restyler.Fit). Recomputed whenever the
    // label's rect changes (layout groups size buttons after Awake). Revert restores the vanilla margin.
    [DisallowMultipleComponent]
    public sealed class PlateLabelFit : MonoBehaviour, IRevertible
    {
        // Serialized: clones of a styled template (rows, mod entries) carry the inset and find their own button.
        public float Inset;
        private RectTransform _button;
        private Vector4 _orig, _set;
        private bool _has, _dirty = true;
        private TMP_Text _text;
        private string _measured;
        private int _next;

        public void Init(RectTransform button, float inset)
        {
            _text = GetComponent<TMP_Text>();
            if (!_has && _text) { _orig = _text.margin; _has = true; }
            _button = button;
            Inset = inset;
            _dirty = true;
            Apply();
        }

        private void OnEnable() => _dirty = true;

        private void OnRectTransformDimensionsChange() => _dirty = true;

        private void LateUpdate()
        {
            if (Reverted) return;
            // Localisation / mods rewrite labels after the restyle: a new text re-decides one line vs two.
            // Glyphs beside the label may be placed after it (screen layout passes): re-measure now and then too.
            if (_dirty || Time.frameCount >= _next || (_text && (!_text.enableAutoSizing || !ReferenceEquals(_text.text, _measured))))
            {
                _next = Time.frameCount + 15 + (GetInstanceID() & 7);
                Apply();
            }
        }

        private void Apply()
        {
            if (!_text) _text = GetComponent<TMP_Text>();
            if (!_button) _button = GetComponentInParent<UnityEngine.UI.Selectable>(true)?.transform as RectTransform;
            if (!_text || !_button || Inset <= 0f) return;
            if (!_has) { _orig = _text.margin; _has = true; }
            var br = _button.rect;
            if (br.width <= 1f || br.height <= 1f) return; // not laid out yet: next change
            _dirty = false;
            var l = _text.rectTransform;
            var lmin = _button.InverseTransformPoint(l.TransformPoint(l.rect.min));
            var lmax = _button.InverseTransformPoint(l.TransformPoint(l.rect.max));
            var inset = LabelFit.Inset(Inset, br.width, ThemeTokens.GetFloat("share.text.fit.inset", 0.12f));
            var v = ThemeTokens.GetFloat("size.text.fit.vinset", 4f);
            // Free span (batch G): a glyph beside the label inside the plate (craft button anvil) narrows it; the label
            // is centred in what is left, the cap inset kept on both sides of that span.
            var span = Span(br, lmin, lmax);
            var m = new Vector4(LabelFit.Margin(inset, lmin.x - span.Min), LabelFit.Margin(v, br.yMax - lmax.y),
                LabelFit.Margin(inset, span.Max - lmax.x), LabelFit.Margin(v, lmin.y - br.yMin));
            // One line while it still reads at >= share.text.fit.oneline of the full size, else two lines (a short label
            // never breaks just because wrapping lets it stay larger).
            var areaW = span.Size - Mathf.Max(0f, inset) * 2f;
            // Screen code may reset the label to a fixed size after the restyle (settings tab column): that size becomes
            // the auto-size maximum again.
            if (!_text.enableAutoSizing)
            {
                _text.fontSizeMax = _text.fontSize;
                _text.fontSizeMin = Mathf.Min(_text.fontSize, ThemeTokens.GetFloat("size.text.fit.min", 13f));
                _text.enableAutoSizing = true;
            }
            var full = _text.fontSizeMax;
            // Measured at the full size with auto-size off (the auto-sized current size depends on the last wrap mode).
            var auto = _text.enableAutoSizing;
            var cur = _text.fontSize;
            var curWrap = _text.textWrappingMode;
            _text.enableAutoSizing = false;
            _text.fontSize = full;
            _text.textWrappingMode = TextWrappingModes.NoWrap; // preferred width = the one-line width
            _measured = _text.text;
            var w = _text.GetPreferredValues(_measured, float.PositiveInfinity, float.PositiveInfinity).x;
            _text.textWrappingMode = curWrap;
            _text.fontSize = cur;
            _text.enableAutoSizing = auto;
            var one = LabelFit.OneLineSize(areaW, w, full);
            var wrap = one < Mathf.Max(_text.fontSizeMin, full * ThemeTokens.GetFloat("share.text.fit.oneline", 0.72f));
            var mode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            if (_text.textWrappingMode != mode) _text.textWrappingMode = mode;
            if (m == _text.margin) return;
            _text.margin = _set = m;
        }

        private static readonly List<Image> Images = new List<Image>(8);
        private static readonly List<LayoutMath.Span> Obstacles = new List<LayoutMath.Span>(4);

        // Plate minus the glyphs beside the drawn label (images inside the button, not the plate, not the label's own).
        private LayoutMath.Span Span(Rect br, Vector3 lmin, Vector3 lmax)
        {
            var tmin = lmin.x;
            var tmax = lmax.x;
            var b = _text.textBounds;
            if (b.size.x > 0.01f && b.size.x < 1e6f)
            {
                var r = TextInset.RectIn(_text.rectTransform, new Rect(b.min.x, b.min.y, b.size.x, b.size.y), _button);
                tmin = r.xMin;
                tmax = r.xMax;
            }
            var plate = _button.GetComponent<Selectable>() is Selectable s ? s.targetGraphic : null;
            Obstacles.Clear();
            Images.Clear();
            _button.GetComponentsInChildren(false, Images);
            foreach (var img in Images)
            {
                if (!img || img == plate || !img.enabled || !img.sprite || img.type == Image.Type.Filled) continue;
                if (img.transform.IsChildOf(transform) || transform.IsChildOf(img.transform)) continue;
                if (Ornament(img)) continue;
                var r = TextInset.Drawn(img, _button);
                if (r.width >= br.width * 0.6f || r.yMax < lmin.y || r.yMin > lmax.y) continue;
                Obstacles.Add(new LayoutMath.Span(r.xMin, r.xMax));
            }
            Images.Clear();
            return LayoutMath.FreeSpan(br.xMin, br.xMax, tmin, tmax, Obstacles);
        }

        // Plate decor (knots, caps) frames a label; it is not an icon beside it.
        internal static bool Ornament(Image img) =>
            img.name.EndsWith("Knot", System.StringComparison.Ordinal) || img.TryGetComponent<SkinMark>(out var mark) && mark.Role != null &&
            (mark.Role.StartsWith("ornament", System.StringComparison.Ordinal) || mark.Role.StartsWith("decor", System.StringComparison.Ordinal));

        public bool Reverted { get; private set; }

        public void Revert()
        {
            Reverted = true;
            if (_text && _has && _text.margin == _set) _text.margin = _orig;
            Destroy(this);
        }
    }
}
