# Light Theme Revert Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Revert the app to a single light theme (light gray + dark text) while keeping semantic Start/Stop button colors.

**Architecture:** Keep styling centralized in `App.xaml` resources/styles. Avoid introducing a theme toggle; prefer a single coherent palette. Minimize changes outside shared styles.

**Tech Stack:** .NET 8, WPF (XAML ResourceDictionary styles), self-contained single-file publish for `win-x64`.

### Task 1: Revert palette to light theme

**Files:**
- Modify: `src/XTranslatorAi.App/App.xaml`

**Step 1: Update theme color resources**
- Set `XT.WindowBackgroundColor`, `XT.SurfaceColor`, `XT.BorderColor`, `XT.TextColor`, `XT.MutedTextColor`, `XT.HeaderColor` to light-theme values.
- Restore `XT.AlternateRowBrush` and row-status brushes to light-friendly pastels.

**Step 2: Align hover/pressed colors**
- Ensure `XT.ToolbarButton` and `XT.TabItem` hover/pressed backgrounds read well on light theme.

### Task 2: Make default WPF controls consistent under light theme

**Files:**
- Modify: `src/XTranslatorAi.App/App.xaml`
- Modify: `src/XTranslatorAi.App/Views/StringsTabView.xaml` (only if needed)

**Step 1: Update SystemColors overrides**
- Either remove SystemColors overrides or set them to match the light palette so default templates don’t clash.

**Step 2: Keep semantic buttons**
- Preserve `XT.SuccessButton` and `XT.DangerButton` styles for Start/Stop.

### Task 3: Progress bar styling

**Files:**
- Modify: `src/XTranslatorAi.App/App.xaml`
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`

**Step 1: Decide on progress style**
- Either rely on default ProgressBar (remove `XT.ProgressBar` usage) or adapt `XT.ProgressBar` to light palette.

### Task 4: Verify and publish

**Files:**
- None (commands)

**Step 1: Build**
- Run: `dotnet build XTranslatorAi.sln -c Release`
- Expected: `오류 0개`

**Step 2: Test**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Expected: `통과` and `전체: 100`

**Step 3: Publish single-file**
- Run:
  - `dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 -o artifacts/publish-win-x64-singlefile -p:PublishSingleFile=true -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false`
- Copy output exe to: `C:\Users\kdw73\Downloads\TulliusTranslator.exe`

