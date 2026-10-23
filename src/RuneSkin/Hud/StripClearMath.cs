using System;
using System.Collections.Generic;

namespace RuneSkin.PlayerHud
{
    // Pure maths of the strip keep-out (no Unity types: unit-tested on net8). Every bottom-anchored HUD element (build
    // piece info + requirements, piece author window, action bar, ship ladder, mount panel, key hints, hold prompt, event
    // bar, centre message) keeps its vanilla / skin home position; when its DRAWN bottom (lowest visible graphic, screen
    // px, bottom-up) at home would reach below the strip's live top edge + gap and it overlaps the strip horizontally, it
    // rises by exactly the difference. Recomputed from home every time (strip height changes with resolution, GUI scale,
    // slot counts), so the lift never accumulates and an element the strip no longer reaches drops back home.
    public static class StripClearMath
    {
        // Screen px an element must rise (0 = stays home). stripTop <= 0 = no strip (HUD hidden).
        public static float Lift(float bottomAtHome, float left, float right, float stripTop, float stripLeft, float stripRight, float gap)
        {
            if (!(stripTop > 0f) || !(right > stripLeft) || !(left < stripRight)) return 0f;
            return Math.Max(0f, stripTop + Math.Max(0f, gap) - bottomAtHome);
        }

        // Drawn bottom the element would have at its home position: the measured bottom minus the lift applied now.
        public static float BottomAtHome(float bottomNow, float appliedLiftPx) => bottomNow - appliedLiftPx;

        // Screen px -> anchored units of the element's parent (pxPerUnit = screen px one parent unit spans).
        public static float Units(float px, float pxPerUnit) => pxPerUnit > 0f ? px / pxPerUnit : 0f;

        // Sibling index the strip must take so every tracked element (their top-level sibling indices under the same
        // parent) draws after it, i.e. above it; -1 = already below all of them (or nothing tracked).
        public static int SiblingBelow(int stripIndex, IEnumerable<int> elementIndices)
        {
            var min = int.MaxValue;
            foreach (var i in elementIndices)
                if (i >= 0 && i < min) min = i;
            return min == int.MaxValue || stripIndex < min ? -1 : min;
        }

        // Harness / audit predicate: an element's drawn box clears the strip (no overlap, bottom >= top + gap - eps).
        public static bool Clear(float bottom, float left, float right, float stripTop, float stripLeft, float stripRight, float gap, float eps = 0.5f) =>
            Lift(bottom, left, right, stripTop, stripLeft, stripRight, gap) <= eps;
    }
}
