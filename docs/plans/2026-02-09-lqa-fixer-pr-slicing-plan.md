# LqaScanner + KoreanTranslationFixer PR-Slicing Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Split the two hotspot methods (`LqaScanner.ScanAsync`, `KoreanTranslationFixer.Fix`) into small, reviewable PRs while preserving behavior.

**Architecture:** Keep public entrypoints unchanged and introduce internal rule/step pipelines behind them. Build characterization tests first, then migrate logic incrementally in vertical slices (rules/steps), so each PR is behavior-safe and reversible. Prefer extraction + delegation over semantic rewrites.

**Tech Stack:** .NET 8, C#, xUnit, WPF app + Core library, vibe-kit (`scripts/vibe.py`).

## Execution Status (2026-02-09)
- `done` Task 1 (PR-01): Characterization safety net.
- `done` Task 2 (PR-02): LQA pipeline skeleton (legacy delegation parity).
- `done` Task 3 (PR-03): Extract LQA Rule Set A (low-risk rules).
- `done` Task 4 (PR-04): Extract LQA Rule Set B (Korean heuristic rules).
- `done` Task 5 (PR-05): Extract LQA tone/grouping + final sort stage.
- `done` Task 6 (PR-06): Korean fixer step pipeline skeleton.
- `done` Task 7 (PR-07): Extract Korean fixer step set A (particles).
- `done` Task 8 (PR-08): Extract Korean fixer step set B (duration/artifact) + regex hardening.
- `done` Task 9 (PR-09): Final consolidation + hotspot check.

---

### Task 1 (PR-01): Characterization Safety Net

**Files:**
- Create: `tests/XTranslatorAi.Tests/LqaScannerCharacterizationTests.cs`
- Create: `tests/XTranslatorAi.Tests/KoreanTranslationFixerCharacterizationTests.cs`
- Modify: `tests/XTranslatorAi.Tests/LqaScannerTests.cs`
- Modify: `tests/XTranslatorAi.Tests/KoreanTranslationFixerTests.cs`

**Step 1: Write the failing tests**

```csharp
[Fact]
public async Task ScanAsync_Characterization_BatchVector_IsStable()
{
    var entries = CharacterizationFixtures.LqaEntries();
    var issues = await LqaScanner.ScanAsync(entries, "ko", CharacterizationFixtures.Glossary());
    Assert.Equal(CharacterizationFixtures.ExpectedIssueCodesById(), Project(issues));
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~Characterization"`
Expected: FAIL (baseline vectors not aligned yet).

**Step 3: Write minimal implementation**

```csharp
internal static class CharacterizationFixtures
{
    public static IReadOnlyList<LqaScanEntry> LqaEntries() => /* representative rows */;
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~Characterization"`
Expected: PASS.

**Step 5: Commit**

```bash
git add tests/XTranslatorAi.Tests/LqaScannerCharacterizationTests.cs tests/XTranslatorAi.Tests/KoreanTranslationFixerCharacterizationTests.cs tests/XTranslatorAi.Tests/LqaScannerTests.cs tests/XTranslatorAi.Tests/KoreanTranslationFixerTests.cs
git commit -m "test: add characterization safety net for lqa scanner and korean fixer"
```

---

### Task 2 (PR-02): LQA Pipeline Skeleton (No Behavior Change)

**Files:**
- Create: `src/XTranslatorAi.Core/Text/Lqa/Internal/LqaScanContext.cs`
- Create: `src/XTranslatorAi.Core/Text/Lqa/Internal/ILqaRule.cs`
- Create: `src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/LegacyMonolithRule.cs`
- Modify: `src/XTranslatorAi.Core/Text/LqaScanner.cs`
- Test: `tests/XTranslatorAi.Tests/LqaScannerCharacterizationTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public async Task ScanAsync_PipelineSkeleton_StillMatchesCharacterization()
{
    // existing characterization assertion reused
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~LqaScannerCharacterization"`
Expected: FAIL after skeleton wiring before legacy delegation.

**Step 3: Write minimal implementation**

```csharp
internal interface ILqaRule
{
    void Apply(LqaScanContext ctx, List<LqaIssue> issues);
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~LqaScannerCharacterization"`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/XTranslatorAi.Core/Text/Lqa/Internal/LqaScanContext.cs src/XTranslatorAi.Core/Text/Lqa/Internal/ILqaRule.cs src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/LegacyMonolithRule.cs src/XTranslatorAi.Core/Text/LqaScanner.cs tests/XTranslatorAi.Tests/LqaScannerCharacterizationTests.cs
git commit -m "refactor: add lqa rule pipeline skeleton with legacy parity"
```

---

### Task 3 (PR-03): Extract LQA Rule Set A (Low-Risk Rules)

**Files:**
- Create: `src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/TmFallbackRule.cs`
- Create: `src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/UntranslatedRule.cs`
- Create: `src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/TokenMismatchRule.cs`
- Create: `src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/BracketMismatchRule.cs`
- Modify: `src/XTranslatorAi.Core/Text/LqaScanner.cs`
- Test: `tests/XTranslatorAi.Tests/LqaScannerTests.cs`
- Test: `tests/XTranslatorAi.Tests/LqaScannerCharacterizationTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public async Task ScanAsync_RuleSetA_ProducesSameCodesAndOrder()
{
    // assert sequence equality by (Id, Code, Severity, OrderIndex)
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~LqaScanner"`
Expected: FAIL before rule extraction is fully wired.

**Step 3: Write minimal implementation**

```csharp
internal sealed class UntranslatedRule : ILqaRule
{
    public void Apply(LqaScanContext ctx, List<LqaIssue> issues) { /* moved logic */ }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~LqaScanner"`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/TmFallbackRule.cs src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/UntranslatedRule.cs src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/TokenMismatchRule.cs src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/BracketMismatchRule.cs src/XTranslatorAi.Core/Text/LqaScanner.cs tests/XTranslatorAi.Tests/LqaScannerTests.cs tests/XTranslatorAi.Tests/LqaScannerCharacterizationTests.cs
git commit -m "refactor: extract lqa low-risk rules from scanner monolith"
```

---

### Task 4 (PR-04): Extract LQA Rule Set B (Korean Heuristic Rules)

**Files:**
- Create: `src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/GlossaryMissingRule.cs`
- Create: `src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/ParticleRules.cs`
- Create: `src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/EnglishResidueRule.cs`
- Modify: `src/XTranslatorAi.Core/Text/LqaScanner.cs`
- Test: `tests/XTranslatorAi.Tests/LqaScannerTests.cs`
- Test: `tests/XTranslatorAi.Tests/LqaScannerCharacterizationTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public async Task ScanAsync_RuleSetB_KeepsKoreanHeuristicParity()
{
    // existing particle/glossary tests + characterization
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~LqaScanner"`
Expected: FAIL before all rule handlers are delegated.

**Step 3: Write minimal implementation**

```csharp
internal sealed class ParticleRules : ILqaRule
{
    public void Apply(LqaScanContext ctx, List<LqaIssue> issues) { /* moved code */ }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~LqaScanner"`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/GlossaryMissingRule.cs src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/ParticleRules.cs src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/EnglishResidueRule.cs src/XTranslatorAi.Core/Text/LqaScanner.cs tests/XTranslatorAi.Tests/LqaScannerTests.cs tests/XTranslatorAi.Tests/LqaScannerCharacterizationTests.cs
git commit -m "refactor: extract lqa korean heuristic rule group"
```

---

### Task 5 (PR-05): Extract LQA Tone/Grouping + Final Sort Stage

**Files:**
- Create: `src/XTranslatorAi.Core/Text/Lqa/Internal/LqaToneClassifier.cs`
- Create: `src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/RecToneRule.cs`
- Create: `src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/DialogueToneConsistencyRule.cs`
- Modify: `src/XTranslatorAi.Core/Text/LqaScanner.cs`
- Test: `tests/XTranslatorAi.Tests/LqaScannerCharacterizationTests.cs`
- Test: `tests/XTranslatorAi.Tests/LqaScannerTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public async Task ScanAsync_ToneRules_KeepIssueOrderStable()
{
    // assert exact sorted output shape
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~LqaScanner"`
Expected: FAIL before tone classifier extraction parity.

**Step 3: Write minimal implementation**

```csharp
internal static class LqaToneClassifier
{
    public static ToneKind Classify(string? text) => /* moved logic */;
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~LqaScanner"`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/XTranslatorAi.Core/Text/Lqa/Internal/LqaToneClassifier.cs src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/RecToneRule.cs src/XTranslatorAi.Core/Text/Lqa/Internal/Rules/DialogueToneConsistencyRule.cs src/XTranslatorAi.Core/Text/LqaScanner.cs tests/XTranslatorAi.Tests/LqaScannerTests.cs tests/XTranslatorAi.Tests/LqaScannerCharacterizationTests.cs
git commit -m "refactor: extract lqa tone and dialogue grouping rules"
```

---

### Task 6 (PR-06): Korean Fixer Step Pipeline Skeleton

**Files:**
- Create: `src/XTranslatorAi.Core/Text/KoreanFix/Internal/KoreanFixContext.cs`
- Create: `src/XTranslatorAi.Core/Text/KoreanFix/Internal/IKoreanFixStep.cs`
- Create: `src/XTranslatorAi.Core/Text/KoreanFix/Internal/Steps/LegacyFixStep.cs`
- Modify: `src/XTranslatorAi.Core/Text/KoreanTranslationFixer.cs`
- Test: `tests/XTranslatorAi.Tests/KoreanTranslationFixerCharacterizationTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void Fix_PipelineSkeleton_StillMatchesCharacterization()
{
    // compare representative vector outputs
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~KoreanTranslationFixerCharacterization"`
Expected: FAIL before legacy delegation.

**Step 3: Write minimal implementation**

```csharp
internal interface IKoreanFixStep
{
    string Apply(in KoreanFixContext context, string text);
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~KoreanTranslationFixerCharacterization"`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/XTranslatorAi.Core/Text/KoreanFix/Internal/KoreanFixContext.cs src/XTranslatorAi.Core/Text/KoreanFix/Internal/IKoreanFixStep.cs src/XTranslatorAi.Core/Text/KoreanFix/Internal/Steps/LegacyFixStep.cs src/XTranslatorAi.Core/Text/KoreanTranslationFixer.cs tests/XTranslatorAi.Tests/KoreanTranslationFixerCharacterizationTests.cs
git commit -m "refactor: add korean fixer step pipeline skeleton with parity"
```

---

### Task 7 (PR-07): Extract Korean Fix Step Set A (Particles)

**Files:**
- Create: `src/XTranslatorAi.Core/Text/KoreanFix/Internal/KoreanParticleSelector.cs`
- Create: `src/XTranslatorAi.Core/Text/KoreanFix/Internal/Steps/ParenthesizedParticleStep.cs`
- Create: `src/XTranslatorAi.Core/Text/KoreanFix/Internal/Steps/AttachedSeparatedParticleStep.cs`
- Modify: `src/XTranslatorAi.Core/Text/KoreanTranslationFixer.cs`
- Test: `tests/XTranslatorAi.Tests/KoreanTranslationFixerTests.cs`
- Test: `tests/XTranslatorAi.Tests/KoreanTranslationFixerCharacterizationTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void Fix_ParticleStepExtraction_KeepsAllParticleCasesPassing()
{
    // existing particle tests should still pass unchanged
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~KoreanTranslationFixer"`
Expected: FAIL before routing moved logic fully.

**Step 3: Write minimal implementation**

```csharp
internal sealed class ParenthesizedParticleStep : IKoreanFixStep
{
    public string Apply(in KoreanFixContext context, string text) => /* moved regex replacements */;
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~KoreanTranslationFixer"`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/XTranslatorAi.Core/Text/KoreanFix/Internal/KoreanParticleSelector.cs src/XTranslatorAi.Core/Text/KoreanFix/Internal/Steps/ParenthesizedParticleStep.cs src/XTranslatorAi.Core/Text/KoreanFix/Internal/Steps/AttachedSeparatedParticleStep.cs src/XTranslatorAi.Core/Text/KoreanTranslationFixer.cs tests/XTranslatorAi.Tests/KoreanTranslationFixerTests.cs tests/XTranslatorAi.Tests/KoreanTranslationFixerCharacterizationTests.cs
git commit -m "refactor: extract korean particle correction steps"
```

---

### Task 8 (PR-08): Extract Korean Fix Step Set B (Duration/Artifact) + Regex Hardening

**Files:**
- Create: `src/XTranslatorAi.Core/Text/KoreanFix/Internal/Steps/DurationProbabilityStep.cs`
- Create: `src/XTranslatorAi.Core/Text/KoreanFix/Internal/Steps/ArtifactCleanupStep.cs`
- Modify: `src/XTranslatorAi.Core/Text/KoreanTranslationFixer.cs`
- Modify: `tests/XTranslatorAi.Tests/KoreanTranslationFixerTests.cs`
- Test: `tests/XTranslatorAi.Tests/KoreanTranslationFixerCharacterizationTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void Fix_DurationAndArtifactExtraction_KeepsComplexStringsStable()
{
    // include long strings with <mag>/<dur>/percent ordering
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~KoreanTranslationFixer"`
Expected: FAIL before step order is matched.

**Step 3: Write minimal implementation**

```csharp
internal sealed class DurationProbabilityStep : IKoreanFixStep
{
    public string Apply(in KoreanFixContext context, string text) => /* moved duration + probability rewrites */;
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~KoreanTranslationFixer"`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/XTranslatorAi.Core/Text/KoreanFix/Internal/Steps/DurationProbabilityStep.cs src/XTranslatorAi.Core/Text/KoreanFix/Internal/Steps/ArtifactCleanupStep.cs src/XTranslatorAi.Core/Text/KoreanTranslationFixer.cs tests/XTranslatorAi.Tests/KoreanTranslationFixerTests.cs tests/XTranslatorAi.Tests/KoreanTranslationFixerCharacterizationTests.cs
git commit -m "refactor: extract korean duration/artifact cleanup steps"
```

---

### Task 9 (PR-09): Final Consolidation + Hotspot Check

**Files:**
- Modify: `src/XTranslatorAi.Core/Text/LqaScanner.cs`
- Modify: `src/XTranslatorAi.Core/Text/KoreanTranslationFixer.cs`
- Modify: `docs/plans/README.md`
- Test: `tests/XTranslatorAi.Tests/LqaScannerTests.cs`
- Test: `tests/XTranslatorAi.Tests/KoreanTranslationFixerTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public async Task Refactor_FinalParity_FullRelevantSuitePasses()
{
    // orchestration test calling both scanner/fixer paths
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~LqaScanner|FullyQualifiedName~KoreanTranslationFixer"`
Expected: FAIL before final wiring/cleanup.

**Step 3: Write minimal implementation**

```csharp
// remove legacy fallback rule/step and use only extracted pipeline entries in fixed order
```

**Step 4: Run test to verify it passes**

Run: `dotnet build tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release && dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --no-build`
Expected: PASS (all tests).

Run: `python3 scripts/vibe.py doctor --full`
Expected: hotspot/complexity report regenerated with reduced scanner/fixer pressure.

**Step 5: Commit**

```bash
git add src/XTranslatorAi.Core/Text/LqaScanner.cs src/XTranslatorAi.Core/Text/KoreanTranslationFixer.cs docs/plans/README.md tests/XTranslatorAi.Tests/LqaScannerTests.cs tests/XTranslatorAi.Tests/KoreanTranslationFixerTests.cs
git commit -m "refactor: finalize lqa/fixer pipeline extraction and parity verification"
```
