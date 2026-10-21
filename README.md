# RuneSkin

Valheim UI skin (BepInEx): restyles the live vanilla uGUI in place from a data-driven theme (carved stone frames,
rune glow, dark glass). No prefab replacements, no transpilers; unknown UI is left untouched and reported.

## Layout

- `src/RuneSkin.API` - `RuneSkin.API.dll`, small typed API other mods reference softly (`RuneSkin.API.Skin`).
- `src/RuneSkin` - the plugin (`morgott.valheim.runeskin`):
  `Theme/` (ThemeTokens, JSON schema, bundle loader), `Classify/` (Classifier), `Restyle/` (Restyler, journal marks),
  `Ownership/` (frozen screen-family ownership), `Diagnostics/`, `Responsive/`, `Api/` (API backend),
  `Screens/` (per-screen adapters).
- `unity/` - Unity 6000.0.75f1 project that builds the `runeskin` asset bundle; `art/` - sources.
- `docs/theme-contract.md` - asset names the theme expects.

## Build

```powershell
dotnet build RuneSkin.sln -c Release            # or -p:ValheimDir=<game dir>
```

Deploy `src\RuneSkin\bin\Release\net472\RuneSkin.dll`, `RuneSkin.API.dll` and the `runeskin` bundle to
`BepInEx\plugins\RuneSkin\`.

## Config (`BepInEx\config\morgott.valheim.runeskin.cfg`)

- `[Ownership] Menu`, `Tooltips` - families RuneSkin styles; frozen at startup. AugaSkin yields owned families.
- `[Ownership] RequireAssets` - take a family only when its art loaded.
- `[Layout] SkinScale` - 0.85-1.15 on top of the game's UI scale.

Console: `runeskin_diag [screen]` (dump to `BepInEx\RuneSkin\diag`), `runeskin_reload` (theme).
