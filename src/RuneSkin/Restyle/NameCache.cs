using System;
using System.Collections.Generic;
using RuneSkin.Diagnostics;

namespace RuneSkin.Restyle
{
    // UnityEngine.Object.name builds a new managed string on every read. Per-frame code that tells children apart by
    // name (stat plates, info tabs, plate ornaments) reads it once per object here (perf audit B2). Objects are not
    // renamed after they are built (vanilla prefabs, clones and RuneSkin's own objects keep their names); destroyed
    // keys leave whenever the table doubled (Bounded).
    public static class NameCache
    {
        private static readonly Dictionary<UnityEngine.Object, string> Names = new Dictionary<UnityEngine.Object, string>();
        private static readonly List<UnityEngine.Object> Dead = new List<UnityEngine.Object>();
        private static readonly Func<UnityEngine.Object, bool> Alive = o => o;
        private static int _prune = 256;

        public static string Of(UnityEngine.Object o)
        {
            if (!o) return "";
            if (Names.TryGetValue(o, out var name)) return name;
            Bounded.PruneWhenDoubled(Names, Alive, ref _prune, Dead);
            name = o.name;
            Names[o] = name;
            return name;
        }

        public static int Count => Names.Count;
    }
}
