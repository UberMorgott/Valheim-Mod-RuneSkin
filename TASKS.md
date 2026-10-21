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
- [ ] Tooltip text touches the frame rail on box-padded-0 tooltips (Epic Loot content has no inner padding): add content inset
