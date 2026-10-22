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
- [x] 0.6.5 live-test fixes: top-left notification text centred in its plate (icon slot only when the message has an icon: vanilla keeps the old sprite at alpha 0; line ascender/descender box vertically; knot insets symmetric); lag ghost only after a decrease (rising value snaps it), fill-end FX ends at the fill edge (no overhang into the trough); shield = upper 40 % band of the HP window (HudMath.ShieldBand + tests), HP fill full height under it. Harness m1 shield-band / bar-ghost / notify-plate: run tools\out\20261022-100654 12/12 PASS

## M6 (code, 2026-10-22) - in-game vanilla screens, batch 1 (0.7.0)

- [x] Families BuildHud / Crosshair / ActionHud / ShipHud / KeyHints (Ownership + RuneSkin.API SkinFamilies, API v5 / 2.1.0), restyled in place (WorldHud\PlayHud.cs, BuildHudSkin.cs, PlayHudMath + PlayHudTests); AugaSkin 2.2.18 yields them (RuneSkinOwnership.OwnsHudPart / Skips, Hud_Setup per-child restyle, HudParts bars + ship, movable offsets)
- [x] Build menu (BuildUi role pass: panel stack, tab plates, tag rows, slot_cell_recess piece sockets, field_search, plate_key hints, key_base sprite role), selected-piece strip (frame_inset well, socket_req cells); crosshair glyph + gold on interactables, hover text outlined body sans, piece health bar_skill, stealth eye (hidden / targeted / alerted tints) + rune-glow bar; action progress + stagger in bar_frame_action (label above), event line band_message + mirrored rules (ember serif), mount panel popup + round portrait socket + bar_skill bars; ship ladder (sail full / half / oar / stop / reverse, active slot_cell_selected + rune glow) + rudder socket, vanilla ship icons alpha 0; key hints plate_key + body sans
- [x] Fonts: theme = two faces (font.body PT Sans for values / labels / input text, font.display Cormorant for titles / plates); input field text + key hint labels no longer keep vanilla Norse / Averia (Classifier, PreWorldKit.Common); status "+N" in the body face
- [x] Harness ui-runeskin-m6 (tools): run tools\out\20261022-102918 (m1 + m5 + m6) 50/50 PASS, unknown=0 in every M6 family; event line verified as restyle only (Vaettir raid bar hides the vanilla line in this pack); ship Slow / Back unit-tested only (Ship drops them out of water); mount panel restyle not shot (no tame stand)
- [x] Batch 2 (0.8.0, API v6 / 2.2.0, AugaSkin 2.2.19): families LoadScreen / Chat / Popups / TextDialogs / Compendium / SplitDialog (GameScreens\GameScreens.cs, role pass as the M5 forms; GameScreenMath + GameScreenTests). Black screens: tip in a panel banner, LoadingIndicator spinner = spinner_runes in a button_round socket, sleep = socket + rune spinner + glyph_time_moon above vanilla Zzz, teleport = minimap_ring + glyph_teleport_swirl turning (SwirlSpin). Chat: window (Terminal.m_chatWindow, not under the Chat object) popup + padded log, input field_search, NPC / raven / world text templates popup + display topic. TextInput panel + field + OK button_selected; TextViewer panel stacks + band_runes (under the topic / under the text, fading with the Animator via AlphaFollow, theme colours over the clip tint via TintKeep), raven panel with rune caps topic. Compendium rows row_recipe_*, reading pane rune topic (AugaSkin active effects page kept: its feature). Split dialog panel + slot_cell_recess icon + M5 slider + OK primary. Popups: achievement / join code / session player list / closed captions roots registered (no live trigger in the harness).
- [x] Harness ui-runeskin-m6b (tools): load-sleep / load-teleport / load-respawn (black screen forced for the captured frame), chat (+ NPC bubble), text-input, text-viewer, raven, compendium, split; m6b-unknown = 0 in every owned family after each state. Run tools\out\20261022-123619 (01 + m1 + m5 + m6 + m6b) 88/88 PASS. Not shot: popups (need unlock / crossplay / MP), intro saga (cinematic), mount panel (batch 1), EN locale.
- [x] Batch 3 (0.9.0, API v7 / 2.3.0, AugaSkin 2.2.20): families Store / Barber / DevPanels / Feedback (GameScreens\TradeScreens.cs). Store: vanilla StoreGui kept (no Auga store swap), panel stack, rows row_recipe_* + selection, icons content, buy button_selected, coin purse body sans; Epic Loot TemperPanel / MerchantPanel excluded (M7, AugaSkin keeps them: Store_EpicLootPanels split out of Store_Setup). Barber (in-world PlayerCustomizaton only; character creation stays MainMenu): panel, vanilla gradient sliders with handle art, apply primary (no AugaBarber swap). DevPanels: F5 console popup + padded log, F2 connect panel + row template. Feedback dialog (M5 rules). Hovered piece author window registered under BuildHud (BuildHud.Author). Saves / credits / server options / changelog were already MainMenu (0.6.x).
- [x] Harness ui-runeskin-m6c: store (Haldor spawned), barber, console, connect, feedback, author; m6c-unknown = 0. Run tools\out\20261022-124812 (01 + m1 + m5 + m6 + m6b + m6c) 101/101 PASS. Not shot: Hildir / Bog Witch stores (same StoreGui), EN locale. Open items closed in 0.9.1 (below).
- [x] 0.9.1 M6 tails (AugaSkin 2.2.21): changelog window = panel stack (its darken_blob backdrop, stretched over the window, was classified as a scrim), connect panel / changelog stacks kept inside the screen (Restyle\OnScreen: vanilla position + PanelMath.EdgeShift of the stacks on every enable), barber title on the rune face (20 px, under size.text.header.min), compendium active effects page drawn by RuneSkin (GameScreens\ActiveEffectsPage.cs: own registration Compendium.ActiveEffects, row_recipe plates, slot_cell_recess icon sockets, display / body / muted faces, theme colours; same rows as AugaSkin's page incl. MorgottTweaks MtActiveEffectRowsV1; AugaSkin 2.2.21 skips its ActiveEffectsPage patches when RuneSkin >= 0.9.1 owns Compendium). Harness: m5-changelog, m6b-effects, m6c-connect on-screen, m6c-barber title face. Run tools\out\20261022-131351 (01 + m1 + m5 + m6 + m6b + m6c) 104/104 PASS.
- [x] M6 code done (batches 1-3 + tails). Not shot-covered: popups (achievement / join code / session player list: need unlock / crossplay / MP), intro saga (cinematic), mount panel (no tame stand), ship Slow / Back (unit-tested only), Hildir / Bog Witch stores (same StoreGui), changelog / connect at resolutions other than 1920x1080 (OnScreen re-fits on every enable), active effects page with an inactive (dimmed) guardian power or a MorgottTweaks provider row, EN locale (no runtime language switch).

## 0.7.1 fix (2026-10-22) - rune face + label fit (user correction, before M6 batch 2)

- [x] Three faces in the theme: font.rune = AugaSkin's loaded TMP "Norsebold SDF" (theme font source "loaded", bound late by name, never given our fallbacks / shader), font.display = Cormorant Bold, font.body = PT Sans. Rune: menu.entry (pause menu), button.label.menu (main menu column), text.header (menu / panel titles, classifier headers), info.title; display: text.display (hotbar binding number, selected piece name), row.label (world rows), tooltip.topic, craft.title, dialog.subject, button labels. Before: every menu entry / title on Cormorant since M0 (78a966d menu.entry font.display; 966ca67 main menu labels button.label). Norsebold SDF: dynamic, 406 chars, full Russian alphabet incl. Ё ё Й й (harness rune-alphabet).
- [x] Button labels fit their plates (theme role fitInset 40, scaled to 16 % of narrow buttons; PlateLabelFit keeps the text area = button rect inset after layout / localisation; auto-size down to size.text.fit.min 12; one line while it reads at >= 80 % size, else two lines). Root causes of the user's empty / overflowing texts: merch store picture button restyled as an empty plate (now excluded), wider theme fonts in vanilla-sized boxes (labels, modded notice truncated -> auto-size), world row name now row.label.
- [x] Harness: m5-rune-font / m6-rune-font (rune face + no missing glyph, element -> font table), m5-text-fit / m6-text-fit (all visible texts of MainMenu / Settings / Connection / pause Menu: glyphs present, button labels inside the plate clear of the caps, no truncation, no empty plate); run tools\out\20261022-114616 (01 + m1 + m5 + m6) 66/66 PASS. EN locale not shot (no runtime language switch in the harness).

## 0.7.2 HUD quality (2026-10-22, user feedback from shots)

- [x] Icons centred in their cells: root cause = StatusStrip anchored the effect icon to the cell top (pivot 0.5,1) to leave the timer room under it; after the cell grew to 62 px (0.6.3) the icon sat high. Now centred on both axes (HudMath.IconSide), timer = a band at the cell bottom (HudMath.TimerBand, size.hud.status.timer.band 17) that never moves it. Food / guardian power / hotbar were already centred (harness confirms).
- [x] Bar values centred on every bar (HP was centred in the filled part, stamina / eitr right-aligned): one rule in BarView (TextAlignmentOptions.Center, box = window or the unlocked part, HudMath.ValueSpan).
- [x] Bar edges: the breathing blurred front FX is off; crisp fill-end rim bar_edge (Codex image gen art/src/kit/edge.png + edge.prompt.txt, candidate 1 cut 6x48) tinted per bar (color.hud.edge.health / stamina / eitr / shield), on the HP / stamina / eitr fills, the shield band end and the action / stagger bars (PlayHud.EdgeRim). Theme float mode.hud.bar.edge 2 (0 = old FX, 1 = clean). Codex scores (cx.ps1; agent-link chat 11cdaf31 seat failed "directory name is invalid"): old FX 5, clean 7.5, rim 8 -> rim ships.
- [x] Harness m1: icon-centre (18 cells, 1 px), bar-value-centre (1 px), bar-edge (shots ui-runeskin-m1-edge-0/1/2, rim on the fill end, no FX); m6-action checks the rim. Run tools\out\20261022-121144 (01 + m1 + m5 + m6) 69/69 PASS.

## Plan: RuneSkin standalone, AugaSkin out of the pack (2026-10-22, audit of Auga-Fork 2.2.21 vs RuneSkin 0.9.1)

Pack today: RuneSkin 0.9.1 + AugaSkin 2.2.21 ship together (client scope, mod-updaters-vps 00d841a). AugaSkin yields the
families in its `Compat\RuneSkinOwnership.Skips` / `OwnsHudPart` / `OwnsInventoryPart` / `OwnsPopup`; everything below
is what it still does with RuneSkin installed (derived from those gates + every Harmony class in Auga-Fork\Auga).

Asset provenance (audit): every bundled sprite = Codex image gen (art/src/kit/*.prompt.txt, cut by art/tools); fonts =
Cormorant 4.002 + PT Sans 2.003W (OFL, art/fonts/licenses); TMP shader + LiberationSans / EmojiOne in unity/Assets/TextMesh Pro
= Unity ugui package (not shipped except the SDF shader, swapped at runtime to the game's); no Auga sprite, font, shader
or code in the repo or the bundle (runeskin.contents.txt: 223 sprites + 2 RuneSkin-* fonts). Only runtime Auga asset =
theme `font.rune` -> AugaSkin's loaded "Norsebold SDF" (default-theme.json:284). Code comments cite AugaSkin behaviour
(FoodRow / HudMath checkerboard geometry idea, EffectTaxonomy from augaskin-engine-recon.md, Almanac/Menu ordering notes):
design references, no copied code. Open: Codex style references `D:\tmp\cx-augaskin\mockup-1.png` / `work\ref\*` are gone
from disk; record what mockup-1 was made from (if from AugaSkin screenshots, the kit is a style derivative, not a copy).

### Batch F - own rune face (decided: (a) vanilla Norsebold)

- [x] Original Norse (Joel Carrouche, fontspace "Norse" zip, v1.001, 2011): 311 glyphs, 0 Cyrillic, EULA no redistribution;
      Cyrillic Norse v2.20 only in Auga's AugaUnity (not an original distribution) -> not used.
- [x] F0 (0.9.2): theme `font.rune` = vanilla runtime TMP asset "Valheim-Norsebold" (source "loaded", bound late by name,
      nothing redistributed; AugaSkin "Norsebold SDF" lookup gone). Face: dynamic, full Russian alphabet incl. Ё ё Й й
      (harness rune-alphabet now probes the source font of dynamic faces). Harness RuneFaceAsset shared by m5 / m6 / m6b / m6c
      (tools 1fbe56e). Runs tools\out\20261022-135707 (01 + m5 + m6, 56/56), 20261022-135940 (same, AugaSkin parked, 56/56),
      20261022-140155 (m6b + m6c, 33/33, barber title Valheim-Norsebold): 0 missing glyphs.

### Batch M7a - Epic Loot panels (families EpicLootEnchanting / EpicLootBoard / EpicLootTemper, AugaSkin yields)

- [ ] Enchanting table: `EnchantingUI` prefab (EnchantingTableUI.Start), MultiSelectItemList ElementPrefab rows,
      CraftSuccessDialog (AugaSkin Compat\EpicLootEnchanting.Init). Art m7 kit (glyph_ench_*, gem_rarity/setting).
- [ ] Adventure board (MerchantPanel under StoreGui) + Hildir TemperPanel (AugaSkin Store_Setup Store_EpicLootPanels;
      RuneSkin TradeScreens excludes them today).
- [ ] AugaSkin side: gate EpicLootEnchanting.Init + Store_EpicLootPanels on the new families (Skips).

### Batch M7b - mod HUD

- [ ] hudroot mod parts AugaSkin restyles in Hud_Awake_ModParts: Epic Loot AbilityBar + DebugText, StarLevelSystem
      NoMapLevelIndicator (any non-RuneSkin child except the Vaettir world boss bar); remaining hudroot children of owned clusters (GP root etc.).
- [ ] Vaettir target panel + raid timer (BossAwakening UI\HudModes.cs copies the boss HUD template "after AugaSkin's
      EnemyHud swap" - re-check its template with AugaSkin absent), Hamingja bleed-out + ally markers (art m7 modhud).
- [ ] HudParts.Bars: GuiBar fills AugaSkin still reskins outside owned parts (mount bars if ActionHud off etc.); confirm none left.

### Batch M7c - generic mod UI

- [ ] Jotunn GUIManager role pass (AugaSkin Compat\JotunnGui: Create*/Apply*Style, colour/gradient pickers): StarLevelSystem
      config UI + startup popups, VNEI BaseUI parts outside the crafting grid, Epic Loot CreateButton / CreateScrollView,
      Jotunn ModCompatibility window.
- [ ] IMGUI fonts (AugaSkin Compat\Imgui): ValheimBuildCamera DismantlePreview badge (shipped), DisplayBepInExInfo
      (shipped); ConfigurationManager = dev only (skip).
- [ ] Leftover vanilla bits AugaSkin restyles: InventoryGui achievements panel + m_achievementsElementPrefab, variant
      dialog, m_dragItemPrefab; VanillaPanels RadialBase (radial menus: owned by the icons-in-radial-menus session),
      EndCredits / ManageSavesMenu only when MainMenu is off; PotalMap portal list (kit, large-map rules); Chatter toggles
      (only if installed).
- [ ] Main menu foreign texts (AugaSkin FejdStartup_Awake_Patch features): DisplayBepInExInfo / V+ version lines,
      StarLevelSystem "Mod Config" corner button overlap; cinematics button hidden; game font fallbacks linked.

### Batch P - port AugaSkin features (non-style, still live with RuneSkin)

- [x] Pause menu Compendium entry (0.13.0, family MenuCompendium, Screens\MenuCompendium.cs: copy of the Settings entry, gamepad
      chain spliced; AugaSkin 2.2.23 yields). Run 20261022-181305: entry after Settings + nav PASS, click opens TextsDialog
      (check FAIL only on Menu.IsVisible's 2-frame lag, harness fixed to m_root). Run 20261022-184353: p 11/11 PASS.
- [ ] Character stats window: 5th Info button + panel (CharacterStatsPanel.cs, 568 lines; RuneSkin InfoTabs mirrors the button).
- [x] Summon frames (0.13.0, family SummonFrames, Hud\SummonFrames.cs + SummonMath.cs: MorgottTweaks mt_sum_ui slots or loaded
      summons, row below the hotbar or above it when the hotbar sits low; socket_hotbar + rail art, $runeskin_summon_* words).
      Run 181305: 3 slot frames (fill 0.50, ES 1.00, trophy icons, RU status words), loaded path PASS, on screen, no Auga frames
      (slot check FAIL only on English words in the harness, fixed). Run 20261022-184353: p-summon-slots PASS.
- [x] Other players' shield rails (0.13.0, family PlayerShields, WorldHud\PlayerShields.cs, own EnemyHud.UpdateHuds postfix:
      mt_es / mt_es_max + mt_shield / mt_shield_max rails under "Health", close up). Run 181305 (local player on the player HUD,
      as 26b): p-shields + p-shields-closeup PASS, no Auga strips. MorgottTweaks 1.124.2 ShareShieldAbsorb cfg text skin-neutral (a69c668).
- [x] Custom cursor (0.13.0, family Cursor, Theme\CursorSkin.cs): Codex art art/src/kit/cursor.png (prompt next to it) cut by
      art/src/kit/textures.json -> art/textures/cursor.png 32x32, bundle readable RGBA32 texture (theme "textures"). Run 181305
      p-cursor PASS (AugaSkin 2.2.23 skips ApplyCursor).
- [x] Drop with AugaSkin (reviewed 0.13.1, no RuneSkin port): AugaLog message log (MorgottTweaks DeathMessages announces
      deaths) + LastHitTracker (only reader = AugaLog's death line; AugaSkin 2.2.24 skips its Player.OnDamaged feed under
      MessageHud), ComplexTooltip (Auga tooltip prefab, Tooltips owned), Text_Patch AlwaysUpper/TitleCase (Auga
      components only), ButtonSfx_Patch (Auga bundle buttons; vanilla sets select sfx on 2 of 59), GuiBar_Patch (vanilla
      GuiBar has no Filled path; RuneSkin fills are Sliced, PlayHud.Fill; FoodRow / GuardianSocket rings are own
      Filled images, not GuiBars), AugaBindingDisplay (Auga settings prefab, Settings owned), auga_audit console (dev),
      MovableHudElement offsets (zero by default, owned parts have none), translations.json (Auga strings).

### Batch X - remove AugaSkin from the pack

- [ ] Preconditions: F + M7a-c + P done, a run with plugins\AugaSkin parked (autotest -DisablePlugin AugaSkin) all PASS,
      unknown = 0 in every family.
- [ ] mod-updaters-vps: mods.json AugaSkin "disabled", drop `plugins\AugaSkin` + cfg from `$clientOnly`, add cfg to
      `$orphanCfgs`; `-Only` never deletes a whole mod -> full `valheim-pack.ps1 -SkipUpdate -Upload` (user OK) removes it.
- [ ] Clean references: RuneSkin HarmonyAfter("morgott.valheim.augaskin") (harmless, remove), Ownership comments, Plugin.cs
      "another skin (AugaSkin) yields" cfg texts; MorgottTweaks HarmonyBefore / comments; Vaettir RarityFrames / HudModes
      AugaSkin template notes; harness Auga layer.

## 0.9.3 minimap fix (2026-10-22, user priority)

- [x] Fog of war back on the small map. Root cause: the ring's Unity Mask draws the map RawImage with a stencil copy of
      Minimap.m_mapSmallShader (StencilMaterial, made once); vanilla writes _zoom / _pixelSize / _mapCenter (CenterMap,
      Minimap.cs:1280-1282) and _SharedFade (:737-748) on m_mapSmallShader only, the copy never got them (not in the
      shader's property block) -> the fog shader read 0. MinimapSkin.SyncMapMaterial mirrors them onto materialForRendering
      every LateUpdate (after Minimap.Update). Large map renders with the base material (no mask): was fine.
- [x] Minimap larger: size.minimap.ring 220 -> 320, share.minimap.ring.h 0.18 -> 0.26 (1080p ring 194 -> 281 px,
      1440p 259 -> 374 px); new cfg Hud.MinimapScale (0.6-1.6, live, scales size and cap). Test DefaultThemeMinimapIsLarge.
- [x] Harness ui-runeskin-m4 state mapfn-<WxH> (tools AutotestDriver.RuneSkinMapFn.cs): m4-map-fog-small (rendered
      material in sync + 12 unexplored / 4 explored screen pixels equal the mask-off vanilla render, 0.06/channel),
      m4-map-zoom-small, m4-map-marker-small, m4-minimap-overlap (ring + plaque vs every other visible HUD graphic),
      m4-map-fog-large, m4-map-zoom-large, m4-map-click-large (raycast at the map centre hits the map), m4-map-pin-large
      (named pin + label). Before fix run tools\out\20261022-141046: fog-small / zoom-small FAIL (worst pixel diff 0.31,
      rendered _zoom missing); after: 20261022-141304 m4 39/39 PASS, 20261022-141439 m1 15/15 PASS.

## 0.9.4 rune face preset (2026-10-22, user: rune texts "bold and smeared")

- [x] Canonical rune preset = the main menu entries (user-approved): vanilla Valheim-Norsebold with the font asset's own
      material, no faux bold. Root cause: texts that already had Norsebold (vanilla topics / titles in owned screens) kept
      vanilla's "Valheim-Norsebold - Outline" material copies and FontStyles.Bold (Settings / changelog / character /
      world / text-input / text-viewer / raven / sleep topics; inventory / crafting / compendium titles), since TMP keeps
      the material when the font does not change. Restyler.Apply: loaded face -> font.material + Bold cleared (SkinMark
      HasBoldOff, reverted with the mark).
- [x] Harness: rune-preset scan after every state of m4 / m5 / m6 / m6b / m6c (RuneSkin-restyled texts on the rune face:
      own material, no Bold); m5-rune-crisp per resolution (crop images ui-runeskin-m5-crisp-<res>-entry / -title, edge
      sharpness title >= 0.85 x menu entry); 01-mainmenu rune-face-probe info lines (vanilla vs RuneSkin material dump:
      identical for the menu entries). Before: run 20261022-142305 37 preset FAILs; after: 20261022-142706 (m4 + m5 + m6 +
      m6b + m6c) 167/167 PASS, 20261022-143109 m5 42/42 (sharpness title 0.521 / 0.536 vs entry 0.522 / 0.526 at 1440p / 1080p).

## 0.10.0 hand mirrors removed (2026-10-22, user: redundant with the hotbar)

- [x] Doll mirror cells right / left / ammo gone (InventorySkin Mirror / MirrorItem / MirrorPrimary / DropOnMirror /
      FitsMirror / MirrorUnequip / focus ring, DollCell Link layer + occupied glyph + linked state, InventoryMath
      DollMirrors / MirrorRows / MirrorX, Words runeskin_slot_right / left / ammo / mirror_hint, theme slot.link,
      glyph.hand.occupied, glyph.slot.weapon / offhand / ammo). API SlotSource.Mirror cells keep slot.mirror + link badge.
      Doll strip = storage | figure centred (size.inv.doll.figure.gap, was mirror.gap); tests updated.
- [ ] Art bundle still holds slot_mirror_linked, glyph_hand_occupied, glyph_slot_hand_right / left, glyph_slot_ammo
      (no theme token points at them): drop at the next art bundle build (art owner).
- [x] Harness m2 no-mirrors (no RuneSkinDoll_right / left / ammo, equipped sword keeps its grid cell, figure right margin
      <= 25 % of the panel: 200 / 240 px at 2560): run tools\out\20261022-150023 m2 + m3 26/26 PASS.

## 0.10.1 batch G - global layout rules (2026-10-22, user: text and icons placed exactly right on every owned screen)

- [x] Harness: one shared check (tools AutotestDriver.RuneLayout.cs, label `<prefix>-layout`) after every state of m1 /
      m2 / m3 / info / m4 / m5 / m6 / m6b / m6c over every visible SkinMark graphic: field text left +
      size.field.inset (+-2), centred plate labels |left - right gap| <= 2 + 3 % of the free span (gaps from icons
      beside the label), row and toggle labels size.row.pad (+-2) from the edge / icon / checkbox, cell icons equal
      margins (<= 1.5), cell texts inside the cell, no truncated text. Baseline run tools\out\20261022-151853: 20 FAIL
      checks, 461 findings (m3 220, m2 93, m6 40, m6b 40, info 31, m1 16, m6c 13, m5 8, m4 0).
- [x] Root causes: (1) row labels had no common inset rule: recipe rows 8 after the icon, build tags 2.5, compendium 4,
      store prices 5.5, settings toggles 15, field placeholders 14 / 16 (MT search after its glyph), the gamepad glyph
      dropdown centred; (2) world row names / seeds 40 units in: the root pass classified the inactive row template as
      button.label (plate fit, 40-unit cap inset), its own pass as row.label, the stale PlateLabelFit and its margin
      stayed (also swapped name / seed lines); (3) craft button label centred on the whole plate, not beside its glyph;
      (4) cell icons placed by screen code off-centre (requirement sockets 4.3 units, level socket 19, store sell glyph
      12), EAQS quick-slot keys 2.1 and amounts 1 unit outside their cells; (5) container weight plate kept the vanilla
      box layout (glyph above the plate_stat strip, half outside); (6) long recipe names cut at the fit floor (30-unit
      right column reserved even without the quality column); (7) feedback category label truncated.
- [x] Fix in the Restyle layer: TextInset (fields / dropdowns / rows / toggles, theme size.field.inset, new size.row.pad
      12; truncating labels auto-size to size.text.fit.min), PlateLabelFit centres in the free span beside glyphs,
      CellFit (theme role flag `"cell": true` on slot / socket / button roles: icon centred, texts kept inside),
      Restyler.Layout (also for clones of styled templates; drops a stale plate fit). Pure geometry LayoutMath
      (LayoutMathTests). Screen code: world row template gets its row roles in the root pass too, container weight plate
      = StatPlate (size.inv.plate.weight.dock 110), recipe name right column only with the quality column.
- [x] Harness hygiene: m2 removes its probe sword again (one per run had filled the test save), m6 action makes room
      in a full grid, m1 / m6 lift the carry limit while the shared save is overloaded (black metal from other shots).
- [x] After: run tools\out\20261022-162224 m1 + m2 + m3 + info + m4 + m5 + m6 + m6b + m6c 269/269 PASS, 54 layout
      checks, 0 findings.

## 0.10.2 (1/4) item cell zones (2026-10-22, user screenshot: stack count on the sword, swords touch the cell edges)

- [x] Root cause: vanilla item icons fill the InventoryElement (count / quality anchored over the icon corners); batch G
      CellFit and the harness took the full-cell m_dropFocus (Color.clear until a touch drag) as the cell icon, so the real
      icon was never centred or checked.
- [x] Fix: theme role flag `"zones"` (slot.cell, new slot.recess.item for EAQS doll cells): CellFit scales the icon to a
      centred square inset by the band (LayoutMath.ZoneBand, share.cell.zone 0.18 / size.cell.zone.min 9, 64-unit cell ->
      11.5 band, icon 41) on every side, upper texts (quality) into the top band, lower texts (amount) and the durability
      GuiBar into the bottom band, texts shrunk to the band (LayoutMath.FitFontSize, floor size.cell.zone.font.min);
      clear-colour images are never the icon. Journaled (positions, scale, font size + auto-size). HUD hotbar unchanged.
- [x] Harness `<prefix>-layout`: zone cells -> icon margin >= band - 1, no text / GuiBar intersecting the icon; clear
      images skipped. Run tools\out\20261022-170700: m2 layout 6/6 PASS (14 zone cells per state, 0 problems).

## 0.10.2 (2/4) crafting requirement names and stray tile (2026-10-22, user screenshot: "...анина" / "Одуванчи...", tile)

- [x] Stray tile = the vanilla `requirements/level` socket (parent of MinLevel, restyled craft.socket): vanilla hides
      only MinLevel for a recipe without a station level (InventoryGui.cs:1554), the socket stayed active and unplaced at its
      old spot over the first cell (the arrow in the screenshot = the mouse pointer). Now scaled to 0 unless MinLevel shows.
- [x] Names: res_name fitted per text change (LabelFit.NameSize / BestSplit): one line from size.craft.font.label down
      to size.craft.font.label.min (10), then two lines at the best word split, tight (size.craft.label.spacing -22) and
      sized by the measured two-line block; below min only when nothing else fits (never clipped).
- [x] Harness m3: names-<state>-<res> (whole, inside the cell, >= min), stray-<state>-<res> (no image outside the active
      cells), new longnames state (DeerMeat / Dandelion / Mushroom / LeatherScraps, no station) + PNG. Run 20261022-170700:
      stray 4/4 PASS, names FAIL ("Кожаные обрывки" second line dropped: 2 x one-line height < TMP's two-line block) ->
      measured block + tight spacing; rerun tools\out\20261022-171358 m3 + m4 90/90 PASS (names 4/4, bar caps 2/2).

## 0.10.2 (3/4) rune spinner glow chase (2026-10-22, user: the runes must not turn)

- [x] Root cause: RuneSpinner rotated its transform in 45 deg steps; the respawn LoadingIndicator spinner (role spinner,
      spinner_runes art) was turned by its vanilla animation.
- [x] Fix: art spinner_rune_0..7 = mechanical 45 deg sector slices of spinner_runes at full brightness (art tool cut
      options `sector` / `norm`); RuneSpinner fades them in sequence (PreWorldMath.RuneGlow, time.spinner.step 0.14,
      size.spinner.tail 3, share.spinner.base 0.22): CanvasRenderer alpha only, world rotation held at the canvas'. The
      respawn vanilla spinner is off (PanelSwap), RuneSkin_RespawnSpinner beside it fades with it (AlphaFollow). Teleport
      swirl (SwirlSpin, a vortex glyph, no runes) still turns.
- [x] Harness AutotestDriver.RuneSpinner.cs: two samples 0.35 s apart -> positions / rotations identical, head moved,
      contrast >= 0.3. Run 20261022-170700: m5-spinner-connecting, m6b-spinner-sleep, m6b-spinner-respawn PASS.

## 0.10.2 (4/4) HUD bar right end caps (2026-10-22, user: the leather ends are "убогие")

- [x] Root cause: the right ends were part of bar_frame (rune block + leather strap, taller than the rails) and
      bar_frame_slim (leather caps), different shapes and heights per frame, the fill's rounded end showing before them.
- [x] Fix: frames cut open at the right (rails to the edge, window.*.right 0); one Codex cap per bar (bar_cap_main with
      the rune knot, bar_cap), size.hud.bar.cap 34 wide, height = the bar's rail band (HudMath.CapBox), so the three caps
      stack into one column; window ends at the cap and clips (RectMask2D), full fill / lag / trough run under the cap
      (HudMath.ClippedEnd). Variants A / B / C crops in one launch (BarView.CapVariantOverride); Codex A 7, B 6, C 8 ->
      C kept, A / B sprites, sheets and prompts dropped. Codex note for later: lighter empty medallions, thinner teal rim.
- [x] Harness AutotestDriver.RuneBarCaps.cs: m4-bar-caps-<res> (equal widths, right edges aligned, gaps <= 1 px, window
      end = cap left, mask) + crops ui-runeskin-m4-barcap-<v>-<res>. Run 20261022-170700: 2/2 + variants PASS.

## 0.12.0 live 3D character preview (2026-10-22)

- [x] CharacterPreview (InventoryScreen): self-contained RawImage placed by a theme rect (share.inv.preview.x/y/w/h +
      size.inv.preview.inset inside the doll figure box, PreviewMath.Place) - movable for the full-screen inventory
      redesign; replaces the silhouette (silhouette = fallback: Layout.CharacterPreview off, no free layer, build error).
- [x] Visual-only clone (FejdStartup.SetupCharacterPreview approach: player prefab + ZNetView.m_forceDisableInit),
      stripped before activation to renderers / animator / VisEquipment / particles / cloth; filled by
      Player.SetupVisEquipment(clone, false) (ragdoll path: armour, hands + back, cape, utility, trinket, hair / beard,
      model, colours, mod items); refresh on Player.SetupVisEquipment / SetPlayerModel / SetSkinColor / SetHairColor of
      the local player only; no ZDO written (MP-neutral).
- [x] Own layer (first unnamed, 30 in game) + manual camera + key / fill / rim rig; world directional lights, fog,
      ambient swapped only for its render (noon vs midnight luma 0.350 / 0.351); drag rotates; idle (wake-up skipped).
- [x] Teardown on close / logout / player change: RT, rig, clone, runtime materials (creation-window ownership;
      PlayerMaterial / _Fem copies leaked 2 per build before). Open state from the animator (IsVisible lags 1 frame).
- [x] Tests PreviewMathTests (placement, texture size, camera fit, yaw, layer pick).
- [x] Runs 20261022-174939 / 20261022-175538 (ui-runeskin-preview): live, nonempty, isolated, time, helmet, drag PASS;
      leak: LiveClones / LiveTextures / RTs / rigs 0, materials 5770 -> 5770.
- [x] 0.13.1: a disable while the inventory stays open keeps the clone (OnDisable tore down -> rebuild on re-enable;
      run 20261022-181305 builds 11 / 10); meshes made while building owned + destroyed like materials; History.
      Run 20261022-184353: builds 0 (control) + 10 + 10, every teardown "inventory hidden", Material / Mesh / Texture2D /
      RT / controller / clip counts flat.
- [x] +17 MB (run 181305) is mostly not the preview: control batch (10 open / close, preview disabled) +12.8 MB managed;
      preview batch B minus control +2.7 MB.
- [ ] Open: residual +9 GameObjects / +45 Components per 10 builds (batch A +8 / +32, control -5 / -23) - preview-leak
      FAIL on it; harness preview-objects (tools 77f4e2d) names them on the next run.
- [x] ui-runeskin-m0 tooltip PASS in run 20261022-184353 (harness: UITooltip reads the Input System pointer, which did
      not follow OS cursor warps; driver now queues the Mouse position too, tools a7131c2).

## Full-coverage directive (user, 2026-10-22; into M7 after B1-B5, not started)

- [ ] 100 % of the UI on RuneSkin: all fonts, buttons, menus, incl. ALL current and future mods (Epic Loot etc.) -> a
      generic fallback restyler for unknown mod UIs (role pass on any foreign canvas), not only per-mod families.
- [x] Text starts exactly where it should everywhere (no left / right skew, alignment / padding; batch G).
- [x] Every icon with equal margins in its cell, counts / numbers below it always fitting (batch G).
- (Global layout rules part = batch G, before B1.)

## 0.11.0 M7 - every UI on RuneSkin (fallback restyler + mod families; API v8 / 2.4.0, AugaSkin 2.2.22)

- [x] Families ModUi (fallback), EpicLootEnchanting, EpicLootBoard, EpicLootTemper, ModHud, InventoryExtras, Radial (config
      Ownership.*, theme families, Classifier control families); AugaSkin 2.2.22 yields each (RuneSkinOwnership flags, Skips,
      OwnsHudPart.IsModHudPart, OwnsInventoryPart, Store_EpicLootPanels / EpicLootEnchanting / JotunnGui / Imgui / VanillaPanels gates).
- [x] ModScreens\ForeignUi: Graphic / TMP OnEnable postfix queues new graphics, LateUpdate resolves each once (cached
      ForeignMath.Step verdict per transform): unit = topmost object below the vanilla boundary (UI singletons + family roots
      and their ancestors) with another plugin's MonoBehaviour and none of the game's, or Jotunn-built (JotunnHook anchors:
      CustomGUIFront / Back via OnCustomGUIAvailable + every GUIManager Create* result); inside family roots only anchors and
      precise types (ForeignRoles.Hints: EL EnchantingTableUI / MerchantPanel / TemperPanel / AbilityBar / DebugText /
      SocketsUI, VNEI BaseUI). Unit = GameScreenKit.Form role pass (+ RootMark so the vanilla family skips it), foreign window
      art -> panel stack (ForeignMath.IsWindowPanel), icon buttons keep their glyph, legacy texts theme colour (titles rune
      face via the dynamic face's source font, ThemeTokens.LegacyFont), EL / board tab glyphs (m7 kit) where the plate has
      room. Opt-out Ownership.ModUiOptOut (default AugaSkin, BA_WorldBossBar); world-space canvases never.
- [x] VanillaExtras (achievements panel + rows, variant dialog, drag icon; radial menu restyled per Open), MainMenuMods
      (version label clear of foreign corner buttons, cinematics button hidden), ImguiFonts (theme body face only when it has
      a source font: the static bundle face has none -> IMGUI keeps its font), Diagnostics\Coverage (Plugin.Coverage: visible
      graphics styled / explained / own / unstyled, m7 + before counts, unit list).
- [x] Tests ForeignUiTests (assembly kinds, Step rules, window panel, glyph maps, theme families).
- [x] Harness ui-runeskin-m7 (tools 48ed1eb), AugaSkin parked: run tools\out\20261022-174219 46/60 (coverage gaps: inner control art
      Unknown, legacy Dropdown, EL rarity toggles squeezed, achievement row icons as cells, family-drawn HUD counted unstyled) ->
      fixes -> run 20261022-174728 47/60: every adapter check PASS (enchant 7 tabs, board, temper, achievements, radial, vnei:
      unstyled in the window 0, unit present; m7-layout 14/14, m7-rune-preset 14/14, menu 0 unstyled); the 13 m7-unstyled FAILs =
      one vanilla item icon of the top-left message (MessageHud family code) the scan did not count as family-drawn (scan fixed
      after the run, not re-run). Coverage before -> after (M7 graphics on screen): enchant 50-98 -> 0, board 125 -> 0 (4 hud),
      temper 43 -> 0, achievements 83 -> 0, radial 23 -> 0, vnei 304 -> 0.
- [x] Default opt-out now AugaSkin, BossAwakening, BA_WorldBossBar: run 174728 showed the BossAwakening raid bar under
      EnemyHud as a fallback unit (EnemyHud / Vaettir bars = the enemy HUD track).
- [x] Legacy faces (0.13.0): the bundle ships PT Sans / Cormorant again as legacy dynamic UnityEngine.Font (BuildBundles
      ImportLegacyFonts, contents check RU+EN missing 0), theme font entry "legacy"; legacy roles take font.body, bold texts the
      display face with the Bold flag cleared (LegacyFaces, no faux bold); ImguiFonts uses them (bold GUIStyles -> display).
      Coverage head adds legacyThemeFace / legacyForeignFace. Run 181305 p-legacy-fonts PASS.
- [x] Coverage scan fix (MessageHud top-left icon): run 181305 m7-world PASS outsideHud=0 (was the 13 FAILs; hudroot 3 = HUD tracks), menu 0 (legacyThemeFace 1).
- [x] ModUi opt-out root cause (run 181305, AugaSkin enabled: NO fallback unit under _GameMain - EL / VNEI / Almanac all
      unstyled): the default opt-out "BossAwakening" matched BossAwakening's WorldBoss component on _GameMain (Game.instance),
      blocking every screen below. Fix: a plugin opt-out blocks only a would-be unit, never the vanilla boundary / family roots
      (ForeignMath.Node.OptedOutPlugin, test OptedOutPluginOnTheBoundaryPassesThrough). Not re-run (one-launch limit).
- [ ] Open: Jotunn ModCompatibility window not shot (only on a version mismatch); Vaettir target / raid / Hamingja (other track).
- [ ] Open: legacy uGUI body texts and IMGUI keep their own face (needs a legacy Font / dynamic body face in the art bundle);
      Jotunn ModCompatibility window not shot (only on a version mismatch); Vaettir target / raid / Hamingja (other track).
## 0.14.0 enemy bars E1-E4 (2026-10-22, user picks enemy-b + boss-c, spec hud-design\spec.md 1-5)

- [x] E1 visibility (Hud.EnemyBars = Smart | Vanilla): per-HUD score (aimed + 1.5 s linger 1000, engaged 8 s 500+, alerted threat
      within 15 m 100 - d, +50 when it targets the local player), top Hud.EnemyBarsMax 4 with at most Hud.EnemyBarsThreats 2
      threats (EnemyHudMath.Score / Pick, insertion sort, no allocation); vanilla keeps them active through its own
      m_hoverTimer (UpdateHuds prefix: 0 = picked / fading, 99999 = off), fade via a template CanvasGroup (in 0.12 / out 0.35 s,
      threats at 0.75); engaged = Character.Damage postfix (attacker = local player) + ApplyDamage on the local player;
      creation radius Hud.EnemyBarsRange 20 m; distance scale x1 <= 6 m .. x0.8 >= 20 m; name only for aimed / engaged.
      Players, bosses, mounts, tamed, friendly: vanilla rules, alpha 1.
- [x] E1 size: size.enemy.scale 1.35 -> 1.0, bar 96 x 8 ref (Widen on the template before any GuiBar caches its width),
      name font 13.
- [x] E2 tiers = enemy-b, art cut mechanically from the approved mockups (coordinator call after Codex image-gen kits
      scored 6-7): art/src/kit/mockup-enemyb.png / mockup-bossc.png, art tools key "bg" (flood from the crop border over
      the blurred scene), "clear" (bar windows cut out), "clone" (mockup caption replaced by its field texture), "keyRed",
      command "strip" (BossAwakening fill / trough strips). frame_enemy_<tier> = socket + rail + chevron cap of the IDLE 3x
      bar in one Sliced sprite (window fitted, bar 96 x 11.5 = mockup 8.4:1); fill / lag / trough = mockup strips
      (bar_enemy_fill_b / lag_b / trough_b, untinted). Offline pre-score `go -C art/tools run . compare-eb` (sprites only,
      Unity Sliced rules) -> Codex normal 8 / magic 8 / rare 8 / unique 9 / boss 8.
- [x] E3 enemy shield band: ZDO mt_es / mt_es_max on the creature when published (MorgottTweaks does not publish it yet: user
      undecided), else the owner's SE_Shield remaining; upper share.hud.shield.band of the window (HudMath.ShieldBand).
- [x] E4 stagger line (bottom inner edge, share.enemy.stagger 0.28): build-up = GetStaggerPercentage (owner) / ZDO mt_stg,
      0.15 s ease, staggered = pale color.enemy.stagger.active + 0.2 s white flash, fill = 1 - normalizedTime of the "stagger"
      tagged animator state, hidden 1 s after zero (EnemyHudMath.StaggerStep + tests).
- [x] Boss bar art for BossAwakening cut from boss-c: boss_frame (Tiled rail stretch), boss_cap (faded joint), boss_plaque
      (whole, Simple, field share.boss.plaque.field.*), boss_marker; theme sprite.boss.*, size.boss.cap.h 2.6,
      size.boss.marker.h 1.85, share.boss.marker.aspect / pivot, share.boss.plaque.h 2.45 (mockup ratios).
- [x] Audit P2: VT_Frame lookup cached with a 1 s retry (was Find per frame per HUD); HudArt rows / suppressed / ppu tables
      pruned when doubled + Reset on EnemyHud.Awake (world reload piled up dead templates).
- [x] Unit tests EnemyBarTests (visibility, pick cap, fades, distance scale, stagger build / decay / active / immune).
- Art pipeline note: a fresh worktree's unity\ has no Library -> the first batch build crashes in the atlas packer
  ("Unsupported Format 0"); copying the main checkout's unity\Library first builds fine.
- [x] Verified: runs tools\out\20261022-184813 and 20261022-185607 (ui-runeskin-enemybars + m4) 74/74 PASS each; Codex fidelity in game normal 9 / magic 8 / rare 8 / unique 8 (run -184813), Vaettir boss bar 8 (run -185607: bar height from width, name on the rune face).
