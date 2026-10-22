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

## M3 leftovers + polish (2026-10-22)

- [x] Recipe rows re-score after flatEnds: hover 8, filters/list top 8, selected/unavailable 7.5 -> unavailable = darkened idle strip, no ember marker, muted content -> 8.5
- [x] M3 polish (coordinator in-game review): kit m3polish (buttons x5, deep well, tab strip), polishglyphs (ranged, magic, refresh, plus, minus, settings); Codex button 8.5, wells 8, glyphs 8 (after `bold` 1), tab_strip 8 (after brighten)
- [x] Bundle BUILD OK 176/176

## M4 minimap / enemy HUD / messages (2026-10-22)

- [x] Mockups (Codex, D:\tmp\cx-augaskin): m4-hud-a/b (minimap, boss, enemy bars, damage, notifications, centre message, biome banner), m4-map-a/b (large map)
- [x] Pick (Codex, user asleep; D:\tmp\cx-augaskin\work\m45\pick-m4.out.md): minimap A (matches hud-v2-a), boss A (no per-boss portrait), enemy bars B (tier read from frame shape+colour), damage A (compact), notifications A, centre message B (readable over bright sky), banner B (open composition), map A (side legend scales to any pin/player count)
- [x] Kit sheets m4ring (ring, rune stone, wind socket, pin ring), m4bars (boss frame, 4 Vaettir tier frames, banner rule, message band), m45glyphs (sun, moon, wind, chevron, bar fill mask); build options `hole`, `fadeV`
- [x] compare-m4: Codex 3 rounds -> 8 of 9 groups 8/10; map legend bottom (zoom slider) 7 (handle switched to the hover state after the cap, not re-scored)

## M5 pre-world screens (2026-10-22)

- [x] Mockups m5-{menu,char,world,load,settings}-{a,b}; pick (Codex, pick-m5.out.md): all A (vertical menu takes mod entries, list scales to any character count, one-panel world select needs fewest vanilla changes, compact connecting + bar, vertical settings tabs take mod tabs); toggle = checkbox (Unity Toggle, no thumb animation); dropdown A (TMP_Dropdown); keybind A (one binding per action as vanilla)
- [x] Kit sheets m5kit (danger buttons, slider diamond x3, checkbox), spinner2 (rune spinner); reuse: frame_boss as loading frame, plate_key keybinds, tab_* categories
- [x] compare-m5: Codex 3 rounds -> 8 of 10 groups >= 8; connecting spinner 7.5 (tail glow weak), loading fill 7.5 (flat) - round cap
- [x] Bundle BUILD OK 200/200

## M6 vanilla in-game screens (2026-10-22)

- [x] Inventory of unowned screens: docs/screen-inventory.md (24 vanilla entries, 20 mod entries)
- [x] Mockups m6-{build,trader,text,load,ship,dialogs}-{a,b} (Codex image gen, D:\tmp\cx-augaskin, prompts D:\tmp\cx-augaskin\work\m67)
- [x] Pick (Codex, user asleep, AgentLink chat 11cdaf31): build A (text tabs keep unknown mod categories; + B stealth eye), trader A (one list = StoreGui), text A (one scrolling text, plain input; + B raven bubble), load B (bottom tip housing; no invented death heading/progress), ship B (vertical ladder fits the native indicators, light raid line), dialogs A (list/detail, slider split, appearance grids reuse controls)
- [x] Kit sheets m6glyphs (eye, crosshair, oar, rudder), m6fix2 (sails as longship icons, outlined stop, wispy swirl), m6fix3 (band_runes, bar_frame_action); 10 new sprites
- [x] compare-m6: Codex 3 rounds -> 15 of 16 groups 8/10; ship ladder 7 (reefed sail reads as a T, round cap)
