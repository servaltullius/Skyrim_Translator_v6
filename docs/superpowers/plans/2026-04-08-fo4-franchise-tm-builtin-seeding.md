# Fallout 4 Franchise TM Built-In Seeding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bundle a Fallout 4-scoped TM seed with the app and auto-import it once into the existing `Fallout` franchise TM database so users do not need to manage a separate TSV file.

**Architecture:** Keep the current `BethesdaFranchise.Fallout` enum and DB path as-is, but explicitly treat it as `Fallout 4 family` in data policy, docs, and seed content. Reuse the existing `tm-import` auto-import pipeline instead of adding a new TM parser: the app writes a bundled FO4 TSV seed into the franchise import directory on first use, then the existing import code persists it into SQLite. Idempotency is handled with a versioned stamp file so the bundled seed is injected only once per seed version.

**Tech Stack:** .NET 8 WPF, embedded resources, SQLite via existing `ProjectDb`, xUnit tests, existing TSV TM import pipeline.

---

## Requirements Summary
- Users must not have to manually distribute or import a separate Fallout TM TSV.
- The built-in Fallout TM must stay isolated from TES and Starfield TM stores.
- The current runtime model should remain simple: no new TM format, no runtime strings parser.
- Existing `Franchise TM` import behavior and `tm-import` folder flow must continue to work for operator-supplied TSV files.
- Current `Fallout` franchise scope should be treated as `Fallout 4 family`; splitting FO3/FNV/FO76 is out of scope for this change.

## Key Tradeoffs
- **Chosen:** keep `BethesdaFranchise.Fallout` unified and define bundled data as FO4-only.
  This avoids enum churn, migration complexity, and prompt/UI branching right now.
- **Rejected:** split Fallout by title immediately.
  This is cleaner long-term, but it touches enum storage, pathing, UI, prompt assets, and migration all at once.
- **Chosen:** bundled TSV seed + existing import path.
  This is lower-risk than inventing a second persistence path or direct DB seeding format.
- **Rejected:** require sidecar `tm-seeds` files in release packages.
  This preserves legal separation but degrades user experience and creates support burden.

## Risks / Mitigations
- **Risk:** bundled FO4 TM may be re-applied on every run.
  **Mitigation:** store a versioned stamp under the franchise import directory and skip injection when already applied.
- **Risk:** future Fallout title support may inherit FO4-biased labels.
  **Mitigation:** document this release as `FO4 family` and defer title-splitting to a dedicated follow-up.
- **Risk:** seed asset is large.
  **Mitigation:** keep TSV compressed inside single-file publish through existing app publish settings; verify publish size delta explicitly.
- **Risk:** seed injection failures could block XML loading.
  **Mitigation:** bundled seed copy must be best-effort and non-fatal, matching the current `Franchise TM` import philosophy.

## Files to Modify / Create
- Modify: `src/XTranslatorAi.App/XTranslatorAi.App.csproj`
- Modify: `src/XTranslatorAi.App/App.xaml.cs`
- Modify: `src/XTranslatorAi.App/Services/EmbeddedAssets.cs`
- Modify: `src/XTranslatorAi.App/Services/ProjectPaths.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Project.cs`
- Modify: `README.md`
- Create: `src/XTranslatorAi.App/Assets/TmSeeds/fallout4-franchise-tm.tsv`
- Create: `src/XTranslatorAi.App/Services/BundledFranchiseTmSeedService.cs`
- Create: `tests/XTranslatorAi.Tests/BundledFranchiseTmSeedServiceTests.cs`
- Create: `tests/XTranslatorAi.Tests/FalloutBuiltInTmWorkflowDocsTests.cs`

### Task 1: Freeze Fallout scope as FO4 family in docs and tests

**Files:**
- Modify: `README.md`
- Create: `tests/XTranslatorAi.Tests/FalloutBuiltInTmWorkflowDocsTests.cs`

- [ ] **Step 1: Write the failing documentation contract test**

```csharp
using System;
using System.IO;
using Xunit;

namespace XTranslatorAi.Tests;

public class FalloutBuiltInTmWorkflowDocsTests
{
    [Fact]
    public void Readme_StatesThatCurrentFalloutScopeIsFo4Family()
    {
        var readme = File.ReadAllText("README.md");

        Assert.Contains("Fallout TM is currently scoped to Fallout 4 family data.", readme, StringComparison.Ordinal);
        Assert.Contains("Bundled Fallout TM is auto-seeded on first Fallout project load.", readme, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter FalloutBuiltInTmWorkflowDocsTests`
Expected: FAIL because the README lines do not exist yet.

- [ ] **Step 3: Update README with exact FO4 scope and bundled-seed behavior**

```md
## Fallout TM

Fallout TM is currently scoped to Fallout 4 family data.
The app bundles a Fallout franchise TM seed and auto-seeds it on first Fallout project load.
Operator-provided TSV imports still work through the existing Franchise TM import flow.
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter FalloutBuiltInTmWorkflowDocsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add README.md tests/XTranslatorAi.Tests/FalloutBuiltInTmWorkflowDocsTests.cs
git commit -m "docs: define fallout tm as fo4-family built-in seed"
```

### Task 2: Add bundled FO4 TM seed resource loading

**Files:**
- Modify: `src/XTranslatorAi.App/XTranslatorAi.App.csproj`
- Modify: `src/XTranslatorAi.App/Services/EmbeddedAssets.cs`
- Create: `src/XTranslatorAi.App/Assets/TmSeeds/fallout4-franchise-tm.tsv`

- [ ] **Step 1: Write the failing resource access test**

```csharp
using System;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;
using Xunit;

namespace XTranslatorAi.Tests;

public class BundledFranchiseTmSeedResourceTests
{
    [Fact]
    public void EmbeddedAssets_LoadBundledFranchiseTmSeed_ReturnsFo4Seed()
    {
        var text = EmbeddedAssets.LoadBundledFranchiseTmSeed(BethesdaFranchise.Fallout);

        Assert.Contains("Source\tTarget", text, StringComparison.Ordinal);
        Assert.Contains("Pip-Boy", text, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter BundledFranchiseTmSeedResourceTests`
Expected: FAIL because `LoadBundledFranchiseTmSeed` does not exist.

- [ ] **Step 3: Embed the FO4 TSV and expose a loader in `EmbeddedAssets`**

```xml
<ItemGroup>
  <EmbeddedResource Include="Assets\TmSeeds\fallout4-franchise-tm.tsv" />
</ItemGroup>
```

```csharp
public static string? LoadBundledFranchiseTmSeed(BethesdaFranchise franchise)
    => franchise switch
    {
        BethesdaFranchise.Fallout => LoadOptionalTextResource("XTranslatorAi.App.Assets.TmSeeds.fallout4-franchise-tm.tsv"),
        _ => null,
    };

private static string? LoadOptionalTextResource(string resourceName)
{
    var assembly = Assembly.GetExecutingAssembly();
    using var stream = assembly.GetManifestResourceStream(resourceName);
    if (stream == null)
    {
        return null;
    }

    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}
```

```tsv
Source	Target
Pip-Boy	핍보이
Vault	볼트
Stimpak	스팀팩
```

- [ ] **Step 4: Run the resource test**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter BundledFranchiseTmSeedResourceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/XTranslatorAi.App/XTranslatorAi.App.csproj src/XTranslatorAi.App/Services/EmbeddedAssets.cs src/XTranslatorAi.App/Assets/TmSeeds/fallout4-franchise-tm.tsv tests/XTranslatorAi.Tests/BundledFranchiseTmSeedServiceTests.cs
git commit -m "feat: add embedded fallout tm seed resource"
```

### Task 3: Inject bundled FO4 seed into the existing import pipeline once per version

**Files:**
- Create: `src/XTranslatorAi.App/Services/BundledFranchiseTmSeedService.cs`
- Modify: `src/XTranslatorAi.App/Services/ProjectPaths.cs`
- Modify: `src/XTranslatorAi.App/App.xaml.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Project.cs`
- Create: `tests/XTranslatorAi.Tests/BundledFranchiseTmSeedServiceTests.cs`

- [ ] **Step 1: Write failing idempotency and path tests**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;
using Xunit;

namespace XTranslatorAi.Tests;

public class BundledFranchiseTmSeedServiceTests
{
    [Fact]
    public async Task EnsureBundledSeedAsync_WritesSeedOnceForFallout()
    {
        var root = Path.Combine(Path.GetTempPath(), "xtai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var service = new BundledFranchiseTmSeedService(root);
        await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);
        await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);

        var importDir = Path.Combine(root, "fallout", "tm-import");
        Assert.Single(Directory.GetFiles(importDir, "*.tsv"));
        Assert.True(File.Exists(Path.Combine(importDir, ".bundled-seed.fallout.v1.stamp")));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter BundledFranchiseTmSeedServiceTests`
Expected: FAIL because the service does not exist.

- [ ] **Step 3: Implement the seed writer and path helper**

```csharp
public sealed class BundledFranchiseTmSeedService
{
    private const string FalloutSeedVersion = "v1";
    private readonly string? _globalRootOverride;

    public BundledFranchiseTmSeedService(string? globalRootOverride = null)
    {
        _globalRootOverride = globalRootOverride;
    }

    public async Task EnsureBundledSeedAsync(BethesdaFranchise franchise, CancellationToken cancellationToken)
    {
        var seedText = EmbeddedAssets.LoadBundledFranchiseTmSeed(franchise);
        if (string.IsNullOrWhiteSpace(seedText))
        {
            return;
        }

        var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(franchise, _globalRootOverride);
        var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(franchise, FalloutSeedVersion, _globalRootOverride);
        if (File.Exists(stampPath))
        {
            return;
        }

        Directory.CreateDirectory(importDir);
        var seedPath = Path.Combine(importDir, "bundled-fallout4-franchise-tm.tsv");
        if (!File.Exists(seedPath))
        {
            await File.WriteAllTextAsync(seedPath, seedText, cancellationToken);
        }

        await File.WriteAllTextAsync(stampPath, FalloutSeedVersion, cancellationToken);
    }
}
```

```csharp
public static string GetGlobalTranslationMemoryImportDir(BethesdaFranchise franchise, string? globalRootOverride = null)
{
    var dbPath = GetGlobalGlossaryDbPath(franchise, globalRootOverride);
    var baseDir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
    if (string.IsNullOrWhiteSpace(baseDir))
    {
        baseDir = GetProjectsBaseDir();
    }

    var dir = Path.Combine(baseDir, "tm-import");
    Directory.CreateDirectory(dir);
    return dir;
}

public static string GetBundledFranchiseTmSeedStampPath(BethesdaFranchise franchise, string version, string? globalRootOverride = null)
    => Path.Combine(GetGlobalTranslationMemoryImportDir(franchise, globalRootOverride), $".bundled-seed.{franchise.ToString().ToLowerInvariant()}.{version}.stamp");
```

```csharp
var bundledFranchiseTmSeedService = new BundledFranchiseTmSeedService();
```

```csharp
await _bundledFranchiseTmSeedService.EnsureBundledSeedAsync(result.Franchise, CancellationToken.None);
await TryAutoImportFranchiseTranslationMemoryAsync();
```

- [ ] **Step 4: Re-run the tests**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter BundledFranchiseTmSeedServiceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/XTranslatorAi.App/Services/BundledFranchiseTmSeedService.cs src/XTranslatorAi.App/Services/ProjectPaths.cs src/XTranslatorAi.App/App.xaml.cs src/XTranslatorAi.App/ViewModels/MainViewModel.Project.cs tests/XTranslatorAi.Tests/BundledFranchiseTmSeedServiceTests.cs
git commit -m "feat: auto-seed bundled fallout franchise tm"
```

### Task 4: Full verification and publish-size check

**Files:**
- Modify: `README.md` if verification notes need clarification

- [ ] **Step 1: Run focused app and test builds**

Run: `dotnet build src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release && dotnet build tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
Expected: both builds succeed with zero errors.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --no-build`
Expected: PASS.

- [ ] **Step 3: Publish the single-file app and inspect size delta**

Run: `dotnet clean src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 && dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 -o artifacts/publish-win-x64-singlefile-min -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:EnableWindowsTargeting=true -p:DeleteExistingFiles=true`
Expected: publish succeeds and `artifacts/publish-win-x64-singlefile-min/TulliusTranslator.exe` size increase is acceptable.

- [ ] **Step 4: Smoke-check the seeded import behavior manually**

Run:
```bash
rm -rf "$HOME/.local/share/XTranslatorAi" 2>/dev/null || true
```

Manual verification:
- Launch the app.
- Select `Fallout`.
- Open a FO4 xTranslator XML.
- Confirm status shows bundled `Franchise TM` auto-import before normal TM reload.
- Confirm the Fallout franchise DB contains imported TM rows without any manual TSV selection.

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "test: verify bundled fallout tm seeding workflow"
```

## Self-Review
- **Spec coverage:** This plan covers FO4-only scope definition, bundled asset loading, first-run auto-injection, existing TSV pipeline reuse, test coverage, and publish verification.
- **Placeholder scan:** No TBD/TODO placeholders remain; all tasks name exact files, commands, and example code.
- **Type consistency:** The proposed service names and helper methods are consistent across tasks and stay within existing `Franchise TM` naming.

Plan complete and saved to `docs/superpowers/plans/2026-04-08-fo4-franchise-tm-builtin-seeding.md`. Two execution options:

1. Subagent-Driven (recommended) - I dispatch a fresh subagent per task, review between tasks, fast iteration
2. Inline Execution - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
