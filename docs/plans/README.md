# Plans Index (Status + Canonical Order)

This index is the current entrypoint for `docs/plans/*`.
It keeps old plans as history, while clarifying which documents are currently authoritative.

Status labels:
- `active`: use this first for current direction.
- `historical`: useful implementation history; not primary guidance.
- `superseded`: preserved for traceability, but replaced by newer direction.

## Active
- `docs/plans/2026-02-10-app-reanalysis-and-rule-decomposition.md`
  - Latest app-layer reanalysis with enforceable decoupling rules and incremental wave order.
- `docs/plans/2026-02-09-batch-refactor-wave2-plan.md`
  - One-shot integrated execution plan for current top hotspots (app orchestration + core binder/TM + test/docs cleanup).
- `docs/plans/2026-02-09-refactor-decoupling-analysis.md`
  - Current refactor/decoupling strategy (incremental seam-first).
- `docs/plans/2026-02-09-lqa-fixer-pr-slicing-plan.md`
  - PR-level slicing plan for `LqaScanner` and `KoreanTranslationFixer` decomposition (execution completed; use as parity/refactor trace log).
- `docs/plans/2026-01-25-multi-franchise-support-and-remove-nexus.md`
  - Franchise split and Nexus removal direction (still relevant in parts; verify against current code before execution).
- `docs/plans/2026-01-24-model-presets-and-compare.md`
  - Preset/model policy behavior reference.

## Superseded
- `docs/plans/2026-01-23-qwen-mt-provider.md`
  - Superseded by Gemini-only backend direction.
- `docs/plans/2026-01-23-qwen-mt-logging-and-cost.md`
  - Superseded by Gemini-only backend direction.
- `docs/plans/2026-01-23-deepseek-engine.md`
  - Superseded by Gemini-only backend direction.
- `docs/plans/2026-01-24-app-di-and-tab-vm-bigbang.md`
  - Superseded by incremental seam-first refactor path.

## Historical
- `docs/plans/2026-01-18-paired-slash-list-expander.md`
- `docs/plans/2026-01-18-session-term-memory-auto-glossary.md`
- `docs/plans/2026-01-19-global-project-glossary-tiers.md`
- `docs/plans/2026-01-19-hotspot-refactor-phase2.md`
- `docs/plans/2026-01-19-project-preflight-session-terms.md`
- `docs/plans/2026-01-19-translation-safety-overhaul.md`
- `docs/plans/2026-01-19-translationservice-refactor-plan.md`
- `docs/plans/2026-01-19-vibe-kit-design.md`
- `docs/plans/2026-01-19-vibekit-and-next-hotspots.md`
- `docs/plans/2026-01-19-vibekit-improvements-and-pipeline-refactor.md`
- `docs/plans/2026-01-22-app-lqa-review-tab.md`
- `docs/plans/2026-01-22-dialogue-context-sliding-window.md`
- `docs/plans/2026-01-22-light-theme-revert.md`
- `docs/plans/2026-01-22-project-context-hardening.md`
- `docs/plans/2026-01-22-vibekit-token-contract-qa-v2.md`
- `docs/plans/2026-01-23-community-glossary-fortify-hardening.md`
- `docs/plans/2026-01-23-quality-escalation-model-routing.md`
- `docs/plans/2026-01-23-strings-tm-hit-highlighting.md`
- `docs/plans/2026-01-24-gemini-only-engine-cleanup.md`

## Maintenance Rule
- When a new plan changes architectural direction, update this file first.
- Do not delete old plan files unless legal/security reasons require removal.
