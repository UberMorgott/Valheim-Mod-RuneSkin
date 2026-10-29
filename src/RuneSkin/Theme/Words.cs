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
            { "runeskin_action_take_all", new[] { "Take all", "Взять всё" } },
            { "runeskin_action_stack_all", new[] { "Stack matching", "Сложить похожее" } },
            { "runeskin_eitr_to_hp", new[] { "EITR → HP", "ЭЙТР → ЗДОРОВЬЕ" } },
            { "runeskin_eitr_to_shield", new[] { "EITR → SHIELD", "ЭЙТР → ЩИТ" } },
            // Reserve tooltip on the HUD bars + stats breakdown lines (auras / summons hold part of the pool).
            { "runeskin_reserve_available", new[] { "Available", "Доступно" } },
            { "runeskin_reserve_auras", new[] { "Reserved by auras", "Резерв аур" } },
            { "runeskin_reserve_summons", new[] { "Reserved by summons", "Резерв призывов" } },
            // Large map legend (mockup m4-map-a). Pin types = Minimap.PinType (Minimap.cs enum).
            { "runeskin_map_pins", new[] { "Pins", "Метки" } },
            { "runeskin_map_players", new[] { "Players", "Игроки" } },
            { "runeskin_map_zoom", new[] { "Zoom", "Масштаб" } },
            // Key labels stay Latin in every language (user rule: key hints English). Minimap.cs: OnMapDblClick adds a
            // pin, OnMapLeftClick checks it, the right-click handler (:547) removes it, OnMapMiddleClick pings.
            { "runeskin_pin_icon0", new[] { "Fire", "Огонь" } },
            { "runeskin_pin_icon1", new[] { "Home", "Дом" } },
            { "runeskin_pin_icon2", new[] { "Hammer", "Молот" } },
            { "runeskin_pin_icon3", new[] { "Point", "Точка" } },
            { "runeskin_pin_icon4", new[] { "Cave", "Пещера" } },
            { "runeskin_pin_death", new[] { "Death", "Смерть" } },
            { "runeskin_pin_boss", new[] { "Boss", "Босс" } },
            { "runeskin_pin_ping", new[] { "Ping", "Сигнал" } },
            { "runeskin_pin_mod", new[] { "Mod pins", "Метки модов" } },
            // Window header titles (RuneWindowHeader) of windows without a title of their own.
            { "runeskin_win_crafting", new[] { "Crafting", "Ремесло" } },
            { "runeskin_win_chest", new[] { "Chest", "Сундук" } },
            { "runeskin_win_backpack", new[] { "Backpack", "Рюкзак" } },
            { "runeskin_win_sockets", new[] { "Shard slots", "Ячейки камней" } },
            { "runeskin_win_inventory", new[] { "Inventory", "Инвентарь" } },
            { "runeskin_layout_reset", new[] { "Reset window layout", "Вернуть окна на места" } },
            { "runeskin_win_character", new[] { "Character", "Персонаж" } },
            { "runeskin_win_map", new[] { "Map", "Карта" } },
            { "runeskin_win_texts", new[] { "Compendium", "Компендиум" } },
            { "runeskin_win_achievements", new[] { "Achievements", "Достижения" } },
            { "runeskin_win_skills", new[] { "Skills", "Навыки" } },
            { "runeskin_win_trophies", new[] { "Trophies", "Трофеи" } },
            // Pre-world (M5).
            { "runeskin_characters", new[] { "Characters", "Персонажи" } },
            // Summon frames (batch P): status line under the creature icon; {0} = seconds.
            { "runeskin_summon_spawning", new[] { "Spawning", "Призыв" } },
            { "runeskin_summon_respawn", new[] { "Respawn {0}s", "Возврат {0}с" } },
            { "runeskin_summon_off", new[] { "Off", "Выкл" } },
            { "runeskin_summon_noreserve", new[] { "No reserve", "Нет резерва" } },
            { "runeskin_summon_waiting", new[] { "Respawning", "Возврат" } },
            { "runeskin_summon_stay", new[] { "Stay", "Стоит" } },
            // Party frame tooltip (0.20.3): weakest unit's health ({0}), combat modes (mt_sum_ui flags bit 1 / 2).
            { "runeskin_party_weakest", new[] { "Weakest: {0}", "Слабейший: {0}" } },
            { "runeskin_party_defensive", new[] { "Defensive", "Оборона" } },
            { "runeskin_party_aggressive", new[] { "Aggressive", "Атака" } },
            // Character stats window (batch P, stats-full-b): card titles, row labels, units.
            { "runeskin_stats_title", new[] { "Character stats", "Характеристики" } },
            { "runeskin_stats_resources", new[] { "Resources", "Ресурсы" } },
            { "runeskin_stats_resists", new[] { "Resistances", "Сопротивления" } },
            { "runeskin_stats_movement", new[] { "Movement and weight", "Передвижение и вес" } },
            { "runeskin_stats_defense", new[] { "Defense", "Защита" } },
            { "runeskin_stats_food", new[] { "Food", "Еда" } },
            { "runeskin_stats_rest", new[] { "Rest", "Отдых" } },
            { "runeskin_stats_combat", new[] { "Battle enchantments", "Боевые чары" } },
            { "runeskin_stats_mt", new[] { "Ward", "Оберег" } },
            { "runeskin_stats_health", new[] { "Health", "Здоровье" } },
            { "runeskin_stats_health_regen", new[] { "Health regen", "Реген. здоровья" } },
            { "runeskin_stats_stamina", new[] { "Stamina", "Выносливость" } },
            { "runeskin_stats_stamina_regen", new[] { "Stamina regen", "Реген. выносливости" } },
            { "runeskin_stats_eitr", new[] { "Eitr", "Эйтр" } },
            { "runeskin_stats_eitr_regen", new[] { "Eitr regen", "Реген. эйтра" } },
            { "runeskin_stats_per_second", new[] { "/s", "/с" } },
            { "runeskin_stats_sec", new[] { "s", "с" } },
            { "runeskin_stats_tick_in", new[] { "tick in", "тик через" } },
            { "runeskin_stats_upto", new[] { "up to", "до" } },
            { "runeskin_stats_when_empty", new[] { "when empty", "при пустой" } },
            { "runeskin_stats_jog", new[] { "Walk speed", "Скорость ходьбы" } },
            { "runeskin_stats_run", new[] { "Run speed", "Скорость бега" } },
            { "runeskin_stats_swim", new[] { "Swim speed", "Скорость плавания" } },
            { "runeskin_stats_weight", new[] { "Carry weight", "Переносимый вес" } },
            { "runeskin_stats_run_drain", new[] { "Run stamina use", "Расход на бег" } },
            { "runeskin_stats_dodge", new[] { "Dodge cost", "Цена уклонения" } },
            { "runeskin_stats_jump", new[] { "Jump cost", "Цена прыжка" } },
            { "runeskin_stats_armor", new[] { "Armor", "Броня" } },
            { "runeskin_stats_reduction", new[] { "reduction", "снижение" } },
            { "runeskin_stats_block", new[] { "Block", "Блок" } },
            { "runeskin_stats_parry", new[] { "Parry", "Парирование" } },
            { "runeskin_stats_hp_short", new[] { "HP", "ОЗ" } },
            { "runeskin_stats_st_short", new[] { "ST", "ВН" } },
            { "runeskin_stats_ei_short", new[] { "EI", "ЭЙ" } },
            { "runeskin_stats_regen_short", new[] { "regen", "реген" } },
            { "runeskin_stats_food_slots", new[] { "Food slots", "Слоты еды" } },
            { "runeskin_stats_potion_slots", new[] { "Potion slots", "Слоты зелий" } },
            { "runeskin_stats_comfort", new[] { "Comfort", "Комфорт" } },
            { "runeskin_stats_rested", new[] { "Rested", "Отдохнувший" } },
            { "runeskin_stats_left", new[] { "left", "осталось" } },
            { "runeskin_stats_not_rested", new[] { "no", "нет" } },
            { "runeskin_stats_unarmed", new[] { "Unarmed", "Без оружия" } },
            { "runeskin_stats_damage", new[] { "Damage", "Урон" } },
            { "runeskin_stats_attack_speed", new[] { "Attack speed", "Скорость атаки" } },
            { "runeskin_stats_backstab", new[] { "Backstab", "Удар в спину" } },
            { "runeskin_stats_double", new[] { "Double damage chance", "Шанс двойного урона" } },
            { "runeskin_stats_lifesteal", new[] { "Life steal", "Вампиризм" } },
            { "runeskin_stats_stagger", new[] { "Stagger", "Ошеломление" } },
            { "runeskin_stats_speed_short", new[] { "speed", "скорость" } },
            { "runeskin_stats_weight_short", new[] { "weight", "вес" } },
            { "runeskin_stats_es", new[] { "Ward strength", "Сила оберега" } },
            { "runeskin_stats_dps", new[] { "Damage per second", "Урон в секунду" } },
            { "runeskin_stats_swim_drain", new[] { "Swim cost", "Расход в воде" } },
            { "runeskin_stats_block_stamina", new[] { "Block cost", "Цена блока" } },
            { "runeskin_stats_attack_stamina", new[] { "Swing cost", "Цена удара" } },
            { "runeskin_stats_secondary", new[] { "Secondary attack", "Вторая атака" } },
            { "runeskin_bd_base", new[] { "Base", "База" } },
            { "runeskin_bd_total", new[] { "Total", "Итого" } },
            { "runeskin_bd_other", new[] { "Enchantments and other", "Чары и прочее" } },
            { "runeskin_bd_enchant", new[] { "Enchantments", "Чары" } },
            { "runeskin_bd_skill", new[] { "Skill", "Навык" } },
            { "runeskin_bd_quality", new[] { "Item level", "Уровень предмета" } },
            { "runeskin_bd_world", new[] { "World settings", "Настройки мира" } },
            { "runeskin_bd_world_level", new[] { "World level", "Уровень мира" } },
            { "runeskin_bd_low_fill", new[] { "Low reserve bonus", "Бонус при низком запасе" } },
            { "runeskin_bd_busy", new[] { "Attacking / dodging", "Атака / уклонение" } },
            { "runeskin_bd_blocking", new[] { "Blocking", "Блок" } },
            { "runeskin_bd_regen_delay", new[] { "Pauses briefly after spending", "Пауза после траты" } },
            { "runeskin_bd_no_food_regen", new[] { "No food heals now", "Еда сейчас не лечит" } },
            { "runeskin_bd_tick_note", new[] { "Heals once every 10 s", "Лечит раз в 10 с" } },
            { "runeskin_bd_no_eitr", new[] { "No eitr (eat eitr food)", "Нет эйтра (нужна еда с эйтром)" } },
            { "runeskin_bd_reference_hit", new[] { "Reference hit", "Пробный удар" } },
            { "runeskin_bd_modifier", new[] { "Modifier", "Модификатор" } },
            { "runeskin_bd_resistance", new[] { "Resistance", "Сопротивление" } },
            { "runeskin_bd_taken", new[] { "Damage taken", "Получено урона" } },
            { "runeskin_bd_reduction", new[] { "Reduction", "Снижение" } },
            { "runeskin_bd_carried", new[] { "Carried now", "Сейчас несёте" } },
            { "runeskin_bd_no_blocker", new[] { "Nothing to block with", "Нечем блокировать" } },
            { "runeskin_bd_no_parry", new[] { "This item cannot parry", "Этим предметом не парировать" } },
            { "runeskin_bd_parry_bonus", new[] { "Parry bonus", "Бонус парирования" } },
            { "runeskin_bd_perfect_block", new[] { "Perfect block", "Идеальный блок" } },
            { "runeskin_bd_block_note", new[] { "Per fully blocked hit", "За полностью отбитый удар" } },
            { "runeskin_bd_no_weapon", new[] { "No weapon", "Нет оружия" } },
            { "runeskin_bd_weapon_base", new[] { "Weapon", "Оружие" } },
            { "runeskin_bd_per_hit", new[] { "Per hit", "За удар" } },
            { "runeskin_bd_damage_note", new[] { "Before the target's armor and resistances", "До брони и сопротивлений цели" } },
            { "runeskin_bd_mean_hit", new[] { "Average hit", "Средний удар" } },
            { "runeskin_bd_no_timing", new[] { "Swing time unknown yet: swing once", "Время удара пока неизвестно: ударьте раз" } },
            { "runeskin_bd_swing", new[] { "Swing time", "Время удара" } },
            { "runeskin_bd_measured", new[] { "measured", "замер" } },
            { "runeskin_bd_animation", new[] { "animation", "анимация" } },
            { "runeskin_bd_dps_note", new[] { "Estimate: primary attack, no misses", "Оценка: основная атака без промахов" } },
            { "runeskin_bd_attack_mult", new[] { "Attack multiplier", "Множитель атаки" } },
            { "runeskin_bd_stamina_base", new[] { "Stamina per swing", "Выносливость за удар" } },
            { "runeskin_stats_rested_in", new[] { "rested in", "отдых через" } },
            { "runeskin_stats_comfort_here", new[] { "here:", "здесь:" } },
            { "runeskin_stats_shelter", new[] { "Shelter", "Укрытие" } },
            { "runeskin_stats_regen_stamina_short", new[] { "stamina regen", "реген. выносливости" } },
            { "runeskin_stats_more", new[] { "+{0} more", "+{0} ещё" } },
            { "runeskin_stats_recharge", new[] { "Recharge", "Перезарядка" } },
            { "runeskin_stats_after", new[] { "after", "через" } },
            { "runeskin_stats_break_delay", new[] { "Delay after break", "Задержка после пробоя" } },
            { "runeskin_stats_potency", new[] { "Potion effect", "Эффект зелий" } },
            // Enchanting table tab tooltip while the feature is locked (ElTabCell): where it is unlocked.
            { "runeskin_el_tab_locked", new[] { "Locked: unlock it on the Upgrade tab", "Закрыто: открывается во вкладке «Улучшить»" } },
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

        // Localization.Localize builds its result in a new StringBuilder per call (Localization.cs): per-frame callers
        // (map legend labels) read a cache instead, cleared whenever a language is set up. Bounded (texts of mods'
        // buttons are keys too).
        private static readonly Dictionary<string, string> Localized = new Dictionary<string, string>();
        public const int MaxLocalized = 1024;

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (Localization.instance == null) return key;
            if (Localized.TryGetValue(key, out var text)) return text;
            text = Localization.instance.Localize(key);
            // "[key]" = not translated yet (a mod may add its words later): asked again next time.
            if (text.StartsWith("[", StringComparison.Ordinal)) return text;
            if (Localized.Count >= MaxLocalized) Localized.Clear();
            Localized[key] = text;
            return text;
        }

        internal static void ClearCache() => Localized.Clear();
    }

    [HarmonyPatch]
    public static class WordsPatch
    {
        [HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
        [HarmonyPostfix]
        private static void SetupLanguage_Postfix(Localization __instance, string language)
        {
            Words.ClearCache();
            Words.Apply(__instance, language);
        }
    }
}
