using UnityEngine;

namespace RuneSkin.InventoryScreen
{
    // Canvas-box placement. Boxes are logical canvas units (top-left origin, InventoryMath); placement converts them
    // through world space into each target's parent, so it works for any parent chain (m_container sits under
    // m_player, EAQS cells under m_player/EaqsSlotRoot) and keeps the target's pivot (EAQS rewrites m_container's
    // pivot every frame, EquipmentPanel.UpdateInventoryPanelForExtraRows). Writes only on change.
    public static class Geo
    {
        private const float Eps = 0.05f;
        private static readonly Vector3[] Corners = new Vector3[4];

        // World position of a canvas-box point.
        public static Vector3 World(RectTransform canvas, float x, float y)
        {
            var r = canvas.rect;
            return canvas.TransformPoint(new Vector3(r.xMin + x, r.yMax - y, 0f));
        }

        // Logical canvas units -> world units (uniform canvas scale).
        public static float WorldPerUnit(RectTransform canvas) => canvas.lossyScale.x;

        // Puts rt on the box: anchors at the parent's centre, size = box (in rt's own scale), pivot kept.
        public static void Place(RectTransform canvas, RectTransform rt, Box box)
        {
            if (!rt || !canvas || box.IsEmpty) return;
            var parent = rt.parent as RectTransform;
            if (!parent) return;
            var tl = (Vector2)parent.InverseTransformPoint(World(canvas, box.X, box.Y));
            var br = (Vector2)parent.InverseTransformPoint(World(canvas, box.Right, box.Bottom));
            var scale = rt.localScale;
            var sx = Mathf.Approximately(scale.x, 0f) ? 1f : scale.x;
            var sy = Mathf.Approximately(scale.y, 0f) ? 1f : scale.y;
            var w = br.x - tl.x;
            var h = tl.y - br.y;
            var size = new Vector2(w / sx, h / sy);
            var pr = parent.rect;
            var center = pr.center;
            var pivotPoint = new Vector2(tl.x + rt.pivot.x * w, br.y + rt.pivot.y * h);
            var half = new Vector2(0.5f, 0.5f);
            if (rt.anchorMin != half || rt.anchorMax != half)
            {
                rt.anchorMin = half;
                rt.anchorMax = half;
            }
            if ((rt.sizeDelta - size).sqrMagnitude > Eps * Eps) rt.sizeDelta = size;
            var pos = pivotPoint - center;
            if ((rt.anchoredPosition - pos).sqrMagnitude > Eps * Eps) rt.anchoredPosition = pos;
        }

        // Keeps rt's size, moves its top-left corner to the box's and scales it down (never up) to fit the box.
        public static void PlaceFit(RectTransform canvas, RectTransform rt, Box box, float maxScale = 1f)
        {
            if (!rt || !canvas || box.IsEmpty) return;
            var size = rt.rect.size;
            if (size.x <= 0f || size.y <= 0f) return;
            var unit = WorldPerUnit(canvas);
            var parentScale = rt.parent ? rt.parent.lossyScale.x : 1f;
            // rt's size in canvas units at scale 1.
            var k = parentScale / Mathf.Max(unit, 1e-6f);
            var fit = Mathf.Min(maxScale, Mathf.Min(box.W / (size.x * k), box.H / (size.y * k)));
            SetScale(rt, fit);
            SetTopLeft(rt, World(canvas, box.X, box.Y));
        }

        // Moves rt so its top-left corner lands on a world point (size/scale untouched).
        public static void SetTopLeft(RectTransform rt, Vector3 world)
        {
            var parent = rt.parent as RectTransform;
            if (!parent) return;
            var target = (Vector2)parent.InverseTransformPoint(world);
            rt.GetWorldCorners(Corners);
            var current = (Vector2)parent.InverseTransformPoint(Corners[1]); // top-left
            var delta = target - current;
            if (delta.sqrMagnitude > Eps * Eps) rt.anchoredPosition += delta;
        }

        public static void SetScale(Transform t, float s)
        {
            var v = new Vector3(s, s, 1f);
            if ((t.localScale - v).sqrMagnitude > 1e-6f) t.localScale = v;
        }

        // Size of a cell in canvas units (its rect x its scale chain, relative to the canvas).
        public static float CanvasWidth(RectTransform canvas, RectTransform rt)
        {
            rt.GetWorldCorners(Corners);
            return (Corners[2].x - Corners[1].x) / Mathf.Max(WorldPerUnit(canvas), 1e-6f);
        }

        // Canvas box of a world rect (HUD elements on another overlay canvas: world = screen px for both).
        public static Box BoxOf(RectTransform canvas, RectTransform rt)
        {
            rt.GetWorldCorners(Corners);
            var tl = (Vector2)canvas.InverseTransformPoint(Corners[1]);
            var br = (Vector2)canvas.InverseTransformPoint(Corners[3]);
            var r = canvas.rect;
            return new Box(tl.x - r.xMin, r.yMax - tl.y, br.x - tl.x, tl.y - br.y);
        }
    }
}
