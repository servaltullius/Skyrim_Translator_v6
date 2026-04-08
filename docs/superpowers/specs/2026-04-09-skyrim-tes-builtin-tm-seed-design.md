# Skyrim/TES Built-In TM Seed Design

**Goal**
- Embed a Skyrim/TES franchise translation-memory seed into the app so Elder Scrolls projects auto-populate the legacy TES franchise TM DB without requiring separate TSV distribution or manual import.

**Current State**
- Elder Scrolls projects already use the legacy franchise TM database path `%LOCALAPPDATA%/XTranslatorAi/Global/global-glossary.sqlite`.
- Fallout built-in seeding is already implemented through embedded TSV resources plus `BundledFranchiseTmSeedService`.
- Elder Scrolls currently has a built-in meta prompt and glossary, but no built-in TM seed.

**Constraints**
- Do not change the on-disk TES DB path or migrate existing user data.
- Do not split Elder Scrolls into a new franchise enum or new DB location in this change.
- Reuse the existing franchise TM auto-import path and stamp-file mechanism.
- Keep the feature best-effort: failure to seed must never block XML loading.

## Recommended Approach

Use a hybrid compatibility-preserving design:
- Keep TES storage on the existing legacy franchise DB path.
- Add an embedded Skyrim/TES TSV seed resource to the app.
- Extend the existing bundled seed service so `BethesdaFranchise.ElderScrolls` writes a versioned seed file into the TES `tm-import` directory before the normal auto-import step runs.

This gives users a built-in TM experience while preserving all existing Elder Scrolls installs and accumulated local TM data.

## Components

### 1. Embedded TSV resource
Add a new embedded resource file under `src/XTranslatorAi.App/Assets/TmSeeds/`.

Responsibilities:
- Store the curated Skyrim/TES TM seed as `Source<TAB>Target` TSV.
- Ship inside the single-file exe and normal app builds.

Non-goals:
- This file is not a glossary replacement.
- This file is not a DB snapshot.

### 2. Embedded asset lookup
Extend `EmbeddedAssets.LoadBundledFranchiseTmSeed`.

Responsibilities:
- Return the Fallout seed for `BethesdaFranchise.Fallout`.
- Return the new Skyrim/TES seed for `BethesdaFranchise.ElderScrolls`.
- Keep returning `null` for franchises without a bundled seed.

### 3. Bundled seed service
Extend `BundledFranchiseTmSeedService`.

Responsibilities:
- Define a TES seed version string, independent from Fallout.
- Define a TES seed file name such as `bundled-skyrim-tes-franchise-tm.tsv`.
- Copy or repair the TES bundled seed into the TES `tm-import` folder.
- Preserve existing best-effort behavior and stamp semantics.

Behavior:
- First run for TES: write seed file if needed and write version stamp.
- Later runs: if stamp exists, only repair a stale or mismatched seed file.
- Never throw outward.

### 4. Existing project-load pipeline
No new loading path is required.

The current pipeline already does the right thing:
- project is opened
- bundled seed service runs
- existing franchise TM auto-import scans `tm-import`
- imported TSV is persisted into the franchise DB

This change only makes TES participate in that existing path.

## Data Flow

1. User opens an Elder Scrolls XML project.
2. App resolves `BethesdaFranchise.ElderScrolls`.
3. `BundledFranchiseTmSeedService.EnsureBundledSeedAsync` loads the embedded TES seed.
4. Service writes `bundled-skyrim-tes-franchise-tm.tsv` to the TES `tm-import` directory if missing or stale.
5. Service writes `.bundled-seed.elderscrolls.<version>.stamp`.
6. Existing auto-import logic imports the TSV into `%LOCALAPPDATA%/XTranslatorAi/Global/global-glossary.sqlite`.
7. Translation runs can hit TES franchise TM without manual import.

## Versioning

Use an explicit TES seed version string, starting at `v1`.

Rules:
- Bump the TES version only when the bundled TES TSV content changes intentionally.
- Do not couple the TES version to Fallout or Starfield versions.
- Preserve existing stamp naming by franchise, so TES and Fallout cannot collide.

## Testing

Add or extend tests to cover:
- Elder Scrolls seed is returned by `EmbeddedAssets.LoadBundledFranchiseTmSeed`.
- First-run TES seeding creates the expected TSV and TES stamp in the legacy TES import directory.
- Re-running with a matching stamp does not duplicate work.
- Re-running with a stale seed file repairs it.
- Non-seeded franchises remain unchanged.

Documentation check:
- Add one workflow-level test or doc assertion that explains Skyrim/TES built-in seeding behavior alongside Fallout.

## Tradeoffs

### Why this is preferred
- Keeps full backward compatibility for existing TES users.
- Minimal implementation footprint because Fallout already established the pattern.
- No migration or DB path changes.
- Users no longer need to distribute Skyrim/TES TM separately.

### Why not split Skyrim into a new DB now
- It would require new storage boundaries, possible migration logic, and new UX semantics.
- It solves a naming purity problem, not an immediate user problem.
- The current request is about built-in distribution and automatic availability, not franchise model redesign.

## Implementation Scope

In scope:
- Add TES embedded TM seed resource.
- Wire TES into bundled seed loading.
- Add tests and docs.

Out of scope:
- Starfield built-in seed.
- TES/Oblivion/Morrowind sub-franchise separation.
- DB schema changes.
- Migration of existing TM data.

## Success Criteria

- A clean install can open a Skyrim/TES XML and get franchise TM hits without manual TSV import.
- Existing TES users keep using the same DB path and retain prior data.
- Existing Fallout bundled seeding behavior remains unchanged.
- Full app and test builds pass.
