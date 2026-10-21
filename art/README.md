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
`D:\tmp\cx-augaskin`); `go -C art/tools run . sheet` -> `contact-sheet.png`.

## Rule: Codex makes the pixels

Every sprite pixel comes from Codex image gen (`cx.ps1 -NoSession -Img "<prompt>" -To <file>`), prompts kept next to
the sheets as `src/kit/*.prompt.txt`. The Go tool is mechanical only: crop, magenta / luminance key, resize,
9-slice assembly (corners kept, mid segments made periodic by cross-fade for Tiled), mirror, tone/leather grading,
alpha fades, transparent bar windows. New art = new kit sheet + `cuts.json` entry, never drawn in code.
Style targets and palette: `STYLE.md`.

## Layout

| Path | What |
| --- | --- |
| `src/kit/*.png` | Codex image-gen kit sheets (panel2, corners, controls, ctrl2, buttons, states, hud, hud2, fill, masks, fx) + `*.prompt.txt` |
| `src/kit/cuts.json` | cut spec: sheet rect, key, scale, nine/period, grading, Unity border/pivot/type/token/notes, palette |
| `tools/` (Go, `go -C art/tools run . <cmd>`) | `build`, `check`, `compare`, `compare-m2`, `sheet`, `zoom`, `sample`, `blobs` (piece rects on a sheet) |
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
