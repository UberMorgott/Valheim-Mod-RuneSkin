using System;
using System.Collections.Generic;
using RuneSkin.API;
using RuneSkin.PlayerHud;

namespace RuneSkin.Api
{
    // Foreign callbacks behind the HUD (resource pools, list capacities, effect descriptors). Latest registration of a
    // key wins; a callback that throws MaxFaults times is disabled for the session and its data hidden (never stale).
    public static class Providers
    {
        public const int MaxFaults = 3;

        private sealed class Entry<T>
        {
            public string Owner;
            public string Key;
            public Func<Player, T> Get;
            public int Faults;
        }

        private static readonly List<Entry<ResourceState>> Resources = new List<Entry<ResourceState>>();
        private static readonly List<Entry<CapacityState>> Capacities = new List<Entry<CapacityState>>();
        private static readonly Dictionary<int, StatusEffectDescriptor> Effects = new Dictionary<int, StatusEffectDescriptor>();
        private static readonly Dictionary<int, string> EffectOwners = new Dictionary<int, string>();

        public static int Revision { get; private set; }

        public static Action AddResource(string owner, string pool, Func<Player, ResourceState> get) =>
            Add(Resources, owner, pool, get);

        public static Action AddCapacity(string owner, string list, Func<Player, CapacityState> get) =>
            Add(Capacities, owner, list, get);

        public static Action AddEffect(string owner, StatusEffectDescriptor d)
        {
            Effects[d.Hash] = d;
            EffectOwners[d.Hash] = owner;
            Revision++;
            return () =>
            {
                if (Effects.TryGetValue(d.Hash, out var cur) && ReferenceEquals(cur, d))
                {
                    Effects.Remove(d.Hash);
                    EffectOwners.Remove(d.Hash);
                    Revision++;
                }
            };
        }

        public static bool TryEffect(int hash, out StatusEffectDescriptor d) => Effects.TryGetValue(hash, out d);

        private static Action Add<T>(List<Entry<T>> list, string owner, string key, Func<Player, T> get)
        {
            var e = new Entry<T> { Owner = owner, Key = key, Get = get };
            list.Add(e);
            Revision++;
            Plugin.Log.LogInfo($"[RuneSkin] provider '{key}' from {owner}");
            return () =>
            {
                list.Remove(e);
                Revision++;
            };
        }

        // Latest live provider of the pool; false = none, faulted or insane snapshot (extension hidden).
        public static bool TryResource(string pool, Player player, bool usesCurrent, out ResourceState state)
        {
            state = default;
            var e = Latest(Resources, pool);
            if (e == null) return false;
            if (!Call(e, player, out state)) return false;
            return ResourceCheck.IsSane(state, usesCurrent);
        }

        public static bool HasResource(string pool) => Latest(Resources, pool) != null;

        public static bool TryCapacity(string list, Player player, out CapacityState state)
        {
            state = default;
            var e = Latest(Capacities, list);
            if (e == null) return false;
            return Call(e, player, out state) && ResourceCheck.IsSane(state);
        }

        public static IEnumerable<string> Describe()
        {
            foreach (var e in Resources) yield return $"resource {e.Key} from {e.Owner} faults={e.Faults}";
            foreach (var e in Capacities) yield return $"capacity {e.Key} from {e.Owner} faults={e.Faults}";
            foreach (var p in EffectOwners) yield return $"effect {p.Key} from {p.Value}";
        }

        private static Entry<T> Latest<T>(List<Entry<T>> list, string key)
        {
            for (var i = list.Count - 1; i >= 0; i--)
                if (list[i].Key == key && list[i].Faults < MaxFaults) return list[i];
            return null;
        }

        private static bool Call<T>(Entry<T> e, Player player, out T value)
        {
            try
            {
                value = e.Get(player);
                return true;
            }
            catch (Exception ex)
            {
                value = default;
                e.Faults++;
                Plugin.Log.LogError($"[RuneSkin] provider '{e.Key}' from {e.Owner} failed ({e.Faults}/{MaxFaults}" +
                                    (e.Faults >= MaxFaults ? ", disabled" : "") + $"): {ex}");
                return false;
            }
        }
    }
}
