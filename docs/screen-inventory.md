# Screen inventory: what RuneSkin does not own yet (2026-10-22)

Scope for art milestones M6 (vanilla screens) and M7 (mod panels). Families owned by 0.6.0
(`src/RuneSkin/Ownership/OwnershipRegistry.cs`): Menu, Tooltips, Hud, Inventory (+ container, EAQS cells,
AdventureBackpacks dock, MultiUserChest bar, CurrencyPocket row), Crafting (+ VNEI grid, CraftingFilter row,
RecipeChains box), Skills, Trophies, Almanac, Minimap (+ large map), EnemyHud (+ Vaettir `VT_Frame` tiers),
MessageHud, DamageText, MainMenu (FejdStartup, UnifiedPopup), Settings, Connection.

Sources: game decompile `E:\DEV\Valheim\ValheimDecompiled-1.0.20` (Hud public fields, UI classes), grep of
`RuneSkin\src` per widget (0 refs = unowned), plugin folder `D:\Steam\steamapps\common\Valheim\BepInEx\plugins`,
our repos, harness diagnostics (`E:\DEV\Valheim\tools\out\20261022-030404`: unknown=0 in every owned family; the
diagnostics only cover owned families, so they list no unowned screens). Until a family takes a screen, AugaSkin
(or vanilla) keeps drawing it.

Frequency: H = every session / constantly, M = most sessions, L = rare.

## Vanilla (M6)

| # | Screen | Class / fields | Freq | Today | Key widgets | Art |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Build HUD: piece selection window | `Hud.m_pieceSelectionWindow`, `m_pieceCategoryTabs[]`, `m_pieceListRoot`, `m_pieceIconPrefab` | H | AugaSkin | category tabs (any count, mods add tabs), piece grid (scroll), hovered piece author window | M6 build |
| 2 | Build HUD: selected piece strip | `Hud.m_buildHud`, `m_buildSelection`, `m_pieceDescription`, `m_buildIcon`, `m_requirementItems[]` | H | AugaSkin | icon, name, description, requirement cells | M6 build |
| 3 | Crosshair + hover text + piece health | `Hud.m_crosshair`, `m_crosshairBow`, `m_hoverName`, `m_pieceHealthRoot/Bar` | H | vanilla | crosshair, hover text with key hints, small bar | M6 build |
| 4 | Stealth / targeted / hidden | `Hud.m_stealthBar`, `m_targeted`, `m_targetedAlert`, `m_hidden` | M | vanilla | eye glyphs, slim bar | M6 build |
| 5 | Action progress / stagger | `Hud.m_actionBarRoot`, `m_actionProgress`, `m_actionName`, `m_staggerProgress` | H | vanilla | slim bar + label | M6 ship |
| 6 | Ship HUD | `Hud.m_shipHudRoot`, `m_shipControlsRoot`, rudder / sail glyphs, `m_shipWindIndicatorRoot`, `m_shipRudderIndicator` | M | AugaSkin | speed ladder 5 states, rudder needle, wind arrow | M6 ship |
| 7 | Mount panel (lox, asksvin) | `Hud.m_mountPanel`, `m_mountIcon`, health / stamina bars, name | L | vanilla | portrait, 2 bars | M6 ship |
| 8 | Event / raid bar | `Hud.m_eventBar`, `m_eventName` | M | AugaSkin | name line (+ bar) | M6 ship |
| 9 | Black screens: respawn after death, sleep, teleport | `Hud.m_loadingScreen`, `m_loadingProgress`, `m_sleepingProgress`, `m_teleportingProgress`, `m_loadingImage`, `m_loadingTip`; `SleepText` | M | vanilla | tip, spinner / progress, sleep text | M6 load |
| 10 | Chat | `Chat` (Terminal) | M | vanilla / AugaSkin | log box, input field | M6 ship |
| 11 | Key hints | `KeyHints` (combat, build, ship, inventory, radials) | H (gamepad) | AugaSkin | key plates + labels | M6 ship |
| 12 | Trader store | `StoreGui` (Haldor, Hildir, Bog Witch) | M | AugaSkin | buy/sell list, price + coin, coin purse, buy/sell button | M6 trader |
| 13 | Text input | `TextInput` (signs, portal tags, map pin name, ward / tamed names) | M | AugaSkin | title, input field, OK / cancel | M6 text |
| 14 | Text viewer | `TextViewer` (runestones, raven, intro saga, dreams) | M | AugaSkin | title, long text, close hint | M6 text |
| 15 | Tutorial raven bubble | `Raven` + `Tutorial` | L (first hours) | AugaSkin | bubble with text, key hint | M6 text |
| 16 | Compendium / lore | `TextsDialog` (InventoryGui info tab "Texts") | L-M | AugaSkin | topic list, reading pane | M6 dialogs |
| 17 | Split stack | `InventoryGui.m_splitDialog` / `SplitDialog` | M | AugaSkin | icon, slider, amount, OK / cancel | M6 dialogs |
| 18 | Barber / mirror | `PlayerCustomizaton` (BarberGui) | L | AugaSkin | hair / beard cells, colour sliders | M6 dialogs |
| 19 | Hovered piece author | `Hud.m_hoveredPieceAuthorWindow` (crossplay) | L | vanilla | name, platform id, picture | kit popup |
| 20 | Achievement popup, join code, session player list, closed captions | `AchievementUnlockPopup`, `JoinCode`, `SessionPlayerList`, `ClosedCaptions` (AugaSkin VanillaPanels list) | L | AugaSkin | small panels | kit popup |
| 21 | Console | `Console` (Terminal, F5) | L (dev) | vanilla | log + input | kit (chat rules) |
| 22 | Saves / changelog / feedback / credits / server options | `ManageSavesMenu`, `ChangeLog`, `Feedback`, `EndCredits`, `ServerOptionsGUI` | L | AugaSkin (MainMenu touches Feedback/ChangeLog guards only) | lists, buttons | kit (M5 rules) |
| 23 | Connect debug panel | `ConnectPanel` (F2) | L (dev) | vanilla | text table | skip |
| 24 | Radial menus | `RadialBase` (gamepad hotbar / emote / item radials) | M (gamepad) | other session | ring of items | NOT here: owned by the icons-in-radial-menus session |

Vanilla total: 24 entries (21 to skin; console / connect debug panel low priority; radials elsewhere).

## Mods in the pack (M7)

| # | Panel | Mod | How built | Freq | Today | Art |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Enchanting table window (Sacrifice, Convert, Enchant, Augment, Disenchant, table Upgrade tabs; multi-select item list, rarity choice, cost, success dialog) | EpicLoot 0.14.13 | own bundle prefab `EnchantingUI` (`EpicLoot_UnityLib.EnchantingTableUI`, `MultiSelectItemList`, `CraftSuccessDialog`) | H | AugaSkin `EpicLootEnchanting` + MorgottTweaks `EpicLootFixesUi` | M7 ench |
| 2 | Adventure board (Secret Stash, Gamble, Treasure Maps, Bounties) | EpicLoot | own bundle prefab next to `StoreGui` | M | AugaSkin | M7 board |
| 3 | Vaettir target panel (aimed enemy, Hybrid/TargetPanelOnly mode) | BossAwakening (Vaettir) `UI\HudModes.cs` | own UGUI under EnemyHud | H | none (own art) | M7 modhud |
| 4 | Vaettir raid timer bar | BossAwakening `UI\RaidBarHud.cs` | own UGUI from boss-bar art | M | none | M7 modhud |
| 5 | Vaettir awakened boss bar | BossAwakening `UI\BossBarHud.cs` | own UGUI at the vanilla boss bar spot | M | none (AugaSkin exempt `BA_WorldBossBar`) | reuse `frame_boss` (M4) |
| 6 | Hamingja bleed-out bar (self) | Hamingja `Client\BleedBarHud.cs` | plain UGUI under the HUD root | M (on death) | none | M7 modhud |
| 7 | Hamingja ally markers (downed ally bar / edge arrow) | Hamingja `Client\AllyHud.cs` | screen-space UGUI | M | none | M7 modhud |
| 8 | Bonemass delirium overlay | BossAwakening `Modules\Delirium.cs` | full-screen effect | L | n/a (effect, not UI) | none |
| 9 | Sort buttons | MorgottTweaks `InventorySort` | buttons in inventory | H | RuneSkin Inventory (actions) | owned |
| 10 | Crafting filter / RecipeChains / trash slot | MorgottTweaks | cloned vanilla templates | H | RuneSkin Crafting / Inventory | owned |
| 11 | Alchemist gem 4th food slot | MorgottTweaks `AlchemistGem` | HUD food socket | M | RuneSkin Hud (`socket_food_gem`) | owned |
| 12 | Portal map / portal list | PotalMap | own UI | M | none | kit (large map rules) |
| 13 | Drawer labels | RossItemDrawers | world-space text | M | n/a (world text) | none |
| 14 | VNEI item browser | Vnei | Jotunn / own prefab | M | RuneSkin Crafting (grid) | owned |
| 15 | Almanac | Almanac (RustyMods) | own prefab | M | RuneSkin Almanac | owned |
| 16 | Seasonality season indicator | Seasonality | status effect icon | H | RuneSkin Hud status row | owned (status row) |
| 17 | BepInEx info text | Valheim.DisplayBepInExInfo | IMGUI text on the main menu | L | none (IMGUI: fonts only) | none |
| 18 | ConfigurationManager | ConfigurationManager | IMGUI | dev only, not shipped | none | skip |
| 19 | Jotunn-created panels (`GUIManager.CreateWoodpanel`, buttons, key hints) | Jotunn (any mod) | Jotunn GUIManager | L | AugaSkin role pass | kit (generic panel + button roles) |
| 20 | Armory, Warfare, Wizardry, Hearthcraft, SeedBed, OreMines, BlacksmithTools, SmoothRegen, ValheimBuildCamera, PlantEverything, WeaponHolsterOverhaul, SolidHitboxes, Skidbladnir, ValheimPlus | various | no own panels (items / mechanics / perf; V+ reuses vanilla UI) | - | - | none |

Mod total: 20 entries; 6 need new art (EpicLoot enchanting + adventure board, Vaettir target panel + raid bar,
Hamingja bleed-out + ally markers), 2 compose from the kit (portal map, Jotunn panels), the rest owned or no UI.

## Radial menus (listed only)

Vanilla `RadialBase` (gamepad hotbar / emote / item radials) and the MorgottTweaks summon command radial
(hold block with a summon staff: Follow / Stay / Dismiss newest / Toggle all, `summons-ui-contract.md` section 4) are
owned by the icons-in-radial-menus session; no RuneSkin art here.
