# Qwen‑MT API Logging + Cost Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Qwen‑MT를 주력 엔진으로 사용할 때, API Logs에 Qwen 호출도 기록하고(429/키 전환 디버깅), 응답 `usage`(토큰) 기반으로 요청별 비용(USD)을 계산해서 보여준다. 기존 “비용 추정” 도구는 Gemini 전용이므로 Qwen 선택 시 안내 메시지로 분기한다.

**Architecture:** Core(`QwenMtClient`)에 선택적 로거(`IQwenMtCallLogger`)를 추가하고, App에서는 Gemini/Qwen 로그를 공통 `ApiCallLogRow`(ViewModel)로 매핑해 DataGrid에서 동일한 컬럼으로 표시한다. Qwen 비용은 `usage.prompt_tokens`/`usage.completion_tokens`를 모델별 단가(이미 UI에 표시 중인 Qwen‑MT Intl pricing)로 환산한다.

**Tech Stack:** .NET 8, WPF, System.Text.Json, HttpClient, CommunityToolkit.Mvvm

---

### Task 1: Core — Qwen‑MT 호출 로깅/usage 파싱

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/QwenMtClient.cs`
- Modify: `src/XTranslatorAi.Core/Translation/QwenMtClient.Types.cs`
- Test: `tests/XTranslatorAi.Tests/QwenMtClientTests.cs`

**Steps:**
1. `QwenMtClient`에 `IQwenMtCallLogger? logger` 파라미터/필드 추가.
2. 응답 JSON에서 `usage.prompt_tokens`, `usage.completion_tokens`, `usage.total_tokens`(있으면) 파싱.
3. 성공/실패 모두에 대해 `QwenMtCallLogEntry` 기록(StartedAt/Duration/Op/Model/HTTP/Success/Error/Usage).
4. 테스트: usage 파싱/로깅 콜백이 호출되는지 검증.

---

### Task 2: App — API Logs UI를 Gemini/Qwen 공통으로 통합

**Files:**
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.ApiKeysAndLogging.cs`
- Modify: `src/XTranslatorAi.App/Views/ApiLogsTabView.xaml`
- Create: `src/XTranslatorAi.App/ViewModels/ApiCallLogRow.cs`

**Steps:**
1. `ApiCallLogs`를 `ObservableRangeCollection<ApiCallLogRow>`로 변경.
2. Gemini 로거는 기존 `GeminiCallLogEntry` → `ApiCallLogRow`로 매핑.
3. Qwen 로거(`UiQwenMtCallLogger`) 추가: `QwenMtCallLogEntry` → `ApiCallLogRow`로 매핑.
4. DataGrid에 `Provider`, `InTok`, `OutTok`, `Cost($)` 컬럼 추가(없으면 공란).

---

### Task 3: App — Qwen 요청별 비용 계산

**Files:**
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.ApiKeysAndLogging.cs`

**Steps:**
1. `TryGetQwenMtPricing(model, out pricing)`를 재사용해 `(promptTok, completionTok)` → USD 환산.
2. API Logs에 요청별 비용(USD) 표시.

---

### Task 4: App — “비용 추정” 버튼 동작을 엔진별로 분기

**Files:**
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Tools.cs`

**Steps:**
1. Gemini 선택 시: 기존 `TranslationCostEstimator` 유지.
2. Qwen 선택 시: “Qwen‑MT는 API Logs의 usage 기반 실비 확인” 안내 메시지 표시(추정값 제공 안 함).

---

### Task 5: Verification + single-file publish

**Commands:**
- Test: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Publish: `dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableWindowsTargeting=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish-win-x64-single`

