using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace RuneSkin.PlayerHud
{
    // One belt cell as RuneSkin reads it: field-for-field copy of MorgottTweaks.BeltSlotState (docs\belt-api.md section
    // 2), so no assembly reference. Item is READ ONLY and valid this frame only.
    public struct BeltCell
    {
        public bool Valid;
        public int Kind;
        public int Index;
        public bool Unlocked;
        public bool GearGranted;
        public ItemDrop.ItemData Item;
        public string ItemName;
        public Sprite Icon;
        public int Count;
        public bool Auto;
        public bool Active;
        public string ActiveName;
        public Sprite ActiveIcon;
        public float ActiveRemaining;
        public float ActiveTotal;
        public float CooldownRemaining;
        public float CooldownTotal;
        public bool Blocked;
        public string BlockedReason;
        public string HotkeyLabel;
    }

    // Soft binding to MorgottTweaks.Belt (Belt API v1, MorgottTweaks docs\belt-api.md): bound once to compiled
    // delegates (no per-frame reflection, no allocation per read), every call main thread / local player. MT owns every
    // rule and all state; RuneSkin only reads cells and forwards presses (HandleClick and the gamepad commands).
    // Missing MT / older MT without Belt / another major ApiVersion = not Bound; the strip then keeps its old sources.
    public static class BeltBridge
    {
        private delegate bool TryGetFn(Player player, int kind, int index, out BeltCell cell);

        private static bool _tried;
        private static Func<bool> _enabled;
        private static Func<Player, int, int> _capacity;
        private static Func<int, int> _base;
        private static Func<int> _version;
        private static TryGetFn _tryGet;
        private static Func<int, int, string> _tooltip;
        private static Func<int, int, int, bool> _click;
        private static Func<int, int, bool> _toggle, _use, _drop, _moveOut;
        private static Func<int, int, int, bool> _beginDrag;

        public static bool Bound { get; private set; }

        // Belt hooked ([Belt] Enabled): the wings show belt cells. False = draw nothing belt-related.
        public static bool On
        {
            get
            {
                Bind();
                if (!Bound) return false;
                try { return _enabled(); }
                catch (Exception e) { return Fail("Enabled", e); }
            }
        }

        // Change counter: layout / content / binding / config changes (timers do not bump it).
        public static int Version => Bound ? _version() : 0;

        public static int Capacity(Player p, int kind) => Bound && p ? BeltMath.Clamp(_capacity(p, kind)) : 0;

        public static int Base(int kind) => Bound ? _base(kind) : 0;

        public static bool TryGet(Player p, int kind, int index, out BeltCell cell)
        {
            cell = default;
            if (!Bound || !p) return false;
            try { return _tryGet(p, kind, index, out cell) && cell.Valid; }
            catch (Exception e) { return Fail("TryGetSlot", e); }
        }

        public static string Tooltip(int kind, int index) => Bound ? _tooltip(kind, index) ?? "" : "";

        // Mouse press on a cell: button 0 = LMB, 1 = RMB; MT reads the modifiers itself (Shift / Ctrl / LeftAlt).
        public static bool Click(int kind, int index, int button) => Bound && Guard("HandleClick", () => _click(kind, index, button));

        public static bool ToggleAuto(int kind, int index) => Bound && Guard("ToggleAuto", () => _toggle(kind, index));

        public static bool UseNow(int kind, int index) => Bound && Guard("UseNow", () => _use(kind, index));

        public static bool DropDragged(int kind, int index) => Bound && Guard("DropDragged", () => _drop(kind, index));

        public static bool MoveToInventory(int kind, int index) => Bound && Guard("MoveToInventory", () => _moveOut(kind, index));

        public static bool BeginDrag(int kind, int index, int amount) => Bound && Guard("BeginDrag", () => _beginDrag(kind, index, amount));

        public static void Bind()
        {
            if (_tried) return;
            _tried = true;
            var belt = Compat.TypeLookup.ByName("MorgottTweaks.Belt");
            if (belt == null) return;
            try
            {
                var api = belt.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static);
                var version = api != null ? Convert.ToInt32(api.GetValue(null)) : -1;
                if (!BeltMath.ApiUsable(version))
                {
                    Plugin.Log.LogWarning($"[RuneSkin] MorgottTweaks Belt API {version} != {BeltMath.SupportedApi}: belt row off.");
                    return;
                }
                var kindType = Compat.TypeLookup.ByName("MorgottTweaks.BeltKind");
                var stateType = Compat.TypeLookup.ByName("MorgottTweaks.BeltSlotState");
                if (kindType == null || stateType == null) return;
                _enabled = Getter<bool>(belt, "Enabled");
                _version = Getter<int>(belt, "Version");
                _capacity = Build<Func<Player, int, int>>(belt, "Capacity", kindType, typeof(Player), typeof(int));
                _base = Build<Func<int, int>>(belt, "Base", kindType, typeof(int));
                _tooltip = Build<Func<int, int, string>>(belt, "Tooltip", kindType, typeof(int), typeof(int));
                _click = Build<Func<int, int, int, bool>>(belt, "HandleClick", kindType, typeof(int), typeof(int), typeof(int));
                _toggle = Build<Func<int, int, bool>>(belt, "ToggleAuto", kindType, typeof(int), typeof(int));
                _use = Build<Func<int, int, bool>>(belt, "UseNow", kindType, typeof(int), typeof(int));
                _drop = Build<Func<int, int, bool>>(belt, "DropDragged", kindType, typeof(int), typeof(int));
                _moveOut = Build<Func<int, int, bool>>(belt, "MoveToInventory", kindType, typeof(int), typeof(int));
                _beginDrag = Build<Func<int, int, int, bool>>(belt, "BeginDrag", kindType, typeof(int), typeof(int), typeof(int));
                _tryGet = BuildTryGet(belt, kindType, stateType);
                Bound = _enabled != null && _version != null && _capacity != null && _base != null && _tooltip != null &&
                        _click != null && _toggle != null && _use != null && _drop != null && _moveOut != null &&
                        _beginDrag != null && _tryGet != null;
                Plugin.Log.LogInfo($"[RuneSkin] MorgottTweaks Belt API {version}: bound={Bound}");
            }
            catch (Exception e)
            {
                Bound = false;
                Plugin.Log.LogWarning($"[RuneSkin] MorgottTweaks Belt bind failed, belt row off: {e.Message}");
            }
        }

        private static Func<T> Getter<T>(Type t, string name)
        {
            var m = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
            return m != null && m.ReturnType == typeof(T) ? (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), m, false) : null;
        }

        // Static method `name` whose BeltKind parameter is passed as int (Expression.Convert int -> enum); every other
        // parameter keeps its type. `pars` = the delegate's parameters in order, the kind is the first int one.
        private static T Build<T>(Type t, string name, Type kindType, params Type[] pars) where T : Delegate
        {
            var types = new Type[pars.Length];
            var kindAt = -1;
            for (var i = 0; i < pars.Length; i++)
            {
                types[i] = pars[i];
                if (kindAt < 0 && pars[i] == typeof(int))
                {
                    kindAt = i;
                    types[i] = kindType;
                }
            }
            var m = t.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, types, null);
            if (m == null) return null;
            var ps = new ParameterExpression[pars.Length];
            var args = new Expression[pars.Length];
            for (var i = 0; i < pars.Length; i++)
            {
                ps[i] = Expression.Parameter(pars[i], "a" + i);
                args[i] = i == kindAt ? Expression.Convert(ps[i], kindType) : (Expression)ps[i];
            }
            // the delegate's Invoke found by scanning, as StatsSources.Make does: PatchCheck cannot resolve a literal-name
            // lookup on a generic T ("T::Invoke type not found", run 20261029-041404); no lookup runs without MT's Belt
            var invoke = Array.Find(typeof(T).GetMethods(), mi => mi.Name == nameof(Action.Invoke));
            Expression body = Expression.Call(m, args);
            if (body.Type != invoke.ReturnType) return null;
            return Expression.Lambda<T>(body, ps).Compile();
        }

        // TryGetSlot(Player, BeltKind, int, out BeltSlotState) -> TryGetFn: the MT struct copied field by field (same
        // name + type; Kind as int) into BeltCell. A field missing on either side is skipped (additive API changes).
        private static TryGetFn BuildTryGet(Type t, Type kindType, Type stateType)
        {
            var m = t.GetMethod("TryGetSlot", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Player), kindType, typeof(int), stateType.MakeByRefType() }, null);
            if (m == null || m.ReturnType != typeof(bool)) return null;
            var player = Expression.Parameter(typeof(Player), "p");
            var kind = Expression.Parameter(typeof(int), "k");
            var index = Expression.Parameter(typeof(int), "i");
            var cell = Expression.Parameter(typeof(BeltCell).MakeByRefType(), "cell");
            var state = Expression.Variable(stateType, "s");
            var ok = Expression.Variable(typeof(bool), "ok");
            var binds = new List<MemberBinding>();
            foreach (var dst in typeof(BeltCell).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var src = stateType.GetField(dst.Name, BindingFlags.Public | BindingFlags.Instance);
                if (src == null) continue;
                Expression read = Expression.Field(state, src);
                if (src.FieldType == kindType && dst.FieldType == typeof(int)) read = Expression.Convert(read, typeof(int));
                else if (src.FieldType != dst.FieldType) continue;
                binds.Add(Expression.Bind(dst, read));
            }
            var body = Expression.Block(new[] { state, ok },
                Expression.Assign(ok, Expression.Call(m, player, Expression.Convert(kind, kindType), index, state)),
                Expression.Assign(cell, Expression.MemberInit(Expression.New(typeof(BeltCell)), binds)),
                ok);
            return Expression.Lambda<TryGetFn>(body, player, kind, index, cell).Compile();
        }

        private static bool Guard(string what, Func<bool> call)
        {
            try { return call(); }
            catch (Exception e) { return Fail(what, e); }
        }

        // A throwing MT member switches the whole row off (logged once) instead of throwing every frame.
        private static bool Fail(string what, Exception e)
        {
            Bound = false;
            Plugin.Log.LogWarning($"[RuneSkin] MorgottTweaks Belt.{what} threw, belt row off: {e.GetBaseException().Message}");
            return false;
        }
    }
}
