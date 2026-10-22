using RuneSkin.Theme;
using TMPro;
using UnityEngine;

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
            // Localisation / mods rewrite labels after the restyle: a new text re-decides one line vs two.
            if (_dirty || (_text && (!_text.enableAutoSizing || !ReferenceEquals(_text.text, _measured)))) Apply();
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
            var m = new Vector4(LabelFit.Margin(inset, lmin.x - br.xMin), LabelFit.Margin(v, br.yMax - lmax.y),
                LabelFit.Margin(inset, br.xMax - lmax.x), LabelFit.Margin(v, lmin.y - br.yMin));
            // One line while it still reads at >= share.text.fit.oneline of the full size, else two lines (a short label
            // never breaks just because wrapping lets it stay larger).
            var areaW = br.width - Mathf.Max(0f, inset) * 2f;
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

        public void Revert()
        {
            if (_text && _has && _text.margin == _set) _text.margin = _orig;
            Destroy(this);
        }
    }
}
