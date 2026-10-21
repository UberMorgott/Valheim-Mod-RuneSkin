# RuneSkin M0 art pipeline - progress

- [x] Plan read (M0, asset pipeline, ThemeTokens); mockup-1 reviewed
- [x] Fonts downloaded to D:\Downloads\fonts-runeskin (Cormorant v4.002 upstream release, PT Sans 2.003W from google/fonts); OFL 1.1 verified; Cyrillic incl. Ё ё № « » present in both
- [x] Unity project created (6000.0.75f1), manifest trimmed to com.unity.ugui 2.0.0 + needed modules
- [x] AI surface textures (stone, leather) via cx -Img -> src/ai
- [x] Relief renderer (src/render_sprites.py) -> sprites/*.png (15) + sprites.json
- [x] Contact sheet + mock pause-menu composite reviewed (art/review, not committed); fixes: rune orientation, rail braid, leather size/stitching, no noise on glass
- [x] Unity BuildBundles.cs: sprite import (borders), SpriteAtlas (BC7 1024x512), TMP static SDF fonts (bake-report: 0 missing of 309 RU+EN), bundle build, contents check
- [x] Batchmode build OK: unity/Build/runeskin 1406178 B, 15/15 sprites + 2 fonts + atlas (runeskin.contents.txt)
- [x] README rebuild command verified, .gitignore (art, unity)

## Open points

- Sprites bind to the SpriteAtlas at load (late binding via m_SpriteAtlas, atlas in the same bundle, includeInBuild on); editor listing shows packed=False/texture null, which is editor-only behaviour - confirm in game. Fallback: SpriteAtlasManager.atlasRequested -> load "RuneSkin" from the bundle.
- Bundle carries TMP shaders (Distance Field + Mobile fallback); runtime may swap font materials to the game's Shader.Find("TextMeshPro/Distance Field").
- ThemeTokens ScriptableObject not in the bundle (type lives in src/, other worker); sprites.json token names are the contract.
- -nographics breaks atlas packing (editor crash), so the build runs with a graphics device.
