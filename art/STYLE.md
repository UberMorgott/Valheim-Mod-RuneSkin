# RuneSkin style sheet (from the approved mockups)

Targets: `D:\tmp\cx-augaskin\mockup-1.png` (inventory + crafting, 1672x941), `hud-status-b.png` (HUD).
Measurements are mockup px at 1672 width; "ref px" = 1920 reference (x 1.148); sprites are authored at
2x ref px (x 2.297 from mockup px), PPU 200.

## Look

- Dark slate carved stone, low contrast, soft painterly light from the top-left; fine interlaced knotwork
  only at ends/corners, plain stone on long rails (stretch/tile safe).
- Near-black glass interiors, slightly translucent (world faintly visible).
- Corner caps: small inset stone block with one glowing cyan rune + a wide worn leather strap crossing the
  corner diagonally over the rail (part of the fixed corner sprite, overhangs the rail).
- Gold rim = active tab; cyan rim/glow = selection/focus; text light silver, titles serif caps.

## Geometry (mockup px -> sprite px)

| Element | Mockup | Sprite |
| --- | --- | --- |
| panel rail thickness | 10-12 | 26 (frame_panel border 32 incl. inner line) |
| panel corner cap (block + strap reach) | ~55-60 | 207x207, rune block 85x82 |
| glass inset under the rail | ~10 | 24 |
| inventory slot | 58, pitch 61.5 | slot_cell 128x124, border 18 |
| tab plaque height | 26-28 | tab_* 256x52, border 14 |
| craft/menu button height | 60 | button_* 328x70, borders L/R 84, T/B 18 |
| HP bar height (hud-status-b) | 104 | bar_frame 480x87, window 42 high |
| stamina/eitr bar height | 58 | bar_frame_slim 380x46, window 22 high |

## Palette (sampled)

| Token | Hex | Where |
| --- | --- | --- |
| stone | #2a343b | rail face |
| stone_block | #222b31 | corner/knot blocks |
| leather | #7e4630 | strap face (graded from the kit: sat x0.72, bright x0.78) |
| leather_edge | #4a2a1c | strap edges |
| rune_glow | #4fe0e6 | runes, inner glow line, select ring |
| glass_top / glass_bottom | #121d23 / #0b1419 | panel fill (alpha 0.9) |
| glass_highlight | #2395b7 | inner cyan line |
| groove_dark / groove_light | #05080a / #5c6d78 | carving |
| text_header | #d9dce0 | titles (serif caps) |
| text_body | #e3e6e8 | body |
| text_disabled | #6f767a | disabled |
| text_active | #e8b64a | active tab label (NEW token) |
| text_missing | #e0413a | missing materials, need counts (M3) |
| text_ok | #9fd66b | requirement met counts (M3) |
| text_item_title | #f0c419 | item title in the crafting detail pane (M3) |
| text_stat_value | #8ab4f0 | stat values (M3) |
| ember | #c0392b | missing-material rims, RecipeChains rune (M3) |

Pool colours (pool_*, dot_*, bar_*) unchanged. Opacity: fill_glass 0.9, slot_cell 1.0, frame_strip 0.97.
Glow: emissive only on runes, selection rims and the faint inner panel line (frame_panel_glow, tint rune_glow).

## Pipeline rule

All pixels are Codex image-gen output (`src/kit/*.png`, prompts beside them as `*.prompt.txt`).
`tools/` (Go) only crops, keys magenta / luminance, resizes, assembles 9-slice parts, grades tone and writes
`sprites/sprites.json` (cut spec: `src/kit/cuts.json`).
