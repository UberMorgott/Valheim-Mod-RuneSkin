using System.Collections.Generic;
using RuneSkin.Theme;
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
    // writes only when off by more than a quarter unit. Journaled: Revert restores positions, scales and font sizes
    // still holding its value.
    // Item cells (role flag "zones", 0.10.2): the icon is scaled to a centred square inset by the zone band
    // (LayoutMath.ZoneBand, theme share.cell.zone) on every side; texts in the upper half go into the top band
    // (quality), texts in the lower half and the durability bar into the bottom band (amount), each shrunk to the band
    // height when taller, so counts never overlap the icon (user screenshot 2026-10-22: stack count on the sword).
    [DisallowMultipleComponent]
    public sealed class CellFit : MonoBehaviour, IRevertible
    {
        private struct Moved
        {
            public RectTransform Rt;
            public Vector2 Orig, Set;
        }

        private struct Scaled
        {
            public Transform T;
            public Vector3 Orig, Set;
        }

        private struct Sized
        {
            public TMP_Text T;
            public float Orig, Set;
            public bool Auto;
        }

        private static readonly List<Image> Images = new List<Image>(16);
        private static readonly List<TMP_Text> Texts = new List<TMP_Text>(8);
        private static readonly List<GuiBar> Bars = new List<GuiBar>(2);
        private readonly List<Moved> _moved = new List<Moved>(4);
        private readonly List<Scaled> _scaled = new List<Scaled>(1);
        private readonly List<Sized> _sized = new List<Sized>(2);
        private Image _cell;
        private bool _zones;

        public static void Ensure(Image cell, bool zones = false)
        {
            if (!cell) return;
            if (!cell.TryGetComponent<CellFit>(out var fit))
            {
                fit = cell.gameObject.AddComponent<CellFit>();
                fit.hideFlags = HideFlags.HideInInspector;
                fit._cell = cell;
            }
            fit._zones = zones;
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
            if (_zones)
            {
                var band = LayoutMath.ZoneBand(Mathf.Min(cr.width, cr.height), ThemeTokens.GetFloat("share.cell.zone", 0.17f), ThemeTokens.GetFloat("size.cell.zone.min", 9f));
                ZoneIcon(scope, space, cr, band);
                ZoneTexts(scope, space, cr, band);
                ZoneBars(scope, space, cr, band);
                return;
            }
            CentreIcon(scope, space, cr);
            KeepTexts(scope, space, cr);
        }

        // Main icon: largest drawn sprite image whose centre lies in the cell, 12-150 % of its area. Images drawn in
        // a clear colour (vanilla InventoryElement.m_dropFocus, Color.clear until a touch drag) are not icons.
        private Image FindIcon(Transform scope, RectTransform space, Rect cr, out Rect ir)
        {
            Images.Clear();
            scope.GetComponentsInChildren(false, Images);
            Image icon = null;
            var best = 0f;
            var area = cr.width * cr.height;
            ir = default;
            foreach (var img in Images)
            {
                if (!img || img == _cell || !img.enabled || !img.sprite || img.type == Image.Type.Filled) continue;
                if (img.canvasRenderer.GetInheritedAlpha() <= 0.05f || img.rectTransform.lossyScale.x <= 0.001f) continue;
                var c = img.color;
                if (c.a <= 0.05f && c.r + c.g + c.b <= 0.05f) continue;
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
            return icon;
        }

        private void CentreIcon(Transform scope, RectTransform space, Rect cr)
        {
            var icon = FindIcon(scope, space, cr, out var ir);
            if (!icon) return;
            var dx = LayoutMath.CentreOffset(cr.xMin, cr.xMax, ir.xMin, ir.xMax);
            var dy = LayoutMath.CentreOffset(cr.yMin, cr.yMax, ir.yMin, ir.yMax);
            Shift(icon.rectTransform, space, dx, dy);
        }

        // Icon scaled (uniform localScale) so its drawn rect's longer side = the zone icon side, then centred.
        private void ZoneIcon(Transform scope, RectTransform space, Rect cr, float band)
        {
            var icon = FindIcon(scope, space, cr, out var ir);
            if (!icon) return;
            var side = LayoutMath.ZoneIconSide(Mathf.Min(cr.width, cr.height), band);
            var cur = Mathf.Max(ir.width, ir.height);
            if (cur > 0.5f && Mathf.Abs(cur - side) > 0.25f)
            {
                Scale(icon.transform, side / cur);
                ir = TextInset.Drawn(icon, space);
            }
            var dx = LayoutMath.CentreOffset(cr.xMin, cr.xMax, ir.xMin, ir.xMax);
            var dy = LayoutMath.CentreOffset(cr.yMin, cr.yMax, ir.yMin, ir.yMax);
            Shift(icon.rectTransform, space, dx, dy);
        }

        private void ZoneTexts(Transform scope, RectTransform space, Rect cr, float band)
        {
            var pad = ThemeTokens.GetFloat("size.cell.zone.pad", 1f);
            var floor = ThemeTokens.GetFloat("size.cell.zone.font.min", 8f);
            Texts.Clear();
            scope.GetComponentsInChildren(false, Texts);
            foreach (var t in Texts)
            {
                if (!t || !t.enabled || t.textInfo == null || t.textInfo.characterCount == 0) continue;
                var b = t.textBounds;
                if (b.size.x <= 0.01f || b.size.y <= 0.01f || b.size.x > 1e6f) continue;
                var r = TextInset.RectIn(t.rectTransform, new Rect(b.min.x, b.min.y, b.size.x, b.size.y), space);
                if (!cr.Contains(r.center)) continue;
                // Zone by the text box (not the glyphs): stable while the value changes.
                var box = TextInset.RectIn(t.rectTransform, t.rectTransform.rect, space);
                var top = (cr.Contains(box.center) ? box.center.y : r.center.y) >= cr.center.y;
                Resize(t, LayoutMath.FitFontSize(t.fontSize, OrigSize(t), r.height, band - 2f * pad, floor));
                var zMin = top ? cr.yMax - band : cr.yMin;
                var dx = LayoutMath.ClampOffset(cr.xMin + pad, cr.xMax - pad, r.xMin, r.xMax);
                var dy = LayoutMath.ClampOffset(zMin + pad, zMin + band - pad, r.yMin, r.yMax);
                Shift(t.rectTransform, space, dx, dy);
            }
            Texts.Clear();
        }

        // Durability bar (vanilla GuiBar) inside the bottom band, inside the cell.
        private void ZoneBars(Transform scope, RectTransform space, Rect cr, float band)
        {
            var pad = ThemeTokens.GetFloat("size.cell.zone.pad", 1f);
            Bars.Clear();
            scope.GetComponentsInChildren(false, Bars);
            foreach (var bar in Bars)
            {
                if (!bar || !(bar.transform is RectTransform rt)) continue;
                var r = TextInset.RectIn(rt, rt.rect, space);
                if (!cr.Contains(r.center)) continue;
                var dx = LayoutMath.ClampOffset(cr.xMin + pad, cr.xMax - pad, r.xMin, r.xMax);
                var dy = LayoutMath.ClampOffset(cr.yMin + pad, cr.yMin + band - pad, r.yMin, r.yMax);
                Shift(rt, space, dx, dy);
            }
            Bars.Clear();
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
            var i = MovedIndex(rt);
            var pos = rt.anchoredPosition;
            // The screen code re-placed it since (position no longer ours): its value is the new original.
            if (i < 0) { _moved.Add(new Moved { Rt = rt, Orig = pos }); i = _moved.Count - 1; }
            else if (pos != _moved[i].Set) _moved[i] = new Moved { Rt = rt, Orig = pos };
            var set = pos + new Vector2(d.x, d.y);
            rt.anchoredPosition = set;
            _moved[i] = new Moved { Rt = rt, Orig = _moved[i].Orig, Set = set };
        }

        // Multiplies t's localScale by k (uniform x / y), journaled like Shift.
        private void Scale(Transform t, float k)
        {
            var i = ScaledIndex(t);
            var cur = t.localScale;
            if (i < 0) { _scaled.Add(new Scaled { T = t, Orig = cur }); i = _scaled.Count - 1; }
            else if (cur != _scaled[i].Set) _scaled[i] = new Scaled { T = t, Orig = cur };
            var set = new Vector3(cur.x * k, cur.y * k, cur.z);
            t.localScale = set;
            _scaled[i] = new Scaled { T = t, Orig = _scaled[i].Orig, Set = set };
        }

        // The font size the screen set (before any zone shrink); a size changed by someone else since is the new one.
        private float OrigSize(TMP_Text t)
        {
            var i = SizedIndex(t);
            if (i < 0) return t.fontSize;
            if (!Mathf.Approximately(t.fontSize, _sized[i].Set)) { _sized.RemoveAt(i); return t.fontSize; }
            return _sized[i].Orig;
        }

        private void Resize(TMP_Text t, float size)
        {
            if (Mathf.Abs(t.fontSize - size) <= 0.1f) return;
            var i = SizedIndex(t);
            if (i < 0) { _sized.Add(new Sized { T = t, Orig = t.fontSize, Auto = t.enableAutoSizing }); i = _sized.Count - 1; }
            if (t.enableAutoSizing) t.enableAutoSizing = false;
            t.fontSize = size;
            _sized[i] = new Sized { T = t, Orig = _sized[i].Orig, Set = size, Auto = _sized[i].Auto };
        }

        // Journal lookups by plain loops: FindIndex with a capturing lambda allocated a closure + delegate per call,
        // i.e. per text per cell per frame (perf audit B2: ~20 KB per frame with the inventory open).
        private int MovedIndex(RectTransform rt)
        {
            for (var i = 0; i < _moved.Count; i++)
                if (_moved[i].Rt == rt) return i;
            return -1;
        }

        private int ScaledIndex(Transform t)
        {
            for (var i = 0; i < _scaled.Count; i++)
                if (_scaled[i].T == t) return i;
            return -1;
        }

        private int SizedIndex(TMP_Text t)
        {
            for (var i = 0; i < _sized.Count; i++)
                if (_sized[i].T == t) return i;
            return -1;
        }

        private static bool IconRole(string role) =>
            role != null && (role.StartsWith("glyph.", System.StringComparison.Ordinal) || role.Contains(".glyph") || role == "map.check" || role == "toggle.check");

        private bool _reverted;

        public void Revert()
        {
            _reverted = true;
            foreach (var m in _moved)
                if (m.Rt && m.Rt.anchoredPosition == m.Set) m.Rt.anchoredPosition = m.Orig;
            foreach (var s in _scaled)
                if (s.T && s.T.localScale == s.Set) s.T.localScale = s.Orig;
            foreach (var s in _sized)
                if (s.T && Mathf.Approximately(s.T.fontSize, s.Set)) { s.T.fontSize = s.Orig; s.T.enableAutoSizing = s.Auto; }
            _moved.Clear();
            _scaled.Clear();
            _sized.Clear();
            Destroy(this);
        }
    }
}
