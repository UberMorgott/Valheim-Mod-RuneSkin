using System;

namespace RuneSkin.WorldHud
{
    // Pure layout math of the M6 in-game HUD families (art\README.md "M6 sprites"), unit tested.
    public static class PlayHudMath
    {
        // Ship speed ladder (mockup m6-ship-b), top-down: full sail, half sail, oar (slow), stop, reverse. Vanilla
        // Ship.Speed: Stop 0, Back 1, Slow 2, Half 3, Full 4.
        public const int LadderCells = 5;

        public static int LadderIndex(int speed)
        {
            switch (speed)
            {
                case 4: return 0; // Full
                case 3: return 1; // Half
                case 2: return 2; // Slow (oar)
                case 1: return 4; // Back
                default: return 3; // Stop
            }
        }

        // Cell i of the ladder (pivot top-left of the column, y down): top edge offset.
        public static float LadderCellTop(int index, float cell, float gap, float pad) => pad + index * (cell + gap);

        public static float LadderHeight(float cell, float gap, float pad) => 2f * pad + LadderCells * cell + (LadderCells - 1) * gap;

        // Rudder glyph rotation (degrees, counter-clockwise positive) from Ship.GetRudderValue (-1 .. 1): full lock =
        // maxDeg, the glyph turns the way the tiller does (right rudder = clockwise).
        public static float RudderAngle(float rudderValue, float maxDeg)
        {
            if (float.IsNaN(rudderValue)) return 0f;
            return -Math.Max(-1f, Math.Min(1f, rudderValue)) * maxDeg;
        }

        // A frame drawn around a target rect, inset per side (frame window inside its border): new anchoredPosition
        // along one axis so the grown rect stays centred on the target whatever its pivot. Size grows by both insets.
        public static float GrowPosition(float position, float pivot, float insetMin, float insetMax) =>
            position - insetMin + pivot * (insetMin + insetMax);

        // Event line rules: x of the inner end of the right rule (the left one mirrors it) beside a centred title.
        public static float RuleOffset(float titleWidth, float gap) => Math.Max(0f, titleWidth) * 0.5f + Math.Max(0f, gap);

        // Key plate width for a key label: at least square, grown to the label plus both side pads.
        public static float KeyPlateWidth(float labelWidth, float height, float pad) => Math.Max(height, labelWidth + 2f * pad);

        // Readable HUD text (theme size.text.hud.min, canvas units = px at 1080p: the HUD canvas scales with the screen
        // height): a themed size below the floor is raised to it.
        public static float Readable(float size, float min) => Math.Max(size, Math.Max(0f, min));

        // Notification plate edges along one axis around the content span [min, max]: the content keeps the plate's
        // full 9-slice border plus the pad on each side, so an icon at the content edge is never drawn under the
        // frame (user screenshot 2026-10-22: the skill icon was clipped by the left knot). Returns (centre, size).
        public static (float center, float size) PlateSpan(float min, float max, float border, float pad)
        {
            var inset = Math.Max(0f, border) + Math.Max(0f, pad);
            var lo = Math.Min(min, max) - inset;
            var hi = Math.Max(min, max) + inset;
            return ((lo + hi) * 0.5f, hi - lo);
        }
    }
}
