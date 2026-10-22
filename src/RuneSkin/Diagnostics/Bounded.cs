using System;
using System.Collections.Generic;

namespace RuneSkin.Diagnostics
{
    // Bounded tables (perf audit B1): tables keyed by objects that can die (Unity objects destroyed with their scene)
    // drop the dead keys whenever they doubled since the last prune (no per-frame cost, size <= 2 x live), and
    // diagnostic message sets keep at most a cap. Pure managed: unit-tested.
    public static class Bounded
    {
        public const int MinPrune = 64;

        // Removes every key `alive` rejects once the table reached `next`; `next` becomes twice the survivors (at
        // least MinPrune). `scratch` is caller-owned (no allocation per prune).
        public static int PruneWhenDoubled<TKey, TValue>(Dictionary<TKey, TValue> table, Func<TKey, bool> alive, ref int next, List<TKey> scratch)
        {
            if (table.Count < next) return 0;
            foreach (var key in table.Keys)
                if (!alive(key)) scratch.Add(key);
            var removed = scratch.Count;
            foreach (var key in scratch) table.Remove(key);
            scratch.Clear();
            next = Math.Max(MinPrune, table.Count * 2);
            return removed;
        }

        // List form (pairs / rows kept in order).
        public static int PruneWhenDoubled<T>(List<T> list, Predicate<T> dead, ref int next)
        {
            if (list.Count < next) return 0;
            var removed = list.RemoveAll(dead);
            next = Math.Max(MinPrune, list.Count * 2);
            return removed;
        }

        // Adds while below the cap; false when already present or the cap is reached (the caller stays quiet).
        public static bool AddCapped<T>(HashSet<T> set, T item, int cap) => set.Count < cap && set.Add(item);

        // Keeps the newest `cap` entries.
        public static void AddCapped<T>(List<T> list, T item, int cap)
        {
            if (cap <= 0) return;
            if (list.Count >= cap) list.RemoveRange(0, list.Count - cap + 1);
            list.Add(item);
        }
    }
}
