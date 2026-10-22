namespace RuneSkin.API
{
    /// <summary>
    /// Screen family names for <see cref="Skin.IsOwned"/> (ApiVersion 8). A family is the ownership unit: while RuneSkin
    /// owns one, no other skin may style its screens. Names are stable strings; an older RuneSkin answers false for a
    /// family it does not know.
    /// </summary>
    public static class SkinFamilies
    {
        /// <summary>Pause menu.</summary>
        public const string Menu = "Menu";
        /// <summary>Item and UI tooltips.</summary>
        public const string Tooltips = "Tooltips";
        /// <summary>Player HUD cluster: bars, food, status effects, hotbar placement.</summary>
        public const string Hud = "Hud";
        /// <summary>Inventory screen layout (player panel, doll, container / backpack dock).</summary>
        public const string Inventory = "Inventory";
        /// <summary>Crafting column of the inventory screen.</summary>
        public const string Crafting = "Crafting";
        /// <summary>Skills dialog.</summary>
        public const string Skills = "Skills";
        /// <summary>Trophies panel.</summary>
        public const string Trophies = "Trophies";
        /// <summary>The Almanac mod's window (restyle only).</summary>
        public const string Almanac = "Almanac";
        /// <summary>Small minimap ring and the large map screen.</summary>
        public const string Minimap = "Minimap";
        /// <summary>Enemy / boss overhead bars.</summary>
        public const string EnemyHud = "EnemyHud";
        /// <summary>Centre / top-left / unlock messages and the biome banner.</summary>
        public const string MessageHud = "MessageHud";
        /// <summary>Floating damage numbers.</summary>
        public const string DamageText = "DamageText";
        /// <summary>Pre-world screens (main menu, character, world / server select, loading).</summary>
        public const string MainMenu = "MainMenu";
        /// <summary>Settings window.</summary>
        public const string Settings = "Settings";
        /// <summary>Server password and connecting dialogs.</summary>
        public const string Connection = "Connection";
        /// <summary>Build menu (BuildUi), selected-piece strip and hovered-piece author window (M6).</summary>
        public const string BuildHud = "BuildHud";
        /// <summary>Crosshair, hover text, piece health bar and the stealth eye (M6).</summary>
        public const string Crosshair = "Crosshair";
        /// <summary>Action progress, stagger bar, event / raid line and the mount panel (M6).</summary>
        public const string ActionHud = "ActionHud";
        /// <summary>Ship HUD: speed ladder and rudder (M6).</summary>
        public const string ShipHud = "ShipHud";
        /// <summary>Key hints (keyboard plates and labels; gamepad glyph art stays) (M6).</summary>
        public const string KeyHints = "KeyHints";
        /// <summary>Black screens over the world: respawn tips, sleep and teleport (M6).</summary>
        public const string LoadScreen = "LoadScreen";
        /// <summary>Chat window, in-world chat texts and NPC / raven speech bubbles (M6).</summary>
        public const string Chat = "Chat";
        /// <summary>Small popups: achievement unlock, join code, session player list, closed captions (M6).</summary>
        public const string Popups = "Popups";
        /// <summary>Text input (signs, portal tags, pins, names) and text viewer (runestones, intro saga, raven) (M6).</summary>
        public const string TextDialogs = "TextDialogs";
        /// <summary>Compendium / lore dialog (TextsDialog) (M6).</summary>
        public const string Compendium = "Compendium";
        /// <summary>Split stack dialog (M6).</summary>
        public const string SplitDialog = "SplitDialog";
        /// <summary>Trader store (Haldor, Hildir, Bog Witch) (M6).</summary>
        public const string Store = "Store";
        /// <summary>Barber / mirror (the in-world character customisation) (M6).</summary>
        public const string Barber = "Barber";
        /// <summary>Developer panels: F5 console and F2 connect panel (M6).</summary>
        public const string DevPanels = "DevPanels";
        /// <summary>Feedback dialog (M6).</summary>
        public const string Feedback = "Feedback";
        /// <summary>Generic mod UI (M7c, ApiVersion 8): the fallback restyler for every uGUI / TMP window not owned by
        /// another family (other mods' canvases and panels), Jotunn GUIManager windows, IMGUI fonts, other mods' main
        /// menu lines.</summary>
        public const string ModUi = "ModUi";
        /// <summary>Epic Loot enchanting table window and its dialogs (M7a, ApiVersion 8).</summary>
        public const string EpicLootEnchanting = "EpicLootEnchanting";
        /// <summary>Epic Loot adventure board at Haldor (MerchantPanel: stash, gamble, treasure maps, bounties) (M7a, ApiVersion 8).</summary>
        public const string EpicLootBoard = "EpicLootBoard";
        /// <summary>Epic Loot tempering panel at Hildir (TemperPanel) (M7a, ApiVersion 8).</summary>
        public const string EpicLootTemper = "EpicLootTemper";
        /// <summary>HUD parts other mods add under the HUD root (Epic Loot ability bar / debug text, level texts) (M7b, ApiVersion 8).</summary>
        public const string ModHud = "ModHud";
        /// <summary>Vanilla inventory leftovers: achievements panel, variant dialog, dragged item (M7c, ApiVersion 8).</summary>
        public const string InventoryExtras = "InventoryExtras";
        /// <summary>Radial menus (emotes, hotbar wheel; Valheim.UI.RadialBase) (M7c, ApiVersion 8).</summary>
        public const string Radial = "Radial";
    }
}
