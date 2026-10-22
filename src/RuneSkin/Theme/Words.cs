using System;
using System.Collections.Generic;
using HarmonyLib;

namespace RuneSkin.Theme
{
    // RuneSkin's own texts as game localization keys ("$runeskin_*"): added to Localization's table whenever a language
    // is set up (Localization.SetupLanguage, Localization.cs:506, called by the constructor and SetLanguage), so every
    // "$runeskin_*" string resolves through Localization.Localize like a vanilla key. English and Russian; any other
    // language gets English. RuneSkin ships no CSV files.
    public static class Words
    {
        // key (without '$') -> { English, Russian }
        public static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            { "runeskin_slot_head", new[] { "Helmet", "Шлем" } },
            { "runeskin_slot_chest", new[] { "Chest armour", "Броня" } },
            { "runeskin_slot_legs", new[] { "Legs", "Поножи" } },
            { "runeskin_slot_back", new[] { "Back (cape or backpack)", "Спина (плащ или рюкзак)" } },
            { "runeskin_slot_belt", new[] { "Belt", "Пояс" } },
            { "runeskin_slot_neck", new[] { "Necklace", "Ожерелье" } },
            { "runeskin_slot_ring", new[] { "Ring", "Кольцо" } },
            { "runeskin_slot_light", new[] { "Light", "Источник света" } },
            { "runeskin_slot_right", new[] { "Right hand", "Правая рука" } },
            { "runeskin_slot_left", new[] { "Left hand", "Левая рука" } },
            { "runeskin_slot_ammo", new[] { "Ammo", "Боеприпасы" } },
            { "runeskin_mirror_hint", new[] { "Equipped from inventory. Drop an item here to equip it, right-click to unequip.", "Экипировано из инвентаря. Перетащите предмет сюда, чтобы надеть; ПКМ - снять." } },
            { "runeskin_action_take_all", new[] { "Take all", "Взять всё" } },
            { "runeskin_action_stack_all", new[] { "Stack matching", "Сложить похожее" } },
            { "runeskin_eitr_to_hp", new[] { "EITR → HP", "ЭЙТР → ЗДОРОВЬЕ" } },
            { "runeskin_eitr_to_shield", new[] { "EITR → SHIELD", "ЭЙТР → ЩИТ" } },
            // Large map legend (mockup m4-map-a). Pin types = Minimap.PinType (Minimap.cs enum).
            { "runeskin_map_pins", new[] { "Pins", "Метки" } },
            { "runeskin_map_players", new[] { "Players", "Игроки" } },
            { "runeskin_map_zoom", new[] { "Zoom", "Масштаб" } },
            // Key labels stay Latin in every language (user rule: key hints English). Minimap.cs: OnMapDblClick adds a
            // pin, OnMapLeftClick checks it, the right-click handler (:547) removes it, OnMapMiddleClick pings.
            { "runeskin_map_hint", new[] { "[2x LMB] place pin   [LMB] check   [RMB] remove   [MMB] ping   [Wheel] zoom", "[2x LMB] метка   [LMB] отметить   [RMB] удалить   [MMB] сигнал   [Wheel] масштаб" } },
            { "runeskin_pin_icon0", new[] { "Fire", "Огонь" } },
            { "runeskin_pin_icon1", new[] { "Home", "Дом" } },
            { "runeskin_pin_icon2", new[] { "Hammer", "Молот" } },
            { "runeskin_pin_icon3", new[] { "Point", "Точка" } },
            { "runeskin_pin_icon4", new[] { "Cave", "Пещера" } },
            { "runeskin_pin_death", new[] { "Death", "Смерть" } },
            { "runeskin_pin_boss", new[] { "Boss", "Босс" } },
            { "runeskin_pin_ping", new[] { "Ping", "Сигнал" } },
            { "runeskin_pin_mod", new[] { "Mod pins", "Метки модов" } },
            // Pre-world (M5).
            { "runeskin_characters", new[] { "Characters", "Персонажи" } },
        };

        public static void Apply(Localization loc, string language)
        {
            if (loc == null) return;
            var ru = string.Equals(language, "Russian", StringComparison.Ordinal);
            foreach (var pair in Table)
                loc.AddWord(pair.Key, ru ? pair.Value[1] : pair.Value[0]);
        }

        // Plugin.Awake: a Localization built before the patch (another plugin touched Localization.instance) gets the
        // words for its current language. Localization.m_instance is read directly: the instance getter would build one.
        public static void ApplyExisting()
        {
            var loc = Localization.m_instance;
            if (loc != null) Apply(loc, loc.GetSelectedLanguage());
        }

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return Localization.instance != null ? Localization.instance.Localize(key) : key;
        }
    }

    [HarmonyPatch]
    public static class WordsPatch
    {
        [HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
        [HarmonyPostfix]
        private static void SetupLanguage_Postfix(Localization __instance, string language) => Words.Apply(__instance, language);
    }
}
