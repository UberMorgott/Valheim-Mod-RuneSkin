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

        // Name above an icon in a cell (requirement sockets, 0.10.2): one line, auto-sized from max down to min; when
        // one line needs less than min, two lines (the best word split, widest line twoLineW); never more than the
        // box. Widths and the line height are per unit of font size (measured at any size / that size); twoLineH = the
        // measured height of the two-line block (line spacing included, run 20261022-170700: 2 x one line was short and
        // TMP dropped the second line); <= 0 = 2 x lineH. lines = 1 / 2.
        // Neither fits at min: the layout that comes closest at the size that fits (below min, never clipped).
        public static float NameSize(float max, float min, float boxW, float boxH, float lineH, float oneLineW, float twoLineW, out int lines, float twoLineH = 0f)
        {
            min = Math.Min(min, max);
            var s1 = Math.Min(max, Math.Min(Per(boxW, oneLineW), Per(boxH, lineH)));
            lines = 1;
            if (s1 >= min) return s1;
            if (twoLineW > 0f)
            {
                var s2 = Math.Min(max, Math.Min(Per(boxW, twoLineW), Per(boxH, twoLineH > 0f ? twoLineH : 2f * lineH)));
                // Two lines below min still beat a clipped name (never clipped wins over the floor).
                if (s2 >= s1) { lines = 2; return s2; }
            }
            return s1 < float.MaxValue ? s1 : min;
        }

        private static float Per(float room, float perUnit) => perUnit > 0f ? Math.Max(0f, room) / perUnit : float.MaxValue;

        // Index of the space that splits text into the two most balanced lines (smallest widest line), -1 without a
        // space. width(i, j) = width of text[i, j).
        public static int BestSplit(string text, Func<int, int, float> width, out float widest)
        {
            widest = 0f;
            var best = -1;
            if (string.IsNullOrEmpty(text) || width == null) return best;
            for (var i = text.IndexOf(' '); i > 0; i = text.IndexOf(' ', i + 1))
            {
                var w = Math.Max(width(0, i), width(i + 1, text.Length));
                if (best >= 0 && w >= widest) continue;
                best = i;
                widest = w;
            }
            return best;
        }

        // The cap inset of a plate scales down on small buttons (a 40 px arrow button keeps its "<"): at most share of
        // the button width per side.
        public static float Inset(float inset, float buttonWidth, float share) =>
            Math.Max(0f, Math.Min(inset, Math.Max(0f, buttonWidth) * Math.Max(0f, share)));
    }
}
