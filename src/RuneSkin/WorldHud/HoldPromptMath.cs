using System;
using RuneSkin.API;

namespace RuneSkin.WorldHud
{
    // Pure math of the hold-to-confirm prompt (HoldPromptSkin, API RegisterHoldPrompt), unit tested.
    public static class HoldPromptMath
    {
        public static float Clamp01(float v) => float.IsNaN(v) || v < 0f ? 0f : v > 1f ? 1f : v;

        // Foreign state made safe to draw: progress 0..1 (NaN = 0), a prompt with neither key nor label is hidden.
        public static HoldPromptState Sanitize(HoldPromptState s)
        {
            s.Progress = Clamp01(s.Progress);
            if (string.IsNullOrEmpty(s.Key) && string.IsNullOrEmpty(s.Label)) s.Visible = false;
            return s;
        }

        // Ring fill is written only when the arc end moves by at least half a reference pixel (no per-frame mesh rebuild
        // while the hold stands still), and always when it reaches empty or full.
        public static bool FillChanged(float shown, float want, float diameter)
        {
            if (shown == want) return false;
            if (want <= 0f || want >= 1f) return true;
            return Math.Abs(shown - want) * Math.PI * diameter >= 0.5;
        }

        // Row = ring + gap + label, centred on the prompt's anchor: x of the ring centre.
        public static float RingCentreX(float ring, float gap, float labelWidth) =>
            -(ring + gap + Math.Max(0f, labelWidth)) * 0.5f + ring * 0.5f;

        // x of the label's left edge in the same row.
        public static float LabelLeftX(float ring, float gap, float labelWidth) => RingCentreX(ring, gap, labelWidth) + ring * 0.5f + gap;
    }
}
