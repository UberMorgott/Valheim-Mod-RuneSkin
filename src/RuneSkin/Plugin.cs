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
        public const string Version = "0.5.0";

        public static ManualLogSource Log { get; private set; }
        public static ConfigEntry<float> SkinScale { get; private set; }
        public static Host ApiHost { get; private set; }
        public static ConfigEntry<float> HudScale { get; private set; }
        public static ConfigEntry<bool> UltrawideBox { get; private set; }
        public static ConfigEntry<KeyboardShortcut> StatusListKey { get; private set; }
        public static ConfigEntry<string> StatusListButton { get; private set; }

        private static ConfigEntry<bool> _ownMenu, _ownTooltips, _ownHud, _ownInventory, _ownCrafting, _ownSkills, _ownTrophies, _ownAlmanac, _requireAssets;
        private static ConfigEntry<bool> _ownMinimap, _ownEnemyHud, _ownMessageHud, _ownDamageText;
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
            HudScale = Config.Bind("Hud", "Scale", 1f, new ConfigDescription(
                "HUD cluster scale on top of the game's UI scale (fonts never go below the readable floor).",
                new AcceptableValueRange<float>(0.75f, 1.5f)));
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
            }, _requireAssets.Value);

            ApiHost = new Host();
            Skin.Attach(ApiHost);

            gameObject.AddComponent<ResponsiveContext>();

            _harmony = new Harmony(Skin.PluginGuid);
            _harmony.PatchAll(typeof(ConsoleCommands));
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
            Log.LogInfo($"[RuneSkin] {Version} ready (API v{Skin.ApiVersion})");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        // Re-reads the theme (override file) and re-applies every live root. Ownership never changes at runtime.
        public static void ReloadTheme()
        {
            if (ThemeTokens.Load()) ScreenRegistry.RestyleAll();
        }

        // Diagnostics entry for tools (AutotestDriver calls it by reflection): writes BepInEx\RuneSkin\diag\<screen>.txt.
        public static string[] DiagDump(string screen)
        {
            PlayerHud.HudCluster.Instance?.Record();
            MapScreen.MinimapSkin.Instance?.Record();
            WorldHud.EnemyHudSkin.Record();
            WorldHud.MessageSkin.Instance?.Record();
            WorldHud.DamageTextSkin.Record();
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
