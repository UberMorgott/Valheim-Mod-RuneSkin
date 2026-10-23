using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Restyle
{
    // UI drawn before an opaque window and lying fully inside its opaque area is never seen: it is gated (RenderGate,
    // owner "cover") while the window shows. Candidates are the subtrees drawn before the window: its earlier siblings at
    // every level up to the root canvas, and other root canvases with a lower sorting order. A candidate whose drawn
    // bounds lie inside the opaque rect is gated whole; one that only overlaps it is searched a few levels down. A
    // subtree holding a canvas that sorts above the window (the HUD strip over an open inventory, HudCluster
    // AboveInventory) is never gated whole, only its other children. Recomputed while the window shows (HUD parts move,
    // appear); only differences are applied.
    public static class CoverCull
    {
        public const string Owner = "cover";
        private const int MaxDepth = 4;
        private const float Tol = 0.5f;

        private static readonly HashSet<GameObject> Covered = new HashSet<GameObject>();
        private static readonly HashSet<GameObject> Next = new HashSet<GameObject>();
        private static readonly List<GameObject> Drop = new List<GameObject>();
        private static readonly List<Graphic> Graphics = new List<Graphic>(256);
        private static readonly List<Canvas> Canvases = new List<Canvas>(16);
        private static readonly List<Canvas> Inner = new List<Canvas>(16);
        private static readonly Vector3[] Corners = new Vector3[4];

        public static int CoveredCount => Covered.Count;

        // windows[i] is drawn over opaque[i] (screen px).
        public static void Refresh(IReadOnlyList<Transform> windows, IReadOnlyList<Rect> opaque)
        {
            Next.Clear();
            for (var i = 0; i < windows.Count; i++)
            {
                var w = windows[i];
                var r = opaque[i];
                if (!w || r.width <= 1f || r.height <= 1f) continue;
                Collect(w, r);
            }
            Drop.Clear();
            foreach (var go in Covered)
                if (!go || !Next.Contains(go)) Drop.Add(go);
            foreach (var go in Drop)
            {
                Covered.Remove(go);
                if (go) RenderGate.Set(go, Owner, false);
            }
            foreach (var go in Next)
                if (Covered.Add(go)) RenderGate.Set(go, Owner, true);
            Next.Clear();
        }

        public static void Clear()
        {
            foreach (var go in Covered)
                if (go) RenderGate.Set(go, Owner, false);
            Covered.Clear();
        }

        private static void Collect(Transform window, Rect opaque)
        {
            var sortCanvas = SortCanvas(window);
            if (!sortCanvas) return;
            var order = sortCanvas.sortingOrder;
            var root = sortCanvas.rootCanvas ? sortCanvas.rootCanvas.transform : null;
            // Earlier siblings at every level, up to the canvas that sorts the window.
            for (var node = window; node && node.parent && node != sortCanvas.transform; node = node.parent)
            {
                var parent = node.parent;
                var index = node.GetSiblingIndex();
                for (var i = 0; i < index; i++) Consider(parent.GetChild(i), opaque, order, 0);
                if (parent == root) break;
            }
            // Root canvases drawn earlier (lower sorting order).
            Canvases.Clear();
            Canvases.AddRange(Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None));
            foreach (var c in Canvases)
            {
                if (!c || !c.isRootCanvas || !c.isActiveAndEnabled || c.renderMode == RenderMode.WorldSpace) continue;
                if (c.sortingOrder >= order || c.transform == root) continue;
                foreach (Transform child in c.transform) Consider(child, opaque, order, 0);
            }
            Canvases.Clear();
        }

        private static void Consider(Transform t, Rect opaque, int windowOrder, int depth)
        {
            if (!t || !t.gameObject.activeInHierarchy || !(t is RectTransform)) return;
            // Sorted above the window by its own (or an ancestor's) override-sorting canvas: drawn after it.
            var sort = SortCanvas(t);
            if (sort && sort.overrideSorting && sort.sortingOrder > windowOrder) return;
            if (!Bounds(t, out var b, out var above)) return;
            if (!RenderGateMath.Overlaps(opaque.xMin, opaque.yMin, opaque.xMax, opaque.yMax, b.xMin, b.yMin, b.xMax, b.yMax)) return;
            var aboveWindow = above != null && above.sortingOrder > windowOrder;
            if (!aboveWindow && RenderGateMath.Inside(opaque.xMin, opaque.yMin, opaque.xMax, opaque.yMax, b.xMin, b.yMin, b.xMax, b.yMax, Tol))
            {
                Next.Add(t.gameObject);
                return;
            }
            if (depth >= MaxDepth) return;
            foreach (Transform child in t) Consider(child, opaque, windowOrder, depth + 1);
        }

        // Screen bounds of the subtree's enabled graphics; above = the highest override-sorting canvas inside it.
        private static bool Bounds(Transform t, out Rect bounds, out Canvas above)
        {
            bounds = default;
            above = null;
            // own list: Collect is iterating Canvases while it calls Consider -> Bounds (combined run 20261023-161531:
            // 36 x 'Collection was modified' from InventoryGui.Update, half-applied gates = covered > 0)
            Inner.Clear();
            t.GetComponentsInChildren(false, Inner);
            foreach (var c in Inner)
                if (c.overrideSorting && c.enabled && (above == null || c.sortingOrder > above.sortingOrder)) above = c;
            Inner.Clear();
            Graphics.Clear();
            t.GetComponentsInChildren(false, Graphics);
            var any = false;
            float x0 = 0f, y0 = 0f, x1 = 0f, y1 = 0f;
            foreach (var g in Graphics)
            {
                if (!g.enabled || !g.canvas) continue;
                var rt = g.rectTransform;
                if (rt.rect.width <= 0f || rt.rect.height <= 0f) continue;
                rt.GetWorldCorners(Corners);
                var cam = g.canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : g.canvas.rootCanvas.worldCamera;
                for (var k = 0; k < 4; k++)
                {
                    var p = RectTransformUtility.WorldToScreenPoint(cam, Corners[k]);
                    if (!any) { x0 = x1 = p.x; y0 = y1 = p.y; any = true; continue; }
                    if (p.x < x0) x0 = p.x;
                    if (p.x > x1) x1 = p.x;
                    if (p.y < y0) y0 = p.y;
                    if (p.y > y1) y1 = p.y;
                }
            }
            Graphics.Clear();
            if (!any) return false;
            bounds = Rect.MinMaxRect(x0, y0, x1, y1);
            return true;
        }

        // The canvas whose sorting order places the window: nearest override-sorting canvas, else the root.
        private static Canvas SortCanvas(Transform t)
        {
            Canvas last = null;
            for (; t; t = t.parent)
            {
                if (!t.TryGetComponent<Canvas>(out var c) || !c.enabled) continue;
                last = c;
                if (c.overrideSorting || c.isRootCanvas) return c;
            }
            return last;
        }
    }
}
