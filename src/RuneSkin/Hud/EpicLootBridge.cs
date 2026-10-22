using System;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using RuneSkin.API;
using UnityEngine;

namespace RuneSkin.PlayerHud
{
    // Soft bridge to Epic Loot (no reference; EpicLoot.dll decompile 2026-10-22): item rarity colour + set membership
    // for the strip's hotbar rims (ItemDataExtensions.IsMagic / GetRarityColor / IsSetItem: the rarity colours come
    // from Epic Loot's own config incl. Mythic and Ancient), and its active abilities (AbilityController on the player,
    // 3 slots: GetCurrentAbility(i), Ability.TimeUntilCooldownEnds, AbilityDef.Cooldown / IconAsset, TryActivate,
    // static GetBindingKeycode(i)) registered as RuneSkin.API ability slots. Delegates are compiled once (no per-frame
    // reflection boxing); any failure turns the bridge off for the session (vanilla look, never stale data).
    public static class EpicLootBridge
    {
        public const string Owner = "morgott.valheim.runeskin";
        private static bool _tried, _ok, _abilitiesOk;
        private static Func<ItemDrop.ItemData, bool> _isMagic, _isSet;
        private static Func<ItemDrop.ItemData, Color> _color;
        private static Type _controllerType;
        private static Func<Component, int, object> _getAbility;
        private static Func<object, float> _cooldownLeft, _cooldownTotal;
        private static Func<object, string> _iconAsset;
        private static Action<object> _activate;
        private static Func<int, string> _binding;
        private static MethodInfo _loadSprite;

        public static bool Present => Init();

        private static bool Init()
        {
            if (_tried) return _ok;
            _tried = true;
            try
            {
                var ext = AccessTools.TypeByName("EpicLoot.ItemDataExtensions");
                if (ext == null) return false;
                var item = typeof(ItemDrop.ItemData);
                _isMagic = Static<Func<ItemDrop.ItemData, bool>>(ext.GetMethod("IsMagic", new[] { item }));
                _isSet = Static<Func<ItemDrop.ItemData, bool>>(ext.GetMethod("IsSetItem", new[] { item }));
                _color = Static<Func<ItemDrop.ItemData, Color>>(ext.GetMethod("GetRarityColor", new[] { item }));
                _ok = _isMagic != null && _isSet != null && _color != null;
                InitAbilities();
                Plugin.Log.LogInfo($"[RuneSkin] Epic Loot bridge: rarity {(_ok ? "on" : "off")}, abilities {(_abilitiesOk ? "on" : "off")}");
            }
            catch (Exception e)
            {
                _ok = _abilitiesOk = false;
                Plugin.Log.LogWarning($"[RuneSkin] Epic Loot bridge off: {e.Message}");
            }
            return _ok;
        }

        private static void InitAbilities()
        {
            _controllerType = AccessTools.TypeByName("EpicLoot.Abilities.AbilityController");
            var ability = AccessTools.TypeByName("EpicLoot.Abilities.Ability");
            var def = AccessTools.TypeByName("EpicLoot.Abilities.AbilityDefinition");
            var main = AccessTools.TypeByName("EpicLoot.EpicLoot");
            if (_controllerType == null || ability == null || def == null) return;
            var c = Expression.Parameter(typeof(Component), "c");
            var i = Expression.Parameter(typeof(int), "i");
            var get = _controllerType.GetMethod("GetCurrentAbility", new[] { typeof(int) });
            _getAbility = Expression.Lambda<Func<Component, int, object>>(
                Expression.Convert(Expression.Call(Expression.Convert(c, _controllerType), get, i), typeof(object)), c, i).Compile();
            var a = Expression.Parameter(typeof(object), "a");
            var typed = Expression.Convert(a, ability);
            _cooldownLeft = Expression.Lambda<Func<object, float>>(Expression.Call(typed, ability.GetMethod("TimeUntilCooldownEnds", Type.EmptyTypes)), a).Compile();
            var defField = Expression.Field(typed, ability.GetField("AbilityDef"));
            _cooldownTotal = Expression.Lambda<Func<object, float>>(Expression.Field(defField, def.GetField("Cooldown")), a).Compile();
            _iconAsset = Expression.Lambda<Func<object, string>>(Expression.Field(defField, def.GetField("IconAsset")), a).Compile();
            _activate = Expression.Lambda<Action<object>>(Expression.Call(typed, ability.GetMethod("TryActivate", Type.EmptyTypes)), a).Compile();
            _binding = Static<Func<int, string>>(_controllerType.GetMethod("GetBindingKeycode", new[] { typeof(int) }));
            var load = main?.GetMethod("LoadAsset", BindingFlags.Public | BindingFlags.Static);
            _loadSprite = load != null && load.IsGenericMethodDefinition ? load.MakeGenericMethod(typeof(Sprite)) : null;
            _abilitiesOk = _getAbility != null && _binding != null;
        }

        private static T Static<T>(MethodInfo m) where T : Delegate =>
            m == null ? null : (T)Delegate.CreateDelegate(typeof(T), m, false);

        // Rarity rim colour of a magic item (Epic Loot's configured colour per rarity); false = mundane / no Epic Loot.
        public static bool Rarity(ItemDrop.ItemData item, out Color color, out bool setItem)
        {
            color = default;
            setItem = false;
            if (item == null || !Init()) return false;
            try
            {
                setItem = _isSet(item);
                if (!_isMagic(item)) return false;
                color = _color(item);
                return true;
            }
            catch (Exception e)
            {
                _ok = false;
                Plugin.Log.LogWarning($"[RuneSkin] Epic Loot rarity read failed, bridge off: {e.Message}");
                return false;
            }
        }

        // ---- abilities -------------------------------------------------------------------------------------------

        private static IDisposable[] _slots;
        private static readonly object[] Shown = new object[3];
        private static readonly Sprite[] Icons = new Sprite[3];
        private static readonly string[] Keys = new string[3];
        private static float _keysAt;

        // Registers Epic Loot's three ability slots through the public API (orders 100-102), once.
        public static void RegisterAbilities()
        {
            if (_slots != null || !Init() || !_abilitiesOk) return;
            _slots = new IDisposable[3];
            for (var n = 0; n < 3; n++)
            {
                var index = n;
                _slots[n] = Skin.RegisterAbilitySlot(Owner, new AbilitySlotDescriptor
                {
                    Id = "epicloot.ability." + index,
                    Order = 100 + index,
                    GetState = p => State(p, index),
                    Activate = p => Activate(p, index)
                });
            }
        }

        private static object Current(Player p, int index)
        {
            if (!p || !_abilitiesOk) return null;
            var controller = p.GetComponent(_controllerType);
            return controller ? _getAbility(controller, index) : null;
        }

        private static AbilitySlotState State(Player p, int index)
        {
            var ability = Current(p, index);
            if (ability == null) { Shown[index] = null; return default; }
            if (!ReferenceEquals(Shown[index], ability))
            {
                Shown[index] = ability;
                Icons[index] = LoadIcon(_iconAsset(ability));
            }
            if (Time.unscaledTime >= _keysAt)
            {
                _keysAt = Time.unscaledTime + 2f; // bindings change only through the config: re-read every 2 s
                for (var k = 0; k < 3; k++) Keys[k] = Label(_binding(k));
            }
            return new AbilitySlotState
            {
                Valid = true,
                Icon = Icons[index],
                Key = Keys[index],
                CooldownLeft = Mathf.Max(0f, _cooldownLeft(ability)),
                CooldownTotal = Mathf.Max(0f, _cooldownTotal(ability))
            };
        }

        private static void Activate(Player p, int index)
        {
            var ability = Current(p, index);
            if (ability != null) _activate(ability);
        }

        private static Sprite LoadIcon(string asset)
        {
            if (string.IsNullOrEmpty(asset) || _loadSprite == null) return null;
            try { return _loadSprite.Invoke(null, new object[] { asset }) as Sprite; }
            catch { return null; }
        }

        // "q" -> "Q", "mouse 3" -> "M3": short Latin label (key hints always English).
        public static string Label(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            key = key.Trim();
            if (key.StartsWith("mouse ", StringComparison.OrdinalIgnoreCase)) return "M" + key.Substring(6);
            return key.Length == 1 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key.Substring(1);
        }
    }
}
