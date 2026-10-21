# RuneSkin art pipeline

## Rebuild (from the repo root, PowerShell)

```powershell
python -I art\src\render_sprites.py; & 'D:\Unity\Editors\6000.0.75f1\Editor\Unity.exe' -batchmode -quit -projectPath unity -executeMethod BuildBundles.Build -logFile unity\Logs\build.log | Out-Null; $LASTEXITCODE
```

(`| Out-Null` makes PowerShell wait for Unity.exe, a GUI-subsystem binary; without it the call returns at once.)

Exit code 0 + `RuneSkin: BUILD OK` in the log = bundle `unity\Build\runeskin` (StandaloneWindows64, LZ4) built and
verified: it is loaded back and every `sprites.json` entry and both fonts are checked
(`unity\Build\runeskin.contents.txt`). Do NOT add `-nographics`: the sprite atlas packer fails without a graphics
device ("Unsupported Format 0", editor crash).

Review aid (not part of the build): `python -I art\src\contact_sheet.py art\review D:\tmp\cx-augaskin\ref.png`
-> `art\review\contact-sheet.png` (every sprite, magenta = 9-slice centre) and `art\review\composite-pause-menu.png`
(pause menu drawn with Unity Sliced/Tiled rules).

## Layout

| Path | What |
| --- | --- |
| `src/render_sprites.py` | height-field relief renderer: bevels, carved runes/braid, leather wraps, one light rig (key light top-left, cavity AO, cast shadows), 4x supersampled. Writes `sprites/*.png` + `sprites/sprites.json`. Deterministic. |
| `src/ai/` | AI surface textures (Codex image gen, prompts in `prompts.txt`): stone + leather albedo, used as periodic luminance detail only |
| `src/contact_sheet.py` | contact sheet + composite |
| `sprites/` | final sprites (authored at 2x reference px, PPU 200) + `sprites.json` |
| `fonts/` | Cormorant Bold 4.002 (display), PT Sans Regular 2.003W (body), `charset-ru-en.txt` (309 chars), `bake-report.txt`, `licenses/` (OFL 1.1) |
| `../unity/Assets/Editor/BuildBundles.cs` | import (9-slice borders, pivot, PPU), SpriteAtlas (BC7, no rotation/tight packing), static TMP SDF fonts, bundle build, contents check |

## sprites.json contract

- `border` in sprite px, Unity `spriteBorder = (left, bottom, right, top)`; `pivot` normalized.
- `imageType` = recommended `Image.type`; `Tiled` items have edge segments that repeat seamlessly (the period equals
  the centre segment), `Sliced` items are constant along their stretch axis or have negligible detail there.
- `token` = intended ThemeTokens name. `palette` = colour tokens the art was made for (glow tint, text colours).
- Panel: `fill_glass` (Sliced) inset 23 sprite px (11.5 ref px) under `frame_panel` (Tiled, fillCenter off),
  `frame_panel_glow` on top with the `rune_glow` tint, then the four `frame_panel_corner_*` caps at native size,
  pivot = their panel corner (never stretched). Minimum panel = 192 sprite px (96 ref px) per side.

## Fonts

- Sources: Cormorant v4.002 (github.com/CatharsisFonts/Cormorant release, `Cormorant_Essentials_v4.002.zip`),
  PT Sans Web 2.003W OFL (github.com/google/fonts `ofl/ptsans`). Downloads kept in `D:\Downloads\fonts-runeskin`.
- Both SIL OFL 1.1 (PT Sans: Reserved Font Names "PT Sans", "ParaType"; fonts ship unmodified, the bundle holds
  SDF atlases). Licence texts in `fonts/licenses/` must ship with the mod.
- Bake: `TMP_FontAsset.CreateFontAsset` (SDFAA) + `TryAddCharacters(charset, includeFontFeatures)` then Static,
  source font dropped, atlas non-readable, material shader `TextMeshPro/Distance Field` (same approach as the
  AugaSkin fork's `FontAtlasBake.cs`, baked directly instead of spliced).
