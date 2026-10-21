using System.Collections.Generic;

namespace RuneSkin.InventoryScreen
{
    // Tooltip names of doll cells (no visible labels on the doll). RuneSkin ships no localization files: its own keys
    // resolve here (English / Russian), anything else goes through the game's Localization (mod keys, "$item_*").
    public static class SlotNames
    {
        private static readonly Dictionary<string, string[]> Words = new Dictionary<string, string[]>
        {
            { "$runeskin_slot_head", new[] { "Helmet", "Шлем" } },
            { "$runeskin_slot_chest", new[] { "Chest armour", "Броня" } },
            { "$runeskin_slot_legs", new[] { "Legs", "Поножи" } },
            { "$runeskin_slot_back", new[] { "Back (cape or backpack)", "Спина (плащ или рюкзак)" } },
            { "$runeskin_slot_belt", new[] { "Belt", "Пояс" } },
            { "$runeskin_slot_neck", new[] { "Necklace", "Ожерелье" } },
            { "$runeskin_slot_ring", new[] { "Ring", "Кольцо" } },
            { "$runeskin_slot_light", new[] { "Light", "Источник света" } },
            { "$runeskin_slot_right", new[] { "Right hand", "Правая рука" } },
            { "$runeskin_slot_left", new[] { "Left hand", "Левая рука" } },
            { "$runeskin_slot_ammo", new[] { "Ammo", "Боеприпасы" } },
            { "$runeskin_mirror_hint", new[] { "Equipped from inventory. Drop an item here to equip it, right-click to unequip.", "Экипировано из инвентаря. Перетащите предмет сюда, чтобы надеть; ПКМ - снять." } },
            { "$runeskin_action_take_all", new[] { "Take all", "Взять всё" } },
            { "$runeskin_action_stack_all", new[] { "Stack matching", "Сложить похожее" } },
        };

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (Words.TryGetValue(key, out var w))
            {
                var ru = Localization.instance != null && Localization.instance.GetSelectedLanguage() == "Russian";
                return ru ? w[1] : w[0];
            }
            return Localization.instance != null ? Localization.instance.Localize(key) : key;
        }
    }
}
