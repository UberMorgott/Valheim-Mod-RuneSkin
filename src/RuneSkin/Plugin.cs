using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using RuneSkin.API;
using RuneSkin.Api;
using RuneSkin.Diagnostics;
using RuneSkin.Ownership;
using RuneSkin.Responsive;
using RuneSkin.Screens;
using RuneSkin.Theme;

namespace RuneSkin
{
    // RuneSkin: clean-room UI skin that restyles live vanilla uGUI from theme tokens (no prefab swaps, no transpilers).
    // Bootstrap order in Awake: config -> theme + asset bundle -> ownership frozen (before AugaSkin's Awake, which
    // soft-depends on this plugin) -> API host attached (replays early registrations) -> Harmony patches of owned
    // families only.
    [BepInPlugin(Skin.PluginGuid, "RuneSkin", Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Version = "0.13.0";

        public static ManualLogSource Log { get; private set; }
        public static ConfigEntry<float> SkinScale { get; private set; }
        public static ConfigEntry<bool> CharacterPreview { get; private set; }
        public static Host ApiHost { get; private set; }
        public static ConfigEntry<float> HudScale { get; private set; }
        public static ConfigEntry<float> MinimapScale { get; private set; }
        public static ConfigEntry<bool> UltrawideBox { get; private set; }
        public static ConfigEntry<KeyboardShortcut> StatusListKey { get; private set; }
        public static ConfigEntry<string> StatusListButton { get; private set; }

        private static ConfigEntry<bool> _ownMenu, _ownTooltips, _ownHud, _ownInventory, _ownCrafting, _ownSkills, _ownTrophies, _ownAlmanac, _requireAssets;
        private static ConfigEntry<bool> _ownMinimap, _ownEnemyHud, _ownMessageHud, _ownDamageText, _ownMainMenu, _ownSettings, _ownConnection;
        private static ConfigEntry<bool> _ownBuildHud, _ownCrosshair, _ownActionHud, _ownShipHud, _ownKeyHints;
        private static ConfigEntry<bool> _ownLoadScreen, _ownChat, _ownPopups, _ownTextDialogs, _ownCompendium, _ownSplitDialog;
        private static ConfigEntry<bool> _ownStore, _ownBarber, _ownDevPanels, _ownFeedback;
        private static ConfigEntry<bool> _ownModUi, _ownElEnchanting, _ownElBoard, _ownElTemper, _ownModHud, _ownInventoryExtras, _ownRadial;
        private static ConfigEntry<bool> _ownMenuCompendium, _ownSummonFrames, _ownPlayerShields, _ownCursor;
        private static ConfigEntry<string> _modUiOptOut;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _ownMenu = Config.Bind("Ownership", "Menu", true,
                "RuneSkin styles the pause menu. Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownTooltips = Config.Bind("Ownership", "Tooltips", true,
                "RuneSkin styles tooltips. Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownHud = Config.Bind("Ownership", "Hud", true,
                "RuneSkin draws the player HUD (bars, food, status effects, hotbar placement). Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownInventory = Config.Bind("Ownership", "Inventory", true,
                "RuneSkin lays out the inventory screen (layout A: doll, grid, chest / backpack dock, crafting placement). Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownCrafting = Config.Bind("Ownership", "Crafting", true,
                "RuneSkin styles and lays out the crafting column (station header, tabs, recipe list, detail, requirements). Needs Inventory. Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownSkills = Config.Bind("Ownership", "Skills", true,
                "RuneSkin styles and lays out the skills dialog (needs Inventory). Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownTrophies = Config.Bind("Ownership", "Trophies", true,
                "RuneSkin styles and lays out the trophies panel (needs Inventory). Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownAlmanac = Config.Bind("Ownership", "Almanac", true,
                "RuneSkin restyles the Almanac mod's window (its layout and content stay Almanac's). Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownMinimap = Config.Bind("Ownership", "Minimap", true,
                "RuneSkin draws the small minimap as a round rune ring (compass runes, wind, day phase, biome plaque) and restyles the large map screen. Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownEnemyHud = Config.Bind("Ownership", "EnemyHud", true,
                "RuneSkin restyles enemy health bars, star levels and the boss bar (Vaettir rarity frames keep their tiers). Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownMessageHud = Config.Bind("Ownership", "MessageHud", true,
                "RuneSkin restyles centre messages, top-left notifications, unlock messages and the biome-discovered banner. Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownDamageText = Config.Bind("Ownership", "DamageText", true,
                "RuneSkin restyles floating damage numbers (font, outline, colour per hit type). Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownMainMenu = Config.Bind("Ownership", "MainMenu", true,
                "RuneSkin styles the pre-world screens (main menu, character select / create, world and server select, dialogs, loading). Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownSettings = Config.Bind("Ownership", "Settings", true,
                "RuneSkin styles the Settings window (category list, sliders, checkboxes, dropdowns, key bindings). Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownConnection = Config.Bind("Ownership", "Connection", true,
                "RuneSkin styles the server password and connecting dialogs. Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownBuildHud = Config.Bind("Ownership", "BuildHud", true,
                "RuneSkin styles the build menu, the selected-piece strip and the hovered-piece author window. Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownCrosshair = Config.Bind("Ownership", "Crosshair", true,
                "RuneSkin styles the crosshair, hover text, piece health bar and the stealth eye. Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownActionHud = Config.Bind("Ownership", "ActionHud", true,
                "RuneSkin styles the action progress and stagger bars, the event / raid line and the mount panel. Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownShipHud = Config.Bind("Ownership", "ShipHud", true,
                "RuneSkin draws the ship HUD as a speed ladder with a rudder socket (wind stays on the minimap ring). Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownKeyHints = Config.Bind("Ownership", "KeyHints", true,
                "RuneSkin styles the key hints (key plates and labels; gamepad glyphs stay). Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownLoadScreen = Config.Bind("Ownership", "LoadScreen", true,
                "RuneSkin styles the black screens over the world (respawn tip banner + rune spinner, sleep, teleport ring). Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownChat = Config.Bind("Ownership", "Chat", true,
                "RuneSkin styles the chat window, in-world chat texts and NPC / raven speech bubbles. Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownPopups = Config.Bind("Ownership", "Popups", true,
                "RuneSkin styles the small popups (achievement unlock, join code, session player list, closed captions). Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownTextDialogs = Config.Bind("Ownership", "TextDialogs", true,
                "RuneSkin styles the text input (signs, portals, pins, names) and the text viewer (runestones, intro saga, raven). Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownCompendium = Config.Bind("Ownership", "Compendium", true,
                "RuneSkin styles the compendium / lore dialog. Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownSplitDialog = Config.Bind("Ownership", "SplitDialog", true,
                "RuneSkin styles the split stack dialog. Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownStore = Config.Bind("Ownership", "Store", true,
                "RuneSkin styles the trader store (Haldor, Hildir, Bog Witch; Epic Loot's own trader panels excluded). Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownBarber = Config.Bind("Ownership", "Barber", true,
                "RuneSkin styles the barber / mirror. Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownDevPanels = Config.Bind("Ownership", "DevPanels", true,
                "RuneSkin styles the F5 console and the F2 connect panel. Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownFeedback = Config.Bind("Ownership", "Feedback", true,
                "RuneSkin styles the feedback dialog. Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownModUi = Config.Bind("Ownership", "ModUi", true,
                "RuneSkin restyles every window no other family owns (other mods' canvases and panels, Jotunn windows, IMGUI fonts, other mods' main menu lines): theme panels, controls and fonts, the mod's own layout kept. Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _modUiOptOut = Config.Bind("Ownership", "ModUiOptOut", "AugaSkin,BossAwakening,BA_WorldBossBar",
                "Comma-separated object or plugin assembly names the fallback restyler (ModUi) never touches, with everything below them (default: AugaSkin's own UI; BossAwakening's boss / raid / target bars keep their own design, styled by the EnemyHud track). Read once at startup.");
            _ownElEnchanting = Config.Bind("Ownership", "EpicLootEnchanting", true,
                "RuneSkin styles Epic Loot's enchanting table window and its dialogs. Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownElBoard = Config.Bind("Ownership", "EpicLootBoard", true,
                "RuneSkin styles Epic Loot's adventure board at Haldor (secret stash, gamble, treasure maps, bounties). Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownElTemper = Config.Bind("Ownership", "EpicLootTemper", true,
                "RuneSkin styles Epic Loot's tempering panel at Hildir. Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownModHud = Config.Bind("Ownership", "ModHud", true,
                "RuneSkin styles HUD parts other mods add (Epic Loot ability bar and debug text, level texts). Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownInventoryExtras = Config.Bind("Ownership", "InventoryExtras", true,
                "RuneSkin styles the achievements panel, the variant dialog and the dragged item. Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownRadial = Config.Bind("Ownership", "Radial", true,
                "RuneSkin styles the radial menus (texts; wheel art and icons stay). Read once at startup (restart to change); another skin (AugaSkin) yields them.");
            _ownMenuCompendium = Config.Bind("Ownership", "MenuCompendium", true,
                "RuneSkin adds a Compendium entry under Settings in the pause menu (needs Menu). Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownSummonFrames = Config.Bind("Ownership", "SummonFrames", true,
                "RuneSkin shows a frame per summon next to the hotbar (icon, health with the summon's energy shield, respawn / stay / off state). Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownPlayerShields = Config.Bind("Ownership", "PlayerShields", true,
                "RuneSkin shows other players' energy shield and absorb shield as rails under their overhead health bar (MorgottTweaks publishes them). Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            _ownCursor = Config.Bind("Ownership", "Cursor", true,
                "RuneSkin sets its rune mouse cursor. Read once at startup (restart to change); another skin (AugaSkin) yields it.");
            HudScale = Config.Bind("Hud", "Scale", 1f, new ConfigDescription(
                "HUD cluster scale on top of the game's UI scale (fonts never go below the readable floor).",
                new AcceptableValueRange<float>(0.75f, 1.5f)));
            MinimapScale = Config.Bind("Hud", "MinimapScale", 1f, new ConfigDescription(
                "Minimap ring size on top of the theme size (size.minimap.ring) and Hud.Scale; its height cap (share.minimap.ring.h) scales with it. Applied live.",
                new AcceptableValueRange<float>(0.6f, 1.6f)));
            UltrawideBox = Config.Bind("Hud", "UltrawideBox", true,
                "On 21:9 and wider screens the HUD corners are those of a centred 16:9 box (off = true screen corners).");
            StatusListKey = Config.Bind("Hud", "StatusListKey", new KeyboardShortcut(UnityEngine.KeyCode.F7),
                "Opens / closes the full list of status effects when the strip shows a +N cell.");
            StatusListButton = Config.Bind("Hud", "StatusListGamepadButton", "JoyDPadDown",
                "Gamepad: this ZInput button while JoyAltKeys is held opens / closes the full status list. Empty = none.");
            _requireAssets = Config.Bind("Ownership", "RequireAssets", true,
                "Take a family only when its theme assets loaded (otherwise the other skin keeps it). Off = take it anyway (development before the art bundle exists).");
            SkinScale = Config.Bind("Layout", "SkinScale", 1f, new ConfigDescription(
                "Extra scale of RuneSkin panels on top of the game's UI scale.",
                new AcceptableValueRange<float>(ResponsiveContext.MinSkinScale, ResponsiveContext.MaxSkinScale)));
            CharacterPreview = Config.Bind("Layout", "CharacterPreview", true,
                "Live 3D preview of your character in the inventory (off = carved silhouette). Applies on the next game start.");

            if (!ThemeTokens.Load())
            {
                Log.LogError("[RuneSkin] inactive: no valid theme");
                OwnershipRegistry.Freeze(new Dictionary<string, bool>(), true);
                return;
            }

            OwnershipRegistry.Freeze(new Dictionary<string, bool>
            {
                [OwnershipRegistry.Menu] = _ownMenu.Value,
                [OwnershipRegistry.Tooltips] = _ownTooltips.Value,
                [OwnershipRegistry.Hud] = _ownHud.Value,
                [OwnershipRegistry.Inventory] = _ownInventory.Value,
                // The crafting column lives inside layout A: without Inventory it stays with the other skin.
                [OwnershipRegistry.Crafting] = _ownCrafting.Value && _ownInventory.Value,
                [OwnershipRegistry.Skills] = _ownSkills.Value && _ownInventory.Value,
                [OwnershipRegistry.Trophies] = _ownTrophies.Value && _ownInventory.Value,
                // Restyle only, no Inventory needed. Owned whether or not Almanac is installed (the plugin load order
                // is not known here); without it the hook finds no window and does nothing.
                [OwnershipRegistry.Almanac] = _ownAlmanac.Value,
                [OwnershipRegistry.Minimap] = _ownMinimap.Value,
                [OwnershipRegistry.EnemyHud] = _ownEnemyHud.Value,
                [OwnershipRegistry.MessageHud] = _ownMessageHud.Value,
                [OwnershipRegistry.DamageText] = _ownDamageText.Value,
                [OwnershipRegistry.MainMenu] = _ownMainMenu.Value,
                [OwnershipRegistry.Settings] = _ownSettings.Value,
                [OwnershipRegistry.Connection] = _ownConnection.Value,
                [OwnershipRegistry.BuildHud] = _ownBuildHud.Value,
                [OwnershipRegistry.Crosshair] = _ownCrosshair.Value,
                [OwnershipRegistry.ActionHud] = _ownActionHud.Value,
                [OwnershipRegistry.ShipHud] = _ownShipHud.Value,
                [OwnershipRegistry.KeyHints] = _ownKeyHints.Value,
                [OwnershipRegistry.LoadScreen] = _ownLoadScreen.Value,
                [OwnershipRegistry.Chat] = _ownChat.Value,
                [OwnershipRegistry.Popups] = _ownPopups.Value,
                [OwnershipRegistry.TextDialogs] = _ownTextDialogs.Value,
                [OwnershipRegistry.Compendium] = _ownCompendium.Value,
                [OwnershipRegistry.SplitDialog] = _ownSplitDialog.Value,
                [OwnershipRegistry.Store] = _ownStore.Value,
                [OwnershipRegistry.Barber] = _ownBarber.Value,
                [OwnershipRegistry.DevPanels] = _ownDevPanels.Value,
                [OwnershipRegistry.Feedback] = _ownFeedback.Value,
                [OwnershipRegistry.ModUi] = _ownModUi.Value,
                [OwnershipRegistry.EpicLootEnchanting] = _ownElEnchanting.Value,
                [OwnershipRegistry.EpicLootBoard] = _ownElBoard.Value,
                [OwnershipRegistry.EpicLootTemper] = _ownElTemper.Value,
                [OwnershipRegistry.ModHud] = _ownModHud.Value,
                [OwnershipRegistry.InventoryExtras] = _ownInventoryExtras.Value,
                [OwnershipRegistry.Radial] = _ownRadial.Value,
                // The entry is styled by the Menu family's pass: without Menu it stays the other skin's.
                [OwnershipRegistry.MenuCompendium] = _ownMenuCompendium.Value && _ownMenu.Value,
                [OwnershipRegistry.SummonFrames] = _ownSummonFrames.Value,
                [OwnershipRegistry.PlayerShields] = _ownPlayerShields.Value,
                [OwnershipRegistry.Cursor] = _ownCursor.Value,
            }, _requireAssets.Value);

            ApiHost = new Host();
            Skin.Attach(ApiHost);

            gameObject.AddComponent<ResponsiveContext>();

            _harmony = new Harmony(Skin.PluginGuid);
            _harmony.PatchAll(typeof(ConsoleCommands));
            _harmony.PatchAll(typeof(Theme.WordsPatch));
            Theme.Words.ApplyExisting();
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Menu)) _harmony.PatchAll(typeof(MenuAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Tooltips)) _harmony.PatchAll(typeof(TooltipAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Hud)) _harmony.PatchAll(typeof(PlayerHud.HudAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Inventory)) _harmony.PatchAll(typeof(InventoryScreen.InventoryAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Inventory) && OwnershipRegistry.IsOwned(OwnershipRegistry.Crafting)) _harmony.PatchAll(typeof(CraftingScreen.CraftingAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Inventory) && OwnershipRegistry.IsOwned(OwnershipRegistry.Skills)) _harmony.PatchAll(typeof(InfoScreens.SkillsAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Inventory) && OwnershipRegistry.IsOwned(OwnershipRegistry.Trophies)) _harmony.PatchAll(typeof(InfoScreens.TrophiesAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Almanac))
            {
                ModScreens.AlmanacAdapter.Harmony = _harmony;
                _harmony.PatchAll(typeof(ModScreens.AlmanacAdapter));
            }
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Minimap)) _harmony.PatchAll(typeof(MapScreen.MinimapAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.EnemyHud)) _harmony.PatchAll(typeof(WorldHud.EnemyHudAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.MessageHud)) _harmony.PatchAll(typeof(WorldHud.MessageAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.DamageText)) _harmony.PatchAll(typeof(WorldHud.DamageTextAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.MainMenu)) _harmony.PatchAll(typeof(PreWorld.MainMenuAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Settings)) _harmony.PatchAll(typeof(PreWorld.SettingsAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Connection)) _harmony.PatchAll(typeof(PreWorld.ConnectionAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.BuildHud) || OwnershipRegistry.IsOwned(OwnershipRegistry.Crosshair) || OwnershipRegistry.IsOwned(OwnershipRegistry.ActionHud) ||
                OwnershipRegistry.IsOwned(OwnershipRegistry.ShipHud) || OwnershipRegistry.IsOwned(OwnershipRegistry.KeyHints))
                _harmony.PatchAll(typeof(WorldHud.PlayHudAdapter));
            if (GameScreens.GameScreensAdapter.AnyOwned()) _harmony.PatchAll(typeof(GameScreens.GameScreensAdapter));
            if (GameScreens.TradeScreensAdapter.AnyOwned()) _harmony.PatchAll(typeof(GameScreens.TradeScreensAdapter));
            // M7: precise mod panels + the fallback restyler for every UI no family owns, Jotunn windows, IMGUI fonts,
            // vanilla leftovers.
            if (ModScreens.VanillaExtrasAdapter.AnyOwned()) _harmony.PatchAll(typeof(ModScreens.VanillaExtrasAdapter));
            if (ModScreens.MainMenuMods.Owned()) _harmony.PatchAll(typeof(ModScreens.MainMenuMods));
            ModScreens.ForeignUi.Init(gameObject, _harmony, _modUiOptOut.Value);
            ModScreens.JotunnHook.Init(_harmony);
            ModScreens.ImguiFonts.Init(gameObject, _harmony);
            // Batch P features.
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.MenuCompendium)) _harmony.PatchAll(typeof(MenuCompendium));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.SummonFrames)) _harmony.PatchAll(typeof(PlayerHud.SummonFramesAdapter));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.PlayerShields)) _harmony.PatchAll(typeof(WorldHud.PlayerShields));
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Cursor)) CursorSkin.Apply();
            Log.LogInfo($"[RuneSkin] {Version} ready (API v{Skin.ApiVersion})");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        // Re-reads the theme (override file) and re-applies every live root. Ownership never changes at runtime.
        public static void ReloadTheme()
        {
            if (!ThemeTokens.Load()) return;
            ScreenRegistry.RestyleAll();
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Cursor)) CursorSkin.Apply();
        }

        // Coverage scan for tools (AutotestDriver calls it by reflection): visible graphics styled / explained / unstyled,
        // writes BepInEx\RuneSkin\diag\coverage-<state>.txt; first line = summary.
        public static string[] Coverage(string state) => Diagnostics.Coverage.Scan(string.IsNullOrEmpty(state) ? "now" : state);

        // Diagnostics entry for tools (AutotestDriver calls it by reflection): writes BepInEx\RuneSkin\diag\<screen>.txt.
        public static string[] DiagDump(string screen)
        {
            PlayerHud.HudCluster.Instance?.Record();
            MapScreen.MinimapSkin.Instance?.Record();
            WorldHud.EnemyHudSkin.Record();
            WorldHud.MessageSkin.Instance?.Record();
            WorldHud.DamageTextSkin.Record();
            WorldHud.PlayHud.Record();
            return SkinDiagnostics.Dump(string.IsNullOrEmpty(screen) ? null : screen).ToArray();
        }
    }

    // Console: runeskin_diag [screen] (dump), runeskin_reload (theme). Registered with the vanilla terminal.
    [HarmonyPatch]
    internal static class ConsoleCommands
    {
        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        [HarmonyPostfix]
        private static void InitTerminal_Postfix()
        {
            _ = new Terminal.ConsoleCommand("runeskin_diag", "[screen] RuneSkin diagnostics dump to BepInEx\\RuneSkin\\diag", args =>
            {
                var paths = Plugin.DiagDump(args.Length > 1 ? args[1] : null);
                args.Context?.AddString($"RuneSkin: {paths.Length} dump(s) in {SkinDiagnostics.DumpDir}");
            });
            _ = new Terminal.ConsoleCommand("runeskin_reload", "reload the RuneSkin theme (override BepInEx\\config\\RuneSkin\\theme.json)", args =>
            {
                try
                {
                    Plugin.ReloadTheme();
                    args.Context?.AddString($"RuneSkin: theme '{ThemeTokens.ThemeName}' rev {ThemeTokens.Revision} from {ThemeTokens.Source}");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"[RuneSkin] reload failed: {e}");
                }
            });
        }
    }
}
