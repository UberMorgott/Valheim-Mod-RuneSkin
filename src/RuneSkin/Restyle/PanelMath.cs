using System;

namespace RuneSkin.Restyle
{
    // Pure panel geometry (no Unity types: unit-tested on net8). Reference px = sprite px x 100 / sprite PPU.
    public static class PanelMath
    {
        // Side panel docked beside a window (boxes as xMin, yMin, xMax, yMax; y up): right of it with gap when the
        // screen has room there, else left of it, else right edge clamped; its top level with the window top, then
        // kept inside the screen. Returns the shift (dx, dy) of the panel box.
        public static (float dx, float dy) DockBeside(float wx0, float wy0, float wx1, float wy1, float px0, float py0, float px1, float py1,
            float sx0, float sy0, float sx1, float sy1, float gap)
        {
            var w = px1 - px0;
            float x;
            if (wx1 + gap + w <= sx1) x = wx1 + gap;
            else if (wx0 - gap - w >= sx0) x = wx0 - gap - w;
            else x = sx1 - w;
            var dx = x - px0;
            var dy = wy1 - py1;
            dx += EdgeShift(px0 + dx, px1 + dx, sx0, sx1);
            dy += EdgeShift(py0 + dy, py1 + dy, sy0, sy1);
            return (dx, dy);
        }

        // Shift (one axis) that brings a box [min, max] inside [lo, hi]: 0 when it already fits, the overhang on the
        // low side pushed up first; a box larger than the range keeps its low edge on lo (the top-left reads first).
        public static float EdgeShift(float min, float max, float lo, float hi)
        {
            if (float.IsNaN(min) || float.IsNaN(max) || float.IsNaN(lo) || float.IsNaN(hi) || hi < lo) return 0f;
            if (min < lo) return lo - min;
            if (max > hi) return Math.Max(hi - max, lo - min);
            return 0f;
        }
        // Dialog box sized to its content: the content union [x0, x1] x [y0, y1] grown by padX left / right and padY top / bottom, then widened
        // / heightened symmetrically to at least minW x minH (a one-word question still reads as a dialog). An empty
        // union (x0 > x1) returns false.
        public static bool FitBox(float x0, float y0, float x1, float y1, float padX, float padY, float minW, float minH,
            out float bx0, out float by0, out float bx1, out float by1)
        {
            bx0 = by0 = bx1 = by1 = 0f;
            if (float.IsNaN(x0) || float.IsNaN(y0) || float.IsNaN(x1) || float.IsNaN(y1) || x0 > x1 || y0 > y1) return false;
            padX = Math.Max(0f, padX);
            padY = Math.Max(0f, padY);
            bx0 = x0 - padX;
            by0 = y0 - padY;
            bx1 = x1 + padX;
            by1 = y1 + padY;
            var growW = Math.Max(0f, minW - (bx1 - bx0)) * 0.5f;
            var growH = Math.Max(0f, minH - (by1 - by0)) * 0.5f;
            bx0 -= growW;
            bx1 += growW;
            by0 -= growH;
            by1 += growH;
            return true;
        }

        // Text inside a framed box (user 2026-10-23, map filter hint: lines ran under the rune end-columns and the
        // copper braces of the corner caps): the text area must lie in the box inset by side (left / right: the whole
        // cap width incl. its brace + margin) and by top / bottom (rail + margin). Given the text rect and the box (same
        // space, xMin..xMax / yMin..yMax) and the text's current margins (left, top, right, bottom, TMP order), returns
        // the margins that bring the area inside, never lowering one. Scale k = box units per text unit.
        public static (float l, float t, float r, float b) InsetMargins(float tx0, float ty0, float tx1, float ty1,
            float bx0, float by0, float bx1, float by1, float side, float vert, float k, float ml, float mt, float mr, float mb)
        {
            if (!(k > 0f)) k = 1f;
            var l = Math.Max(ml, (bx0 + side - tx0) / k);
            var r = Math.Max(mr, (tx1 - (bx1 - side)) / k);
            var b = Math.Max(mb, (by0 + vert - ty0) / k);
            var t = Math.Max(mt, (ty1 - (by1 - vert)) / k);
            return (l, t, r, b);
        }

        // Sorting order of the modal band (confirm dialogs): above everything the top overlay sorts over (base = the
        // highest of the GUI root / inventory / HUD strip orders), below the top overlay itself (tooltips, drag icon,
        // TopOverlay.Band above base), so a tooltip still reads over a dialog and nothing else does.
        public static int ModalOrder(int baseOrder, int topBand) => baseOrder + Math.Max(1, topBand / 2);

        // Corner-cap keep-out: the art states per cap sprite its clearance C (sprite px, theme float "clear.<asset>"):
        // every opaque cap pixel (rune block and leather strap) has min(dx, dy) < C from the panel corner the cap
        // sits on. A content rect inset by C on both sides of every corner therefore never runs under a cap. The
        // caps sit on the panel stack, which reaches pad beyond the content host, so the host-side inset is C - pad.
        public static float CapInset(float clearSpritePx, float spritePpu, float pad)
        {
            if (!(clearSpritePx > 0f) || !(spritePpu > 0f)) return 0f;
            return Math.Max(0f, clearSpritePx * 100f / spritePpu - Math.Max(0f, pad));
        }
    }
}
