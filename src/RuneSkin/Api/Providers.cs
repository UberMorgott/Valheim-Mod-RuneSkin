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

        // Ability zone slots (RuneSkin.API RegisterAbilitySlot), kept sorted by Order (stable: registration order on
        // ties). AbilityRevision changes on every add / remove, so the strip re-lays out only then.
        private sealed class Ability
        {
            public string Owner;
            public AbilitySlotDescriptor D;
            public int Faults;
        }

        private static readonly List<Ability> Abilities = new List<Ability>();

        public static int AbilityRevision { get; private set; }

        public static int AbilitySlotCount => Abilities.Count;

        public static Action AddAbility(string owner, AbilitySlotDescriptor d)
        {
            var a = new Ability { Owner = owner, D = d };
            var at = Abilities.Count;
            while (at > 0 && Abilities[at - 1].D.Order > d.Order) at--;
            Abilities.Insert(at, a);
            AbilityRevision++;
            Plugin.Log.LogInfo($"[RuneSkin] ability slot '{d.Id}' (order {d.Order}) from {owner}");
            return () =>
            {
                if (Abilities.Remove(a)) AbilityRevision++;
            };
        }

        // Slot i in order; false = faulted (hidden) or out of range.
        public static bool TryAbility(int i, Player player, out AbilitySlotState state, out bool round)
        {
            state = default;
            round = false;
            if (i < 0 || i >= Abilities.Count) return false;
            var a = Abilities[i];
            round = a.D.Round;
            if (a.Faults >= MaxFaults) return false;
            try
            {
                state = a.D.GetState(player);
                return state.Valid;
            }
            catch (Exception ex)
            {
                a.Faults++;
                Plugin.Log.LogError($"[RuneSkin] ability slot '{a.D.Id}' from {a.Owner} failed ({a.Faults}/{MaxFaults}): {ex}");
                return false;
            }
        }

        public static void ActivateAbility(int i, Player player)
        {
            if (i < 0 || i >= Abilities.Count || Abilities[i].D.Activate == null || Abilities[i].Faults >= MaxFaults) return;
            try { Abilities[i].D.Activate(player); }
            catch (Exception ex) { Plugin.Log.LogError($"[RuneSkin] ability slot '{Abilities[i].D.Id}' activate failed: {ex}"); }
        }

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

        private static readonly List<Entry<HoldPromptState>> Holds = new List<Entry<HoldPromptState>>();

        public static Action AddHold(string owner, string id, Func<Player, HoldPromptState> get) => Add(Holds, owner, owner + ":" + id, get);

        // Newest live prompt that wants to be shown (sanitized: progress clamped to 0..1, empty prompts hidden).
        public static bool TryHold(Player player, out HoldPromptState state)
        {
            for (var i = Holds.Count - 1; i >= 0; i--)
            {
                var e = Holds[i];
                if (e.Faults >= MaxFaults || !Call(e, player, out state)) continue;
                state = WorldHud.HoldPromptMath.Sanitize(state);
                if (state.Visible) return true;
            }
            state = default;
            return false;
        }

        public static IEnumerable<string> Describe()
        {
            foreach (var e in Holds) yield return $"hold {e.Key} from {e.Owner} faults={e.Faults}";
            foreach (var e in Resources) yield return $"resource {e.Key} from {e.Owner} faults={e.Faults}";
            foreach (var e in Capacities) yield return $"capacity {e.Key} from {e.Owner} faults={e.Faults}";
            foreach (var p in EffectOwners) yield return $"effect {p.Key} from {p.Value}";
            foreach (var a in Abilities) yield return $"ability {a.D.Id} order={a.D.Order} from {a.Owner} faults={a.Faults}";
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
