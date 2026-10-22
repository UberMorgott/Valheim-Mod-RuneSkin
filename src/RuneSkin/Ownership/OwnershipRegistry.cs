using System.Collections.Generic;
using RuneSkin.Theme;

namespace RuneSkin.Ownership
{
    // Screen families are the ownership unit: each family is styled by exactly one skin. RuneSkin decides its set
    // once in Awake, BEFORE AugaSkin's Awake (AugaSkin soft-depends on RuneSkin and asks Skin.IsOwned before its
    // Harmony patching), and never changes it in the session: a change needs a restart, so no skin ever hands a
    // half-mutated screen back. A requested family is owned only when its theme assets passed the preflight
    // (otherwise the old skin keeps it), unless RequireAssets is off (dev runs before the art bundle exists).
    public static class OwnershipRegistry
    {
        public const string Menu = "Menu";
        public const string Tooltips = "Tooltips";
        // Player HUD cluster: health / stamina / eitr / adrenaline bars, food, status effects, hotbar placement.
        public const string Hud = "Hud";

        // Inventory screen layout A (M2): InventoryGui player panel (doll, grid, footer), container / backpack dock,
        // info header, grid cell templates. Skills/texts/trophies and dialogs stay with the other skin.
        public const string Inventory = "Inventory";

        // Crafting column (M3): InventoryGui.m_crafting subtree (station header, mode tabs, CraftingFilter row, recipe
        // list, detail, requirements, craft / repair buttons). Needs Inventory (layout A places the column).
        public const string Crafting = "Crafting";

        // Skills dialog and trophies panel (M3, skills-a / trophies-a): right-hand panels of layout A. Need Inventory.
        public const string Skills = "Skills";
        public const string Trophies = "Trophies";

        // The Almanac mod's window (RustyMods.Almanac; it replaces the trophies panel): restyle only, its layout stays.
        public const string Almanac = "Almanac";

        // M4 (live vanilla restyles, no prefab swaps): small minimap ring + large map screen, enemy / boss bars, centre /
        // top-left / biome / unlock messages, floating damage numbers.
        public const string Minimap = "Minimap";
        public const string EnemyHud = "EnemyHud";
        public const string MessageHud = "MessageHud";
        public const string DamageText = "DamageText";

        // M5 pre-world screens (restyled in place, no prefab swaps): FejdStartup's screens (main menu, character select /
        // create, world / server select, create world, server options, dialogs, loading) + the scene's UnifiedPopup; the
        // Settings window (main menu and pause menu); ZNet's password / connecting dialogs.
        public const string MainMenu = "MainMenu";
        public const string Settings = "Settings";
        public const string Connection = "Connection";

        // M6 in-game vanilla screens (restyled in place, art\README.md "M6 sprites"): build menu + selected-piece strip,
        // crosshair / hover / piece health / stealth eye, action progress / stagger / event line / mount panel, ship
        // HUD (speed ladder, rudder), key hints. Names shared with RuneSkin.API.SkinFamilies.
        public const string BuildHud = RuneSkin.API.SkinFamilies.BuildHud;
        public const string Crosshair = RuneSkin.API.SkinFamilies.Crosshair;
        public const string ActionHud = RuneSkin.API.SkinFamilies.ActionHud;
        public const string ShipHud = RuneSkin.API.SkinFamilies.ShipHud;
        public const string KeyHints = RuneSkin.API.SkinFamilies.KeyHints;

        // M6 batch 2 (restyled in place, GameScreens\): black screens (respawn / sleep / teleport), chat + speech
        // bubbles, small popups, text input / viewer (+ raven panel), compendium, split dialog.
        public const string LoadScreen = RuneSkin.API.SkinFamilies.LoadScreen;
        public const string Chat = RuneSkin.API.SkinFamilies.Chat;
        public const string Popups = RuneSkin.API.SkinFamilies.Popups;
        public const string TextDialogs = RuneSkin.API.SkinFamilies.TextDialogs;
        public const string Compendium = RuneSkin.API.SkinFamilies.Compendium;
        public const string SplitDialog = RuneSkin.API.SkinFamilies.SplitDialog;

        public static readonly string[] Known = { Menu, Tooltips, Hud, Inventory, Crafting, Skills, Trophies, Almanac, Minimap, EnemyHud, MessageHud, DamageText, MainMenu, Settings, Connection, BuildHud, Crosshair, ActionHud, ShipHud, KeyHints, LoadScreen, Chat, Popups, TextDialogs, Compendium, SplitDialog };

        private static readonly HashSet<string> Owned = new HashSet<string>();
        public static bool Frozen { get; private set; }

        public static void Freeze(IDictionary<string, bool> requested, bool requireAssets)
        {
            if (Frozen) return;
            foreach (var family in Known)
            {
                if (!requested.TryGetValue(family, out var want) || !want)
                {
                    Plugin.Log.LogInfo($"[RuneSkin] ownership {family}: not requested (config)");
                    continue;
                }
                var missing = new List<string>();
                var ready = ThemeTokens.FamilyReady(family, missing);
                if (!ready && requireAssets)
                {
                    Plugin.Log.LogWarning($"[RuneSkin] ownership {family}: NOT taken, assets missing: {string.Join(", ", missing)}");
                    continue;
                }
                Owned.Add(family);
                Plugin.Log.LogInfo(ready
                    ? $"[RuneSkin] ownership {family}: owned"
                    : $"[RuneSkin] ownership {family}: owned without assets (RequireAssets=false), missing: {string.Join(", ", missing)}");
            }
            Frozen = true;
        }

        public static bool IsOwned(string family) => Owned.Contains(family);
    }
}
