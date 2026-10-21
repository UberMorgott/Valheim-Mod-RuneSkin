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
