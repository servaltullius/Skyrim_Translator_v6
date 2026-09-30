# TES and Starfield Built-In TM Seed Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bundle Skyrim/TES and Starfield TM seeds inside the app and auto-seed them into the existing franchise TM import pipeline on first project load, without changing current DB paths.

**Architecture:** Reuse the existing Fallout built-in seed pattern. Add embedded TSV resources for TES and Starfield, extend `EmbeddedAssets` and `BundledFranchiseTmSeedService` with franchise-specific lookup/version/file-name mappings, and let the existing `TryAutoImportFranchiseTranslationMemoryAsync()` path persist those TSVs into the current franchise DBs.

**Tech Stack:** .NET 8 WPF, embedded resources, xUnit, existing `ProjectPaths`/`EmbeddedAssets`/`BundledFranchiseTmSeedService` services.

---

## File Map

- Create: `src/XTranslatorAi.App/Assets/TmSeeds/skyrim-tes-franchise-tm.tsv`
- Create: `src/XTranslatorAi.App/Assets/TmSeeds/starfield-franchise-tm.tsv`
- Modify: `src/XTranslatorAi.App/XTranslatorAi.App.csproj`
- Modify: `src/XTranslatorAi.App/Services/EmbeddedAssets.cs`
- Modify: `src/XTranslatorAi.App/Services/BundledFranchiseTmSeedService.cs`
- Modify: `tests/XTranslatorAi.Tests/BundledFranchiseTmSeedServiceTests.cs`
- Create: `tests/XTranslatorAi.Tests/BundledFranchiseTmWorkflowDocsTests.cs`
- Delete: `tests/XTranslatorAi.Tests/FalloutBuiltInTmWorkflowDocsTests.cs`
- Modify: `README.md`

### Task 1: Bundle TES and Starfield seed assets

**Files:**
- Create: `src/XTranslatorAi.App/Assets/TmSeeds/skyrim-tes-franchise-tm.tsv`
- Create: `src/XTranslatorAi.App/Assets/TmSeeds/starfield-franchise-tm.tsv`
- Modify: `src/XTranslatorAi.App/XTranslatorAi.App.csproj`
- Modify: `src/XTranslatorAi.App/Services/EmbeddedAssets.cs`
- Test: `tests/XTranslatorAi.Tests/BundledFranchiseTmSeedServiceTests.cs`

- [ ] **Step 1: Write the failing asset-load tests**

```csharp
[Fact]
public void LoadBundledFranchiseTmSeed_ElderScrolls_ReturnsEmbeddedSeedText()
{
    var seed = EmbeddedAssets.LoadBundledFranchiseTmSeed(BethesdaFranchise.ElderScrolls);

    Assert.NotNull(seed);
    Assert.Contains("Source\tTarget", seed, StringComparison.Ordinal);
    Assert.Contains("Skyrim", seed, StringComparison.Ordinal);
}

[Fact]
public void LoadBundledFranchiseTmSeed_Starfield_ReturnsEmbeddedSeedText()
{
    var seed = EmbeddedAssets.LoadBundledFranchiseTmSeed(BethesdaFranchise.Starfield);

    Assert.NotNull(seed);
    Assert.Contains("Source\tTarget", seed, StringComparison.Ordinal);
    Assert.Contains("House Va'ruun", seed, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:
```bash
dotnet test .worktrees/release-1.4-fo4-seed/tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter FullyQualifiedName~LoadBundledFranchiseTmSeed
```

Expected:
- FAIL because `EmbeddedAssets.LoadBundledFranchiseTmSeed` returns `null` for `BethesdaFranchise.ElderScrolls` and `BethesdaFranchise.Starfield`.

- [ ] **Step 3: Copy the curated seed TSVs into app assets**

Run:
```bash
cp /mnt/c/Users/kdw73/AppData/Local/XTranslatorAi/Global/tm-import/imported/skyrim_community_tm.imported.20260126-182944.tsv .worktrees/release-1.4-fo4-seed/src/XTranslatorAi.App/Assets/TmSeeds/skyrim-tes-franchise-tm.tsv
cp /home/kdw73/Skyrim_Translator_v6/artifacts/tm/starfield-franchise-tm.tsv .worktrees/release-1.4-fo4-seed/src/XTranslatorAi.App/Assets/TmSeeds/starfield-franchise-tm.tsv
```

Expected first lines:
```text
Source	Target
"...and unwavering obedience to the officers of his great Empire."
```

```text
Source	Target
"A first?" I take it something's bothering you?
```

- [ ] **Step 4: Register the new assets as embedded resources**

Update `src/XTranslatorAi.App/XTranslatorAi.App.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Assets\메타프롬프트.md" />
  <EmbeddedResource Include="Assets\메타프롬프트_폴아웃.md" />
  <EmbeddedResource Include="Assets\메타프롬프트_스타필드.md" />
  <EmbeddedResource Include="Assets\기본용어집.md" />
  <EmbeddedResource Include="Assets\기본용어집_폴아웃.md" />
  <EmbeddedResource Include="Assets\기본용어집_스타필드.md" />
  <EmbeddedResource Include="Assets\TmSeeds\fallout4-franchise-tm.tsv" />
  <EmbeddedResource Include="Assets\TmSeeds\skyrim-tes-franchise-tm.tsv" />
  <EmbeddedResource Include="Assets\TmSeeds\starfield-franchise-tm.tsv" />
</ItemGroup>
```

- [ ] **Step 5: Extend embedded asset lookup for TES and Starfield**

Update `src/XTranslatorAi.App/Services/EmbeddedAssets.cs`:

```csharp
public static string? LoadBundledFranchiseTmSeed(BethesdaFranchise franchise)
    => franchise switch
    {
        BethesdaFranchise.ElderScrolls => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm.tsv"),
        BethesdaFranchise.Fallout => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.fallout4-franchise-tm.tsv"),
        BethesdaFranchise.Starfield => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.starfield-franchise-tm.tsv"),
        _ => null,
    };
```

- [ ] **Step 6: Run the focused tests and verify they pass**

Run:
```bash
dotnet test .worktrees/release-1.4-fo4-seed/tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter FullyQualifiedName~LoadBundledFranchiseTmSeed
```

Expected:
- PASS
- TES and Starfield resource-load tests succeed.

- [ ] **Step 7: Commit asset bundling changes**

Run:
```bash
cd .worktrees/release-1.4-fo4-seed
git add src/XTranslatorAi.App/Assets/TmSeeds/skyrim-tes-franchise-tm.tsv src/XTranslatorAi.App/Assets/TmSeeds/starfield-franchise-tm.tsv src/XTranslatorAi.App/XTranslatorAi.App.csproj src/XTranslatorAi.App/Services/EmbeddedAssets.cs tests/XTranslatorAi.Tests/BundledFranchiseTmSeedServiceTests.cs
git commit -m "feat: bundle tes and starfield tm seed assets"
```

### Task 2: Extend bundled seed service for TES and Starfield

**Files:**
- Modify: `src/XTranslatorAi.App/Services/BundledFranchiseTmSeedService.cs`
- Modify: `tests/XTranslatorAi.Tests/BundledFranchiseTmSeedServiceTests.cs`

- [ ] **Step 1: Write the failing seed-service tests**

Add these tests to `tests/XTranslatorAi.Tests/BundledFranchiseTmSeedServiceTests.cs`:

```csharp
[Fact]
public async Task EnsureBundledSeedAsync_WritesSeedOnceForElderScrolls()
{
    var root = CreateTempRoot();
    try
    {
        var service = new BundledFranchiseTmSeedService(root);

        await service.EnsureBundledSeedAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);
        await service.EnsureBundledSeedAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);

        var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.ElderScrolls, root);
        var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.ElderScrolls, "v1", root);
        var files = Directory.GetFiles(importDir, "*.tsv", SearchOption.TopDirectoryOnly);

        Assert.Single(files);
        Assert.Equal("bundled-skyrim-tes-franchise-tm.tsv", Path.GetFileName(files[0]));
        Assert.True(File.Exists(stampPath));
    }
    finally
    {
        TryDeleteDirectory(root);
    }
}

[Fact]
public async Task EnsureBundledSeedAsync_WritesSeedOnceForStarfield()
{
    var root = CreateTempRoot();
    try
    {
        var service = new BundledFranchiseTmSeedService(root);

        await service.EnsureBundledSeedAsync(BethesdaFranchise.Starfield, CancellationToken.None);
        await service.EnsureBundledSeedAsync(BethesdaFranchise.Starfield, CancellationToken.None);

        var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Starfield, root);
        var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.Starfield, "v1", root);
        var files = Directory.GetFiles(importDir, "*.tsv", SearchOption.TopDirectoryOnly);

        Assert.Single(files);
        Assert.Equal("bundled-starfield-franchise-tm.tsv", Path.GetFileName(files[0]));
        Assert.True(File.Exists(stampPath));
    }
    finally
    {
        TryDeleteDirectory(root);
    }
}
```

Add stale-seed repair coverage for both franchises:

```csharp
[Theory]
[InlineData(BethesdaFranchise.ElderScrolls, "bundled-skyrim-tes-franchise-tm.tsv")]
[InlineData(BethesdaFranchise.Starfield, "bundled-starfield-franchise-tm.tsv")]
public async Task EnsureBundledSeedAsync_RepairsExistingSeedWhenStampAlreadyExists(BethesdaFranchise franchise, string fileName)
{
    var root = CreateTempRoot();
    try
    {
        var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(franchise, root);
        var seedPath = Path.Combine(importDir, fileName);
        var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(franchise, "v1", root);
        Directory.CreateDirectory(importDir);

        await File.WriteAllTextAsync(seedPath, "Source\tTarget\nWRONG\tWRONG", CancellationToken.None);
        await File.WriteAllTextAsync(stampPath, "v1", CancellationToken.None);

        var service = new BundledFranchiseTmSeedService(root);
        await service.EnsureBundledSeedAsync(franchise, CancellationToken.None);

        var seed = await File.ReadAllTextAsync(seedPath, CancellationToken.None);
        Assert.DoesNotContain("WRONG", seed, StringComparison.Ordinal);
    }
    finally
    {
        TryDeleteDirectory(root);
    }
}
```

- [ ] **Step 2: Run the focused seed-service tests and verify they fail**

Run:
```bash
dotnet test .worktrees/release-1.4-fo4-seed/tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter FullyQualifiedName~EnsureBundledSeedAsync
```

Expected:
- FAIL because TES and Starfield currently have no seed version or file-name mappings.

- [ ] **Step 3: Refactor the bundled seed service to support three franchises**

Update `src/XTranslatorAi.App/Services/BundledFranchiseTmSeedService.cs`:

```csharp
private static string? GetSeedVersion(BethesdaFranchise franchise)
    => franchise switch
    {
        BethesdaFranchise.ElderScrolls => "v1",
        BethesdaFranchise.Fallout => "v1",
        BethesdaFranchise.Starfield => "v1",
        _ => null,
    };

private static string GetSeedFileName(BethesdaFranchise franchise)
    => franchise switch
    {
        BethesdaFranchise.ElderScrolls => "bundled-skyrim-tes-franchise-tm.tsv",
        BethesdaFranchise.Fallout => "bundled-fallout4-franchise-tm.tsv",
        BethesdaFranchise.Starfield => "bundled-starfield-franchise-tm.tsv",
        _ => "bundled-franchise-tm.tsv",
    };
```

Keep the existing control flow intact:
- load embedded seed text
- return if seed text or version is blank
- if stamp exists, repair mismatched seed content only
- otherwise write seed if needed and then stamp
- swallow exceptions

- [ ] **Step 4: Run the focused seed-service tests and verify they pass**

Run:
```bash
dotnet test .worktrees/release-1.4-fo4-seed/tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter FullyQualifiedName~BundledFranchiseTmSeedServiceTests
```

Expected:
- PASS
- Fallout behavior still passes
- TES and Starfield first-run and stale-repair tests pass

- [ ] **Step 5: Commit the seed-service changes**

Run:
```bash
cd .worktrees/release-1.4-fo4-seed
git add src/XTranslatorAi.App/Services/BundledFranchiseTmSeedService.cs tests/XTranslatorAi.Tests/BundledFranchiseTmSeedServiceTests.cs
git commit -m "feat: auto-seed bundled tes and starfield tm"
```

### Task 3: Update README and workflow documentation tests

**Files:**
- Modify: `README.md`
- Create: `tests/XTranslatorAi.Tests/BundledFranchiseTmWorkflowDocsTests.cs`
- Delete: `tests/XTranslatorAi.Tests/FalloutBuiltInTmWorkflowDocsTests.cs`

- [ ] **Step 1: Write the failing workflow doc test**

Create `tests/XTranslatorAi.Tests/BundledFranchiseTmWorkflowDocsTests.cs`:

```csharp
using System;
using System.IO;

namespace XTranslatorAi.Tests;

public class BundledFranchiseTmWorkflowDocsTests
{
    [Fact]
    public void Readme_BuiltInTmSection_DescribesBundledSeedContracts()
    {
        var readme = ReadRepoFile("README.md");

        Assert.Contains("Fallout TM is currently scoped to Fallout 4 family data.", readme, StringComparison.Ordinal);
        Assert.Contains("Bundled Fallout TM is auto-seeded on first Fallout project load.", readme, StringComparison.Ordinal);
        Assert.Contains("Bundled Skyrim/TES TM is auto-seeded on first Elder Scrolls project load.", readme, StringComparison.Ordinal);
        Assert.Contains("Bundled Starfield TM is auto-seeded on first Starfield project load.", readme, StringComparison.Ordinal);
        Assert.Contains("Operator-provided TSV imports still work through the existing Franchise TM import flow.", readme, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.Combine(repoRoot, relativePath);
        return File.ReadAllText(path);
    }
}
```

- [ ] **Step 2: Remove the Fallout-only doc test**

Run:
```bash
rm .worktrees/release-1.4-fo4-seed/tests/XTranslatorAi.Tests/FalloutBuiltInTmWorkflowDocsTests.cs
```

- [ ] **Step 3: Run the doc test and verify it fails**

Run:
```bash
dotnet test .worktrees/release-1.4-fo4-seed/tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter FullyQualifiedName~BundledFranchiseTmWorkflowDocsTests
```

Expected:
- FAIL because README only documents Fallout bundled TM today.

- [ ] **Step 4: Update README to describe all bundled franchise seeds**

Replace the current `## Fallout TM` section in `README.md` with:

```md
## Built-In Franchise TM

Fallout TM is currently scoped to Fallout 4 family data.
Bundled Fallout TM is auto-seeded on first Fallout project load.
Bundled Skyrim/TES TM is auto-seeded on first Elder Scrolls project load.
Bundled Starfield TM is auto-seeded on first Starfield project load.
Operator-provided TSV imports still work through the existing Franchise TM import flow.
```

- [ ] **Step 5: Run the doc test and verify it passes**

Run:
```bash
dotnet test .worktrees/release-1.4-fo4-seed/tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter FullyQualifiedName~BundledFranchiseTmWorkflowDocsTests
```

Expected:
- PASS

- [ ] **Step 6: Commit the docs changes**

Run:
```bash
cd .worktrees/release-1.4-fo4-seed
git add README.md tests/XTranslatorAi.Tests/BundledFranchiseTmWorkflowDocsTests.cs tests/XTranslatorAi.Tests/FalloutBuiltInTmWorkflowDocsTests.cs
git commit -m "docs: document bundled tes and starfield tm seeding"
```

### Task 4: Full verification and release-candidate artifact build

**Files:**
- Verify only: existing modified files from Tasks 1-3

- [ ] **Step 1: Run the full test build**

Run:
```bash
dotnet build .worktrees/release-1.4-fo4-seed/src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release
dotnet build .worktrees/release-1.4-fo4-seed/tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release
dotnet test .worktrees/release-1.4-fo4-seed/tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --no-build
```

Expected:
- All builds succeed.
- Full xUnit suite passes.

- [ ] **Step 2: Publish a single-file Windows build**

Run:
```bash
cd .worktrees/release-1.4-fo4-seed
dotnet restore src/XTranslatorAi.App/XTranslatorAi.App.csproj -r win-x64 -p:EnableWindowsTargeting=true
dotnet clean src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 -p:EnableWindowsTargeting=true
dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 -o artifacts/publish-win-x64-singlefile-min -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:EnableWindowsTargeting=true -p:DeleteExistingFiles=true
sha256sum artifacts/publish-win-x64-singlefile-min/TulliusTranslator.exe > artifacts/publish-win-x64-singlefile-min/TulliusTranslator.exe.sha256
```

Expected:
- `artifacts/publish-win-x64-singlefile-min/TulliusTranslator.exe` exists.
- `artifacts/publish-win-x64-singlefile-min/TulliusTranslator.exe.sha256` exists.

- [ ] **Step 3: Smoke-check the bundled seed files landed in the build branch**

Run:
```bash
ls .worktrees/release-1.4-fo4-seed/src/XTranslatorAi.App/Assets/TmSeeds
```

Expected output includes:
```text
fallout4-franchise-tm.tsv
skyrim-tes-franchise-tm.tsv
starfield-franchise-tm.tsv
```

- [ ] **Step 4: Commit the verified release-candidate state**

Run:
```bash
cd .worktrees/release-1.4-fo4-seed
git status --short
git commit --allow-empty -m "chore: verify bundled tes and starfield tm seed candidate"
```

Expected:
- Working tree is clean before the empty verification commit.
- Verification commit records the final checked state.
