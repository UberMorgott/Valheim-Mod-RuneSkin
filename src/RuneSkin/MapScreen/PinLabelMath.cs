namespace RuneSkin.MapScreen
{
    // Pure placement of map pin name labels (unit tested): a label hangs a fixed gap under its own pin icon, centred on
    // it, and where labels collide the lower-priority one is hidden (greedy, highest priority first).
    public static class PinLabelMath
    {
        public struct Box
        {
            public float X0, Y0, X1, Y1;
            public int Priority; // higher wins
            public int Order;    // tie-break: lower (earlier pin) wins
        }

        // Label root origin (parent space, y up) that puts the text's top-centre (textTopX / textTopY, relative to the
        // label root origin) `gap` under the pin icon's bottom edge, horizontally centred on the pin.
        public static void Hang(float pinX, float pinBottom, float gap, float textTopX, float textTopY, out float x, out float y)
        {
            x = pinX - textTopX;
            y = pinBottom - gap - textTopY;
        }

        // Strict overlap, shrunk by `pad` on each side (touching labels are not a collision).
        public static bool Overlap(in Box a, in Box b, float pad) =>
            a.X0 + pad < b.X1 - pad && b.X0 + pad < a.X1 - pad && a.Y0 + pad < b.Y1 - pad && b.Y0 + pad < a.Y1 - pad;

        private static bool Before(in Box a, in Box b) => a.Priority != b.Priority ? a.Priority > b.Priority : a.Order < b.Order;

        // shown[i] = label i kept. `order` is scratch (length >= count). Allocation-free; O(n^2) (insertion sort + pairwise).
        // Returns the number of hidden labels.
        public static int Declutter(Box[] boxes, int count, int[] order, bool[] shown, float pad)
        {
            for (var i = 0; i < count; i++)
            {
                var k = i;
                while (k > 0 && Before(boxes[i], boxes[order[k - 1]]))
                {
                    order[k] = order[k - 1];
                    k--;
                }
                order[k] = i;
            }
            var hidden = 0;
            for (var k = 0; k < count; k++)
            {
                var i = order[k];
                var ok = true;
                for (var j = 0; j < k && ok; j++)
                {
                    var o = order[j];
                    if (shown[o] && Overlap(boxes[i], boxes[o], pad)) ok = false;
                }
                shown[i] = ok;
                if (!ok) hidden++;
            }
            return hidden;
        }
    }
}
