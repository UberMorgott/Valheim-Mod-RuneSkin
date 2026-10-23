using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    // Bootstrap order in Awake: config -> theme + asset bundle -> ownership frozen -> API host attached (replays early registrations) -> Harmony patches of owned
    // families only.
    [BepInPlugin(Skin.PluginGuid, "RuneSkin", Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Version = "0.18.2";

        public static ManualLogSource Log { get; private set; }
        public static ConfigEntry<float> SkinScale { get; private set; }
        public static ConfigEntry<bool> CharacterPreview { get; private set; }
        public static Host ApiHost { get; private set; }
        public static ConfigEntry<float> HudScale { get; private set; }
        public static ConfigEntry<float> MinimapScale { get; private set; }
        public static ConfigEntry<bool> UltrawideBox { get; private set; }
        public static ConfigEntry<float> StripWidth { get; private set; }
        public static ConfigEntry<float> StripHeight { get; private set; }
        public static ConfigEntry<int> StatusPerRow { get; private set; }
        public static ConfigEntry<int> StatusRows { get; private set; }
        public static ConfigEntry<string> BuffSide { get; private set; }
        public static ConfigEntry<bool> CenterGauges { get; private set; }
        public static ConfigEntry<float> GaugeIdleDelay { get; private set; }
        public static ConfigEntry<float> GaugeOpacity { get; private set; }
        public static ConfigEntry<bool> RageInRow { get; private set; }
        public static ConfigEntry<KeyboardShortcut> StatusListKey { get; private set; }
        public static ConfigEntry<string> StatusListButton { get; private set; }
        public static ConfigEntry<KeyboardShortcut> StatsKey { get; private set; }
        public static ConfigEntry<float> StatsRefresh { get; private set; }
        public static ConfigEntry<float> StatsReferenceHit { get; private set; }

        private static ConfigEntry<bool> _ownMenu, _ownTooltips, _ownHud, _ownInventory, _ownCrafting, _ownSkills, _ownTrophies, _ownAlmanac, _requireAssets;
        private static ConfigEntry<bool> _ownMinimap, _ownEnemyHud, _ownMessageHud, _ownDamageText, _ownMainMenu, _ownSettings, _ownConnection;
        private static ConfigEntry<bool> _ownBuildHud, _ownCrosshair, _ownActionHud, _ownShipHud, _ownKeyHints;
        private static ConfigEntry<bool> _ownLoadScreen, _ownChat, _ownPopups, _ownTextDialogs, _ownCompendium, _ownSplitDialog;
        private static ConfigEntry<bool> _ownStore, _ownBarber, _ownDevPanels, _ownFeedback;
        private static ConfigEntry<bool> _ownModUi, _ownElEnchanting, _ownElBoard, _ownElTemper, _ownModHud, _ownInventoryExtras, _ownRadial;
        private static ConfigEntry<bool> _ownMenuCompendium, _ownSummonFrames, _ownPlayerShields, _ownCursor, _ownCharacterStats;
        private static ConfigEntry<string> _modUiOptOut;
        public static ConfigEntry<bool> WatchTheme { get; private set; }
        private int _ownOrder;

        // Ownership switches: restart-only by design (families freeze at startup), advanced in ConfigurationManager.
        private ConfigEntry<bool> Own(string key, string description) =>
            Config.Bind("Ownership", key, true, new ConfigDescription(description, null,
                new ConfigurationManagerAttributes { IsAdvanced = true, Order = 1000 - _ownOrder++ }));
        private Harmony _harmony;

        private long _initStart;

        private void Awake()
        {
            _initStart = Stopwatch.GetTimestamp();
            Log = Logger;
            _ownMenu = Own("Menu",
                "RuneSkin styles the pause menu. Read once at startup (restart to change).");
            _ownTooltips = Own("Tooltips",
                "RuneSkin styles tooltips. Read once at startup (restart to change).");
            _ownHud = Own("Hud",
                "RuneSkin draws the player HUD (bars, food, status effects, hotbar placement). Read once at startup (restart to change).");
            _ownInventory = Own("Inventory",
                "RuneSkin lays out the inventory screen (layout A: doll, grid, chest / backpack dock, crafting placement). Read once at startup (restart to change).");
            _ownCrafting = Own("Crafting",
                "RuneSkin styles and lays out the crafting column (station header, tabs, recipe list, detail, requirements). Needs Inventory. Read once at startup (restart to change).");
            _ownSkills = Own("Skills",
                "RuneSkin styles and lays out the skills dialog (needs Inventory). Read once at startup (restart to change).");
            _ownTrophies = Own("Trophies",
                "RuneSkin styles and lays out the trophies panel (needs Inventory). Read once at startup (restart to change).");
            _ownAlmanac = Own("Almanac",
                "RuneSkin restyles the Almanac mod's window (its layout and content stay Almanac's). Read once at startup (restart to change).");
            _ownMinimap = Own("Minimap",
                "RuneSkin draws the small minimap as a round rune ring (compass runes, wind, day phase, biome plaque) and restyles the large map screen. Read once at startup (restart to change).");
            _ownEnemyHud = Own("EnemyHud",
                "RuneSkin restyles enemy health bars, star levels and the boss bar (Vaettir rarity frames keep their tiers). Read once at startup (restart to change).");
            _ownMessageHud = Own("MessageHud",
                "RuneSkin restyles centre messages, top-left notifications, unlock messages and the biome-discovered banner. Read once at startup (restart to change).");
            _ownDamageText = Own("DamageText",
                "RuneSkin restyles floating damage numbers (font, outline, colour per hit type). Read once at startup (restart to change).");
            _ownMainMenu = Own("MainMenu",
                "RuneSkin styles the pre-world screens (main menu, character select / create, world and server select, dialogs, loading). Read once at startup (restart to change).");
            _ownSettings = Own("Settings",
                "RuneSkin styles the Settings window (category list, sliders, checkboxes, dropdowns, key bindings). Read once at startup (restart to change).");
            _ownConnection = Own("Connection",
                "RuneSkin styles the server password and connecting dialogs. Read once at startup (restart to change).");
            _ownBuildHud = Own("BuildHud",
                "RuneSkin styles the build menu, the selected-piece strip and the hovered-piece author window. Read once at startup (restart to change).");
            _ownCrosshair = Own("Crosshair",
                "RuneSkin styles the crosshair, hover text, piece health bar and the stealth eye. Read once at startup (restart to change).");
            _ownActionHud = Own("ActionHud",
                "RuneSkin styles the action progress and stagger bars, the event / raid line and the mount panel. Read once at startup (restart to change).");
            _ownShipHud = Own("ShipHud",
                "RuneSkin draws the ship HUD as a speed ladder with a rudder socket (wind stays on the minimap ring). Read once at startup (restart to change).");
            _ownKeyHints = Own("KeyHints",
                "RuneSkin styles the key hints (key plates and labels; gamepad glyphs stay). Read once at startup (restart to change).");
            _ownLoadScreen = Own("LoadScreen",
                "RuneSkin styles the black screens over the world (respawn tip banner + rune spinner, sleep, teleport ring). Read once at startup (restart to change).");
            _ownChat = Own("Chat",
                "RuneSkin styles the chat window, in-world chat texts and NPC / raven speech bubbles. Read once at startup (restart to change).");
            _ownPopups = Own("Popups",
                "RuneSkin styles the small popups (achievement unlock, join code, session player list, closed captions). Read once at startup (restart to change).");
            _ownTextDialogs = Own("TextDialogs",
                "RuneSkin styles the text input (signs, portals, pins, names) and the text viewer (runestones, intro saga, raven). Read once at startup (restart to change).");
            _ownCompendium = Own("Compendium",
                "RuneSkin styles the compendium / lore dialog. Read once at startup (restart to change).");
            _ownSplitDialog = Own("SplitDialog",
                "RuneSkin styles the split stack dialog. Read once at startup (restart to change).");
            _ownStore = Own("Store",
                "RuneSkin styles the trader store (Haldor, Hildir, Bog Witch; Epic Loot's own trader panels excluded). Read once at startup (restart to change).");
            _ownBarber = Own("Barber",
                "RuneSkin styles the barber / mirror. Read once at startup (restart to change).");
            _ownDevPanels = Own("DevPanels",
                "RuneSkin styles the F5 console and the F2 connect panel. Read once at startup (restart to change).");
            _ownFeedback = Own("Feedback",
                "RuneSkin styles the feedback dialog. Read once at startup (restart to change).");
            _ownModUi = Own("ModUi",
                "RuneSkin restyles every window no other family owns (other mods' canvases and panels, Jotunn windows, IMGUI fonts, other mods' main menu lines): theme panels, controls and fonts, the mod's own layout kept. Read once at startup (restart to change).");
            _modUiOptOut = Config.Bind("Ownership", "ModUiOptOut", "BossAwakening,BA_WorldBossBar", new ConfigDescription(
                "Comma-separated object or plugin assembly names the fallback restyler (ModUi) never touches, with everything below them (default: BossAwakening's boss / raid / target bars keep their own design, styled by the EnemyHud track). Read once at startup.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 1000 - _ownOrder++ }));
            _ownElEnchanting = Own("EpicLootEnchanting",
                "RuneSkin styles Epic Loot's enchanting table window and its dialogs. Read once at startup (restart to change).");
            _ownElBoard = Own("EpicLootBoard",
                "RuneSkin styles Epic Loot's adventure board at Haldor (secret stash, gamble, treasure maps, bounties). Read once at startup (restart to change).");
            _ownElTemper = Own("EpicLootTemper",
                "RuneSkin styles Epic Loot's tempering panel at Hildir. Read once at startup (restart to change).");
            _ownModHud = Own("ModHud",
                "RuneSkin styles HUD parts other mods add (Epic Loot ability bar and debug text, level texts). Read once at startup (restart to change).");
            _ownInventoryExtras = Own("InventoryExtras",
                "RuneSkin styles the achievements panel, the variant dialog and the dragged item. Read once at startup (restart to change).");
            _ownRadial = Own("Radial",
                "RuneSkin styles the radial menus (texts; wheel art and icons stay). Read once at startup (restart to change).");
            _ownMenuCompendium = Own("MenuCompendium",
                "RuneSkin adds a Compendium entry under Settings in the pause menu (needs Menu). Read once at startup (restart to change).");
            _ownSummonFrames = Own("SummonFrames",
                "RuneSkin shows a frame per summon next to the hotbar (icon, health with the summon's energy shield, respawn / stay / off state). Read once at startup (restart to change).");
            _ownPlayerShields = Own("PlayerShields",
                "RuneSkin shows other players' energy shield and absorb shield as rails under their overhead health bar (MorgottTweaks publishes them). Read once at startup (restart to change).");
            _ownCursor = Own("Cursor",
                "RuneSkin sets its rune mouse cursor. Read once at startup (restart to change).");
            _ownCharacterStats = Config.Bind("Ownership", "CharacterStats", true,
                "RuneSkin adds the character stats window (5th button of the inventory info row, or Hud.StatsKey): pools and regeneration per second, resistances, movement and weight, defense, food, rest, combat modifiers, MorgottTweaks shield and slots (needs Inventory). Read once at startup (restart to change).");
            HudScale = Config.Bind("Hud", "Scale", 1f, new ConfigDescription(
                "HUD cluster scale on top of the game's UI scale (fonts never go below the readable floor).",
                new AcceptableValueRange<float>(0.75f, 1.5f)));
            MinimapScale = Config.Bind("Hud", "MinimapScale", 1f, new ConfigDescription(
                "Minimap ring size on top of the theme size (size.minimap.ring) and Hud.Scale; its height cap (share.minimap.ring.h) scales with it. Applied live.",
                new AcceptableValueRange<float>(0.6f, 1.6f)));
            UltrawideBox = Config.Bind("Hud", "UltrawideBox", true,
                "On 21:9 and wider screens the HUD corners are those of a centred 16:9 box (off = true screen corners).");
            StripWidth = Config.Bind("HudStrip", "Width", 0.99f, new ConfigDescription(
                "Widest the bottom HUD strip may get, as a share of the screen (or of the centred 16:9 box on ultrawide); its width follows its height in the mockup's proportions.", new AcceptableValueRange<float>(0.5f, 1f)));
            StripHeight = Config.Bind("HudStrip", "Height", 130f, new ConfigDescription(
                "Height of the bottom HUD strip in reference pixels at 1080p (130 = 12 % of the screen height; scaled with the screen and Hud.Scale).", new AcceptableValueRange<float>(80f, 320f)));
            StatusPerRow = Config.Bind("HudStrip", "StatusPerRow", 10, new ConfigDescription(
                "Status effect icons per row above the HUD strip (buffs on one side, debuffs on the other); a full row wraps upward.", new AcceptableValueRange<int>(1, 30)));
            StatusRows = Config.Bind("HudStrip", "StatusRows", 2, new ConfigDescription(
                "Rows of status icons per side before the rest folds into \"+N\" (F7 lists all).", new AcceptableValueRange<int>(1, 6)));
            BuffSide = Config.Bind("HudStrip", "BuffSide", "Left", new ConfigDescription(
                "Side of the HUD strip the buffs grow from (debuffs take the other side).", new AcceptableValueList<string>("Left", "Right")));
            CenterGauges = Config.Bind("HudCenter", "Enabled", true,
                "Rage (adrenaline) ring around the crosshair and the player's stagger column beside it, shown while they change (the vanilla stagger bar and adrenaline bar are hidden).");
            GaugeIdleDelay = Config.Bind("HudCenter", "IdleDelay", 1.5f, new ConfigDescription(
                "Seconds a centre gauge stays after its value stopped changing before it fades out (a full rage ring stays while full).", new AcceptableValueRange<float>(0.2f, 10f)));
            GaugeOpacity = Config.Bind("HudCenter", "Opacity", 0.85f, new ConfigDescription(
                "Opacity of the centre gauges while shown.", new AcceptableValueRange<float>(0.2f, 1f)));
            RageInRow = Config.Bind("HudCenter", "RageInBottomRow", false,
                "Also show rage (adrenaline) as a bar in the strip's resource row next to stamina / eitr while it is above 0.");
            StatusListKey = Config.Bind("Hud", "StatusListKey", new KeyboardShortcut(UnityEngine.KeyCode.F7),
                "Opens / closes the full list of status effects when the strip shows a +N cell.");
            StatusListButton = Config.Bind("Hud", "StatusListGamepadButton", "JoyDPadDown",
                "Gamepad: this ZInput button while JoyAltKeys is held opens / closes the full status list. Empty = none.");
            StatsKey = Config.Bind("Hud", "StatsKey", new KeyboardShortcut(UnityEngine.KeyCode.None),
                "Opens / closes the character stats window (with the inventory). None = only the inventory info button.");
            StatsRefresh = Config.Bind("Hud", "StatsRefresh", 0.25f, new ConfigDescription(
                "Seconds between value updates of the open character stats window.", new AcceptableValueRange<float>(0.05f, 2f)));
            StatsReferenceHit = Config.Bind("Hud", "StatsReferenceHit", 100f, new ConfigDescription(
                "Character stats resistances: the share of a hit of this size, per damage type, that Epic Loot, resistances and armor remove (the game's own damage steps run on it).",
                new AcceptableValueRange<float>(1f, 1000f)));
            WorldHud.EnemyHudConfig.Bind(Config);
            // [Diagnostics]: development switches, not looks (perf audit B4: were mixed into [Ownership]).
            _requireAssets = Config.Bind("Diagnostics", "RequireAssets", true, new ConfigDescription(
                "Take a family only when its theme assets loaded (otherwise the other skin keeps it). Off = take it anyway (development before the art bundle exists). Read once at startup.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 2 }));
            WatchTheme = Config.Bind("Diagnostics", "WatchTheme", true, new ConfigDescription(
                "Reload the theme when the override BepInEx\\config\\RuneSkin\\theme.json is written (0.5 s after the last change; same as the runeskin_reload console command). The override lists only the entries it changes (layered over the shipped theme; // comments allowed). Read once at startup.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 1 }));
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
                // The window lives in the inventory screen (its info row button, InventorySkin layout).
                [OwnershipRegistry.CharacterStats] = _ownCharacterStats.Value && _ownInventory.Value,
            }, _requireAssets.Value);

            ApiHost = new Host();
            Skin.Attach(ApiHost);

            gameObject.AddComponent<ResponsiveContext>();
            if (WatchTheme.Value) ThemeWatcher.Attach(gameObject);

            var patchStart = Stopwatch.GetTimestamp();
            _harmony = new Harmony(Skin.PluginGuid);
            // M7: precise mod panels + the fallback restyler for every UI no family owns, Jotunn windows, IMGUI fonts
            // (their own runtime hooks), set up before the class patches that read their state.
            ModScreens.AlmanacAdapter.Harmony = _harmony;
            ModScreens.ForeignUi.Init(gameObject, _harmony, _modUiOptOut.Value);
            ModScreens.JotunnHook.Init(_harmony);
            ModScreens.ImguiFonts.Init(gameObject, _harmony);
            // One PatchAll(Assembly) (perf audit B3): every patch class gates itself on its family's ownership (static
            // Prepare), so FastStartup's HarmonyBatcher builds each patched game method once (22 PatchAll(Type) calls
            // rebuilt InventoryGui.Awake 6x, Hud.Awake 2x, InventoryGui.Update 2x). Ties between RuneSkin's own
            // postfixes on one method are explicit (PatchOrder): class order in the assembly is not a contract.
            // WorldHud.PlayHudAdapter has no Prepare: each of its hooks gates per family (PlayHud.Owned).
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Theme.Words.ApplyExisting();
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Cursor)) CursorSkin.Apply();
            var ms = (Stopwatch.GetTimestamp() - patchStart) * 1000.0 / Stopwatch.Frequency;
            Log.LogInfo($"[RuneSkin] {Version} ready (API v{Skin.ApiVersion}); init {(patchStart - _initStart) * 1000.0 / Stopwatch.Frequency:0} ms theme + ownership, {ms:0} ms patches");
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

    // Order of RuneSkin's own postfixes that share one game method (HarmonyX: higher priority first, ties by
    // registration). Priority.Last = 0; the old sequential PatchAll(Type) order, kept: InventoryGui.Awake / Update
    // inventory -> crafting -> skills -> trophies -> Almanac -> game screens -> extras; Hud.Awake HUD cluster ->
    // play HUD (WorldHud.PlayHudAdapter, Priority.Last) -> game screens -> extras; FejdStartup.Start main menu -> mod lines.
    internal static class PatchOrder
    {
        public const int Hud = Priority.Last + 1;
        public const int HudGameScreens = Priority.Last - 1, HudExtras = Priority.Last - 2;
        public const int Crafting = Priority.Last - 1, Skills = Priority.Last - 2, Trophies = Priority.Last - 3, Almanac = Priority.Last - 4,
            InventoryGameScreens = Priority.Last - 5, InventoryExtras = Priority.Last - 6, CharacterStats = Priority.Last - 7;
        public const int MainMenuMods = Priority.Last - 1;
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
