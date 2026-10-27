using System;

namespace RuneSkin.ModScreens
{
    // Pure math of the Epic Loot modal dialogs (ElDialogs; unit tested). Frame units, y up from the frame's bottom edge.
    public static class ElDialogMath
    {
        // Epic Loot's augment choice stack (AugmentHelper.CreateAugmentChoiceDialog, AugmentHelper.cs:88): 300 x 40
        // buttons, centres at 55 + k * 45 from the frame bottom -> lowest bottom edge 35, gap 5.
        public const float ElBase = 35f, ElGap = 5f, ElHeight = 40f;

        public struct ChoiceLayout
        {
            public float[] Bottoms;     // bottom edge of each button, index 0 = top button (Epic Loot's order)
            public float Top;           // top edge of the stack
            public float ScrollHeight;  // description scroll view height above it
        }

        // The stack sits above the bottom corner caps (capBand = their top edge over the frame bottom) and never lower
        // than Epic Loot's own base; the description view (top inset scrollTop) ends a gap above the stack, at least
        // minScroll tall.
        public static ChoiceLayout Stack(float frameH, float scrollTop, float[] heights, float capBand, float gap, float minScroll)
        {
            var n = heights?.Length ?? 0;
            var s = new ChoiceLayout { Bottoms = new float[n] };
            var y = Math.Max(ElBase, capBand + gap);
            for (var i = n - 1; i >= 0; i--)
            {
                s.Bottoms[i] = y;
                y += heights[i] + (i > 0 ? gap : 0f);
            }
            s.Top = n > 0 ? y : Math.Max(ElBase, capBand + gap);
            s.ScrollHeight = Math.Max(minScroll, frameH - scrollTop - s.Top - gap);
            return s;
        }

        // Epic Loot's choice button width (AugmentHelper.cs:88).
        public const float ElWidth = 300f;

        // Choice button width above the corner caps: the frame between its drawn side borders (left / right inset, frame
        // units, border + row pad), never narrower than Epic Loot's 300.
        public static float InnerWidth(float frameWidth, float left, float right) =>
            Math.Max(ElWidth, frameWidth - Math.Max(0f, left) - Math.Max(0f, right));

        // Epic Loot's dialog frame width (AugmentHelper.CreateDialog / CraftSuccessDialog.Create: 380 x 550).
        public const float ElFrameWidth = 380f;

        // Least gap between the widened frame and the safe area's side edges (frame units).
        public const float ScreenMargin = 24f;

        // Frame width that shows the widest content on one line: content width + the frame's side insets (left / right,
        // frame units), never narrower than Epic Loot's 380, never wider than the room on screen (maxWidth; at least 380:
        // a screen narrower than Epic Loot's own frame keeps its frame and the labels wrap).
        public static float FrameWidth(float contentWidth, float left, float right, float maxWidth)
        {
            var want = Math.Max(0f, contentWidth) + Math.Max(0f, left) + Math.Max(0f, right);
            return Math.Max(ElFrameWidth, Math.Min(want, Math.Min(maxWidth, MaxFrameWidth)));
        }

        // Readable line length: a sentence-long tooltip line in the craft result wraps instead of a screen-wide box
        // (twice Epic Loot's frame; the longest augment label needs far less).
        public const float MaxFrameWidth = 2f * ElFrameWidth;

        // Shift (x, frame units) that keeps [xMin, xMax] inside [lo, hi]; centred when it is wider than the room.
        public static float ShiftInto(float xMin, float xMax, float lo, float hi)
        {
            if (xMax - xMin >= hi - lo) return (lo + hi) * 0.5f - (xMin + xMax) * 0.5f;
            if (xMin < lo) return lo - xMin;
            if (xMax > hi) return hi - xMax;
            return 0f;
        }

        // A label stays on one line (no wrap) when its unwrapped width at the readable floor fits its text width.
        public static bool OneLine(float floorWidth, float textWidth) => floorWidth <= textWidth + 0.5f;

        // Button height for a label that needs textNeeded (at the readable floor, wrapped in the button's text width):
        // Epic Loot's height, or the label plus its vertical padding when it needs more (no clipping).
        public static float ButtonHeight(float baseHeight, float textNeeded, float vpad) =>
            Math.Max(baseHeight, textNeeded + 2f * Math.Max(0f, vpad));

        // A RootMark on an object that is no registered root came with Instantiate from a RuneSkin root (Epic Loot clones
        // the restyled vanilla variant dialog): it is no root, and as a "nested root" it would keep the owning unit's pass
        // off the whole dialog.
        public static bool IsStaleRootMark(bool hasMark, bool registeredRoot) => hasMark && !registeredRoot;
    }
}
