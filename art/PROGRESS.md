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

## M3 crafting / skills / trophies art (2026-10-22)

- [x] Crafting kit sheets (Codex): craft (star badge, first-pass pieces), craft2 (requirement upgrade socket, arrow plaque, missing box, quality pips, round button), craft3 (thin rework after Codex round 1: square tabs, search field, borderless recipe rows, inset well, stat leader, compact requirement cells), craftglyphs (24 glyphs: categories, search, arrows, stars, craft/upgrade/repair, check/missing, EpicLoot enchant/sacrifice/augment)
- [x] Build tool: `despill` (violet magenta spill -> glass hue) and `flatEnds` (centre-column RGB over borderless end caps) cut options
- [x] compare-m3 (`go -C art/tools run . compare-m3`): compare-crafting-m3 ([mockup-1 | ours | layout-a2 | ours]) + compare-groups-m3; Codex 3 rounds: 7 groups 8/10, recipe rows (3 groups) 7.5 - violet cap bands, fixed after round 3 with flatEnds (not re-scored, round cap)
- [x] Skills/trophies mockups (Codex, D:\tmp\cx-augaskin): skills-a list, skills-b 3-column cards, trophies-a boss strip + grid, trophies-b biome shelves
- [x] Pick (Codex, user asleep, criteria: mockup-1 style, readability, 9-slice buildable, dynamic counts): skills-a (single list scales to any number of modded skills, no category metadata needed) and trophies-a (scrolling grid handles unknown counts/biomes; boss strip metadata-driven and wrapping, unclassified trophies stay in the grid)
- [x] Skills/trophies pieces: m3glyphs (glyph_boss, glyph_death, glyph_sort_level, glyph_tab_skills/trophies/character), skillbar (bar_skill_trough, bar_skill_fill), frame_corner_small_* (popup corner caps); rest reused (tab_square_*, row_recipe_*, slot_cell_recess/_selected, frame_inset, tab_square_selected = boss cell)
- [x] compare-m3s: compare-skills-m3 / compare-trophies-m3 / compare-groups-m3s; Codex 2 rounds, final 8/10 all 8 groups
- [x] Bundle BUILD OK, 169/169 sprites
