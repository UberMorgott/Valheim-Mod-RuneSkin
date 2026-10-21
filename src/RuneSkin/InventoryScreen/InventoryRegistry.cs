using System;
using System.Collections.Generic;
using RuneSkin.API;
using UnityEngine;

namespace RuneSkin.InventoryScreen
{
    // API registrations the inventory screen renders: doll slots (Skin.RegisterSlot), panel actions
    // (Skin.RegisterPanelAction), slot overlays (Skin.SetSlotOverlay) and docked panels (Skin.RegisterPanel with an
    // "inventory.*" region). Keys are owner-qualified; a later registration of the same key replaces the earlier one.
    // Foreign callbacks run through Guard: MaxFaults exceptions disable that registration for the session.
    public static class InventoryRegistry
    {
        public const int MaxFaults = 3;

        public sealed class Slot
        {
            public string Key, Owner;
            public SlotDescriptor D;
            public int Faults;
            public bool Dead => Faults >= MaxFaults;
        }

        public sealed class Action
        {
            public string Key, Owner;
            public PanelActionDescriptor D;
            public int Faults;
            public bool Dead => Faults >= MaxFaults;
        }

        public struct OverlayKey : IEquatable<OverlayKey>
        {
            public string Owner;
            public Inventory Inventory;
            public int X, Y;

            public bool Equals(OverlayKey other) => string.Equals(Owner, other.Owner, StringComparison.Ordinal) && ReferenceEquals(Inventory, other.Inventory) && X == other.X && Y == other.Y;
            public override bool Equals(object obj) => obj is OverlayKey o && Equals(o);
            public override int GetHashCode() => ((Owner == null ? 0 : StringComparer.Ordinal.GetHashCode(Owner)) * 397) ^ (Inventory?.GetHashCode() ?? 0) ^ (X * 31 + Y);
            public static bool operator ==(OverlayKey a, OverlayKey b) => a.Equals(b);
            public static bool operator !=(OverlayKey a, OverlayKey b) => !a.Equals(b);
        }

        public static readonly List<Slot> Slots = new List<Slot>();
        public static readonly List<Action> Actions = new List<Action>();
        public static readonly Dictionary<OverlayKey, string> Overlays = new Dictionary<OverlayKey, string>();
        public static readonly List<KeyValuePair<RectTransform, string>> Panels = new List<KeyValuePair<RectTransform, string>>();

        // Bumped on every change: the screen re-lays out / rebuilds cells only then.
        public static int Revision { get; private set; }
        public static int OverlayRevision { get; private set; }

        public static System.Action AddSlot(string owner, SlotDescriptor d)
        {
            var key = owner + ":" + d.Id;
            Slots.RemoveAll(s => s.Key == key);
            var entry = new Slot { Key = key, Owner = owner, D = d };
            Slots.Add(entry);
            Revision++;
            Plugin.Log.LogInfo($"[RuneSkin] doll slot '{key}' ({d.Source}, group {d.Group}, order {d.Order})");
            return () => { if (Slots.Remove(entry)) Revision++; };
        }

        public static System.Action AddAction(string owner, PanelActionDescriptor d)
        {
            var key = owner + ":" + d.Id;
            Actions.RemoveAll(a => a.Key == key && a.D.Panel == d.Panel);
            var entry = new Action { Key = key, Owner = owner, D = d };
            Actions.Add(entry);
            Revision++;
            Plugin.Log.LogInfo($"[RuneSkin] panel action '{key}' on {d.Panel}");
            return () => { if (Actions.Remove(entry)) Revision++; };
        }

        public static System.Action SetOverlay(string owner, Inventory inventory, int x, int y, string token)
        {
            var key = new OverlayKey { Owner = owner, Inventory = inventory, X = x, Y = y };
            Overlays[key] = token;
            OverlayRevision++;
            return () =>
            {
                if (Overlays.TryGetValue(key, out var cur) && ReferenceEquals(cur, token))
                {
                    Overlays.Remove(key);
                    OverlayRevision++;
                }
            };
        }

        public static System.Action AddPanel(RectTransform rect, string region)
        {
            var entry = new KeyValuePair<RectTransform, string>(rect, region);
            Panels.Add(entry);
            Revision++;
            return () => { if (Panels.Remove(entry)) Revision++; };
        }

        public static RectTransform Panel(string region)
        {
            for (var i = Panels.Count - 1; i >= 0; i--)
                if (Panels[i].Value == region && Panels[i].Key) return Panels[i].Key;
            return null;
        }

        // Overlay token of a cell for any owner (last one wins when two owners mark the same cell).
        public static string OverlayAt(Inventory inventory, int x, int y)
        {
            if (Overlays.Count == 0 || inventory == null) return null;
            string token = null;
            foreach (var pair in Overlays)
                if (ReferenceEquals(pair.Key.Inventory, inventory) && pair.Key.X == x && pair.Key.Y == y)
                    token = pair.Value;
            return token;
        }

        public static T Guard<T>(Slot s, Func<T> call, T fallback)
        {
            if (s.Dead) return fallback;
            try { return call(); }
            catch (Exception e) { Fault(ref s.Faults, s.Key, e); return fallback; }
        }

        public static T Guard<T>(Action a, Func<T> call, T fallback)
        {
            if (a.Dead) return fallback;
            try { return call(); }
            catch (Exception e) { Fault(ref a.Faults, a.Key, e); return fallback; }
        }

        private static void Fault(ref int faults, string key, Exception e)
        {
            faults++;
            Plugin.Log.LogError($"[RuneSkin] callback of '{key}' failed ({faults}/{MaxFaults}){(faults >= MaxFaults ? ", disabled" : "")}: {e}");
            Revision++;
        }
    }
}
