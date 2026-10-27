using System.Collections.Generic;

namespace RuneSkin.Restyle
{
    // Pure parts of RenderGate (no Unity runtime): the owner ledger, the fade floor and the cover geometry.
    public static class RenderGateMath
    {
        // A CanvasGroup at or below this alpha draws nothing visible: its subtree is gated (not submitted to the GPU).
        public const float FadeFloor = 0.001f;

        public static bool Concealed(float alpha) => !(alpha > FadeFloor);

        // A big fade group (InventoryGui: ~2600 objects, every CanvasGroup write broadcasts OnCanvasGroupChanged to all of
        // them, inactive ones included; rp-open-hitch run 20261027-194038: 0.42 ms per write) takes intermediate alpha
        // values at most at 60 Hz: at or under 60 fps every frame still gets its value, faster frames hold the last value
        // for under one 60 Hz period. The end values (0 = parked, 1 = shown) are written the frame they are reached.
        public const float FadeWriteInterval = 1f / 60f;

        public static bool FadeWrite(float written, float next, float target, float sinceWrite) =>
            System.Math.Abs(next - written) > 1e-5f && (next == target || sinceWrite >= FadeWriteInterval);

        // Inner rect (x0, y0, x1, y1 with x0 <= x1, y0 <= y1) lies fully inside outer, with tol px of slack.
        public static bool Inside(float outerLeft, float outerBottom, float outerRight, float outerTop, float innerLeft, float innerBottom, float innerRight, float innerTop, float tol) =>
            innerLeft >= outerLeft - tol && innerBottom >= outerBottom - tol && innerRight <= outerRight + tol && innerTop <= outerTop + tol;

        public static bool Overlaps(float outerLeft, float outerBottom, float outerRight, float outerTop, float innerLeft, float innerBottom, float innerRight, float innerTop) =>
            innerLeft < outerRight && innerRight > outerLeft && innerBottom < outerTop && innerTop > outerBottom;

        // Opaque interior of a window drawn over (x0, y0)-(x1, y1) (screen px, y up) whose art is designW x designH design
        // px with the given transparent margins (design px; top = the high-y edge on screen). Empty (x0 >= x1) when the
        // margins swallow the window.
        public static void Interior(float x0, float y0, float x1, float y1, float designW, float designH,
            float left, float top, float right, float bottom, out float innerLeft, out float innerBottom, out float innerRight, out float innerTop)
        {
            var sx = designW > 0f ? (x1 - x0) / designW : 0f;
            var sy = designH > 0f ? (y1 - y0) / designH : 0f;
            innerLeft = x0 + left * sx;
            innerRight = x1 - right * sx;
            innerBottom = y0 + bottom * sy;
            innerTop = y1 - top * sy;
            if (innerLeft >= innerRight || innerBottom >= innerTop) innerLeft = innerRight = innerBottom = innerTop = 0f;
        }
    }

    // Which owners conceal which key. A key is concealed while at least one owner holds it; the first hold and the
    // last release are the only transitions (the gate component is added / removed once, whatever the owner count).
    public sealed class GateLedger<TKey>
    {
        public enum Change { None, Concealed, Revealed }

        private readonly Dictionary<TKey, HashSet<string>> _owners = new Dictionary<TKey, HashSet<string>>();

        public int Count => _owners.Count;

        public bool Has(TKey key) => _owners.ContainsKey(key);

        public bool Holds(TKey key, string owner) => _owners.TryGetValue(key, out var set) && set.Contains(owner);

        public IEnumerable<string> Owners(TKey key) => _owners.TryGetValue(key, out var set) ? set : (IEnumerable<string>)System.Array.Empty<string>();

        public IEnumerable<TKey> Keys => _owners.Keys;

        public Change Set(TKey key, string owner, bool conceal)
        {
            if (conceal)
            {
                if (_owners.TryGetValue(key, out var set)) { set.Add(owner); return Change.None; }
                _owners[key] = new HashSet<string> { owner };
                return Change.Concealed;
            }
            if (!_owners.TryGetValue(key, out var held) || !held.Remove(owner)) return Change.None;
            if (held.Count > 0) return Change.None;
            _owners.Remove(key);
            return Change.Revealed;
        }

        // Every key the owner holds is released; returns the keys that became revealed.
        public List<TKey> ReleaseAll(string owner)
        {
            var revealed = new List<TKey>();
            var keys = new List<TKey>(_owners.Keys);
            foreach (var k in keys)
                if (Set(k, owner, false) == Change.Revealed) revealed.Add(k);
            return revealed;
        }

        public void Forget(TKey key) => _owners.Remove(key);
    }
}
