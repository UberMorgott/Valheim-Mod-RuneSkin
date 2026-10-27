using System;
using System.Reflection;
using HarmonyLib;

namespace RuneSkin.InfoScreens
{
    // Soft dependencies of the character stats window, bound once to typed delegates (no reflection per read, no
    // allocation): Epic Loot's effect sum (EpicLoot.PlayerExtensions.GetTotalActiveMagicEffectValue(Player, string,
    // float, ItemData), decompiled EL 0.14.13 PlayerExtensions.cs:65) and its incoming-damage steps
    // (EpicLoot.MagicItemEffects.ModifyResistance.ModifyIncoming / Shards.DamageReductionAtNight.ModifyIncoming,
    // run by its Character.RPC_Damage prefix), MorgottTweaks' public read APIs (FoodSlots.Max, PotionSlotCount.Max /
    // Active / Enabled, EnergyShieldInfo.TryGet) and its armor-curve resistance step (ArmorResistance.ApplyResistance).
    // Every member is null when the mod or the member is missing; callers fall back to vanilla.
    internal static class StatsSources
    {
        internal delegate bool EnergyShieldGet(Player player, out float current, out float max, out float rate, out float delay, out float breakDelay);
        internal delegate void ArmorResistanceStep(HitData hit, HitData.DamageModifiers modifiers, out HitData.DamageModifier significant, Character character);

        private static Func<Player, string, float, ItemDrop.ItemData, float> _elTotal;
        public static Action<Character, HitData> ElResistance { get; private set; }
        public static Action<Character, HitData> ElNight { get; private set; }
        public static Func<Player, int> FoodSlots { get; private set; }
        public static Func<Player, int> PotionSlots { get; private set; }
        public static Func<Player, int> PotionsActive { get; private set; }
        public static Func<bool> PotionSlotsEnabled { get; private set; }
        public static EnergyShieldGet EnergyShield { get; private set; }
        public static ArmorResistanceStep ArmorResistance { get; private set; }
        // MorgottTweaks ResourceAffixesModule pays Epic Loot's flat AddStaminaRegen / AddEitrRegen per second.
        public static bool ResourceAffixes { get; private set; }
        public static bool EpicLoot => _elTotal != null;
        public static bool MorgottTweaks => FoodSlots != null || PotionSlots != null || EnergyShield != null;

        private static bool _bound;

        public static void Bind()
        {
            if (_bound) return;
            _bound = true;
            _elTotal = Make<Func<Player, string, float, ItemDrop.ItemData, float>>("EpicLoot.PlayerExtensions", "GetTotalActiveMagicEffectValue");
            ElResistance = Make<Action<Character, HitData>>("EpicLoot.MagicItemEffects.ModifyResistance", "ModifyIncoming");
            ElNight = Make<Action<Character, HitData>>("EpicLoot.MagicItemEffects.Shards.DamageReductionAtNight", "ModifyIncoming");
            FoodSlots = Make<Func<Player, int>>("MorgottTweaks.FoodSlots", "Max");
            PotionSlots = Make<Func<Player, int>>("MorgottTweaks.PotionSlotCount", "Max");
            PotionsActive = Make<Func<Player, int>>("MorgottTweaks.PotionSlotCount", "Active");
            var enabled = RuneSkin.Compat.TypeLookup.ByName("MorgottTweaks.PotionSlotCount")?.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
            PotionSlotsEnabled = enabled != null ? (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), enabled, false) : null;
            EnergyShield = Make<EnergyShieldGet>("MorgottTweaks.EnergyShieldInfo", "TryGet");
            ArmorResistance = Make<ArmorResistanceStep>("MorgottTweaks.ArmorResistance", "ApplyResistance");
            ResourceAffixes = _elTotal != null && RuneSkin.Compat.TypeLookup.ByName("MorgottTweaks.ResourceAffixesModule") != null;
            Plugin.Log.LogInfo($"[RuneSkin] stats sources: EpicLoot={EpicLoot} resist={ElResistance != null} night={ElNight != null} " +
                               $"foodSlots={FoodSlots != null} potionSlots={PotionSlots != null} potionsActive={PotionsActive != null} " +
                               $"energyShield={EnergyShield != null} armorCurve={ArmorResistance != null}");
        }

        // Epic Loot effect total of the local player (EffectValue sum x scale), 0 without Epic Loot.
        public static float El(Player player, string effect, float scale = 1f)
        {
            if (_elTotal == null || !player) return 0f;
            try { return _elTotal(player, effect, scale, null); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[RuneSkin] stats: Epic Loot effect '{effect}' read failed, Epic Loot rows off: {e.Message}");
                _elTotal = null;
                return 0f;
            }
        }

        // Public static method `name` of `typeName` as delegate T (exact parameter list), else null.
        private static T Make<T>(string typeName, string name) where T : Delegate
        {
            var type = RuneSkin.Compat.TypeLookup.ByName(typeName);
            if (type == null) return null;
            // The delegate's signature (its Invoke method), found by scanning: no literal-name lookup on the generic T.
            var invoke = Array.Find(typeof(T).GetMethods(), m => m.Name == nameof(Action.Invoke));
            var pars = invoke.GetParameters();
            var types = new Type[pars.Length];
            for (var i = 0; i < pars.Length; i++) types[i] = pars[i].ParameterType;
            var m = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, types, null);
            return m != null && m.ReturnType == invoke.ReturnType ? (T)Delegate.CreateDelegate(typeof(T), m, false) : null;
        }
    }
}
