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

| Token | Asset name | Use |
| --- | --- | --- |
| `sprite.frame.panel` | `rs_frame_panel` | 9-slice dialog frame (stone, rune border) |
| `sprite.frame.button` | `rs_frame_button` | 9-slice button frame |
| `sprite.frame.tooltip` | `rs_frame_tooltip` | 9-slice tooltip frame |
| `sprite.fill.glass` | `rs_fill_glass` | 9-slice dark glass backdrop |
| `sprite.divider` | `rs_divider` | horizontal divider (sliced) |
| `sprite.ornament.knot` | `rs_knot` | small ornament beside menu entries (simple, aspect kept) |
| `font.display` | `Cormorant-SemiBold SDF` | headers, menu entries, button labels |
| `font.body` | `PTSans-Regular SDF` | body text, tooltips |

Families need their tokens before RuneSkin takes them (asset preflight, `families` in the theme):
`Menu` = frame.panel, frame.button, font.display, font.body; `Tooltips` = frame.tooltip, font.display, font.body.
