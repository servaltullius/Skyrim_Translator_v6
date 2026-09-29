# Starfield Franchise TM Seeding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a safe, repeatable workflow that turns locally held Starfield English/Korean `.strings` assets into `Starfield`-scoped TM TSV data and imports it into the app without contaminating TES/Fallout TM data.

**Architecture:** Keep the app runtime unchanged: the WPF app continues to consume TM as TSV and store it in the franchise-selected SQLite DB. The new work happens outside the runtime path as a local seeding pipeline: extract or point to official English strings and a locally held Korean patch, generate deterministic `Source<TAB>Target` TSV using the existing Bethesda strings seeder, then import that TSV through the existing `Franchise TM` flow.

**Tech Stack:** Python 3 scripts, .NET 8/WPF app, existing SQLite `ProjectDb`, xUnit for .NET-side verification, existing `TranslationMemoryFileService` TSV format.

---

## Requirements Summary

- Starfield TM must stay isolated from Elder Scrolls and Fallout TM.
- Third-party translation assets must not be bundled into the repo, release assets, or embedded resources.
- The workflow must be local-only and operator-driven: the user supplies official English strings and the community Korean patch they legally hold.
- The generated TSV must be deterministic and reusable with the existing `Import franchise TM` UI or auto-import folder.
- The first pass should favor precision over recall: skip noisy or ambiguous long-form entries by default.
- The operator must be able to inspect what was generated before import.

## Non-Goals

- No direct `.7z` ingestion inside the WPF app in this change.
- No auto-download of Team Waldo or any other third-party patch.
- No cross-franchise shared TM or automatic promotion into TES/Fallout.
- No built-in redistribution of Starfield Korean translations.
- No fuzzy matching or TM ranking redesign.

## Non-Functional Constraints

- **Licensing/privacy:** treat third-party Korean patch data as user-supplied local input only.
- **Repeatability:** the same source/target input directories must produce the same TSV ordering.
- **Auditability:** the operator must be able to see how many files and pairs were matched before import.
- **Failure containment:** bad or sparse source data must fail without touching the app DB.
- **Compatibility:** the existing `Franchise TM` import UI and `tm-import` folder flow must remain valid.

## Current System Notes

- Franchise DB selection already exists in `src/XTranslatorAi.App/Services/GlobalProjectDbService.cs`.
- Starfield already has its own TM DB path under `LocalAppData/XTranslatorAi/Global/starfield/global-glossary.sqlite` via `src/XTranslatorAi.App/Services/ProjectPaths.cs`.
- The app already supports `TSV(Source<TAB>Target)` import through `src/XTranslatorAi.App/Services/FranchiseTranslationMemoryService.cs` and `src/XTranslatorAi.App/ViewModels/MainViewModel.GlobalTranslationMemory.cs`.
- A generic Bethesda strings seeder already exists: `scripts/seed_tm_from_bethesda_strings_dirs.py`.
- Starfield prompt asset exists, but `src/XTranslatorAi.App/Assets/기본용어집_스타필드.md` is effectively empty, so Starfield consistency currently depends more on TM than built-in glossary.

## Architecture Decision

**Recommendation:** Implement Starfield TM as a local seeding workflow around the existing generic directory-based seeder, then import the generated TSV into the existing `Franchise TM` surface.

**Why this is the simplest viable architecture**
- The app already has the right runtime boundary: franchise-selected TM storage and TSV import.
- The repo already has a generic `.strings/.dlstrings/.ilstrings` matching script that works on directory pairs, which fits Starfield without inventing a new parser.
- Local-only TSV generation avoids shipping third-party data and avoids adding archive/format complexity to the app itself.

**Rejected alternatives**
- Add direct `.7z` import inside the app: rejected because it increases UI/runtime complexity and pulls third-party corpus handling into the shipped product.
- Import Team Waldo data as built-in assets: rejected because it mixes third-party content into the app and makes updates/licensing unsafe.
- Promote Starfield TM entries into a cross-franchise global TM: rejected because identical English strings across Bethesda games can diverge in tone and terminology.
- Seed directly from the app DB without an intermediate TSV: rejected because it removes the operator review point and makes rollback harder.

**Key risks**
- Generated TSV may include UI-noise or context-sensitive lines that should not become reusable TM.
- Operators may accidentally seed TES/Fallout DBs if the selected franchise is wrong during import.
- Community patch filenames may not perfectly match official English filenames for every DLC or optional component.
- Long `DLSTRINGS/ILSTRINGS` entries can introduce context-dependent translations that are poor TM candidates.

**Mitigations**
- Default seeding excludes long/multiline entries unless explicitly opted in.
- Add generation report output with matched-file and pair counts.
- Document that import must be done with `SelectedFranchise = Starfield`.
- Keep the generated TSV external and reviewable before import.

## File Map

**Operator documentation**
- Create: `docs/starfield-franchise-tm.md`
  Responsibility: local-only workflow, prerequisites, commands, review checklist, import steps.
- Modify: `README.md`
  Responsibility: link to the Starfield TM workflow and clarify that third-party corpora are not shipped.

**Seeder pipeline**
- Modify: `scripts/seed_tm_from_bethesda_strings_dirs.py`
  Responsibility: keep the generic seeding logic, add report output and more explicit CLI docs for Starfield use.
- Create: `artifacts/.gitkeep`
  Responsibility: keep generated artifacts out of source control while preserving the folder convention.

**Verification**
- Create: `tests/XTranslatorAi.Tests/SeedTmWorkflowDocsTests.cs`
  Responsibility: lightweight characterization that the documented Starfield import path and TSV format examples stay in sync with `ProjectPaths` and the current import contract.

---

### Task 1: Freeze the Starfield TM data policy in docs

**Files:**
- Create: `docs/starfield-franchise-tm.md`
- Modify: `README.md`

- [ ] **Step 1: Write the Starfield TM workflow document**

```md
# Starfield Franchise TM Workflow

## 목적
- 로컬에 보유한 Starfield 영문 `.strings`와 한국어 패치 `.strings`를 비교해 `Starfield` 전용 TM TSV를 생성한다.
- 생성된 TSV는 앱의 `Franchise TM` 가져오기로만 적재한다.

## 데이터 정책
- 제3자 번역 데이터는 저장소/릴리즈/임베디드 리소스에 포함하지 않는다.
- Starfield TM은 TES/Fallout TM으로 승격하지 않는다.
- 생성 결과물은 검토 가능한 TSV로 남긴다.

## 준비물
- Starfield 영문 `Data/Strings` 디렉터리
- 로컬에 보유한 한국어 패치 `Data/Strings` 디렉터리
- Python 3

## 생성 명령
```bash
python3 scripts/seed_tm_from_bethesda_strings_dirs.py \
  --source-root "/path/to/Starfield-English-Strings" \
  --target-root "/path/to/Starfield-KoreanPatch-Strings" \
  --source-locale en \
  --out artifacts/tm/starfield-franchise-tm.tsv
```

## 검토 체크리스트
- TSV 첫 줄이 `Source<TAB>Target` 헤더인지 확인한다.
- 고유명사, UI, 시스템 메시지가 의도대로 들어갔는지 샘플 확인한다.
- 장문/문맥 의존 항목이 과도하면 `--include-long` 없이 다시 생성한다.

## 앱 import
- 앱에서 `SelectedFranchise = Starfield` 상태를 확인한다.
- `Franchise TM 가져오기`로 `artifacts/tm/starfield-franchise-tm.tsv`를 선택한다.
```

- [ ] **Step 2: Link the workflow from the README**

```md
## Starfield TM

Starfield 번역 메모리는 별도 `Franchise TM`으로 운영합니다. 제3자 한글 패치는 앱에 내장하지 않으며, 로컬에 보유한 문자열 자산으로 TSV를 생성한 뒤 가져오는 방식만 지원합니다.

자세한 절차는 `docs/starfield-franchise-tm.md`를 참고하세요.
```

- [ ] **Step 3: Run a docs terminology check**

Run: `rg -n "Starfield TM|Franchise TM|제3자|로컬" README.md docs/starfield-franchise-tm.md`
Expected: workflow doc and README both describe local-only Starfield franchise TM usage.

- [ ] **Step 4: Commit**

```bash
git add README.md docs/starfield-franchise-tm.md
git commit -m "docs: define local-only starfield franchise TM workflow"
```

### Task 2: Harden the generic Bethesda strings seeder for operator auditability

**Files:**
- Modify: `scripts/seed_tm_from_bethesda_strings_dirs.py`

- [ ] **Step 1: Add a failing smoke check command using a temporary fixture**

Run:

```bash
TMP_DIR="$(mktemp -d)"
mkdir -p "$TMP_DIR/en/Data/Strings" "$TMP_DIR/ko/Data/Strings"
python3 - <<'PY' "$TMP_DIR/en/Data/Strings/starfield_en.strings" "$TMP_DIR/ko/Data/Strings/starfield_en.strings"
import struct, sys

def write_strings(path, entries):
    directory = []
    payload = bytearray()
    for sid, text in entries:
        off = len(payload)
        raw = text.encode('utf-8') + b'\x00'
        payload += raw
        directory.append((sid, off))
    with open(path, 'wb') as f:
        f.write(struct.pack('<II', len(entries), len(payload)))
        for sid, off in directory:
            f.write(struct.pack('<II', sid, off))
        f.write(payload)

write_strings(sys.argv[1], [(1, 'Ship')])
write_strings(sys.argv[2], [(1, '함선')])
PY
python3 scripts/seed_tm_from_bethesda_strings_dirs.py \
  --source-root "$TMP_DIR/en" \
  --target-root "$TMP_DIR/ko" \
  --source-locale en \
  --out "$TMP_DIR/out.tsv"
cat "$TMP_DIR/out.tsv"
```

Expected: output contains `Source<TAB>Target` and one `Ship<TAB>함선` row.

- [ ] **Step 2: Extend the script with a machine-readable report output**

```python
ap.add_argument("--report-json", help="Optional JSON path for generation summary.")
...
report = {
    "matched_files": matched_files,
    "missing_target_files": missing_target_files,
    "pairs": len(pairs),
    "source_root": str(source_root),
    "target_root": str(target_root),
    "source_locale": source_locale,
    "include_long": bool(args.include_long),
}
if args.report_json:
    report_path = Path(args.report_json).expanduser()
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
```

- [ ] **Step 3: Re-run the smoke command with a report file**

Run:

```bash
TMP_DIR="$(mktemp -d)"
mkdir -p "$TMP_DIR/en/Data/Strings" "$TMP_DIR/ko/Data/Strings"
python3 - <<'PY' "$TMP_DIR/en/Data/Strings/starfield_en.strings" "$TMP_DIR/ko/Data/Strings/starfield_en.strings"
import struct, sys

def write_strings(path, entries):
    directory = []
    payload = bytearray()
    for sid, text in entries:
        off = len(payload)
        raw = text.encode('utf-8') + b'\x00'
        payload += raw
        directory.append((sid, off))
    with open(path, 'wb') as f:
        f.write(struct.pack('<II', len(entries), len(payload)))
        for sid, off in directory:
            f.write(struct.pack('<II', sid, off))
        f.write(payload)

write_strings(sys.argv[1], [(1, 'Ship')])
write_strings(sys.argv[2], [(1, '함선')])
PY
python3 scripts/seed_tm_from_bethesda_strings_dirs.py \
  --source-root "$TMP_DIR/en" \
  --target-root "$TMP_DIR/ko" \
  --source-locale en \
  --out "$TMP_DIR/out.tsv" \
  --report-json "$TMP_DIR/report.json"
python3 - <<'PY' "$TMP_DIR/report.json"
import json, sys
report = json.load(open(sys.argv[1], encoding='utf-8'))
assert report['matched_files'] == 1, report
assert report['pairs'] == 1, report
print(report)
PY
```

Expected: report JSON exists, `matched_files == 1`, `pairs == 1`.

- [ ] **Step 4: Commit**

```bash
git add scripts/seed_tm_from_bethesda_strings_dirs.py
git commit -m "feat: add audit report output to bethesda TM seeder"
```

### Task 3: Keep the app import contract explicit and testable

**Files:**
- Create: `tests/XTranslatorAi.Tests/SeedTmWorkflowDocsTests.cs`
- Modify: `README.md`
- Modify: `docs/starfield-franchise-tm.md`

- [ ] **Step 1: Write a failing characterization test for the import contract**

```csharp
using System;
using System.IO;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;
using Xunit;

namespace XTranslatorAi.Tests;

public class SeedTmWorkflowDocsTests
{
    [Fact]
    public void StarfieldFranchiseTmDocs_ReferenceFranchiseImportAndTsvHeader()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var docPath = Path.Combine(repoRoot, "docs", "starfield-franchise-tm.md");
        var text = File.ReadAllText(docPath);

        Assert.Contains("Franchise TM 가져오기", text, StringComparison.Ordinal);
        Assert.Contains("Source\tTarget", text, StringComparison.Ordinal);
        Assert.Contains("SelectedFranchise = Starfield", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectPaths_StarfieldImportDir_RemainsStarfieldScoped()
    {
        var path = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Starfield)
            .Replace('\\', '/');

        Assert.Contains("/Global/starfield/", path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("/tm-import", path, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run the new focused test and verify the docs contract fails or passes intentionally**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter SeedTmWorkflowDocsTests`
Expected: fail until the document and import path wording align, then pass.

- [ ] **Step 3: Update docs/examples until the tests pass**

```md
- `SelectedFranchise = Starfield` 상태에서만 가져오기 실행
- TSV header example: `Source<TAB>Target`
- Starfield import path is franchise-scoped and not shared with TES/Fallout
```

- [ ] **Step 4: Re-run the focused tests**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter SeedTmWorkflowDocsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add README.md docs/starfield-franchise-tm.md tests/XTranslatorAi.Tests/SeedTmWorkflowDocsTests.cs
git commit -m "test: lock starfield franchise TM workflow contract"
```

### Task 4: Perform one documented dry run using local Starfield assets

**Files:**
- Modify: `docs/starfield-franchise-tm.md`
- Create: `artifacts/.gitkeep`

- [ ] **Step 1: Create the artifacts convention directory**

```bash
mkdir -p artifacts/tm
touch artifacts/.gitkeep
```

- [ ] **Step 2: Add a dry-run section to the workflow doc with a real command template**

```md
## Dry run example

```bash
python3 scripts/seed_tm_from_bethesda_strings_dirs.py \
  --source-root "/mnt/g/Starfield/Data/Strings" \
  --target-root "/mnt/g/Starfield_KoreanPatch/Data/Strings" \
  --source-locale en \
  --out artifacts/tm/starfield-franchise-tm.tsv \
  --report-json artifacts/tm/starfield-franchise-tm.report.json
```

생성 후 다음을 확인한다.
- `artifacts/tm/starfield-franchise-tm.tsv`
- `artifacts/tm/starfield-franchise-tm.report.json`
```

- [ ] **Step 3: Run the dry run with local operator-provided paths**

Run:

```bash
python3 scripts/seed_tm_from_bethesda_strings_dirs.py \
  --source-root "/actual/path/to/Starfield-English-Strings" \
  --target-root "/actual/path/to/Starfield-KoreanPatch-Strings" \
  --source-locale en \
  --out artifacts/tm/starfield-franchise-tm.tsv \
  --report-json artifacts/tm/starfield-franchise-tm.report.json
```

Expected: TSV and report JSON are generated locally. No repo assets or embedded resources change.

- [ ] **Step 4: Import the generated TSV into the app with the Starfield franchise selected**

Run:
- Launch the app.
- Set franchise to `Starfield`.
- Use `Franchise TM 가져오기` and select `artifacts/tm/starfield-franchise-tm.tsv`.
- Open the `Franchise TM` tab and confirm rows appear.

Expected: imported rows land in the Starfield franchise DB only.

- [ ] **Step 5: Run the full repo verification after code/doc changes**

Run: `dotnet build src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release && dotnet build tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release && dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --no-build`
Expected: all builds/tests pass after doc/script/test additions.

- [ ] **Step 6: Commit**

```bash
git add artifacts/.gitkeep docs/starfield-franchise-tm.md
git commit -m "docs: add starfield franchise TM dry-run workflow"
```

## Self-Review

- **Spec coverage:** covers local-only Starfield TM policy, deterministic TSV generation, audit output, import contract, and a real dry-run path.
- **Placeholder scan:** no `TODO`, `TBD`, or deferred placeholders remain in the task steps.
- **Type consistency:** uses the current franchise terminology (`Franchise TM`, `SelectedFranchise = Starfield`, `GetGlobalTranslationMemoryImportDir`) and preserves the current app import contract.

## Recommended Execution Order

1. Task 1 first, because it freezes the allowed data boundary.
2. Task 2 second, because report output makes seeding reviewable before import.
3. Task 3 third, because it locks docs and import path expectations with tests.
4. Task 4 last, because the real dry run only makes sense after docs and seeder output are stable.
