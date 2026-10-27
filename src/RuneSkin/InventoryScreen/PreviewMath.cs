using System;

namespace RuneSkin.InventoryScreen
{
    // Pure math of the live character preview (CharacterPreview): where it sits, how big its texture is, how far the
    // camera stands, how a drag turns it and which culling layer it may take. Managed-only (unit tested).
    public static class PreviewMath
    {
        // Theme rect of the preview: a normalized rect (x, y, w, h; top-left origin, 0..1) of a host box, inset by
        // `inset` canvas units on every side. The host is the doll figure box today; a full-screen inventory layout
        // can hand another host box or move the rect by theme without touching the component.
        public static Box Place(Box host, float x, float y, float w, float h, float inset)
        {
            if (host.IsEmpty) return default;
            x = Clamp01(x); y = Clamp01(y);
            w = Math.Min(Clamp01(w), 1f - x);
            h = Math.Min(Clamp01(h), 1f - y);
            var b = new Box(host.X + host.W * x, host.Y + host.H * y, host.W * w, host.H * h);
            inset = Math.Max(0f, inset);
            var bw = b.W - 2f * inset;
            var bh = b.H - 2f * inset;
            return bw > 0f && bh > 0f ? new Box(b.X + inset, b.Y + inset, bw, bh) : default;
        }

        // Render texture size for a box of w x h canvas units at `scale` physical pixels per unit, clamped to
        // [min, max] per side (aspect kept when the long side is clamped). Even sizes (MSAA resolve friendly).
        public static (int W, int H) TextureSize(float w, float h, float scale, int min = 64, int max = 2048)
        {
            if (!(w > 0f) || !(h > 0f) || !(scale > 0f)) return (0, 0);
            double pw = w * scale, ph = h * scale;
            var longSide = Math.Max(pw, ph);
            if (longSide > max)
            {
                var k = max / longSide;
                pw *= k; ph *= k;
            }
            return (Even(Math.Max(min, Math.Min(max, (int)Math.Round(pw)))), Even(Math.Max(min, Math.Min(max, (int)Math.Round(ph)))));
        }

        // A new texture only when a side changes by more than `slack` pixels (panel animations nudge rects).
        public static bool NeedsResize(int curW, int curH, int wantW, int wantH, int slack = 2) =>
            curW <= 0 || curH <= 0 || Math.Abs(curW - wantW) > slack || Math.Abs(curH - wantH) > slack;

        // Level camera (looking along -z, no tilt: verticals stay straight) that fits the figure's world height span
        // [yMin, yMax] (live renderer bounds: feet .. helmet / raised weapon) to the whole view height, `top` / `bottom`
        // shares of the view left free above the head and under the feet. Height first: a held weapon or shield wider
        // than the view overflows sideways instead of shrinking the body (user 2026-10-28: "the character fills the
        // window, not small in the centre"); only `coreW` (shoulder width) is kept inside a very narrow view.
        // `near` = how far the extreme points (feet, helmet) may stand in front of the figure plane (their horizontal
        // radius around the turn axis): perspective draws a nearer point further from the view centre, so the span is fit
        // at that depth and the camera stands `near` further back (run 20261028-004636: feet turned toward the camera
        // projected 1-2.5% below the image without it).
        // Returns the camera distance from the figure plane and the camera's (and view centre's) height.
        public static (float Distance, float CenterY) FitHeight(float yMin, float yMax, float aspect, float vFovDeg,
            float top, float bottom, float coreW, float near = 0f)
        {
            if (!(near > 0f)) near = 0f;
            var tan = Math.Tan(Math.Max(1f, Math.Min(170f, vFovDeg)) * Math.PI / 360.0);
            if (!(aspect > 0f)) aspect = 1f;
            if (float.IsNaN(yMin) || float.IsNaN(yMax) || yMax - yMin < MinSpan) yMax = yMin + MinSpan;
            top = Math.Max(0f, Math.Min(0.4f, top));
            bottom = Math.Max(0f, Math.Min(0.4f, bottom));
            var fill = 1.0 - top - bottom;
            var byH = (yMax - yMin) / fill * 0.5 / tan;
            var byW = Math.Max(0f, coreW) * 0.5 / (tan * aspect);
            var d = Math.Max(byH, byW);
            var viewH = 2.0 * d * tan;
            // feet `bottom` above the view's lower edge (a width-bound view only grows above the head)
            return ((float)(d + near), (float)(yMin - bottom * viewH + viewH * 0.5));
        }

        // Shortest height span FitHeight frames (no renderer bounds yet / a degenerate box).
        public const float MinSpan = 0.5f;

        // Frame-rate independent ease of a framed value towards its measured target (equip changes, idle sway): changes
        // under `deadband` metres are ignored (no breathing jitter), NaN current = first measure (snap).
        public static float Smooth(float current, float target, float dt, float rate, float deadband)
        {
            if (float.IsNaN(current)) return target;
            var delta = target - current;
            if (Math.Abs(delta) <= deadband) return current;
            if (!(dt > 0f) || !(rate > 0f)) return target;
            return current + delta * (float)(1.0 - Math.Exp(-rate * dt));
        }

        // Yaw after a horizontal drag of dx pixels (degrees per pixel), wrapped to [0, 360).
        public static float Yaw(float yaw, float dx, float degPerPixel)
        {
            var y = (yaw - dx * degPerPixel) % 360f;
            return y < 0f ? y + 360f : y;
        }

        // First free culling layer, searched from the top (31) down to `lowest`: a layer without a name that is not
        // taken. nameOf = LayerMask.LayerToName. -1 = none free (the preview then falls back to the silhouette).
        public static int PickLayer(Func<int, string> nameOf, int lowest = 8)
        {
            if (nameOf == null) return -1;
            for (var i = 31; i >= Math.Max(0, lowest); i--)
                if (string.IsNullOrEmpty(nameOf(i))) return i;
            return -1;
        }

        private static float Clamp01(float v) => v < 0f || float.IsNaN(v) ? 0f : v > 1f ? 1f : v;

        private static int Even(int v) => (v & 1) == 0 ? v : v + 1;
    }
}
