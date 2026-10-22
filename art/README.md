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
`D:\tmp\cx-augaskin`); `go -C art/tools run . sheet` -> `contact-sheet.png`. M2/M3: `compare-m2`, `compare-m3` (crafting: `compare-crafting-m3.png`, `compare-groups-m3.png`), `compare-m3s` (skills/trophies: `compare-skills-m3.png`, `compare-trophies-m3.png`, `compare-groups-m3s.png`), `compare-m4` (`compare-hud-m4a/b.png`, `compare-map-m4.png`, `compare-groups-m4.png`).

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

