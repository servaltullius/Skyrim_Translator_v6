# Skyrim/TES and Starfield Built-In TM Seed Design

**Goal**
- Embed Skyrim/TES and Starfield franchise translation-memory seeds into the app so those projects auto-populate their franchise TM DBs without requiring separate TSV distribution or manual import.

**Current State**
- Elder Scrolls projects already use the legacy franchise TM database path `%LOCALAPPDATA%/XTranslatorAi/Global/global-glossary.sqlite`.
- Starfield projects already use the franchise-scoped database path `%LOCALAPPDATA%/XTranslatorAi/Global/starfield/global-glossary.sqlite`.
- Fallout built-in seeding is already implemented through embedded TSV resources plus `BundledFranchiseTmSeedService`.
- Elder Scrolls and Starfield currently have built-in meta prompts and glossaries, but no built-in TM seed.

**Constraints**
- Do not change the on-disk TES DB path or migrate existing user data.
- Do not split Elder Scrolls into a new franchise enum or new DB location in this change.
- Do not change the existing Starfield franchise DB location.
- Reuse the existing franchise TM auto-import path and stamp-file mechanism.
- Keep the feature best-effort: failure to seed must never block XML loading.

## Recommended Approach

Use a hybrid compatibility-preserving design:
- Keep TES storage on the existing legacy franchise DB path.
- Keep Starfield storage on its existing franchise DB path.
- Add embedded Skyrim/TES and Starfield TSV seed resources to the app.
- Extend the existing bundled seed service so `BethesdaFranchise.ElderScrolls` and `BethesdaFranchise.Starfield` write versioned seed files into their franchise `tm-import` directories before the normal auto-import step runs.

This gives users a built-in TM experience while preserving existing local TM data and the current franchise storage boundaries.

## Components

### 1. Embedded TSV resource
Add a new embedded resource file under `src/XTranslatorAi.App/Assets/TmSeeds/`.

Responsibilities:
- Store the curated Skyrim/TES and Starfield TM seeds as `Source<TAB>Target` TSV.
- Ship inside the single-file exe and normal app builds.

Non-goals:
- This file is not a glossary replacement.
- This file is not a DB snapshot.

### 2. Embedded asset lookup
Extend `EmbeddedAssets.LoadBundledFranchiseTmSeed`.

Responsibilities:
- Return the Fallout seed for `BethesdaFranchise.Fallout`.
- Return the new Skyrim/TES seed for `BethesdaFranchise.ElderScrolls`.
- Return the new Starfield seed for `BethesdaFranchise.Starfield`.
- Keep returning `null` for franchises without a bundled seed.

### 3. Bundled seed service
Extend `BundledFranchiseTmSeedService`.

Responsibilities:
- Define TES and Starfield seed version strings, independent from Fallout and from each other.
- Define seed file names such as `bundled-skyrim-tes-franchise-tm.tsv` and `bundled-starfield-franchise-tm.tsv`.
- Copy or repair the TES and Starfield bundled seeds into each franchise `tm-import` folder.
- Preserve existing best-effort behavior and stamp semantics.

Behavior:
- First run for each supported franchise: write seed file if needed and write version stamp.
- Later runs: if stamp exists, only repair a stale or mismatched seed file.
- Never throw outward.

### 4. Existing project-load pipeline
No new loading path is required.

The current pipeline already does the right thing:
- project is opened
- bundled seed service runs
- existing franchise TM auto-import scans `tm-import`
- imported TSV is persisted into the franchise DB

This change only makes TES and Starfield participate in that existing path.

## Data Flow

1. User opens an Elder Scrolls or Starfield XML project.
2. App resolves `BethesdaFranchise.ElderScrolls` or `BethesdaFranchise.Starfield`.
3. `BundledFranchiseTmSeedService.EnsureBundledSeedAsync` loads the embedded franchise seed.
4. Service writes `bundled-skyrim-tes-franchise-tm.tsv` or `bundled-starfield-franchise-tm.tsv` to the franchise `tm-import` directory if missing or stale.
5. Service writes `.bundled-seed.<franchise>.<version>.stamp`.
6. Existing auto-import logic imports the TSV into the franchise TM DB.
7. Translation runs can hit franchise TM without manual import.

## Versioning

Use explicit per-franchise seed version strings, each starting at `v1`.

Rules:
- Bump each franchise version only when that franchise TSV content changes intentionally.
- Do not couple TES, Starfield, and Fallout versions.
- Preserve existing stamp naming by franchise, so TES, Starfield, and Fallout cannot collide.

## Testing

Add or extend tests to cover:
- Elder Scrolls seed is returned by `EmbeddedAssets.LoadBundledFranchiseTmSeed`.
- Starfield seed is returned by `EmbeddedAssets.LoadBundledFranchiseTmSeed`.
- First-run TES seeding creates the expected TSV and TES stamp in the legacy TES import directory.
- First-run Starfield seeding creates the expected TSV and Starfield stamp in the Starfield import directory.
- Re-running with a matching stamp does not duplicate work.
- Re-running with a stale seed file repairs it.
- Non-seeded franchises remain unchanged.

Documentation check:
- Add one workflow-level test or doc assertion that explains Skyrim/TES and Starfield built-in seeding behavior alongside Fallout.

## Tradeoffs

### Why this is preferred
- Keeps full backward compatibility for existing TES users.
- Keeps the existing Starfield storage boundary unchanged.
- Minimal implementation footprint because Fallout already established the pattern.
- No migration or DB path changes.
- Users no longer need to distribute Skyrim/TES or Starfield TM separately.

### Why not split Skyrim into a new DB now
- It would require new storage boundaries, possible migration logic, and new UX semantics.
- It solves a naming purity problem, not an immediate user problem.
- The current request is about built-in distribution and automatic availability, not franchise model redesign.

## Implementation Scope

In scope:
- Add TES and Starfield embedded TM seed resources.
- Wire TES and Starfield into bundled seed loading.
- Add tests and docs.

Out of scope:
- TES/Oblivion/Morrowind sub-franchise separation.
- DB schema changes.
- Migration of existing TM data.

## Success Criteria

- A clean install can open a Skyrim/TES XML and get franchise TM hits without manual TSV import.
- A clean install can open a Starfield XML and get franchise TM hits without manual TSV import.
- Existing TES users keep using the same DB path and retain prior data.
- Existing Starfield users keep using the same DB path and retain prior data.
- Existing Fallout bundled seeding behavior remains unchanged.
- Full app and test builds pass.
