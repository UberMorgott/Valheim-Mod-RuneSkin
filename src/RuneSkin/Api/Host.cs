using System;
using System.Collections.Generic;
using RuneSkin.API;
using RuneSkin.Classify;
using RuneSkin.Ownership;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Api
{
    // Roots RuneSkin restyles: adapter-owned vanilla roots and roots registered through the API. Each root keeps
    // the factory of its ScreenContext, so Invalidate and theme reloads re-run the same classification.
    public static class ScreenRegistry
    {
        private static readonly Dictionary<Transform, Func<ScreenContext>> Roots = new Dictionary<Transform, Func<ScreenContext>>();

        private static readonly List<Transform> Dead = new List<Transform>();

        // Bumped on every new root or removal: the fallback restyler (ModScreens\ForeignUi) re-derives what is foreign.
        public static int Version { get; private set; }

        public static void Register(Transform root, Func<ScreenContext> context)
        {
            // Destroyed roots (tooltip instances die on every hide) leave the registry here.
            foreach (var key in Roots.Keys)
                if (!key) Dead.Add(key);
            foreach (var key in Dead) Roots.Remove(key);
            Dead.Clear();
            if (!Roots.ContainsKey(root)) Version++;
            Roots[root] = context;
        }

        public static void Unregister(Transform root)
        {
            if (Roots.Remove(root)) Version++;
        }

        public static bool IsRoot(Transform t) => t && Roots.ContainsKey(t);

        public static IEnumerable<Transform> All => Roots.Keys;

        public static bool Restyle(Transform root)
        {
            if (!root || !Roots.TryGetValue(root, out var make)) return false;
            Restyler.RestyleRoot(root, make());
            return true;
        }

        public static void RestyleAll()
        {
            var dead = new List<Transform>();
            foreach (var pair in new List<KeyValuePair<Transform, Func<ScreenContext>>>(Roots))
            {
                if (!pair.Key) { dead.Add(pair.Key); continue; }
                Restyler.RestyleRoot(pair.Key, pair.Value());
            }
            foreach (var d in dead) Roots.Remove(d);
        }

        // Nearest registered ancestor (or self).
        public static Transform RootOf(Transform t)
        {
            for (var p = t; p; p = p.parent)
                if (Roots.ContainsKey(p)) return p;
            return null;
        }

        public static ScreenContext ContextOf(Transform root) => root && Roots.TryGetValue(root, out var make) ? make() : null;
    }

    // Backend of RuneSkin.API.Skin.
    public sealed class Host : IHost
    {
        private static readonly HashSet<string> Capabilities = new HashSet<string> { "root.restyle", "bind", "invalidate", "theme.read", "ownership" };

        public readonly List<KeyValuePair<string, object>> Records = new List<KeyValuePair<string, object>>();

        public bool IsOwned(string family) => OwnershipRegistry.IsOwned(family);

        // HUD rendering capabilities exist only while RuneSkin owns the HUD family.
        private static readonly HashSet<string> HudCapabilities = new HashSet<string> { "effect.render", "resource.render", "capacity.render" };

        // Inventory rendering capabilities exist only while RuneSkin owns the inventory family.
        private static readonly HashSet<string> InventoryCapabilities = new HashSet<string> { "panel.dock", "slot.render", "panel.action", "slot.overlay" };

        public bool Supports(string capability) =>
            Capabilities.Contains(capability) || (HudCapabilities.Contains(capability) && OwnershipRegistry.IsOwned(OwnershipRegistry.Hud)) ||
            (InventoryCapabilities.Contains(capability) && OwnershipRegistry.IsOwned(OwnershipRegistry.Inventory));

        public IDisposable RegisterRoot(string owner, Transform root, string screenId)
        {
            if (!root) return new Handle(null); // destroyed while queued before attach
            var id = owner + ":" + screenId;
            var mark = root.GetComponent<RootMark>();
            if (!mark) mark = root.gameObject.AddComponent<RootMark>();
            mark.ScreenId = id;
            mark.Owner = owner;
            ScreenRegistry.Register(root, () => new ScreenContext(id, "Registered"));
            ScreenRegistry.Restyle(root);
            return new Handle(() =>
            {
                ScreenRegistry.Unregister(root);
                if (!root) return;
                Restyler.RevertRoot(root);
                var m = root.GetComponent<RootMark>();
                if (m) UnityEngine.Object.Destroy(m);
            });
        }

        public IDisposable Bind(string owner, Graphic graphic, string role)
        {
            if (!graphic) return new Handle(null);
            Classifier.Bindings[graphic] = Classification.Of(role, $"bound by {owner}");
            Restyle(graphic);
            return new Handle(() =>
            {
                Classifier.Bindings.Remove(graphic);
                if (!graphic) return;
                var mark = graphic.GetComponent<SkinMark>();
                if (mark)
                {
                    // Reset in place (a Destroy would land after the restyle below and take its journal with it).
                    Restyler.Revert(mark);
                    mark.Role = null;
                    mark.ThemeRevision = -1;
                }
                Restyle(graphic);
            });
        }

        private static void Restyle(Graphic graphic)
        {
            if (!graphic) return;
            var root = ScreenRegistry.RootOf(graphic.transform);
            var ctx = ScreenRegistry.ContextOf(root);
            if (ctx != null) Restyler.Style(graphic, ctx);
        }

        public void Invalidate(Transform root)
        {
            if (!ScreenRegistry.Restyle(root))
            {
                var registered = ScreenRegistry.RootOf(root);
                if (registered) ScreenRegistry.Restyle(registered);
            }
        }

        // "inventory.*" regions dock in the inventory layout (M2); others are recorded for later milestones.
        public IDisposable RegisterPanel(string owner, RectTransform rect, string region) =>
            region.StartsWith("inventory.", StringComparison.Ordinal)
                ? new Handle(InventoryScreen.InventoryRegistry.AddPanel(rect, region))
                : Record(owner, "panel", new KeyValuePair<RectTransform, string>(rect, region));

        public IDisposable RegisterSlot(string owner, SlotDescriptor descriptor) => new Handle(InventoryScreen.InventoryRegistry.AddSlot(owner, descriptor));

        public IDisposable SetSlotOverlay(string owner, Inventory inventory, int x, int y, string glyphToken) =>
            new Handle(InventoryScreen.InventoryRegistry.SetOverlay(owner, inventory, x, y, glyphToken));

        public IDisposable RegisterStatusEffect(string owner, StatusEffectDescriptor descriptor) =>
            new Handle(Providers.AddEffect(owner, descriptor));

        public IDisposable RegisterResourceProvider(string owner, string pool, Func<Player, ResourceState> provider) =>
            new Handle(Providers.AddResource(owner, pool, provider));

        public IDisposable RegisterCapacityProvider(string owner, string list, Func<Player, CapacityState> provider) =>
            new Handle(Providers.AddCapacity(owner, list, provider));

        public IDisposable RegisterPanelAction(string owner, PanelActionDescriptor descriptor) => new Handle(InventoryScreen.InventoryRegistry.AddAction(owner, descriptor));

        public bool TryGetColor(string token, out Color color) => ThemeTokens.TryGetColor(token, out color);

        public bool TryGetSprite(string token, out Sprite sprite) => ThemeTokens.TryGetSprite(token, out sprite);

        public bool TryGetFloat(string token, out float value) => ThemeTokens.TryGetFloat(token, out value);

        // M0: accepted and kept for the milestone that renders it (Supports(...) stays false until then).
        private IDisposable Record(string owner, string kind, object payload)
        {
            var entry = new KeyValuePair<string, object>(owner + "|" + kind, payload);
            Records.Add(entry);
            Plugin.Log.LogInfo($"[RuneSkin] api {kind} from {owner}: accepted, not rendered yet");
            return new Handle(() => Records.Remove(entry));
        }

        private sealed class Handle : IDisposable
        {
            private Action _dispose;
            public Handle(Action dispose) => _dispose = dispose;

            public void Dispose()
            {
                var d = _dispose;
                _dispose = null;
                d?.Invoke();
            }
        }
    }
}
