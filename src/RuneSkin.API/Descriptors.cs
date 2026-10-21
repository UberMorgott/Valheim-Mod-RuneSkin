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
    /// How RuneSkin presents a status effect. Copied when registered. Rendering arrives in M1
    /// (<c>Skin.Supports("effect.render")</c>).
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
        /// <summary>True when the effect has no defensible remaining-damage estimate (never numeric).</summary>
        public bool NoForecast;
        /// <summary>Remaining damage the effect will still deal to the local player, or null when unknown.</summary>
        public Func<StatusEffect, float?> PendingDamage;
        /// <summary>Remaining seconds for effects whose timer is not the status effect's own ttl (e.g. cooldowns).</summary>
        public Func<StatusEffect, float?> Timer;
    }

    /// <summary>
    /// Snapshot of one resource pool for the HUD (energy shield, reserves, conversions).
    /// PROVISIONAL until M1: <c>Skin.Supports("resource.render")</c> is false and the shape may still change with an
    /// ApiVersion bump. NaN or inconsistent values make RuneSkin hide the extension, never show stale values.
    /// </summary>
    public struct ResourceState
    {
        /// <summary>False = provider has nothing to show (pool hidden).</summary>
        public bool Valid;
        /// <summary>Current amount, in <see cref="Units"/>.</summary>
        public float Current;
        /// <summary>Usable maximum B (after reservations).</summary>
        public float Base;
        /// <summary>Total capacity C (before reservations), &gt;= Base.</summary>
        public float Cap;
        /// <summary>Unit name for numbers (e.g. "hp", "eitr").</summary>
        public string Units;
        /// <summary>Pool this one converts into (e.g. "health"), or null.</summary>
        public string ConversionTarget;
        /// <summary>Conversion rate per second into <see cref="ConversionTarget"/>.</summary>
        public float ConversionRate;
        /// <summary>True while regeneration of this pool is blocked.</summary>
        public bool RegenBlocked;
        /// <summary>True while the pool absorbs incoming damage.</summary>
        public bool Absorbing;
        /// <summary>Seconds until recharge starts again (0 = recharging now).</summary>
        public float RechargeDelay;
        /// <summary><c>UnityEngine.Time.time</c> when the snapshot was taken.</summary>
        public float Timestamp;
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
    }
}
