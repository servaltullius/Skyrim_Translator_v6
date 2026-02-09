# Global + Project Glossary (2-tier) Implementation Plan

**Goal:** Add a 2-tier glossary system where a shared “Global Glossary” applies to all projects, and each XML/project can override it with a project-local glossary.

**Architecture:**
- Keep the existing per-XML project DB (`%LOCALAPPDATA%\\XTranslatorAi\\Projects\\*.sqlite`) as the project-local glossary store.
- Add a separate global SQLite DB (`%LOCALAPPDATA%\\XTranslatorAi\\Global\\global-glossary.sqlite`) using the same `ProjectDb` schema, but only the `Glossary` table is used.
- At translation time, merge `global + project` glossary:
  - Project overrides global by `SourceTerm` (case-insensitive, trimmed). A disabled project entry also suppresses the global entry for that term.
  - Reassign glossary IDs in the merged list to avoid token mapping collisions in `GlossaryApplier`.

**Tech Stack:** .NET 8, WPF (App), SQLite (`Microsoft.Data.Sqlite`), CommunityToolkit.Mvvm, xUnit.

## Tasks (implemented)

1. Add global DB path helper.
2. Open/initialize global glossary DB when loading a project.
3. Add a WPF tab to view/edit/import/export the global glossary.
4. Merge global+project glossary in:
   - `TranslationService.TranslateIdsAsync`
   - `TranslationCostEstimator.EstimateAsync`
   - Preflight and Project Context scanners
5. Run tests and publish Windows single-file EXE.

## How to test

- Unit tests: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Build WPF on non-Windows: `dotnet build src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -p:EnableWindowsTargeting=true`
- Publish win-x64 EXE: `dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 -o artifacts/publish-win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableWindowsTargeting=true`

