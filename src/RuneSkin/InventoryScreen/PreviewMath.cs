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

        // Camera distance that fits a standing body (height x width metres) with `pad` margin (share of the size) in a
        // view of vertical field of view `vFovDeg` and aspect w/h: the tighter of the height and width fits. `share` =
        // part of the view height the body takes (figure box height over the rendered stage height, Share): the body
        // stays that tall on screen whatever the stage around it, the width fit still keeps `bodyW` inside the stage.
        public static float Distance(float bodyH, float bodyW, float aspect, float vFovDeg, float pad, float share = 1f)
        {
            var tan = Math.Tan(Math.Max(1f, Math.Min(170f, vFovDeg)) * Math.PI / 360.0);
            if (!(aspect > 0f)) aspect = 1f;
            if (!(share > 0f) || share > 1f) share = 1f;
            var byH = bodyH * 0.5 * (1.0 + pad) / (tan * share);
            var byW = bodyW * 0.5 * (1.0 + pad) / (tan * aspect);
            return (float)Math.Max(byH, byW);
        }

        // Share of the stage height the figure box takes (both canvas units), in (0, 1]; no figure = the whole stage.
        public static float Share(float figureH, float stageH) =>
            figureH > 0f && stageH > 0f ? Math.Min(1f, figureH / stageH) : 1f;

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
