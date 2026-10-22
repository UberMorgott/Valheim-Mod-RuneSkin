using System;

namespace RuneSkin.Restyle
{
    // Pure label-fit math (unit tested): TMP margin on one side that puts the text area exactly inset from the button
    // edge, given the gap the label rect leaves there (gap < 0: the label pokes out). Negative = the text area grows
    // past a small vanilla label rect (two lines in a tall plate instead of a truncated one).
    public static class LabelFit
    {
        public static float Margin(float inset, float gap) => Math.Max(0f, inset) - gap;

        // Font size at which a text of preferred width w (measured at size) fits one line of areaWidth.
        public static float OneLineSize(float areaWidth, float w, float size) =>
            w <= 0f || size <= 0f ? float.MaxValue : Math.Max(0f, areaWidth) * size / w;

        // The cap inset of a plate scales down on small buttons (a 40 px arrow button keeps its "<"): at most share of
        // the button width per side.
        public static float Inset(float inset, float buttonWidth, float share) =>
            Math.Max(0f, Math.Min(inset, Math.Max(0f, buttonWidth) * Math.Max(0f, share)));
    }
}
