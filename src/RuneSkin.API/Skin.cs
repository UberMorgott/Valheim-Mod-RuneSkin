using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.API
{
    /// <summary>
    /// Public entry point of the RuneSkin UI skin. Reference RuneSkin.API.dll softly: call it from a method that
    /// only runs when the assembly is present (check the plugin GUID <see cref="PluginGuid"/> in
    /// <c>BepInEx.Bootstrap.Chainloader.PluginInfos</c> first), so your mod still loads without RuneSkin.
    /// All members must be called on the Unity main thread. Owner ids are your plugin GUID; ids you pass are
    /// owner-qualified internally, so two mods never collide.
    /// Registrations made before RuneSkin finished loading are kept and replayed when it attaches; disposing the
    /// handle before that cancels them.
    /// </summary>
    public static class Skin
    {
        /// <summary>BepInEx GUID of the RuneSkin plugin.</summary>
        public const string PluginGuid = "morgott.valheim.runeskin";

        /// <summary>
        /// Version of the installed API surface (a property, not a const, so you read the installed value at runtime).
        /// Bumped on every public change; check it before using newer members.
        /// </summary>
        public static int ApiVersion => 1;

        private static IHost _host;
        private static int _mainThread = -1;
        private static readonly List<Registration> Pending = new List<Registration>();

        /// <summary>
        /// True once the RuneSkin plugin has attached. Being available does not mean a feature is rendered: ask
        /// <see cref="Supports"/> before disabling your own UI.
        /// </summary>
        public static bool IsAvailable => _host != null;

        /// <summary>
        /// Whether the running RuneSkin renders a capability. Known names: "root.restyle", "bind", "invalidate",
        /// "theme.read", "ownership" (M0); "panel.dock", "slot.render", "effect.render", "resource.render",
        /// "panel.action" (later milestones, false in M0).
        /// </summary>
        public static bool Supports(string capability) => _host != null && capability != null && _host.Supports(capability);

        /// <summary>
        /// Whether RuneSkin owns a screen family (e.g. "Menu", "Tooltips") in this session. Frozen at startup;
        /// only families whose assets loaded count. Other skins must not style an owned family.
        /// </summary>
        public static bool IsOwned(string family) => _host != null && family != null && _host.IsOwned(family);

        /// <summary>
        /// Puts a UI subtree under RuneSkin styling. The root is classified and restyled now and again on
        /// <see cref="Invalidate"/>; disposing the handle reverts what RuneSkin changed and forgets the root.
        /// </summary>
        /// <param name="owner">Your plugin GUID.</param>
        /// <param name="root">Root transform of your panel.</param>
        /// <param name="screenId">Stable id used in diagnostics (e.g. "MyMod.CraftPanel").</param>
        public static IDisposable RegisterRoot(string owner, Transform root, string screenId)
        {
            CheckOwner(owner);
            if (!root) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrEmpty(screenId)) throw new ArgumentNullException(nameof(screenId));
            return Track(h => h.RegisterRoot(owner, root, screenId));
        }

        /// <summary>
        /// Declares the role of one graphic explicitly (highest classifier priority), e.g. "panel", "button",
        /// "text.header", "text.body", "content" (keep as is). Disposing removes the binding and reverts the graphic.
        /// </summary>
        public static IDisposable Bind(string owner, Graphic graphic, string role)
        {
            CheckOwner(owner);
            if (!graphic) throw new ArgumentNullException(nameof(graphic));
            if (string.IsNullOrEmpty(role)) throw new ArgumentNullException(nameof(role));
            return Track(h => h.Bind(owner, graphic, role));
        }

        /// <summary>Re-classifies and restyles a registered or owned root after you added or changed children.</summary>
        public static void Invalidate(Transform root)
        {
            CheckThread();
            if (root) _host?.Invalidate(root);
        }

        /// <summary>
        /// Registers a panel for layout docking in a screen region (e.g. "inventory.left"). M0 records it only
        /// (<c>Supports("panel.dock")</c> is false).
        /// </summary>
        public static IDisposable RegisterPanel(string owner, RectTransform rect, string region)
        {
            CheckOwner(owner);
            if (!rect) throw new ArgumentNullException(nameof(rect));
            if (string.IsNullOrEmpty(region)) throw new ArgumentNullException(nameof(region));
            return Track(h => h.RegisterPanel(owner, rect, region));
        }

        /// <summary>Registers a character-doll slot (see <see cref="SlotDescriptor"/>). Recorded in M0, rendered in M2.</summary>
        public static IDisposable RegisterSlot(string owner, SlotDescriptor descriptor)
        {
            CheckOwner(owner);
            if (descriptor == null || string.IsNullOrEmpty(descriptor.Id)) throw new ArgumentNullException(nameof(descriptor));
            var copy = Copy(descriptor);
            return Track(h => h.RegisterSlot(owner, copy));
        }

        /// <summary>Registers how a status effect is presented. Recorded in M0, rendered in M1.</summary>
        public static IDisposable RegisterStatusEffect(string owner, StatusEffectDescriptor descriptor)
        {
            CheckOwner(owner);
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var copy = Copy(descriptor);
            return Track(h => h.RegisterStatusEffect(owner, copy));
        }

        /// <summary>
        /// Registers a provider for a resource pool (e.g. "energyshield"). Provisional, recorded in M0, rendered in
        /// M1. The provider runs on the main thread; exceptions disable it after repeated faults.
        /// </summary>
        public static IDisposable RegisterResourceProvider(string owner, string pool, Func<Player, ResourceState> provider)
        {
            CheckOwner(owner);
            if (string.IsNullOrEmpty(pool)) throw new ArgumentNullException(nameof(pool));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            return Track(h => h.RegisterResourceProvider(owner, pool, provider));
        }

        /// <summary>Registers an inventory panel action button. Recorded in M0, rendered in M2.</summary>
        public static IDisposable RegisterPanelAction(string owner, PanelActionDescriptor descriptor)
        {
            CheckOwner(owner);
            if (descriptor == null || string.IsNullOrEmpty(descriptor.Id)) throw new ArgumentNullException(nameof(descriptor));
            var copy = Copy(descriptor);
            return Track(h => h.RegisterPanelAction(owner, copy));
        }

        /// <summary>Read-only access to the active theme tokens.</summary>
        public static class Theme
        {
            /// <summary>Colour of a token (e.g. "color.text.body"); false when unknown or RuneSkin is absent.</summary>
            public static bool TryGetColor(string token, out Color color)
            {
                color = default;
                return _host != null && token != null && _host.TryGetColor(token, out color);
            }

            /// <summary>Sprite of a token (e.g. "sprite.frame.panel"); false when unknown or its asset is missing.</summary>
            public static bool TryGetSprite(string token, out Sprite sprite)
            {
                sprite = null;
                return _host != null && token != null && _host.TryGetSprite(token, out sprite);
            }

            /// <summary>Number of a token (e.g. "size.tooltip.padding", reference pixels).</summary>
            public static bool TryGetFloat(string token, out float value)
            {
                value = 0f;
                return _host != null && token != null && _host.TryGetFloat(token, out value);
            }
        }

        // Called once by the RuneSkin plugin from its Awake (main thread): replays registrations made before.
        internal static void Attach(IHost host)
        {
            if (_host != null) throw new InvalidOperationException("RuneSkin host already attached");
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _mainThread = Environment.CurrentManagedThreadId;
            var pending = Pending.ToArray();
            Pending.Clear();
            foreach (var registration in pending)
            {
                // One bad queued registration (e.g. its root destroyed meanwhile) must not drop the others.
                try { registration.Connect(host); }
                catch (Exception e) { Debug.LogError($"[RuneSkin.API] replayed registration failed: {e}"); }
            }
        }

        private static IDisposable Track(Func<IHost, IDisposable> make)
        {
            var registration = new Registration(make);
            if (_host != null)
                registration.Connect(_host);
            else
                Pending.Add(registration);
            return registration;
        }

        private static void CheckOwner(string owner)
        {
            CheckThread();
            if (string.IsNullOrEmpty(owner)) throw new ArgumentNullException(nameof(owner));
        }

        private static void CheckThread()
        {
            if (_mainThread >= 0 && Environment.CurrentManagedThreadId != _mainThread)
                throw new InvalidOperationException("RuneSkin.API must be called on the Unity main thread");
        }

        private static SlotDescriptor Copy(SlotDescriptor d) => new SlotDescriptor
        {
            Id = d.Id,
            Group = d.Group,
            Order = d.Order,
            CellRect = d.CellRect,
            Glyph = d.Glyph,
            GlyphToken = d.GlyphToken,
            TooltipKey = d.TooltipKey,
            Source = d.Source,
            GetItem = d.GetItem,
            Accepts = d.Accepts,
            SetEquipped = d.SetEquipped,
            FocusId = d.FocusId
        };

        private static StatusEffectDescriptor Copy(StatusEffectDescriptor d) => new StatusEffectDescriptor
        {
            Hash = d.Hash,
            Channels = d.Channels,
            ColorToken = d.ColorToken,
            Priority = d.Priority,
            NoForecast = d.NoForecast,
            PendingDamage = d.PendingDamage,
            Timer = d.Timer
        };

        private static PanelActionDescriptor Copy(PanelActionDescriptor d) => new PanelActionDescriptor
        {
            Id = d.Id,
            Panel = d.Panel,
            Glyph = d.Glyph,
            GlyphToken = d.GlyphToken,
            TooltipKey = d.TooltipKey,
            InputButton = d.InputButton,
            Enabled = d.Enabled,
            OnInvoke = d.OnInvoke,
            Order = d.Order
        };

        // Handle returned to callers: connects to the host now or on attach; Dispose before attach cancels the replay.
        private sealed class Registration : IDisposable
        {
            private Func<IHost, IDisposable> _make;
            private IDisposable _inner;
            private bool _disposed;

            public Registration(Func<IHost, IDisposable> make) => _make = make;

            public void Connect(IHost host)
            {
                if (_disposed || _make == null) return;
                _inner = _make(host);
                _make = null;
            }

            public void Dispose()
            {
                if (_disposed) return;
                CheckThread();
                _disposed = true;
                _make = null;
                Pending.Remove(this);
                _inner?.Dispose();
                _inner = null;
            }
        }
    }

    // Backend implemented by the RuneSkin plugin (InternalsVisibleTo). Not part of the public contract.
    internal interface IHost
    {
        bool IsOwned(string family);
        bool Supports(string capability);
        IDisposable RegisterRoot(string owner, Transform root, string screenId);
        IDisposable Bind(string owner, Graphic graphic, string role);
        void Invalidate(Transform root);
        IDisposable RegisterPanel(string owner, RectTransform rect, string region);
        IDisposable RegisterSlot(string owner, SlotDescriptor descriptor);
        IDisposable RegisterStatusEffect(string owner, StatusEffectDescriptor descriptor);
        IDisposable RegisterResourceProvider(string owner, string pool, Func<Player, ResourceState> provider);
        IDisposable RegisterPanelAction(string owner, PanelActionDescriptor descriptor);
        bool TryGetColor(string token, out Color color);
        bool TryGetSprite(string token, out Sprite sprite);
        bool TryGetFloat(string token, out float value);
    }
}
