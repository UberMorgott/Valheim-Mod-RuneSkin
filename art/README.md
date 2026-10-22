# RuneSkin art pipeline

## Rebuild (from the repo root, PowerShell)

```powershell
go -C art/tools run . build; & 'D:\Unity\Editors\6000.0.75f1\Editor\Unity.exe' -batchmode -quit -projectPath unity -executeMethod BuildBundles.Build -logFile unity\Logs\build.log | Out-Null; $LASTEXITCODE
```

(`| Out-Null` makes PowerShell wait for Unity.exe, a GUI-subsystem binary; without it the call returns at once.)

`build` cuts `art/src/kit/*.png` per `art/src/kit/cuts.json` into `art/sprites/*.png` + `sprites.json` and runs
`check` (sizes, borders, orphans). Exit code 0 + `RuneSkin: BUILD OK` in the log = bundle `unity\Build\runeskin`
(StandaloneWindows64, LZ4) built and verified: it is loaded back and every `sprites.json` entry and both fonts are
checked (`unity\Build\runeskin.contents.txt`). Do NOT add `-nographics`: the sprite atlas packer fails without a
graphics device ("Unsupported Format 0", editor crash).

Review aids (not part of the build, output in `art/review/`, git-ignored):
`go -C art/tools run . compare` -> `compare-inventory.png`, `compare-hud.png`, `compare-corner.png`,
`compare-groups.png` ([mockup | ours] rebuilt from our sprites with Unity Sliced/Tiled rules; mockups in
`D:\tmp\cx-augaskin`); `go -C art/tools run . sheet` -> `contact-sheet.png`. M2/M3: `compare-m2`, `compare-m3` (crafting: `compare-crafting-m3.png`, `compare-groups-m3.png`), `compare-m3s` (skills/trophies: `compare-skills-m3.png`, `compare-trophies-m3.png`, `compare-groups-m3s.png`), `compare-m4` (`compare-hud-m4a/b.png`, `compare-map-m4.png`, `compare-groups-m4.png`), `compare-m5` (`compare-{menu,char,world,load,settings}-m5.png`, `compare-groups-m5.png`), `compare-m6` (`compare-{build,trader,text,load,ship,dialogs}-m6.png`, `compare-groups-m6.png`).

## Rule: Codex makes the pixels

Every sprite pixel comes from Codex image gen (`cx.ps1 -NoSession -Img "<prompt>" -To <file>`), prompts kept next to
the sheets as `src/kit/*.prompt.txt`. The Go tool is mechanical only: crop, magenta / luminance key, resize,
9-slice assembly (corners kept, mid segments made periodic by cross-fade for Tiled), mirror, tone/leather grading, magenta-spill fixes (`despill`, `flatEnds`), `bold` (alpha max-filter), `fadeV`, `hole` (ring hole -> mask),
alpha fades, transparent bar windows. New art = new kit sheet + `cuts.json` entry, never drawn in code.
Style targets and palette: `STYLE.md`.

## Layout

| Path | What |
| --- | --- |
| `src/kit/*.png` | Codex image-gen kit sheets (panel2, corners, controls, ctrl2, buttons, states, hud, hud2, fill, masks, fx) + `*.prompt.txt` |
| `src/kit/cuts.json` | cut spec: sheet rect, key, scale, nine/period, grading, Unity border/pivot/type/token/notes, palette |
| `tools/` (Go, `go -C art/tools run . <cmd>`) | `build`, `check`, `compare`, `compare-m2`, `compare-m3`, `compare-m3s`, `sheet`, `zoom`, `sample`, `blobs` (piece rects on a sheet) |
| `sprites/` | final sprites (authored at 2x reference px, PPU 200) + `sprites.json` |
| `fonts/` | Cormorant Bold 4.002 (display), PT Sans Regular 2.003W (body), `charset-ru-en.txt` (309 chars), `bake-report.txt`, `licenses/` (OFL 1.1) |
| `../unity/Assets/Editor/BuildBundles.cs` | import (9-slice borders, pivot, PPU), SpriteAtlas (BC7, no rotation/tight packing), static TMP SDF fonts, bundle build, contents check |

## sprites.json contract

- `border` in sprite px, Unity `spriteBorder = (left, bottom, right, top)`; `pivot` normalized.
- `imageType` = recommended `Image.type`; `Tiled` items have periodic edge/centre segments, `Sliced` items have
  fixed ornament inside their borders and plain stretchable middles.
- `token` = intended ThemeTokens name; `palette` = colour tokens the art was made for. Per-sprite geometry
  (windows, sockets, insets) is in each entry's `notes`.
- Panel: `fill_glass` (Sliced, alpha 0.9) inset 24 sprite px under `frame_panel` (Tiled, fillCenter off, border 32),
  `frame_panel_glow` with the `rune_glow` tint, then the four `frame_panel_corner_*` caps (207x207) at native size
  on top of the panel content, pivot = the panel's outer corner (rune block flush with the outer edges).
- Small groups (quick slots, food row, stat boxes): `frame_small`; status-effect row: `frame_strip` + `separator_v`;
  cells: `slot_cell` / `slot_cell_selected`; tabs: `tab_normal` / `tab_hover` / `tab_selected`; menu/craft
  buttons: `button_*` (knot end caps in the 84 px borders).

## HUD sprites

- Masks (`bar_fill`, `rail_fill` = opaque grey shading; `bar_lag`, `bar_growth`, `bar_end_line`, `dot_*`,
  `bar_reserved_edge`, `glyph_*`, `food_timer_ring`, `ring_select` = white + alpha) are tinted by `Image.color`
  with the palette token named in their notes.
- HP bar = `bar_frame` 480x87 (window inset L 95, R 92, T 28, B 17 -> 42 high, transparent except the strap);
  stamina/eitr = `bar_frame_slim` 380x46 (window inset L 70, R 36, T 13, B 11 -> 22 high; icon recess centre (49,23)).
  Window z-order: `bar_trough` -> `bar_lag` -> `bar_fill` -> `dot_*` -> `bar_reserved` (+ edge, `glyph_lock`) ->
  frame -> overlay (`overlay_frost` top-left, `overlay_wet` along the bottom) -> numbers.
- ES / adrenaline rails: `rail_frame_es` / `rail_frame_adrenaline` 400x40, window inset x 36, y 15 (14 high).
- DoT strips tile horizontally (bleed period 20, fire 34, poison 36), shapes differ (slashes / chevrons / dots).

## Fonts

- Sources: Cormorant v4.002 (github.com/CatharsisFonts/Cormorant release, `Cormorant_Essentials_v4.002.zip`),
  PT Sans Web 2.003W OFL (github.com/google/fonts `ofl/ptsans`). Downloads kept in `D:\Downloads\fonts-runeskin`.
- Both SIL OFL 1.1 (PT Sans: Reserved Font Names "PT Sans", "ParaType"; fonts ship unmodified, the bundle holds
  SDF atlases). Licence texts in `fonts/licenses/` must ship with the mod.
- Bake: `TMP_FontAsset.CreateFontAsset` (SDFAA) + `TryAddCharacters(charset, includeFontFeatures)` then Static,
  source font dropped, atlas non-readable, material shader `TextMeshPro/Distance Field` (same approach as the
  AugaSkin fork's `FontAtlasBake.cs`, baked directly instead of spliced).

## M3 sprites (crafting, skills, trophies)

- Crafting: station title = serif text (no plaque) + `badge_level_star` (digit text_active at (76,75)); mode tabs `tab_*`;
  category filter row = `tab_square_*` + `glyph_cat_*` (scroll with `glyph_arrow_left/right`); `field_search` + `glyph_search`;
  list well + detail well `frame_inset`; rows `row_recipe_normal/hover/selected/unavailable` (unavailable text text_disabled);
  detail: title text_item_title, `pip_quality_*`, `row_stat` (value text_stat_value); requirements `socket_req` (count text_ok) /
  `socket_req_missing` (count text_missing), upgrade preview `socket_req_upgrade` + `plaque_arrow`; craft button `button_*` +
  `glyph_act_craft/upgrade/repair` left of the label; `button_round` for repair/upgrade shortcuts; RecipeChains box
  `frame_missing` (lines text_missing); EpicLoot tabs = `tab_*` + `glyph_ench_*`.
- Skills (skills-a): character tabs `tab_square_*` + `glyph_tab_*`; summary row `row_recipe_normal` + `glyph_death` +
  sort toggles `tab_square_*` + `glyph_sort_level/name`; rows `row_recipe_*`, icon socket `slot_cell_recess`, progress
  `bar_skill_trough` + `bar_skill_fill` (tint rune_glow, inset 3), level text_active; tooltip = popup.
- Trophies (trophies-a): boss strip `frame_inset` + boss cells `tab_square_selected` + `glyph_boss` corner badge (text_active);
  grid `slot_cell_recess`, selection `slot_cell_selected`; locked = trophy icon tinted dark (no per-trophy silhouette art).
- Popup (tooltips): `fill_glass` inset 24 + `frame_panel` + `frame_panel_glow` + `frame_corner_small_*` (78x78, pivot = outer corner).

## M3 polish sprites (2026-10-22, from the M3 in-game review)

- `button_*` (5 states) redrawn from kit `m3polish.png` to match mockup-1 "СОЗДАТЬ": cool grey slate, deeper carved rim, bronze only on
  the tips. Same names, size 346x70 (was 328x70), borders unchanged (L/R 84, T/B 18). If it still reads brown/flat in game, check the
  code side: `Image.color` must be white and the Button `ColorBlock`/transition must not tint the sprite (vanilla buttons tint brownish).
- `frame_inset` redrawn with a deep carved bevel: border 24 (was 16), content inset 20 (was 14).
- `tab_strip` (Sliced, borders L/R 26 T/B 8, pivot top-centre): slim carved ledge under a tab row (character tabs skills/trophies,
  settings, EpicLoot tabs); tabs overlap its top by ~4 px. Skills sort toggles stay `tab_square_*` + `glyph_sort_level/name`.
- New glyph masks (48x48, white, tint like `glyph_cat_*`): `glyph_cat_ranged` (bow), `glyph_cat_magic` (staff) for CraftingFilter
  Ranged/Magic; `glyph_refresh`, `glyph_plus`, `glyph_minus`, `glyph_settings` (map zoom, server refresh, settings).
- Codex: button 8.5, wells 8, ranged/magic 8, tab_strip 8. Build tool: new `bold` cut option (alpha max-filter for thin glyph masks).

## M4 sprites (minimap, enemy HUD, messages, damage text)

Picks (Codex, user asleep; mockups in `D:\tmp\cx-augaskin`): minimap `m4-hud-a`, boss bar A (shortened, unique tier), enemy bars B,
damage text A, notifications A, centre message B, biome banner B, large map `m4-map-a` (legend toggles = M5 checkbox).
- Minimap (220 ref = 440 sprite px, clamp <= 18% safe height, top-right): `minimap_ring` (Simple 440, hole = map) over the map image
  masked by `minimap_mask` (Unity Mask, white disc, same box; Jotunn MinimapManager overlays as children of the masked map);
  `minimap_rune_stone` x4 centred on the ring band at N/E/S/W (letter = localisable serif text tint rune_glow, ~25 ref px);
  `minimap_wind_socket` upper-right + `glyph_wind` (rotate; sailing only); `glyph_time_sun` / `glyph_time_moon` on the top arc
  (tint text_active / text_header); biome name on `tab_normal` under the ring.
- Boss bar: `frame_boss` (Sliced, window transparent, inset L/R 154 T/B 34 sprite px) over `bar_trough` + `bar_enemy_fill`
  (tint pool health); name on `header_plaque` above. Vaettir: this is the Unique tier for boss EnemyHud bars.
- Enemy bars: `frame_enemy_normal|magic|rare|unique` (Vaettir tiers 1/2/3/boss) = the `VT_Frame` Sliced child stretched over the
  Health bar, all 120x36 sprite, borders L/R 36 T/B 14, fillCenter off; template rim disabled under it; stars hidden (Vaettir
  §6.2). Fill `bar_enemy_fill` (white mask, tint health red; tamed = vanilla green). Name above: body font + dark outline.
- Damage text: font only. Body font (PT Sans; the display serif has old-style digits), TMP Bold / SDF dilate ~0.2, dark outline
  ~1 ref px, ~24 ref px, crit ~30-36; colours physical text_body, crit text_active, fire #f08a3a, frost #8ac8f0,
  poison #9fd66b, spirit #b28bdc. No sprites.
- Notifications (top-left, pooled, variable count): `frame_small` row ~336x47 ref, icon left, serif text text_body.
- Centre message: `band_message` (soft borderless band, alpha 0.6 baked) + serif text. Biome banner: `band_message` + `divider`
  lines above/below + `banner_rule` left and mirrored right (scale.x -1) at native height + serif caps title + sans subtitle.
- Large map: standard panel + `header_plaque`; legend column `slot_cell_recess` pin icons, `checkbox_box` + `glyph_check`
  toggles, `tab_*` filter, `divider`, `field_search` name, zoom `button_round` + `glyph_plus/minus` + vertical
  `bar_skill_trough`/`bar_skill_fill` + `slider_handle_*`; selected pin `ring_pin_select` + `tooltip_frame` label.

## M5 sprites (main menu, character, world/server, connecting/loading, settings)

Picks (Codex): all variant A (`m5-menu-a`, `m5-char-a`, `m5-world-a`, `m5-load-a`, `m5-settings-a`); toggle = checkbox (B);
delete confirm centred modal (from B). Menu entries / characters / worlds / servers / settings tabs / keybinds are lists of any
count (scroll).
- Menu: vertical `button_*` column (hover = `button_hover`), news = panel + `header_plaque` + `scroll_*` + `button_normal`;
  link buttons `button_icon_*` + glyphs; version text text_disabled.
- Character: list rows = `frame_inset` wells (selected + `row_recipe_selected`), portrait `slot_cell_recess`; delete
  `button_icon_*` + `glyph_trash`; confirm modal = panel (or popup) + `button_danger_normal|hover` (ember line; pressed/disabled
  reuse `button_pressed|disabled`) + `button_normal`. Create panel: `field_search` name, body type `tab_square_*`, style cells
  `slot_cell_recess|selected`, colour sliders = `bar_skill_trough` + vanilla gradient + `slider_handle_*`.
- World/server: `tab_*` on `tab_strip`, rows as above with thumbnail in `slot_cell_recess`, modifiers `button_icon_*`,
  `checkbox_box`, `field_search` (password), primary `button_*` gold text. Join/password dialog = panel + `field_search` +
  `button_selected` (primary) / `button_normal`. Server list refresh `glyph_refresh`.
- Connecting: panel + `spinner_runes` (tint rune_glow, rotate in 45 deg steps or smoothly) + `button_normal` cancel.
  Loading: `frame_boss` reused as the progress frame + `rail_fill` (tint rune_glow) + `bar_end_line` leading edge (only with
  real progress, else spinner); tip on `frame_strip`.
- Settings: category list `tab_normal|tab_selected` (vertical, scroll), content `frame_inset`, rows `divider`; Slider = track
  `bar_skill_trough`, fill `bar_skill_fill` tint rune_glow, handle `slider_handle_normal|hover|disabled` (32 ref visible,
  hit >= 44); Toggle = `checkbox_box` + `glyph_check`; TMP_Dropdown = `field_search` + `glyph_chevron_down`, list
  `frame_inset` + `row_recipe_selected` item highlight (opens upward when no room); keybind = label + `plate_key` field,
  waiting = `ring_select` tint rune_glow + "Нажмите клавишу..."; bottom `button_*` (Apply gold text).

## M6 sprites (vanilla in-game screens)

Inventory of unowned screens: `docs/screen-inventory.md`. Picks (Codex, user asleep; mockups in `D:\tmp\cx-augaskin`): build HUD
`m6-build-a` (+ stealth eye from B), trader `m6-trader-a`, sign input + text viewer `m6-text-a` (+ raven bubble from B), black screens
`m6-load-b` (respawn without a progress bar), ship HUD `m6-ship-b`, compendium / split / barber `m6-dialogs-a`. Codex final: 15 of 16
groups 8/10, ship ladder 7 (reefed sail, round cap). New: `glyph_stealth_eye`, `glyph_crosshair`, `glyph_ship_oar`,
`glyph_ship_sail_half|full`, `glyph_rudder`, `glyph_teleport_swirl`, `glyph_stop`, `band_runes`, `bar_frame_action`.
- Panel titles (trader, text viewer, compendium, respawn banner): serif caps on the top rail with `knot` left + mirrored right
  (no `header_plaque` when the title is long).
- Build HUD (`Hud.m_pieceSelectionWindow`): standard panel; category row `tab_*` on `tab_strip` (any count, mod tabs, scroll);
  piece grid `slot_cell_recess`, hovered/selected `slot_cell_selected`; scroll `scroll_*`; selected strip (`m_buildHud`) = `frame_inset`
  well + icon in `slot_cell_recess` + requirement cells `socket_req` / `socket_req_missing` (counts text_ok / text_missing) + key hint
  `plate_key`.
- Crosshair: `glyph_crosshair` (40 sprite px, tint text_body ~0.9; bow crosshair stays vanilla); hover text sans + `plate_key`;
  piece health `bar_skill_trough` + `bar_skill_fill` (tint text_ok, vanilla gradient colour allowed); stealth `glyph_stealth_eye`
  (tint text_body hidden / ember targeted) + slim `bar_skill_fill` tint rune_glow under it.
- Trader (`StoreGui`): one list (any item count) in `frame_inset`, rows `row_recipe_*` with icon `slot_cell_recess`, price right-aligned with
  `glyph_coins` tint text_active (unaffordable price text_missing); buy/sell = `tab_*`; detail well title text_item_title; purse
  `frame_small` (or M7 `plate_currency`); buy/sell button `button_*` gold label.
- Text input (`TextInput`): panel, field `field_search` + `ring_select` tint rune_glow (focus), counter text_disabled only when the input
  has a limit, `button_selected` (OK) + `button_normal`. Text viewer (`TextViewer`): panel, `frame_inset` well, `band_runes` (Tiled,
  height 22 ref) above and below the text, serif body, close = `button_normal` with the key hint. Raven bubble: panel (big caps) +
  raven content + `button_normal` "[E] ...".
- Black screens (`Hud.m_loadingScreen`): tip = panel banner bottom-centre (title + `divider` + tip); respawn/sleep without real
  progress = `spinner_runes` inside `button_round`; sleep = `glyph_time_moon`; teleport = `minimap_ring` + `glyph_teleport_swirl`
  (tint rune_glow, slow rotation).
- Ship HUD: vertical popup column (small caps), ladder glyphs top-down `glyph_ship_sail_full`, `glyph_ship_sail_half`,
  `glyph_ship_oar`, `glyph_stop`, `glyph_chevron_down` (reverse); active = `slot_cell_selected` + glyph tint rune_glow; rudder
  `button_round` + `glyph_rudder` rotated by the rudder value; wind stays on the minimap ring.
- Action progress / stagger: `bar_frame_action` (Sliced, window inset L/R 40, T/B 9 sprite px) over `bar_trough` + `bar_fill` tint
  rune_glow; label serif. Event/raid line: `band_message` + `banner_rule` (small, mirrored) + ember serif text.
- Mount panel: popup + portrait in `button_round` + `bar_skill_trough`/`bar_skill_fill` (red / yellow). Chat: popup, names text_active,
  input `field_search`. Key hints: `plate_key` + sans label (row or column).
- Compendium (`TextsDialog`): panel, `tab_*` on `tab_strip`, topic list `frame_inset` + `row_recipe_*` (unread = `pip_quality_full`),
  reading pane `frame_strip`. Split dialog: panel + `slot_cell_recess` + slider (M5) + buttons. Barber: panel, style cells
  `slot_cell_recess`, selected `tab_square_selected`, colour slider = `bar_skill_trough` + vanilla gradient + `slider_handle_*`.
