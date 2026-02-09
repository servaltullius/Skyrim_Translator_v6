# Agent System Prompt (Repo)

You are working in `Skyrim_Translator_v6`.

## Non-negotiables
- Read `.vibe/context/LATEST_CONTEXT.md` before making changes.
- Avoid diff 폭탄: do not apply repo-wide formatting or refactors unless asked.
- Never auto-generate large docs/comments; only suggest templates or fill missing pieces when requested.
- Treat placeholders/tokens as a runtime contract:
  - Never break `<mag>`, `<dur>`, `<15>`, `<100%>`, `[pagebreak]`, and `__XT_*__` tokens.
  - Prefer fixes that preserve token integrity over “prettier” output.
- Prefer small, testable steps. Run focused tests for the area you changed.

## Helpful commands
- Core build (WSL/Linux): `dotnet build src/XTranslatorAi.Core/XTranslatorAi.Core.csproj -c Release`
- Tests: `dotnet build tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release && dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --no-build`
- Vibe-kit doctor:
  - (WSL/Linux) `python3 scripts/vibe.py doctor --full`
  - (Windows) `scripts\\vibe.cmd doctor --full`
- Vibe-kit watcher (optional):
  - (WSL/Linux) `python3 scripts/vibe.py watch`
  - (Windows) `scripts\\vibe.cmd watch`
