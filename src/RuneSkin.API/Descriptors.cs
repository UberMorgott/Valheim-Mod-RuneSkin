using System;
using UnityEngine;

namespace RuneSkin.API
{
    /// <summary>Whether a doll slot stores its own item (EAQS-style) or mirrors vanilla equipment (hands, ammo).</summary>
    public enum SlotSource
    {
        /// <summary>The slot is backed by the registering mod's own storage.</summary>
        Storage,
        /// <summary>The slot shows an item that lives elsewhere (vanilla equipment, ammo); it has no storage.</summary>
        Mirror
    }

    /// <summary>
    /// A character-doll slot registered by a gameplay mod. RuneSkin draws the cell; the mod owns the data.
    /// Fields are copied when registered: changing this object afterwards has no effect, register again instead.
    /// Rendering arrives in M2 (<c>Skin.Supports("slot.render")</c>).
    /// </summary>
    public sealed class SlotDescriptor
    {
        /// <summary>Slot id, unique per owner (owner-qualified internally).</summary>
        public string Id;
        /// <summary>Doll group (e.g. "armor", "jewelry", "quick").</summary>
        public string Group;
        /// <summary>Order inside the group, ascending.</summary>
        public int Order;
        /// <summary>Optional cell the mod already built; RuneSkin creates its own cell when null.</summary>
        public RectTransform CellRect;
        /// <summary>Empty-slot glyph sprite; when null, <see cref="GlyphToken"/> or a generic rune is used.</summary>
        public Sprite Glyph;
        /// <summary>Theme token naming the empty-slot glyph (e.g. "glyph.slot.ring").</summary>
        public string GlyphToken;
        /// <summary>Localization key of the slot name (tooltip and controller narration; slots carry no visible label).</summary>
        public string TooltipKey;
        /// <summary>Storage-backed or mirror slot.</summary>
        public SlotSource Source;
        /// <summary>Returns the item currently in the slot, or null. Called on the main thread.</summary>
        public Func<ItemDrop.ItemData> GetItem;
        /// <summary>Whether an item may be dropped into the slot. Null = accepts nothing.</summary>
        public Func<ItemDrop.ItemData, bool> Accepts;
        /// <summary>Equips (true) or unequips (false) the item; returns success.</summary>
        public Func<ItemDrop.ItemData, bool, bool> SetEquipped;
        /// <summary>Stable focus id for controller navigation.</summary>
        public string FocusId;
    }

    /// <summary>Presentation channels a status effect may use (multi-label).</summary>
    [Flags]
    public enum EffectChannels
    {
        /// <summary>Icon in the status strip only.</summary>
        None = 0,
        /// <summary>Pending-damage stripe on the affected bar (needs a forecast).</summary>
        PendingStripe = 1,
        /// <summary>Tint of the affected bar.</summary>
        BarTint = 2,
        /// <summary>Frame overlay (one primary at a time).</summary>
        Overlay = 4,
        /// <summary>Small badge.</summary>
        Badge = 8
    }

    /// <summary>
    /// How RuneSkin presents a status effect, overriding its automatic classification. Copied when registered.
    /// Rendered since M1 (<c>Skin.Supports("effect.render")</c>).
    /// </summary>
    public sealed class StatusEffectDescriptor
    {
        /// <summary>Status effect name hash (<c>StatusEffect.NameHash()</c>).</summary>
        public int Hash;
        /// <summary>Channels the effect may use.</summary>
        public EffectChannels Channels;
        /// <summary>Theme colour token for the effect (e.g. "color.dot.poison").</summary>
        public string ColorToken;
        /// <summary>Priority inside its category; higher wins presentation budgets.</summary>
        public int Priority;
        /// <summary>True for harmful effects (sorted first in the status strip).</summary>
        public bool Harmful;
        /// <summary>Bar the channels apply to: "health" (default), "stamina" or "eitr".</summary>
        public string Bar;
        /// <summary>True when the effect has no defensible remaining-damage estimate (never numeric).</summary>
        public bool NoForecast;
        /// <summary>Remaining damage the effect will still deal to the local player, or null when unknown.</summary>
        public Func<StatusEffect, float?> PendingDamage;
        /// <summary>Remaining seconds for effects whose timer is not the status effect's own ttl (e.g. cooldowns).</summary>
        public Func<StatusEffect, float?> Timer;
    }

    /// <summary>Pool names for <see cref="Skin.RegisterResourceProvider"/>.</summary>
    public static class ResourcePools
    {
        /// <summary>Vanilla health (provider adds reservation / conversion data; the value is the game's).</summary>
        public const string Health = "health";
        /// <summary>Vanilla stamina.</summary>
        public const string Stamina = "stamina";
        /// <summary>Vanilla eitr.</summary>
        public const string Eitr = "eitr";
        /// <summary>A mod pool drawn as a thin rail above health (value comes from the provider).</summary>
        public const string EnergyShield = "energyshield";
    }

    /// <summary>Recharge phase of a pool that recharges on its own (energy shield).</summary>
    public enum ResourcePhase
    {
        /// <summary>Full or regenerating normally.</summary>
        Active,
        /// <summary>Waiting before recharge starts (<see cref="ResourceState.PhaseSeconds"/> left).</summary>
        RechargeDelay,
        /// <summary>Recharging now.</summary>
        Recharging,
        /// <summary>Depleted by damage this moment (short pulse, then RechargeDelay).</summary>
        Broken
    }

    /// <summary>
    /// Snapshot of one resource pool for the HUD (ApiVersion 2). Definitions: <see cref="Max"/> = B, the pool maximum
    /// after conversions and before reservations; <see cref="Available"/> = C, what the player can use
    /// (vanilla GetMax* for the vanilla pools); B - C is drawn as a locked segment at the bar end.
    /// NaN, negative or inconsistent values (C &gt; B, reserved parts &gt; B - C) make RuneSkin hide the extension for that
    /// frame, never show stale values. A provider that throws 3 times is disabled for the session.
    /// </summary>
    public struct ResourceState
    {
        /// <summary>False = provider has nothing to show (extension hidden; for "energyshield" the rail is hidden).</summary>
        public bool Valid;
        /// <summary>Current amount. Used for mod pools only ("energyshield"); vanilla pools use the game's value.</summary>
        public float Current;
        /// <summary>B: maximum before reservations (bar length and fill denominator).</summary>
        public float Max;
        /// <summary>C: usable maximum after reservations, &lt;= <see cref="Max"/>.</summary>
        public float Available;
        /// <summary>Part of B - C held by auras (tooltip), &gt;= 0.</summary>
        public float ReservedAuras;
        /// <summary>Part of B - C held by summons (tooltip), &gt;= 0.</summary>
        public float ReservedSummons;
        /// <summary>
        /// Set when this pool is converted away by the player's choice (e.g. eitr -&gt; "health" / "shield"): the bar
        /// collapses to a sealed plaque naming the target. Null = not converted.
        /// </summary>
        public string ConversionTarget;
        /// <summary>True while regeneration of this pool waits on something else (paused rune next to the number).</summary>
        public bool RegenBlocked;
        /// <summary>Recharge phase (mod pools).</summary>
        public ResourcePhase Phase;
        /// <summary>Seconds left in <see cref="Phase"/> (RechargeDelay countdown), 0 when unknown.</summary>
        public float PhaseSeconds;
        /// <summary>This pool carries the player (e.g. max health 1): drawn as the main bar, health becomes a badge.</summary>
        public bool MainBar;
        /// <summary>The pool exists but does not act (e.g. absorbs nothing): drawn grey.</summary>
        public bool Inert;
        /// <summary><c>UnityEngine.Time.time</c> when the snapshot was taken.</summary>
        public float Timestamp;
    }

    /// <summary>
    /// Live size of a variable HUD list (ApiVersion 2), e.g. list "food": number of food sockets. Sockets from
    /// <see cref="ExtraFrom"/> on are drawn with the <see cref="ExtraGlyphToken"/> rim (e.g. a gem-granted socket).
    /// </summary>
    public struct CapacityState
    {
        /// <summary>False = provider has nothing to say (fallback: vanilla count).</summary>
        public bool Valid;
        /// <summary>Number of cells, &gt;= 0.</summary>
        public int Count;
        /// <summary>Index of the first special cell; &gt;= Count = none.</summary>
        public int ExtraFrom;
        /// <summary>Theme sprite token of the special cells' rim (e.g. "sprite.hud.socket.gem"), or null.</summary>
        public string ExtraGlyphToken;
    }

    /// <summary>Panel an action button belongs to.</summary>
    public enum PanelKind
    {
        /// <summary>The player inventory.</summary>
        Player,
        /// <summary>An open container.</summary>
        Container,
        /// <summary>An equipped backpack's inventory.</summary>
        Backpack
    }

    /// <summary>
    /// A glyph-only action button on an inventory panel (sort, take all, ...). Logic stays in the registering mod.
    /// Copied when registered. Rendering arrives in M2 (<c>Skin.Supports("panel.action")</c>).
    /// </summary>
    public sealed class PanelActionDescriptor
    {
        /// <summary>Action id, unique per owner.</summary>
        public string Id;
        /// <summary>Panel the action shows on.</summary>
        public PanelKind Panel;
        /// <summary>Glyph sprite; when null, <see cref="GlyphToken"/> is used.</summary>
        public Sprite Glyph;
        /// <summary>Theme token of the glyph.</summary>
        public string GlyphToken;
        /// <summary>Localization key of the tooltip.</summary>
        public string TooltipKey;
        /// <summary>ZInput button name for keyboard and gamepad, or null.</summary>
        public string InputButton;
        /// <summary>Presentation-only enabled state; null = always enabled.</summary>
        public Func<bool> Enabled;
        /// <summary>Invoked on click / input on the main thread.</summary>
        public Action OnInvoke;
        /// <summary>Order in the action bar, ascending.</summary>
        public int Order;
        /// <summary>
        /// ApiVersion 3: invoked instead of <see cref="OnInvoke"/> with the inventory of the panel instance the button
        /// sits on (player inventory, the open container's, or the worn backpack's), so one registration serves every
        /// container / backpack panel. Null = <see cref="OnInvoke"/>.
        /// </summary>
        public Action<Inventory> OnInvokeFor;
        /// <summary>ApiVersion 3: presentation-only enabled state for that panel instance; null = <see cref="Enabled"/>.</summary>
        public Func<Inventory, bool> EnabledFor;
    }
}
