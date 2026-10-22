using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Restyle
{
    // Global cell rule (batch G) on a restyled cell graphic (theme role "cell": slots, sockets, icon buttons): while the
    // graphic is square-ish (LayoutMath.IsCellShape), its main icon (largest drawn image inside it, 12-150 % of the cell
    // area) is centred on the cell, and every text drawn inside it (amount, timer, key, level) is moved back inside
    // the cell when it pokes out. Contents = the cell's subtree, or its parent's when the cell is a backdrop layer
    // without children. Runs after the screen code every frame (LateUpdate; screens may re-place icons each frame),
    // writes only when off by more than a quarter unit. Journaled: Revert restores positions still holding its value.
    [DisallowMultipleComponent]
    public sealed class CellFit : MonoBehaviour, IRevertible
    {
        private struct Moved
        {
            public RectTransform Rt;
            public Vector2 Orig, Set;
        }

        private static readonly List<Image> Images = new List<Image>(16);
        private static readonly List<TMP_Text> Texts = new List<TMP_Text>(8);
        private readonly List<Moved> _moved = new List<Moved>(4);
        private Image _cell;

        public static void Ensure(Image cell)
        {
            if (!cell || cell.TryGetComponent<CellFit>(out _)) return;
            var fit = cell.gameObject.AddComponent<CellFit>();
            fit.hideFlags = HideFlags.HideInInspector;
            fit._cell = cell;
        }

        private void LateUpdate()
        {
            if (_reverted) return;
            if (!_cell) _cell = GetComponent<Image>();
            if (!_cell || !_cell.enabled) return;
            var space = _cell.rectTransform;
            var cr = space.rect;
            if (!LayoutMath.IsCellShape(cr.width, cr.height)) return;
            var scope = transform.childCount > 0 || !transform.parent ? transform : transform.parent;
            CentreIcon(scope, space, cr);
            KeepTexts(scope, space, cr);
        }

        private void CentreIcon(Transform scope, RectTransform space, Rect cr)
        {
            Images.Clear();
            scope.GetComponentsInChildren(false, Images);
            Image icon = null;
            var best = 0f;
            var area = cr.width * cr.height;
            Rect ir = default;
            foreach (var img in Images)
            {
                if (!img || img == _cell || !img.enabled || !img.sprite || img.type == Image.Type.Filled) continue;
                if (img.canvasRenderer.GetInheritedAlpha() <= 0.05f || img.rectTransform.lossyScale.x <= 0.001f) continue;
                if (img.TryGetComponent<SkinMark>(out var mark) && mark.Outcome != Outcome.Content && !IconRole(mark.Role)) continue;
                var r = TextInset.Drawn(img, space);
                if (!cr.Contains(r.center)) continue;
                var a = r.width * r.height;
                if (a < area * 0.12f || a > area * 1.5f || a <= best) continue;
                icon = img;
                best = a;
                ir = r;
            }
            Images.Clear();
            if (!icon) return;
            var dx = LayoutMath.CentreOffset(cr.xMin, cr.xMax, ir.xMin, ir.xMax);
            var dy = LayoutMath.CentreOffset(cr.yMin, cr.yMax, ir.yMin, ir.yMax);
            Shift(icon.rectTransform, space, dx, dy);
        }

        private void KeepTexts(Transform scope, RectTransform space, Rect cr)
        {
            Texts.Clear();
            scope.GetComponentsInChildren(false, Texts);
            foreach (var t in Texts)
            {
                if (!t || !t.enabled || t.textInfo == null || t.textInfo.characterCount == 0) continue;
                var b = t.textBounds;
                if (b.size.x <= 0.01f || b.size.y <= 0.01f || b.size.x > 1e6f) continue;
                var r = TextInset.RectIn(t.rectTransform, new Rect(b.min.x, b.min.y, b.size.x, b.size.y), space);
                if (!cr.Contains(r.center)) continue;
                var dx = LayoutMath.ClampOffset(cr.xMin, cr.xMax, r.xMin, r.xMax);
                var dy = LayoutMath.ClampOffset(cr.yMin, cr.yMax, r.yMin, r.yMax);
                Shift(t.rectTransform, space, dx, dy);
            }
            Texts.Clear();
        }

        // Moves rt by (dx, dy) units of space (anchoredPosition is in its parent's units).
        private void Shift(RectTransform rt, RectTransform space, float dx, float dy)
        {
            if (Mathf.Abs(dx) <= 0.25f && Mathf.Abs(dy) <= 0.25f) return;
            var parent = rt.parent ? rt.parent : (Transform)space;
            var d = parent.InverseTransformVector(space.TransformVector(new Vector3(dx, dy, 0f)));
            var i = _moved.FindIndex(m => m.Rt == rt);
            var pos = rt.anchoredPosition;
            // The screen code re-placed it since (position no longer ours): its value is the new original.
            if (i < 0) { _moved.Add(new Moved { Rt = rt, Orig = pos }); i = _moved.Count - 1; }
            else if (pos != _moved[i].Set) _moved[i] = new Moved { Rt = rt, Orig = pos };
            var set = pos + new Vector2(d.x, d.y);
            rt.anchoredPosition = set;
            _moved[i] = new Moved { Rt = rt, Orig = _moved[i].Orig, Set = set };
        }

        private static bool IconRole(string role) =>
            role != null && (role.StartsWith("glyph.", System.StringComparison.Ordinal) || role.Contains(".glyph") || role == "map.check" || role == "toggle.check");

        private bool _reverted;

        public void Revert()
        {
            _reverted = true;
            foreach (var m in _moved)
                if (m.Rt && m.Rt.anchoredPosition == m.Set) m.Rt.anchoredPosition = m.Orig;
            _moved.Clear();
            Destroy(this);
        }
    }
}
