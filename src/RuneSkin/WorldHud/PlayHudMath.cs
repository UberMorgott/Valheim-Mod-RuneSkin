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

        // Unlock toast (MessageHud m_unlockMsgPrefab: bkg plate, UnlockTitle, icon_bkg + UnlockDescription row), y up,
        // plate parent space. Vanilla anchors the title on the plate's top edge, so the frame's top rail ran through
        // the letters (user screenshot 2026-10-23). The plate top stays; the title's line box starts the rail's 9-slice
        // border + pad below it; the row follows the title by gap; the plate bottom grows to the row plus pad and the
        // bottom border. Returns the title and row shifts and the new plate bottom.
        public static (float titleShift, float rowShift, float plateBottom) UnlockStack(float plateTop, float titleTop, float titleBottom,
            float rowTop, float rowBottom, float borderTop, float borderBottom, float pad, float gap)
        {
            pad = Math.Max(0f, pad);
            var titleShift = plateTop - Math.Max(0f, borderTop) - pad - titleTop;
            var rowShift = titleBottom + titleShift - Math.Max(0f, gap) - rowTop;
            return (titleShift, rowShift, rowBottom + rowShift - pad - Math.Max(0f, borderBottom));
        }

        // Slot y of an unlock toast: vanilla stacks slot i at m_maxUnlockMsgSpace * i below the first (UpdateUnlockMsg);
        // a plate grown past that spacing gets plate height + gap so neighbours never overlap.
        public static float UnlockSlotY(float baseY, int slot, float vanillaSpace, float plateHeight, float gap) =>
            baseY - Math.Max(0, slot) * Math.Max(vanillaSpace, plateHeight + Math.Max(0f, gap));

        // Centre message plate pad along one axis, canvas units: the themed pad, raised so it is at least minPx screen
        // pixels at the canvas scale (px per unit; at 720p the HUD canvas shrinks below 1).
        public static float PadUnits(float pad, float minPx, float scale) =>
            Math.Max(Math.Max(0f, pad), scale > 1e-4f ? Math.Max(0f, minPx) / scale : 0f);

        // Centre message plate (user screenshot 2026-10-24: rune caps half outside the band, which was centred on the
        // text rect and sized from preferred values): the rendered glyph box [min, max] grown by the pad (opaque core)
        // plus the soft fade border on each side. Returns (centre, size) along one axis.
        public static (float center, float size) CenterPlate(float min, float max, float pad, float fade) =>
            PlateSpan(min, max, fade, pad);

        // Union of glyph quads (x0, y0, x1, y1 per glyph) -> (minX, minY, maxX, maxY); false when no glyph has area.
        public static bool GlyphBox(float[] quads, int count, out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = minY = float.PositiveInfinity;
            maxX = maxY = float.NegativeInfinity;
            var any = false;
            for (var i = 0; quads != null && i < count && 4 * i + 3 < quads.Length; i++)
            {
                float x0 = quads[4 * i], y0 = quads[4 * i + 1], x1 = quads[4 * i + 2], y1 = quads[4 * i + 3];
                if (float.IsNaN(x0) || float.IsNaN(y0) || float.IsNaN(x1) || float.IsNaN(y1)) continue;
                if (!(Math.Abs(x1 - x0) > 1e-4f) && !(Math.Abs(y1 - y0) > 1e-4f)) continue;
                minX = Math.Min(minX, Math.Min(x0, x1)); maxX = Math.Max(maxX, Math.Max(x0, x1));
                minY = Math.Min(minY, Math.Min(y0, y1)); maxY = Math.Max(maxY, Math.Max(y0, y1));
                any = true;
            }
            return any;
        }

        // Soft plate edge alpha (0..1) at a distance from the outer edge over a ramp: smoothstep, 0 at the edge, 1 from
        // the ramp's inner end on.
        public static float EdgeFade(float distanceFromEdge, float ramp)
        {
            if (!(ramp > 0f)) return distanceFromEdge >= 0f ? 1f : 0f;
            var t = Math.Max(0f, Math.Min(1f, distanceFromEdge / ramp));
            return t * t * (3f - 2f * t);
        }
    }
}
