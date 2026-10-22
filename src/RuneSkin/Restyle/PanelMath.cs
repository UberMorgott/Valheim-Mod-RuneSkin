using System;

namespace RuneSkin.Restyle
{
    // Pure panel geometry (no Unity types: unit-tested on net8). Reference px = sprite px x 100 / sprite PPU.
    public static class PanelMath
    {
        // Shift (one axis) that brings a box [min, max] inside [lo, hi]: 0 when it already fits, the overhang on the
        // low side pushed up first; a box larger than the range keeps its low edge on lo (the top-left reads first).
        public static float EdgeShift(float min, float max, float lo, float hi)
        {
            if (float.IsNaN(min) || float.IsNaN(max) || float.IsNaN(lo) || float.IsNaN(hi) || hi < lo) return 0f;
            if (min < lo) return lo - min;
            if (max > hi) return Math.Max(hi - max, lo - min);
            return 0f;
        }
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
