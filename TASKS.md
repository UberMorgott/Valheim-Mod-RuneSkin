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
- [ ] In-game run 1920x1080, 0 unexplained unknowns on Menu
- [ ] Codex final diff review

## Later

- Settings family (M0 plan lists it; this pass = pause Menu + tooltips only)
- Compendium menu entry moves from AugaSkin to MorgottTweaks before AugaSkin retires (Codex)
