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

## M1 HUD art (2026-10-21)

- [x] Spec read (hud-resource-states.md, plan M1 + 8a), refs hud-status-b / hud-v2-a / hud-v2-b
- [x] render_sprites.py: 36 HUD sprites (bar frame/trough/fill/lag/growth/end line, ES + adrenaline rails, 3 DoT masks,
      reserved + edge + lock, seal plaque + seal, ES glyph, frost/wet overlays, status/food/gem/quick/hotbar sockets,
      plates, select ring, food timer ring, scrollbar track + 3 handle states); palette tokens added
- [x] hud_review.py: contact-sheet-m1, composite-hud, dot-grayscale reviewed; fixes: wider leather bands, softer
      fill grain, larger frost shards and wet drops
- [x] Bundle BUILD OK: 52/52 sprites + 2 fonts (runeskin.contents.txt)

## Open points

- HUD sprites: pool glyphs for HP/stamina/eitr (left cap socket) not made yet (M2 glyph set); ES rail number sits
  over the right cap in the composite, layout must reserve ~40 px or place it outside.

- Sprites bind to the SpriteAtlas at load (late binding via m_SpriteAtlas, atlas in the same bundle, includeInBuild on); editor listing shows packed=False/texture null, which is editor-only behaviour - confirm in game. Fallback: SpriteAtlasManager.atlasRequested -> load "RuneSkin" from the bundle.
- Bundle carries TMP shaders (Distance Field + Mobile fallback); runtime may swap font materials to the game's Shader.Find("TextMeshPro/Distance Field").
- ThemeTokens ScriptableObject not in the bundle (type lives in src/, other worker); sprites.json token names are the contract.
- -nographics breaks atlas packing (editor crash), so the build runs with a graphics device.

## Art redo to match the mockups (2026-10-21)

- [x] Style sheet from mockup-1 / hud-status-b (STYLE.md)
- [x] All sprites re-cut from Codex image-gen kit sheets (src/kit, prompts kept); procedural Python renderer + AI albedos retired
- [x] Go mechanical pipeline art/tools (build/check/compare/sheet); 61 sprites (52 old names + slot_cell(_selected), tab_*, bar_frame_slim, frame_strip, frame_small, separator_v)
- [x] Compare images + 3 Codex review rounds (final: inventory 7.5, HUD 7.5, corner 6.5)

## M2 inventory art, layout A (2026-10-21)

- [x] Codex kit sheets doll (12 slot glyphs), actions (18 action/sort/stat/panel glyphs + equipped marker, link, occupied hand), doll2 (mirror cells, hotbar header, number plate, lock badge), doll3 (frosted halo backdrop, silhouette, stat plate, glyph buttons), fx3 (lock veil, scroll fade), fx4 (wet drips redraw)
- [x] 46 new sprites (114 total), slot_cell_recess graded from slot_cell; overlay_wet redrawn 160x88 + _2/_3 variants
- [x] compare-m2 (go -C art/tools run . compare-m2 [old_wet.png]): compare-inventory-m2 / compare-dock-m2 / compare-groups-m2; Codex 3 rounds, final 8/10 all 10 groups
- [x] Bundle BUILD OK, 114/114 sprites
