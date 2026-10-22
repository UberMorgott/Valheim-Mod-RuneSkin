using System.Collections.Generic;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.MapScreen
{
    // Large map key hint row: ONE row, the vanilla one. Vanilla large/KeyHints (in Minimap.m_hints, shown with the
    // game's KeyHints setting, Minimap.cs:1238-1242) holds keyboard_hints {AddPin, CrossOffPin, RemovePin, PingPanel},
    // each a mouse glyph (keyboard_hint Image) + Label (TMP, auto-size 1-20, Truncate: "Указать метку" was cut), plus
    // gamepad_hints / gamepadmouse_hints text rows for controllers. RuneSkin used to draw its own text line in the same
    // band, so both overlapped (user screenshot 2026-10-22). Now KeyHints itself sits in the band under the map image,
    // the keyboard items get the theme body font / colour (no auto-size, no truncation), a zoom item (cloned from
    // PingPanel, mouse wheel = middle button glyph) is appended, and the items are spaced evenly; labels shrink to
    // size.map.hint.font.min and trailing items drop before anything overlaps or clips (MinimapMath.FitHints).
    public sealed partial class MinimapSkin
    {
        private RectTransform _keyRow;
        private readonly List<RectTransform> _hintItems = new List<RectTransform>();
        private readonly List<Image> _hintIcons = new List<Image>();
        private readonly List<TMP_Text> _hintLabels = new List<TMP_Text>();
        private float[] _hintIconW = new float[0], _hintLabelW = new float[0];
        private string _hintKey;

        private void BuildHints()
        {
            _footer = _large.Find("KeyHints") as RectTransform;
            if (!_footer) return;
            _keyRow = _footer.Find("keyboard_hints") as RectTransform;
            if (!_keyRow) return;
            var ping = _keyRow.Find("PingPanel");
            if (ping && !_keyRow.Find("RuneSkin_Zoom"))
            {
                var zoom = Instantiate(ping.gameObject, _keyRow, false);
                zoom.name = "RuneSkin_Zoom";
                // Vanilla's Localize component would rewrite the label to the ping text on enable.
                foreach (var c in zoom.GetComponentsInChildren<MonoBehaviour>(true))
                    if (c && c.GetType().Name == "Localize") DestroyImmediate(c);
            }
            // RuneSkin places the row and its items (UIInputHint force-rebuilds layouts when the input source changes).
            foreach (var g in _footer.GetComponents<LayoutGroup>()) g.enabled = false;
            foreach (var f in _footer.GetComponents<ContentSizeFitter>()) f.enabled = false;
            foreach (var g in _keyRow.GetComponentsInChildren<LayoutGroup>(true)) g.enabled = false;
            foreach (var f in _keyRow.GetComponentsInChildren<ContentSizeFitter>(true)) f.enabled = false;
            for (var i = 0; i < _keyRow.childCount; i++)
            {
                var item = _keyRow.GetChild(i) as RectTransform;
                var label = item ? item.Find("Label") : null;
                var icon = item ? item.Find("keyboard_hint") : null;
                if (!label || !icon || !label.TryGetComponent<TMP_Text>(out var text) || !icon.TryGetComponent<Image>(out var img)) continue;
                _hintItems.Add(item);
                _hintLabels.Add(text);
                _hintIcons.Add(img);
                img.preserveAspect = true;
                text.enableAutoSizing = false;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                var font = ThemeTokens.GetFont("font.body");
                if (font)
                {
                    text.font = font;
                    if (font.material) text.fontSharedMaterial = font.material;
                }
                text.color = ThemeTokens.GetColor("color.text.body", Color.white);
            }
            _hintIconW = new float[_hintItems.Count];
            _hintLabelW = new float[_hintItems.Count];
        }

        private void LayoutHints(float inset)
        {
            if (!_footer) return;
            var band = HintBand();
            _footer.anchorMin = Vector2.zero;
            _footer.anchorMax = new Vector2(1f, 0f);
            _footer.pivot = new Vector2(0.5f, 0f);
            var right = _map.m_mapImageLarge.rectTransform.offsetMax.x;
            var min = new Vector2(inset, inset);
            var max = new Vector2(right, inset + band);
            if (_footer.offsetMin != min) _footer.offsetMin = min;
            if (_footer.offsetMax != max) _footer.offsetMax = max;
            if (_footer.localScale != Vector3.one) _footer.localScale = Vector3.one;
            if (!_keyRow || _hintItems.Count == 0 || band <= 0f) return;
            Stretch(_keyRow);

            var zoomText = Words.Get("$runeskin_map_zoom");
            var zoomLabel = _hintLabels[_hintLabels.Count - 1];
            if (_hintItems[_hintItems.Count - 1].name == "RuneSkin_Zoom" && zoomLabel.text != zoomText) zoomLabel.text = zoomText;

            var width = _footer.rect.width;
            var key = width.ToString("0.#") + "|" + band.ToString("0.#");
            for (var i = 0; i < _hintLabels.Count; i++) key += "|" + _hintLabels[i].text;
            if (key == _hintKey) return;
            _hintKey = key;

            var font = ThemeTokens.GetFloat("size.map.hint.font", 18f);
            var minFont = ThemeTokens.GetFloat("size.map.hint.font.min", 13f);
            var iconSize = Mathf.Min(ThemeTokens.GetFloat("size.map.hint.icon", 26f), band - 2f);
            var gap = ThemeTokens.GetFloat("size.map.hint.gap", 6f);
            var spacingMin = ThemeTokens.GetFloat("size.map.hint.spacing", 16f);
            for (var i = 0; i < _hintLabels.Count; i++)
            {
                _hintLabels[i].fontSize = font;
                _hintIconW[i] = iconSize;
                _hintLabelW[i] = Mathf.Ceil(_hintLabels[i].GetPreferredValues(_hintLabels[i].text, float.PositiveInfinity, float.PositiveInfinity).x) + 1f;
            }
            var shown = MinimapMath.FitHints(_hintIconW, _hintLabelW, _hintItems.Count, width, gap, spacingMin, minFont / font, out var scale, out var spacing);
            var x = spacing;
            for (var i = 0; i < _hintItems.Count; i++)
            {
                var item = _hintItems[i];
                var on = i < shown;
                if (item.gameObject.activeSelf != on) item.gameObject.SetActive(on);
                if (!on) continue;
                var labelW = _hintLabelW[i] * scale;
                var w = iconSize + gap + labelW;
                item.anchorMin = item.anchorMax = new Vector2(0f, 0.5f);
                item.pivot = new Vector2(0f, 0.5f);
                item.localScale = Vector3.one;
                item.anchoredPosition = new Vector2(x, 0f);
                item.sizeDelta = new Vector2(w, band);
                var ic = _hintIcons[i].rectTransform;
                ic.anchorMin = ic.anchorMax = new Vector2(0f, 0.5f);
                ic.pivot = new Vector2(0f, 0.5f);
                ic.localScale = Vector3.one;
                ic.anchoredPosition = Vector2.zero;
                ic.sizeDelta = new Vector2(iconSize, iconSize);
                var lt = _hintLabels[i].rectTransform;
                lt.anchorMin = lt.anchorMax = new Vector2(0f, 0.5f);
                lt.pivot = new Vector2(0f, 0.5f);
                lt.localScale = Vector3.one;
                lt.anchoredPosition = new Vector2(iconSize + gap, 0f);
                lt.sizeDelta = new Vector2(labelW, band);
                _hintLabels[i].fontSize = font * scale;
                x += w + spacing;
            }
        }
    }
}
