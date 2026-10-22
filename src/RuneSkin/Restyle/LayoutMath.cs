using System;
using System.Collections.Generic;

namespace RuneSkin.Restyle
{
    // Pure geometry of the global layout rules (batch G, unit tested): one axis at a time, any consistent local unit.
    // - free span: the box minus the icons beside a text (an icon whose centre lies before the text start bounds the
    //   span on the left, after the text end on the right);
    // - start inset: TMP margin that puts a text area start exactly pad after its edge;
    // - centring: offset that centres an icon on its cell;
    // - clamp: offset that brings a text back inside its cell.
    public static class LayoutMath
    {
        public struct Span
        {
            public float Min, Max;

            public Span(float min, float max)
            {
                Min = min;
                Max = max;
            }

            public float Centre => (Min + Max) * 0.5f;
            public float Size => Max - Min;
        }

        // Box [boxMin, boxMax] narrowed by the obstacles that lie beside the text [textMin, textMax].
        public static Span FreeSpan(float boxMin, float boxMax, float textMin, float textMax, IEnumerable<Span> obstacles)
        {
            var left = boxMin;
            var right = boxMax;
            if (obstacles != null)
                foreach (var o in obstacles)
                {
                    if (o.Centre < boxMin || o.Centre > boxMax) continue;
                    if (o.Centre < textMin) left = Math.Max(left, o.Max);
                    else if (o.Centre > textMax) right = Math.Min(right, o.Min);
                }
            return new Span(left, Math.Max(left, right));
        }

        // Left margin that starts the text area at edge + pad, given the rect start of the text (margin-free).
        public static float StartMargin(float rectMin, float edge, float pad) => edge + Math.Max(0f, pad) - rectMin;

        // Right margin that ends the text area at edge - pad, given the rect end of the text.
        public static float EndMargin(float rectMax, float edge, float pad) => rectMax - (edge - Math.Max(0f, pad));

        // Offset that moves [min, max] to the centre of [cellMin, cellMax].
        public static float CentreOffset(float cellMin, float cellMax, float min, float max) => (cellMin + cellMax - min - max) * 0.5f;

        // Offset that brings [min, max] inside [boxMin, boxMax] (centred when it is wider than the box); 0 when inside.
        public static float ClampOffset(float boxMin, float boxMax, float min, float max)
        {
            if (max - min > boxMax - boxMin) return CentreOffset(boxMin, boxMax, min, max);
            if (min < boxMin) return boxMin - min;
            if (max > boxMax) return boxMax - max;
            return 0f;
        }

        // A cell is a square-ish box (slots, sockets, icon buttons): width and height in [16, 240], aspect 0.7-1.45.
        public static bool IsCellShape(float width, float height)
        {
            if (width < 16f || height < 16f || width > 240f || height > 240f) return false;
            var aspect = width / height;
            return aspect >= 0.7f && aspect <= 1.45f;
        }
    }
}
