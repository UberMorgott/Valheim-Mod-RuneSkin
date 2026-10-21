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
- [ ] Acceptance with mockup art df8dc3a: deployed 5169e46, run queued on broker (exclusive holder); then compare D:\tmp\cx-augaskin\compare-ingame-hud.png ([hud-status-b | in-game], scratchpad compare.ps1) + Codex score >= 8
- [ ] Panel corner caps above content (art df8dc3a note) for Menu / tooltip PanelDecor
- [ ] Stamina / eitr pool glyphs for the slim frame icon recess (no glyph sprites yet)
- [ ] Hotbar slot art (socket.hotbar, plate.number) and quick-slot art: M2 with doll slots
- [ ] Reserve tooltip (auras / summons) on hover: needs inventory-open hit area (M2)
- [ ] Bar-cap pool glyph sockets are empty in the art (no HP/stamina/eitr glyphs yet)