using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RuneSkin.Restyle
{
    // Hidden = not rendered. A subtree RuneSkin hides while its GameObjects must stay active (vanilla code, animators and
    // state machines keep running on them) is gated by a disabled Canvas on its root: a disabled Canvas issues no draw
    // calls for anything under it, so its graphics are neither batched nor drawn, while every component keeps running and
    // the managed Graphics keep their parent canvas (raycast/registry unchanged; input is blocked by the callers' own
    // CanvasGroups). A CanvasGroup at alpha 0 only multiplies vertex alpha: the geometry is still batched and drawn
    // (overdraw), which is what this replaces. Keyed by owner, so independent reasons (full window, large map, fade)
    // stack: the gate goes on with the first owner and comes off with the last; a Canvas RuneSkin added is removed then,
    // an existing one gets its own enabled state back.
    public static class RenderGate
    {
        private sealed class Entry
        {
            public Canvas Canvas;
            public bool Added;
            public bool Prior;
        }

        private static readonly GateLedger<GameObject> Ledger = new GateLedger<GameObject>();
        private static readonly Dictionary<GameObject, Entry> Entries = new Dictionary<GameObject, Entry>();
        private static int _pruneAt = 64;
        private static readonly List<GameObject> Scratch = new List<GameObject>();

        // Harness: gates lifted (canvases back to their own state) to measure the old behaviour in place.
        public static bool Suspended { get; private set; }

        public static int Count => Entries.Count;

        public static void Set(Component c, string owner, bool conceal)
        {
            if (!c) return;
            Set(c.gameObject, owner, conceal);
        }

        public static void Set(GameObject go, string owner, bool conceal)
        {
            if (!go) return;
            switch (Ledger.Set(go, owner, conceal))
            {
                case GateLedger<GameObject>.Change.Concealed:
                    Attach(go, owner);
                    break;
                case GateLedger<GameObject>.Change.Revealed:
                    Detach(go);
                    break;
            }
        }

        public static void Conceal(Component c, string owner) => Set(c, owner, true);

        public static void Reveal(Component c, string owner) => Set(c, owner, false);

        // Writes the group's alpha and gates its subtree while it is at 0 (a faded-out element is not rendered).
        public static void Fade(CanvasGroup group, float alpha, string owner)
        {
            if (!group) return;
            if (group.alpha != alpha) group.alpha = alpha;
            Set(group.gameObject, owner, RenderGateMath.Concealed(alpha));
        }

        public static void ReleaseAll(string owner)
        {
            foreach (var go in Ledger.ReleaseAll(owner)) Detach(go);
        }

        public static bool Holds(Component c, string owner) => c && Ledger.Holds(c.gameObject, owner);

        // The transform or one of its parents is gated (and the gates are not suspended).
        public static bool IsConcealed(Transform t)
        {
            if (Suspended || Entries.Count == 0) return false;
            for (; t; t = t.parent)
                if (Entries.ContainsKey(t.gameObject)) return true;
            return false;
        }

        public static void Suspend(bool on)
        {
            if (Suspended == on) return;
            Suspended = on;
            foreach (var e in Entries.Values)
                if (e.Canvas) e.Canvas.enabled = on && e.Prior;
        }

        private static void Attach(GameObject go, string owner)
        {
            if (!(go.transform is RectTransform))
            {
                // A Canvas needs a RectTransform (adding one would convert the Transform); nothing UI to gate.
                Ledger.Forget(go);
                Plugin.Log?.LogWarning($"[RuneSkin] render gate: '{go.name}' ({owner}) has no RectTransform; left as is");
                return;
            }
            Prune();
            var canvas = go.GetComponent<Canvas>();
            var added = !canvas;
            if (added) canvas = go.AddComponent<Canvas>();
            Entries[go] = new Entry { Canvas = canvas, Added = added, Prior = added || canvas.enabled };
            if (!Suspended) canvas.enabled = false;
        }

        private static void Detach(GameObject go)
        {
            if (!Entries.TryGetValue(go, out var e)) return;
            Entries.Remove(go);
            if (!e.Canvas) return;
            // Immediately: a deferred Destroy leaves the disabled canvas for the rest of the frame, and a gate taken
            // again in that frame would find (and later lose) the dying component.
            if (e.Added) Object.DestroyImmediate(e.Canvas);
            else e.Canvas.enabled = e.Prior;
        }

        private static void Prune()
        {
            if (Entries.Count < _pruneAt) return;
            Scratch.Clear();
            foreach (var go in Entries.Keys)
                if (!go) Scratch.Add(go);
            foreach (var go in Scratch)
            {
                Entries.Remove(go);
                Ledger.Forget(go);
            }
            Scratch.Clear();
            _pruneAt = System.Math.Max(64, Entries.Count * 2);
        }

        // Harness / diagnostics: "path [owners]" per gate.
        public static string Describe()
        {
            var sb = new StringBuilder();
            foreach (var pair in Entries)
            {
                if (!pair.Key) continue;
                sb.Append(Diagnostics.SkinDiagnostics.PathOf(pair.Key.transform, null)).Append(" [")
                  .Append(string.Join(",", Ledger.Owners(pair.Key))).Append("]\n");
            }
            return sb.ToString();
        }
    }
}
