# RuneSkin tasks

Plan: side-session notes `ui-skin-engine-plan.md` (M0..M8). Art bundle + unity project: separate worker.

## M0 (code)

- [x] Codex review of module boundaries/API (chat 11cdaf31): replay early registrations, Supports != IsAvailable,
      ownership only after asset preflight, conditional journal rollback, Menu postfix after AugaSkin, tooltip refit
- [x] Repo scaffold: sln, RuneSkin + RuneSkin.API csproj, deterministic path-independent build, .gitignore
- [x] ThemeTokens (embedded JSON + bundle TextAsset + override, schema validation, last-good fallback, bundle loader)
- [x] Classifier (registration > adapter > semantics > sprite data), SkinMark
- [x] Restyler with property journal + conditional revert
- [x] Ownership registry (frozen, preflight), Diagnostics (counts, unknowns, dumps, floor/safe-area checks)
- [x] Responsive base (tuple compare, SkinScale on content roots)
- [x] Public API (all signatures; M0 implements RegisterRoot/Bind/Invalidate/RegisterPanel record)
- [x] Menu adapter, Tooltip adapter (+ frame for box-less tooltips)
- [x] GitHub repo + push (78a966d, <https://github.com/UberMorgott/Valheim-Mod-RuneSkin>)
- [x] AugaSkin yields owned families (soft dep + skip list), 2.2.9 (Auga c8d1133; push after in-game check)
- [x] quality gate wired + clean (qgate -All -Full exit 0)
- [x] Harness: RuneSkin in autotest `$mods`, shot `ui-runeskin-m0` (tools 9191ff1, pushed)
- [x] In-game run 1920x1080 (tools\out\20261021-141846): Menu unknown=0 styled=64 degraded=20 (knot art missing); item tooltip styled=4 unknown=3 (EL scroll view: fixed after the run, not re-run)
- [x] Codex final diff review: 8 findings fixed (Bind reset, nested-root revert, replay isolation, runtime ApiVersion, tooltip root pruning, font/material pair revert, stricter shot checks)

## Later

- Settings family; Compendium entry to MorgottTweaks before AugaSkin retires

## M0 completion (2026-10-21)

- [x] Panel stack (glass, frame, glow, corners, plaque) on Menu + tooltip (PanelDecor)
- [x] Knot art added (art pipeline), degraded 0
- [x] Menu entry hover: Button Transition.Animation + Animator -> MenuEntryStyle (ColorTint on label, knots on hover/select)
- [x] Final run tools\out\20261021-143820: Menu styled=92 degraded=0 unknown=0; tooltip styled=9 degraded=0 unknown=0; offSafeArea=0
- [x] Tooltip text touches the frame rail on box-padded-0 tooltips: ContentInset raises LayoutGroup padding to size.tooltip.content.inset (M1)

## M1 (code, 2026-10-21)

- [x] Codex structure review (chat 11cdaf31): own renderer, wrapper suppression, own status duties, bounded forecasts, floors win, named action
- [x] API v2: ResourceState (Max=B, Available=C, reserved split, conversion, phase, MainBar, Inert), RegisterCapacityProvider, ResourcePools; Supports effect/resource/capacity.render while Hud owned
- [x] Providers registry (latest wins, 3 faults -> disabled, sanity check hides extension)
- [x] HudMath (bar length clamp, ghost step, stripes, reserve, flow wrap + "+N", cluster scale floors, 16:9 box) + EffectTaxonomy + ResourceCheck, 66 unit tests
- [x] HudCluster (family Hud): bars HP/stamina/eitr + ES/adrenaline rails, lag + growth ghosts, DoT stripes, reserve + lock, overlay, plaque, paused rune; food sockets; status strip + full list (F7 / Alt+DPadDown); hotbar top-left hidden with inventory; EAQS quick slots + GP placed after food
- [x] Frame window geometry read from sprite borders (art may change)
- [x] Scrollbar roles (track / handle + state sprites) in every skinned root
- [x] MorgottTweaks 1.122.0 RuneSkinHud providers (2c23285); AugaSkin 2.2.10 yields Hud (e2d1b70)
- [x] In-game run ui-runeskin-m1 (tools 2021c6a) with art ad65a9c: tools\out\20261021-153950 all 6 checks PASS (Hud unknown=0 degraded=0, MT providers, +N list, EL tooltip, 21:9 box)
- [x] Codex final review: 9 findings fixed (1392c13, MT c57b773)
- [ ] Acceptance with mockup art df8dc3a: Codex HUD 5/10 (runs 20261021-165440, -172719) -> 7/10 -> 7.5/10 -> 7.5/10 (runs 20261021-182522, -185104, -185500; 2560x1440, m0+m1 10/10 checks PASS, 0.2.3). Fixed: broken frames = HUD canvas referencePixelsPerUnit 50 (dump refPpu=50) vs geometry at 100 -> HudArt multiplier refPPU/100/scale; fixed equal bar width size.hud.bar.outer 438 (user call, proportional + growth ghosts removed, max step pulses value/frame); HP fill always red (no whole-bar tint); lag ghost alpha 0.3 / hold 0.3 s; wet = 3 drip clusters; rail numbers inside the window; Menu without empty plaque; tooltip double glass + bottom inset 30. Remaining = art: glyph_stamina / glyph_eitr 40x40 masks (slim recess (49,23) r 12), bar_fill bevel/texture, dot_poison sparser 3-4 px dots
- [x] Panel corner caps above content (PanelCaps sibling container, 04539ed)
- [x] Menu / tooltip canvas refPpu=50 (dump): Restyler multiplies every themed Sliced/Tiled multiplier by refPpu/100, corner caps sized at reference 100 (Restyler.SetReferenceSize); tooltip content inset 26 / bottom 44 (0.2.4)
- [x] Stamina / eitr pool glyphs (Codex glyphs.png) in the slim recess (theme recess.bar_frame_slim.x/y/r); bevel bar_fill + sparse dot_poison (fill3.png); fill-end FX front_health/stamina/eitr + clear wet drips (fx2.png); GP socket + ring (gp.png) (0.2.4)
- [x] 0.2.4 layout (user + lead calls): gap 0, slim x1.1, value centred in the fill, debuff-only strip with count-length frame (hidden at 0, one row reserved), buff row above the food, food zigzag in a frame_small group, own guardian-power socket (cooldown / active ring, click = StartGuardianPower, vanilla m_gpRoot suppressed); Codex HUD bars 8/10 (run 20261021-193600)
- [x] Frame overlays per art notes (wet drips from frame bottom, since 0.2.3 three clusters, frost cluster native at top-left), trough/glass alpha 1.0, cluster width fit measures food row + EAQS/GP (0.2.2)
- [x] 0.2.5: panel content inset from corner-cap keep-out (theme clear.<cap> = alpha-measured min(dx,dy) 85-87 sprite px -> 43.5 ref; PanelMath.CapInset; tooltip ContentInset all sides + column controls child width, menu FitToChildren pad), opaque panel backing (decor.backing color.panel.backing #101C22FF under the glass; single 0.9 glass let the world through), HUD frames rail to rail (theme edge.<frame>.top/bottom, HudMath.StackStep, pixel-snapped), food checkerboard (HudMath.DiamondStep, equal diagonal steps, AugaSkin geometry); runs tools\out\20261021-204453 (m0+m1 12/12 PASS) + 20261021-205505 (m0 recheck 4/4 PASS)
- [ ] EAQS quick-slot placement not verified: bar empty in the test save (harness check reports n/a, tools commit)
- [ ] Hotbar slot art (socket.hotbar, plate.number) and quick-slot art: M2 with doll slots
- [ ] Reserve tooltip (auras / summons) on hover: needs inventory-open hit area (M2)
- [ ] Bar-cap pool glyph sockets are empty in the art (no HP/stamina/eitr glyphs yet)

## M2 (code, 2026-10-21) - inventory layout A

- [x] Codex adapter design review (chat 11cdaf31): 12 points adopted (no m_uiGroups insert, equip via UseItem queue, EAQS rows not grid height, reposition not rebuild, dock fallback = halves + scroll, action instance context, ownership by property)
- [x] InventoryMath (pure, 10 tests): Full/Narrow/Stacked by width, centred 1920 column, HUD band, safe area, SkinScale, 44 px physical floor, container fit + scroll, dock split by needed height, doll rows + scroll
- [x] InventorySkin: panel placement (m_player/m_container/m_info/crafting place), grid pitch via m_elementSpace + reposition, EAQS cells on the doll (3x3 + appended), quick cells over the HUD quick bar, hand mirrors (drop = UseItem equip, click = source highlight, right-click = unequip, 2H linked), RegisterSlot cells, hotbar header + number plates, equipped marker, slot overlays
- [x] API v3: SetSlotOverlay(owner, Inventory, x, y, token); PanelActionDescriptor.OnInvokeFor/EnabledFor (panel inventory); Supports panel.dock/slot.render/panel.action/slot.overlay while Inventory owned
- [x] Action bars: vanilla Take all / Stack all as glyph buttons (listeners kept), registered actions per panel kind, hotkeys gated
- [x] Backpack dock: m_container showing the worn backpack, fork AB_BackpackPanel or Skin.RegisterPanel("inventory.backpack")
- [x] AugaSkin 2.2.11 yields Inventory (EaqsSlotPanel, TrashPlate_Skin, slot templates/marks, owned subtrees)
- [x] Harness shot ui-runeskin-m2
- [x] Gamepad: EAQS spatial DPad navigation reaches the doll cells from our placement, hidden doll rows scroll into view; vanilla m_uiGroups + per-grid selection kept (focus memory = per grid)
- [ ] Gamepad focus on hand mirrors / API-registered doll cells (X on the source grid cell toggles equip meanwhile)
- [x] Placement runs in LateUpdate (InventoryGui Animator writes panel positions after Update), world-measured cell placement
- [x] Art f907652 pickup (recess cells, glyphs, mirrors + link badge + occupied hand, hotbar header digits, icon buttons, stat plates, header glyphs, wet drip variants)
- [x] In-game ui-runeskin-m2 run tools\out\20261021-222046: 9/9 PASS (2560x1440 inventory, small chest, large chest, backpack alone, chest + backpack; 1920x1080 large chest + backpack), Inventory dumps unknown=0 degraded=0
- [x] Compare D:\tmp\cx-augaskin\compare-ingame-inventory.png, Codex 5.5 -> 7 -> 8/10
- [x] MorgottTweaks 1.123.2: TrashSlot.LayoutColumn yields when Skin.IsOwned("Inventory"); RecipeChains MT_ChainTree docks inside crafting over the detail, above the requirements
- [x] 0.3.1: one top + one bottom edge for dock / centre / crafting (bottom = live HUD cluster top, buff row reserved, HudCluster.ReservedScreenTop), chest fills the dock, backpack keeps content height under it, no container = no dock (centre + crafting re-centre); doll + grid pitch shrink by one factor; footer plate_stat plates (armour, coins, weight, trash); name on header_plaque in capitals; crafting width-fit + column height. Run tools\out\20261021-224935 9/9 PASS; Codex geometry 6.5/10
- [x] 0.3.2 Codex 6.5 fixes: header + inner gaps give height back before cells shrink (size.inv.header.min, size.inv.inner.gap.min); info tabs own row, Background + icon sized with the button; doll silhouette portrait box (share.inv.doll.figure 0.66), storage | figure | mirrors centred (InventoryMath.Strip); dock grid view top-aligned = visible rows (ContainerGridView); dock action row inset by corner-cap keep-out (ActionRow, PanelDecor.ContentInset); backpack panel cells keep slot.cell (were tagged action.button); empty-cell glyphs 0.82 scale, softer tint
- [x] HUD band (user call 2026-10-21, inventory-layout.md 4a): inventory open -> HudCluster band profile under the column block (block width, edge-aligned): [food | GP | Z/V/B] [bars, equal length], status row below (buffs left, debuffs under bars), one panel housing; closed -> M1 stack. Columns 684 -> 830 tall at 1080, grid pitch 59.5 -> 78. Unit tests InventoryBandTests (9)
- [x] 0.3.2 runs tools\out\20261021-234304, -235153, 20261022-000221: 9/9 PASS each (harness now deploys RuneSkin + API + bundle, deploy=PASS hash=PASS); band in one panel housing (glass + frame + caps, content clear of caps), header 150 (reclaim floor 136, bigger tabs); compare D:\tmp\cx-augaskin\compare-ingame-inventory.png (+ -a3), Codex 6.5 -> 7.5 -> 8/10
- [x] Band values (stamina / eitr numbers) small on the long slim bars (Codex 0.3.2 #3): 0.3.3 slim bars x1.4 (window ~15 ref px) + value font 19, normal stacked HUD too (user approved)
- [x] 0.3.3 compact band (user call 2026-10-22): band = content height; left block one straight row of size.hud.band.cell 76 sockets (food diamonds side by side, GP, Z/V/B quick sockets = HudCluster.BandQuickCell boxes the inventory quick cells sit on), status row = one cell + pads; run tools\out\20261022-010617 (m1+m2 17/17 checks PASS)
- [ ] HUD-side: EAQS quick cells placed over the HUD quick block (empty bar in the test save; verify with items)

## M3 art (2026-10-22) - crafting kit + skills/trophies pick

- Skills = design skills-a (D:\tmp\cx-augaskin\skills-a.png): one list panel in the character tabs, rows icon/name/gold level/cyan progress + %, hover/focus tooltip. Picked by Codex (user asleep): single list scales to any number of modded skills without category metadata; readable; built from the 9-slice kit.
- Trophies = design trophies-a (D:\tmp\cx-augaskin\trophies-a.png): boss strip (gold cells + boss emblem) over a scrolling trophy grid, locked = dark icon, tooltip. Picked by Codex: grid handles unknown trophy counts/biomes; boss strip must be metadata-driven and wrap/scroll, unclassified (modded) trophies stay in the main grid, no fixed 7 bosses or 9x6.
- Rejected: skills-b (3 fixed categories need category data for modded skills), trophies-b (biome shelves need biome metadata for modded trophies).
- Sprite mapping for code: art\README.md "M3 sprites".

## M3 (code, 2026-10-22) - crafting column, skills, trophies

- [x] Codex adapter design review (chat 11cdaf31, 15 points): requirement array grown per GUI (paging never triggers), hidden cells reactivated by layout, shortage from V+ "have/need" text else vanilla blink with hold (no own recount), reveal selection after re-pitch; open: VNEI internal SetSize, description scrolling, RecipeChains height contract
- [x] Family "Crafting" (needs Inventory; AugaSkin 2.2.12 yields m_crafting + recipe template): CraftingMath (pure, 7 tests): header (station icon | serif title | level star), mode tab row (any tab count, scrolls), CraftingFilter category row (kit glyph_cat_*, scrolls), search (field_search + magnifier), list well | detail well (title, icon, description, wrapping requirement grid N cells, no vanilla paging) + button row (round repair | craft with act glyph)
- [x] Compat: VNEI tab + panel placed over the body; Epic Loot row icon / magicItemIconBG / RepairButton/Glow paths kept; CraftingFilter MT_CraftFilter re-placed (names kept); RecipeChains MT_ChainTree docks above the requirement grid, styled frame_missing; V+ CraftFromChest "have/need" parsed for the missing socket state
- [x] Families "Skills" / "Trophies" (AugaSkin 2.2.13 yields): skills rows icon socket / name / progress-to-next-level bar + % / big level, any skill count; trophies = every Trophy item in ObjectDB (vanilla m_trophyPos order, unlocked vanilla elements repositioned, locked = pooled dark cells), boss strip data-driven (Character.m_boss drops), count "unlocked / total", tooltips
- [x] Harness shots ui-runeskin-m3 (workbench, 6-requirement recipe, no paging) + ui-runeskin-info (skills, trophies); runs tools\out\20261022-011243, -014448, -015602 (open flake), -020045 (22/22 PASS, Almanac parked)
- [x] Almanac (pack mod) replaces the trophies window: family "Almanac" (ModScreens\AlmanacSkin, restyle only: panel stack on its bkg rect, tabs craft.tab + selected overlay, grid cells slot.recess / slot.focus, list rows craft.row, wells, buttons, dividers, legacy Text colours via Restyler legacy branch; refresh hooks on AlmanacPanel Show/On*Tab + nested Create*); AugaSkin 2.2.14 yields Compat.Almanac. RuneSkin trophies show only without Almanac
- [x] Skills / trophies: in-panel info tab row (InfoTabs: mirrors InventoryGui.m_info buttons, own tab selected, click = close; tab_strip ledge), title knots, close buttons hidden; skills summary well (death glyph + skill loss, sort toggles level / name, InfoMath.SkillOrder), full-screen closer + ListRoot no longer painted (button / scrim roles were filling the screen)
- [x] Crafting polish: craft button role craft.button (redrawn button_*), round repair only while repairable (else craft spans the row), long descriptions scroll (own clipped ScrollRect view), RecipeChains box capped (size.craft.chain.max / .min) with a scrolling tree, VNEI grid sized to the body (BaseUI.SetSize by reflection, CraftingMath.VneiCells), rows 40 / font 15, Ranged / Magic category glyphs
- [ ] Final small fixes after the last run unverified in game: skills title backdrop + total text, trophy count / boss label prefix / boss icon fill / one-row strip, level-requirement socket station icon, 2-line ingredient labels

## M4 art (2026-10-22) - minimap, enemy HUD, messages, damage text

- Picks (Codex, user asleep; mockups D:\tmp\cx-augaskin\m4-*.png, reasons in art\PROGRESS.md): minimap = m4-hud-a (round knotwork ring, N/E/S/W rune stones, wind socket, sun/moon on the top arc, biome plate); boss bar = A (symmetric, no per-boss portrait, unique tier); enemy bars = m4-hud-b (rarity read from frame shape + colour: Vaettir normal/magic/rare/unique); damage text = A (compact, font only); notifications = A; centre message = B (soft dark band); biome banner = B (open: knotwork rules, no plate); large map = m4-map-a (side legend scales to any pin type/player count, Jotunn pins with their own icons, scrolls).
- Rejected: minimap B (runes cut in the ring, curved leather ribbon), boss B (needs a portrait per boss), enemy A fills, map B (bottom toolbar overflows with mod pin types).
- Code pickup: art\README.md "M4 sprites" (minimap_mask = Unity Mask; VT_Frame = frame_enemy_* Sliced over Health; damage text body font + Bold/dilate).

## M5 art (2026-10-22) - pre-world screens

- Picks (Codex): main menu = m5-menu-a (vertical button column takes mod entries; news secondary, hidden when no room); character = m5-char-a (scrolling list; delete confirm as a centred modal, initial focus Cancel; vanilla appearance controls kept); world/server = m5-world-a (one panel with tabs, simple join/password dialog, only data the game has); connecting/loading = m5-load-a (spinner when no real progress, bar only with progress); settings = m5-settings-a (vertical category list incl. mod tabs, separate scroll for categories and content, pinned bottom buttons). Controls: slider A (diamond handle), toggle = checkbox (Unity Toggle), dropdown A (TMP_Dropdown, opens upward when needed), keybind A (one plate per action as vanilla).
- Rejected: menu B (bottom rail overflows), char B carousel, world B (invented server data), load B (fixed rune sequence), settings B (two-column, second binding not supported by vanilla).
- Code pickup: art\README.md "M5 sprites".

## M4 (code, 2026-10-22) - minimap, enemy HUD, messages, damage text (0.5.0)

- [x] Families Minimap / EnemyHud / MessageHud / DamageText (live vanilla restyle, no prefab swaps); AugaSkin 2.2.15 yields them (3c11946), 2.2.16 skips its AugaLog hooks under the MessageHud yield (cfc8b31: AugaMessageLog never exists, the SpawnPlayer NRE looped respawns), both published
- [x] Minimap (MapScreen\MinimapSkin, MinimapMath + tests): small root -> RuneSkin_MapMask (minimap_mask Unity Mask; the map shader takes the stencil, inscribed-square fallback + note otherwise) holding map / MapClick / name pins, minimap_ring, N/E/S/W rune stones + Latin letters, wind socket + glyph_wind (rotation of the vanilla wind marker, on a ship only), sun / moon glyph in a button_round socket on the top arc (EnvMan day fraction), biome name on header_plaque; ring 220 ref clamped to 18 % safe height, top-right of the safe / 16:9 box; pins outside the hole culled (CanvasGroup)
- [x] Large map (MapScreen\LargeMap): panel stack, map inset left of a legend well housing IconPanel / IconPanel2 (mod filters) / IconPingPanel / Public+Shared panels with slot_cell_recess sockets, title plaque = cursor biome else player biome, role pass (Jotunn overlay panel: well / button / checkbox), own Canvas sorting, player HUD cluster hidden while open
- [x] EnemyHud (WorldHud\EnemyHudSkin, EnemyHudMath + tests): templates restyled once (bar_trough, bar_enemy_fill, outlined body-font names, badge stars, RuneSkin_EnemyFrame fitted to Health, boss header plaque, x1.35); Vaettir VT_Frame kept, redrawn per tier with frame_enemy_<tier> after its UpdateHuds postfix; Vaettir's own boss bar (enhanced bosses) untouched (Codex agreed)
- [x] MessageHud (WorldHud\MessageSkin): centre text serif + band_message following its fade; top-left notification row (icon left, frame_small plate, below the summon frames); biome banner (band + mirrored banner_rule from rendered title bounds + dividers, vanilla decor off); unlock messages plate / recess / fonts
- [x] DamageText (WorldHud\DamageTextSkin): body font Bold + outline / dilate on the template, sizes 24 / 15, colour per TextType (self red kept)
- [x] Harness ui-runeskin-m4 (tools 61c5d23, 42abb40); runs tools\out\20261022-033704 (driver NRE from the AugaSkin respawn loop; Almanac 0.4.2 fix verified: wells, no black block), -035103, -035440 (12/12 PASS), -040729 (12/12 PASS, final)
- [x] Codex (chat 11cdaf31) r1 -> r2: minimap 7.5 -> 7.5, enemy HUD 6.5 -> 7, messages 5.5 -> 8, large map 5.5 -> 6.5; r2 fixes coded after the last run (unverified): plaque text clear of the knot ends, health_fast deep red / slow = lag ghost, HUD cluster hidden under the large map
- [x] Enemy frame window reads vertically compressed: Health 5 -> size.enemy.bar.h 11 (GuiBar writes only the horizontal size; non-stretched children scaled by the same factor), frame borders at a fixed scale.enemy.frame.border 1.3 with the Sliced centre stretched to the bar (window fitting gave giant caps, run 20261022-043546), name / stars lifted clear of the frame top (0.5.1)
- [x] Large map legend as labelled rows (MapLegend: header + divider, row per pin-type button = socket + vanilla icon button + localized name + checkbox on m_visibleIconTypes / ToggleIconFilter, mod filters click themselves; Public / Shared toggles; players from ZNet.GetPlayerList; zoom row minus | slider on LargeZoom (log scale) | plus; key hint line under the map with KeyHints on); hotbar hidden with the cluster under the map. Localisation: Theme\Words adds "$runeskin_*" keys to the game Localization on SetupLanguage (EN + RU); SlotNames moved there (0.5.1)
- [x] Verify in game (harness ui-runeskin-m4 states sail + unlock + checks m4-map-legend / m4-map-hud-hidden / m4-enemy-bar-height / m4-wind-socket(-off) / m4-unlock): run tools\out\20261022-043546 23/23 PASS; unlock plates dropped below the notification row (size.message.unlock.drop)

## M5 (code, 2026-10-22) - pre-world screens (0.6.0)

- [x] Families MainMenu / Settings / Connection (restyled in place, no prefab swaps; AugaSkin 2.2.17 yields them: FejdStartup Start / ServerListGui / UnifiedPopup restyles, Settings.Awake restyle, ZNet dialog prefab swap skipped; FejdStartup.Awake keeps the cinematics-button hide + font fallbacks; VanillaPanels skips ManageSavesMenu / EndCredits)
- [x] Classifier control families: Toggle = checkbox_box + glyph_check, Slider = bar_skill trough / fill + slider_handle_* (SpriteSwap), TMP_Dropdown = field_search + chevron, list frame_inset, items row_recipe_*, input fields = field_search; TabHandler tabs = button plates, selected (non-interactable) = button_selected
- [x] PreWorldKit: vanilla wood / backdrop panel images -> RuneSkin panel stack (PanelSwap journal), ButtonPlate for text-only buttons (Animator off, plate = target graphic), RuneSpinner (45 deg steps, unscaled time), fallback roles (sprite-less = skip, InputFieldBackground = field, other vanilla icons = content)
- [x] Main menu: plated entry column left under the logo (logo top-left x0.62), MenuList backdrop off, version label text.version; character select = scrolling CharacterList (rows: initial socket, name, save source; click = m_profileIndex + UpdateCharacterList; carousel arrows hidden); remove dialogs: Yes = button_danger, Cancel = UIGroupHandler default; creation colour sliders keep the vanilla gradient; world / server row templates restyled once (MainMenu.<template>); loading + please-wait spinner; UnifiedPopup restyled
- [x] Settings: category column (TabHandler buttons stacked left, window widened by the column, children shifted right), Ok / Back buttons
- [x] Connecting dialog: rune spinner left of the text (vanilla hammer icon off); password dialog panel / field / buttons
- [x] Harness ui-runeskin-m5 (tools 86b7df5)
- [x] Runs tools\out\20261022-045045 (discovery), -053130, -053740 (16/16 PASS, unknown=0 in every family), -055949 (final: m5 + m4 + m0 + 27e/27f/27g/27h/28z smoke: respawn + logout to menu PASS, AugaSkin 2.2.17 clean)
- [x] Codex M5 scores (chat 11cdaf31) r1 -> final: menu 6.5 -> 7, characters 6 -> 6.5, delete 6.5 -> 7, worlds 5.5 -> 6.5, servers 6 -> 7, settings 5.5 -> 7, settings/controller 5 -> 6.5, connecting 7 -> 7.5
- [x] Menu-return NRE in CharacterList.Sync verified fixed (runs -064153 / -065352 / -071214: logout to menu + 28zb re-enter world, no NullReference)
- [x] Character action bar on screen at 2560x1440 and 1920x1080 (harness m5-charbar-onscreen, lift 110)
- [x] Polish 0.6.1 (Codex r4/r5): menu entries lit rim (role button.menu, ppuMultiplier 0.45 for the knot caps), links 330x46 / 22 px centred; Start / world Start / Settings Ok = button.primary + gold label; delete confirm: Yes danger plate + red label, buttons 150x46, character name shown (autosize 30..16; TMP truncation hid it), list below the modal; world rows 58 ref two-line (template + m_worldListElementStep, selection sprite on the template), mode tabs 44 high; server filter labels fit; settings gamepad page inset 0.93; connecting spinner 64 follows the laid-out text centre; map legend 420 / rows 52 / font 24, hotbar + EAQS quick bar hidden under the large map (per-frame restores re-showed them), Epic Loot filter names from its AdventurePinFilter objects; unlock plate aligned to the notification plate per frame
- [x] Harness flakiness root cause (harness, not RuneSkin): 27h death left the inventory in a TombStone and respawned at the world spawn facing elsewhere -> m0 found no item to hover, m4 spawned Greydwarfs "ahead" of the body but behind the camera (EnemyHud hides HUDs behind the camera). 27h now restores the stand (teleport back, SetLookDir, TombStone.Interact, re-equip), m4 spawns along the look direction; m1 pins midday Clear (world Wet / cold broke the debuff strip widths); 28zb-reenter-world added
- [x] Codex final (run -072233): menu 8, delete 8, settings 8, controller 8, unlock 8; characters 7.5, worlds 7.5, servers 7.5, connecting 7.5, large map 7.5
- [x] 0.6.2 Below-8 fixes (run tools\out\20261022-094642, ui-runeskin-m5 17/17 PASS): CharacterFraming moves m_cameraMarkerCharacter so the preview stands large in the free area right of the list (PreWorldMath.FrameMarker + test), start-game tabs grown via Grow (any anchors / layout, 44 high, width capped by sibling gap), password / server filter / add-server fields = field_search 40 high with inset text area, connecting spinner centred on the rendered text bounds; gui_framework referenced (GuiInputField)
- [ ] Large map: Epic Loot filter rows showed "Задание: $1" for both -> keys $mod_epicloot_merchant_bounties / treasuremaps + exact button match in 0.6.1 (unverified)
- [ ] Harness: ui-runeskin-m1 debuffs failed in rain (Wet re-applied); Seasonality Summer pin added in tools 64091ec, unverified

## M6 art (2026-10-22) - vanilla in-game screens

- Inventory: docs/screen-inventory.md - 24 vanilla entries still unowned (build HUD, crosshair/hover, stealth, action progress, ship HUD, mount, event bar, black screens, chat, key hints, StoreGui, TextInput, TextViewer, raven, TextsDialog, split, barber, piece author, small popups, console, saves/changelog/feedback/credits/server options, connect panel; radials listed only = icons-in-radial-menus session) and 20 mod entries (M7).
- Picks (Codex, user asleep; mockups D:\tmp\cx-augaskin\m6-*.png, reasons in art\PROGRESS.md): build = A (text tabs + wide grid, + B stealth eye), trader = A (one list), text = A (+ B raven bubble), black screens = B (tip banner, spinner when no progress, teleport ring + swirl), ship = B (vertical ladder + rudder socket, light raid line, mount panel only when riding), dialogs = A.
- Rejected: build B (icon-only fixed categories), trader B (separate sell inventory), text B (drop caps, no panel input), load A (invented "you died" heading / player count), ship A (horizontal plate), dialogs B (book spread, invented tabs).
- Code pickup: art\README.md "M6 sprites". New sprites: glyph_stealth_eye, glyph_crosshair, glyph_ship_oar, glyph_ship_sail_half/full, glyph_rudder, glyph_teleport_swirl, glyph_stop, band_runes, bar_frame_action.

## M7 art (2026-10-22) - mod panels

- Mod panels needing art (docs/screen-inventory.md): Epic Loot enchanting table + adventure board, Vaettir target panel + raid timer, Hamingja bleed-out + ally markers; portal map / Jotunn panels compose from the kit; the rest are owned or have no UI.
- Picks (Codex; mockups D:\tmp\cx-augaskin\m7-*.png): enchanting = A (text tab column + checkbox rows + detail well = Epic Loot's own controls; tabs/rarities from its config), adventure board = A (labelled tabs, native tab contents, status text not colour only), mod HUD = A (+ B compact ally-marker housing; target + raid stacked top-centre).
- Rejected: enchanting B (icon-only tabs, item grid, ritual ring), board B (card inventories, separate gamble view), mod HUD B (raid ring, bleed-out replacing the HP bar).
- Code pickup: art\README.md "M7 sprites". New sprites: glyph_ench_convert, glyph_ench_disenchant, glyph_board_stash/gamble/treasure/bounty, frame_target, plate_marker, plate_currency, arrow_marker, gem_rarity, gem_setting.
- Open (art): shared tab_* reads flat next to the mockups (forged bevel) - global redraw needs a re-review of every screen; glyph_ship_sail_half Codex 7.
- [x] 0.6.3 status cells (user ask): cell side = max(theme 42, HudMath.StatusCell of the measured worst icon text '88m 88s' in the timer font) -> 62 ref at font 15; timer inside the cell under the icon, auto-size down to the 12 px floor for longer texts; tests StatusCellHoldsWorstTimer; harness m1 status-fit (run tools\out\20261022-095512, m1 9/9 PASS)
- [x] 0.6.4 HP numbers + shield (user ask): bar values in the body sans (lining digits) with outline + dilate 0.25 (TextLook 'hudvalue'), HP 24; shield = MorgottTweaks energy shield provider + vanilla SE_Shield remaining absorb, one translucent color.hud.shield layer over the HP fill (HudMath.ShieldOverlay, clamped; tests ShieldOverlayWidth), ES rail removed, label 'HP + shield' (shield in color.hud.shield.text), no shield = 'HP'; harness m1 hud adds a staff shield and checks the layer + label (run tools\out\20261022-100220, m1 9/9 PASS)
