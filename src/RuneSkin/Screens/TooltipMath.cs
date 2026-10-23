using System;

namespace RuneSkin.Screens
{
    // Screen rect in pixels, y up (Screen Space Overlay world = screen px).
    public readonly struct TipRect
    {
        public readonly float XMin, YMin, XMax, YMax;

        public TipRect(float xMin, float yMin, float xMax, float yMax)
        {
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

        public float W => XMax - XMin;
        public float H => YMax - YMin;

        public static TipRect Sized(float xMin, float yMin, float w, float h) => new TipRect(xMin, yMin, xMin + w, yMin + h);

        public bool Overlaps(TipRect o) => XMin < o.XMax && XMax > o.XMin && YMin < o.YMax && YMax > o.YMin;

        public bool Contains(float x, float y) => x >= XMin && x <= XMax && y >= YMin && y <= YMax;

        public TipRect Union(TipRect o) => new TipRect(Math.Min(XMin, o.XMin), Math.Min(YMin, o.YMin), Math.Max(XMax, o.XMax), Math.Max(YMax, o.YMax));

        public TipRect Intersect(TipRect o) => new TipRect(Math.Max(XMin, o.XMin), Math.Max(YMin, o.YMin), Math.Min(XMax, o.XMax), Math.Min(YMax, o.YMax));

        public bool Empty => !(XMax > XMin) || !(YMax > YMin);
    }

    // Pure tooltip placement / scroll math (TooltipPlacer). Bounds = the safe area with the HUD strip top as its floor.
    public static class TooltipMath
    {
        // The usable area: safe area inset by margin, floor raised to the strip top (0 = no strip).
        public static TipRect Bounds(TipRect safe, float stripTop, float margin) =>
            new TipRect(safe.XMin + margin, Math.Max(safe.YMin, stripTop) + margin, safe.XMax - margin, safe.YMax - margin);

        // Tallest visible tooltip: the whole height of the bounds (overflowing content scrolls).
        public static float MaxHeight(TipRect bounds) => Math.Max(0f, bounds.H);

        // Where a w x h tooltip goes next to the hovered element (anchor): beside it, never over it (a gap away, right
        // side first, left when the right does not fit, clamped into the bounds as the last resort). Vertically its top
        // aligns with the anchor top and it hangs down; when that crosses the floor it flips to grow upward from the
        // anchor bottom; never below the floor (the strip) nor above the ceiling.
        public static TipRect Place(TipRect anchor, float w, float h, TipRect bounds, float gap)
        {
            float x;
            if (anchor.XMax + gap + w <= bounds.XMax) x = anchor.XMax + gap;
            else if (anchor.XMin - gap - w >= bounds.XMin) x = anchor.XMin - gap - w;
            else
            {
                // Neither side fits: the side with more room, clamped.
                var right = bounds.XMax - anchor.XMax;
                var left = anchor.XMin - bounds.XMin;
                x = right >= left ? bounds.XMax - w : bounds.XMin;
                x = Math.Max(bounds.XMin, Math.Min(x, bounds.XMax - w));
            }

            var y = anchor.YMax - h; // hang down from the anchor top
            if (y < bounds.YMin) y = Math.Max(anchor.YMin, bounds.YMin); // flip: grow upward from the anchor bottom
            if (y + h > bounds.YMax) y = bounds.YMax - h;
            if (y < bounds.YMin) y = bounds.YMin; // taller than the bounds: the cap keeps this from happening
            return TipRect.Sized(x, y, w, h);
        }

        // Content taller than its clip by more than the tolerance scrolls.
        public static bool Overflows(float contentH, float clipH) => contentH > clipH + 0.5f;

        // Normalized vertical position (1 = top) after one wheel step of stepPx toward wheel's sign (wheel > 0 = up).
        public static float Scrolled(float normalized, float wheel, float stepPx, float contentH, float clipH)
        {
            var range = contentH - clipH;
            if (range <= 0f || wheel == 0f) return normalized;
            var n = normalized + Math.Sign(wheel) * stepPx / range;
            return Math.Max(0f, Math.Min(1f, n));
        }

        // Scroll indicator handle in a track of trackH: its height (clip share, at least minH) and its bottom offset
        // from the track bottom (normalized 1 = top of the content shown = handle at the track top).
        public static void Handle(float clipH, float contentH, float normalized, float trackH, float minH, out float handleH, out float handleY)
        {
            var share = contentH > 0f ? Math.Min(1f, clipH / contentH) : 1f;
            handleH = Math.Min(trackH, Math.Max(minH, trackH * share));
            var n = Math.Max(0f, Math.Min(1f, normalized));
            handleY = (trackH - handleH) * n;
        }

        // Edge fades: top while content is hidden above, bottom while content is hidden below.
        public static void Fades(bool overflow, float normalized, out bool top, out bool bottom)
        {
            top = overflow && normalized < 0.999f;
            bottom = overflow && normalized > 0.001f;
        }
    }
}
