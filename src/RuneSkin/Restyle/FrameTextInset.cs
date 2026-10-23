using System.Collections.Generic;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Restyle
{
    // Texts placed directly in a box that carries the RuneSkin panel stack, without a LayoutGroup to pad (ContentInset
    // covers those): user 2026-10-23, the large-map filter hint ("Метки карты можно отфильтровать…") ran its first /
    // last letters under the rune end-columns and copper braces of the corner caps - the box kept vanilla's text
    // margins. Each text's area is kept inside the box inset by the stack's keep-out (PanelDecor.ContentInset, top /
    // bottom) and, for a box shorter than its caps, the whole cap width + margin left / right (PanelDecor.SideInset);
    // TMP margins only, never lowered below vanilla's. A box without a ContentSizeFitter grows to the wrapped text's
    // height (text stretched with the box); with a fitter the margins already raise the text's preferred height.
    // Journaled (margins, box size) and reverted while RuneSkin's values still hold.
    [DisallowMultipleComponent]
    public sealed class FrameTextInset : MonoBehaviour, IRevertible
    {
        private static readonly List<TMP_Text> Texts = new List<TMP_Text>(8);
        private readonly Dictionary<TMP_Text, (Vector4 orig, Vector4 set)> _margins = new Dictionary<TMP_Text, (Vector4, Vector4)>();
        private PanelDecor _decor;
        private bool _hasSize;
        private Vector2 _origSize, _setSize;
        private float _side;
        private int _next;

        public static void Ensure(RectTransform box)
        {
            if (!box || box.GetComponent<FrameTextInset>()) return;
            box.gameObject.AddComponent<FrameTextInset>().hideFlags = HideFlags.HideInInspector;
        }

        private void OnEnable() => _next = 0;

        private void LateUpdate()
        {
            if (Time.frameCount < _next) return;
            _next = Time.frameCount + 5;
            Apply();
        }

        public void Apply()
        {
            var box = (RectTransform)transform;
            if (!_decor)
            {
                var d = box.Find(PanelDecor.ChildName);
                if (!d || !d.TryGetComponent(out _decor)) return;
            }
            var margin = ThemeTokens.GetFloat("size.frame.text.margin", 8f);
            var vert = Mathf.Max(_decor.ContentInset(), ThemeTokens.GetFloat("size.tooltip.content.inset", 26f)) + margin;
            // Latched: once the box needed the side inset it keeps it (growing the box must not hand the width back).
            _side = Mathf.Max(_side, _decor.SideInset());
            var side = Mathf.Max(vert, _side);
            var br = box.rect;
            Texts.Clear();
            box.GetComponentsInChildren(false, Texts);
            var grow = 0f;
            foreach (var t in Texts)
            {
                if (!t || !t.enabled || string.IsNullOrEmpty(t.text)) continue;
                if (t.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                if (t.GetComponentInParent<Selectable>() || t.GetComponentInParent<PanelDecor>() || t.GetComponentInParent<PanelCaps>()) continue;
                if (t.GetComponentInParent<LayoutGroup>() is LayoutGroup g && g.transform.IsChildOf(box)) continue; // its group pads it
                var rt = t.rectTransform;
                var tr = TextInset.RectIn(rt, rt.rect, box);
                var k = Mathf.Abs(box.InverseTransformVector(rt.TransformVector(Vector3.right)).x);
                if (!(k > 0.0001f)) k = 1f;
                var orig = _margins.TryGetValue(t, out var j) && t.margin == j.set ? j.orig : t.margin;
                var (l, top, r, b) = PanelMath.InsetMargins(tr.xMin, tr.yMin, tr.xMax, tr.yMax, br.xMin, br.yMin, br.xMax, br.yMax,
                    side, vert, k, orig.x, orig.y, orig.z, orig.w);
                var want = new Vector4(l, top, r, b);
                if (rt.rect.width - l - r < rt.rect.width / 4f) continue; // a wrong measurement must not erase the text
                if ((want - t.margin).sqrMagnitude > 0.0025f) t.margin = want;
                _margins[t] = (orig, want);
                // Height the wrapped text needs inside its margins vs what the rect gives.
                if (rt.parent == box && !Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y))
                {
                    var need = t.GetPreferredValues(t.text, Mathf.Max(1f, rt.rect.width - l - r), 0f).y + top + b;
                    grow = Mathf.Max(grow, (need - rt.rect.height) * k);
                }
            }
            Texts.Clear();
            if (grow > 0.5f && !box.GetComponent<ContentSizeFitter>() && !box.GetComponent<LayoutGroup>())
            {
                if (!_hasSize || box.sizeDelta != _setSize) { _origSize = box.sizeDelta; _hasSize = true; }
                box.sizeDelta = _setSize = box.sizeDelta + new Vector2(0f, grow);
            }
        }

        public void Revert()
        {
            foreach (var kv in _margins)
                if (kv.Key && kv.Key.margin == kv.Value.set) kv.Key.margin = kv.Value.orig;
            _margins.Clear();
            var box = (RectTransform)transform;
            if (_hasSize && box.sizeDelta == _setSize) box.sizeDelta = _origSize;
            Destroy(this);
        }
    }
}
