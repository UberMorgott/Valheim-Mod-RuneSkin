using UnityEngine;
using UnityEngine.UI;

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
            IgnoreLayout(rt);
            var parent = rt.parent as RectTransform;
            if (!parent) return;
            var tl = (Vector2)parent.InverseTransformPoint(World(canvas, box.X, box.Y));
            var br = (Vector2)parent.InverseTransformPoint(World(canvas, box.Right, box.Bottom));
            var scale = rt.localScale;
            var sx = Mathf.Approximately(scale.x, 0f) ? 1f : scale.x;
            var sy = Mathf.Approximately(scale.y, 0f) ? 1f : scale.y;
            var w = (br.x - tl.x) / sx;
            var h = (tl.y - br.y) / sy;
            // Size through the current anchors, then move by the measured world corner: correct for any anchor /
            // pivot setup of the target and its parent (first in-game run: anchor-based math left children offset).
            var r = rt.rect;
            if (Mathf.Abs(r.width - w) > Eps) rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            if (Mathf.Abs(r.height - h) > Eps) rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
            SetTopLeft(rt, World(canvas, box.X, box.Y));
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
            IgnoreLayout(rt);
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

        // A rect of ANOTHER canvas (HUD) as a box of this canvas: through screen space with each canvas's camera
        // (overlay = null camera), so it holds for overlay and camera canvases alike.
        public static Box ForeignBox(RectTransform canvas, RectTransform rt)
        {
            rt.GetWorldCorners(Corners);
            var own = canvas.GetComponent<Canvas>();
            var other = rt.GetComponentInParent<Canvas>();
            var otherCam = other && other.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? other.rootCanvas.worldCamera : null;
            var ownCam = own && own.renderMode != RenderMode.ScreenSpaceOverlay ? own.worldCamera : null;
            var a = RectTransformUtility.WorldToScreenPoint(otherCam, Corners[1]);
            var b = RectTransformUtility.WorldToScreenPoint(otherCam, Corners[3]);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, a, ownCam, out var tl);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, b, ownCam, out var br);
            var r = canvas.rect;
            return new Box(tl.x - r.xMin, r.yMax - tl.y, br.x - tl.x, tl.y - br.y);
        }

        // A rect that would sit at a world pivot point of another canvas (size in that transform's local units).
        public static Box ForeignRect(RectTransform canvas, Transform space, Vector3 pivotWorld, Vector2 size, Vector2 pivot)
        {
            var tlW = pivotWorld + space.TransformVector(new Vector3(-pivot.x * size.x, (1f - pivot.y) * size.y, 0f));
            var brW = pivotWorld + space.TransformVector(new Vector3((1f - pivot.x) * size.x, -pivot.y * size.y, 0f));
            var other = space.GetComponentInParent<Canvas>();
            var own = canvas.GetComponent<Canvas>();
            var otherCam = other && other.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? other.rootCanvas.worldCamera : null;
            var ownCam = own && own.renderMode != RenderMode.ScreenSpaceOverlay ? own.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, RectTransformUtility.WorldToScreenPoint(otherCam, tlW), ownCam, out var tl);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, RectTransformUtility.WorldToScreenPoint(otherCam, brW), ownCam, out var br);
            var r = canvas.rect;
            return new Box(tl.x - r.xMin, r.yMax - tl.y, br.x - tl.x, tl.y - br.y);
        }

        // Layout groups would re-arrange a child we place at the next canvas rebuild: opt it out.
        public static void IgnoreLayout(RectTransform rt)
        {
            if (!rt || !rt.parent || !rt.parent.GetComponent<LayoutGroup>()) return;
            var le = rt.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
            if (!le.ignoreLayout) le.ignoreLayout = true;
        }

        // Canvas box of a rect of this canvas.
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
