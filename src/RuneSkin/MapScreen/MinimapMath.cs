using System;

namespace RuneSkin.MapScreen
{
    // Pure geometry of the round minimap (unit tested): ring size, day-phase arc, compass rune spots, disc culling.
    public static class MinimapMath
    {
        // Ring box in reference px: the theme size, clamped so the ring never takes more than `share` of the safe height.
        public static float RingSize(float refSize, float safeHeightPx, float pxPerRef, float share)
        {
            if (!(pxPerRef > 0f) || !(share > 0f)) return refSize;
            var cap = share * safeHeightPx / pxPerRef;
            return Math.Max(1f, Math.Min(refSize, cap));
        }

        // Position along the ring's top arc (centre-relative, y up). Day: sunrise (fraction 0.25) at the left end (180
        // deg), noon at the top, sunset (0.75) at the right end. Night: the moon runs the same arc from dusk to dawn.
        // EnvMan.GetDayFraction (EnvMan.cs:937): 0 = midnight, 0.5 = noon.
        public static void ArcPoint(float dayFraction, bool day, float radius, out float x, out float y)
        {
            var f = dayFraction - (float)Math.Floor(dayFraction);
            var t = day ? (f - 0.25f) / 0.5f : ((f - 0.75f + 1f) % 1f) / 0.5f;
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            var a = Math.PI * (1.0 - t);
            x = (float)(radius * Math.Cos(a));
            y = (float)(radius * Math.Sin(a));
        }

        // Compass rune spot: 0 = N (top), 1 = E, 2 = S, 3 = W.
        public static void Cardinal(int index, float radius, out float x, out float y)
        {
            switch (index & 3)
            {
                case 0: x = 0f; y = radius; break;
                case 1: x = radius; y = 0f; break;
                case 2: x = 0f; y = -radius; break;
                default: x = -radius; y = 0f; break;
            }
        }

        // A map pin (centre-relative) is drawn only while its icon stays inside the ring hole.
        public static bool InsideDisc(float x, float y, float radius, float pinHalf) =>
            x * x + y * y <= Math.Max(0f, radius - pinHalf * 0.5f) * Math.Max(0f, radius - pinHalf * 0.5f);

        // Map image side inside the hole: the full hole diameter when the map shader clips to the Mask, otherwise the
        // inscribed square (no map corner can poke out over the ring).
        public static float MapSide(float box, float holeRatio, bool masked) =>
            masked ? box * holeRatio : box * holeRatio / (float)Math.Sqrt(2.0);

        // Wind glyph angle (deg, UI z) from the wind direction's compass heading (deg, 0 = north, clockwise), as
        // Minimap.UpdateWindMarker (Minimap.cs:1702-1706) writes it.
        public static float WindAngle(float headingDeg) => -headingDeg;
    }
}
