# Theme contract (code <-> art bundle)

RuneSkin loads one AssetBundle named `runeskin`, placed next to `RuneSkin.dll`
(`BepInEx\plugins\RuneSkin\runeskin`). Unity build output: `unity\Build\runeskin`.

## Lookup

- Sprites are indexed by asset name from `LoadAllAssets<Sprite>()` and from every packed `SpriteAtlas`
  (`(Clone)` suffix stripped). 9-slice borders and pixels-per-unit come from the sprite import settings.
- TMP fonts are indexed by `TMP_FontAsset.name` from `LoadAllAssets<TMP_FontAsset>()`.
- Optional `TextAsset` named `runeskin-theme` (same JSON schema as `src\RuneSkin\Theme\default-theme.json`)
  replaces the embedded default theme, so art-side values (colours, padding) ship with the art.
- Dev override (wins over both): `BepInEx\config\RuneSkin\theme.json`, console `runeskin_reload`.
- A missing bundle or asset never crashes: the token resolves to nothing, the affected role keeps the vanilla
  look, the problem is logged (`[RuneSkin] theme: ...`) and listed in `runeskin_diag` dumps.

## Names the M0 theme expects

Art side: `art\sprites\sprites.json` (sprite names, borders, art tokens), `unity\Build\runeskin.contents.txt`.
Sprites are 2x art (PPU 200), SpriteAtlas `RuneSkin`; TMP materials are switched to the game's own SDF shader at load.

| Token | Asset name | Use |
| --- | --- | --- |
| `sprite.frame.panel` | `frame_panel` | dialog frame, Tiled, centre not filled |
| `sprite.button.normal` / `.hover` / `.pressed` / `.selected` / `.disabled` | `button_*` | button art per Selectable state (SpriteSwap) |
| `sprite.frame.tooltip` | `tooltip_frame` | tooltip frame, Sliced |
| `sprite.fill.glass` | `fill_glass` | dark glass backdrop, Sliced |
| `sprite.divider` | `divider` | horizontal divider, Sliced |
| `sprite.ornament.knot` | `knot` | menu entry ornament, shown on hover/selection |
| `font.display` | `RuneSkin-Display Cormorant-Bold SDF` | headers, menu entries, button labels |
| `font.body` | `RuneSkin-Body PTSans-Regular SDF` | body text, tooltips |

Panel stack (`Restyle\PanelDecor.cs`, children behind the host): `decor.glass` (`fill_glass`, inset `size.panel.glass.inset`), `decor.frame` (`frame_panel`), `decor.glow` (`frame_panel_glow`, `color.glow`), `decor.corner.*` (`frame_panel_corner_*`, native size), `decor.plaque` (`header_plaque`, Menu only).

Families need their tokens before RuneSkin takes them (asset preflight, `families` in the theme):
`Menu` = frame.panel, button.normal, font.display, font.body; `Tooltips` = frame.tooltip, font.display, font.body.

## HUD (M1)

Tokens -> assets: `sprite.hud.*` in `default-theme.json` (bar_frame, bar_trough, bar_fill, bar_lag, bar_growth, bar_end_line,
rail_frame_es/adrenaline, rail_trough/fill, dot_*, bar_reserved(+_edge), glyph_lock/es/seal, plaque_seal, overlay_frost/wet,
socket_status(+_more), plate_timer, socket_food(+_gem), food_timer_ring, socket_quick, plate_key, socket_hotbar, plate_number,
ring_select), `sprite.scrollbar.*` (scroll_track, scroll_handle_normal/hover/pressed).
Geometry is read from the sprites at runtime: bar/rail window inset = 9-slice border, window height = native height minus top and
bottom border (sprite px x 100 / PPU). Optional theme floats `size.hud.frame.inset.x/.y`, `size.hud.rail.inset.x/.y`,
`size.hud.bar.height`, `size.hud.rail.height` override for tuning. Sprite entries may name a `fallback` token (used and reported
while the asset is missing).