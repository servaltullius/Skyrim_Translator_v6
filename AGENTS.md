# Agent Notes (Skyrim_Translator_v6)

## Quick start
- Read: `.vibe/context/LATEST_CONTEXT.md`
- Run (WSL/Linux): `python3 scripts/vibe.py doctor --full`
- Run (Windows): `scripts\\vibe.cmd doctor --full`

## Helpful vibe-kit commands
- Search: `python3 scripts/vibe.py search <query>`
- Impact: `python3 scripts/vibe.py impact <path>`
- Placeholder QA (xTranslator XML): `python3 scripts/vibe.py qa <file.xml>`
- Context pack (for agents): `python3 scripts/vibe.py pack --scope staged` (no git: `--scope recent`)
- AGENTS size lint: `python3 scripts/vibe.py agents lint`
- Watcher (optional): `python3 scripts/vibe.py watch`

## MCP + Skills (Codex)
- Prefer MCP tools when relevant (e.g., `context7` for up-to-date docs, `search` for web research, `filesystem` for repo file IO).
- If skills/superpowers are available, follow the skill workflow (bootstrap + use-skill) instead of improvising.

## Tests
- Core tests: `dotnet build tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release && dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --no-build`

## Publish
- Win-x64 single-file: `dotnet clean src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 && dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 -o artifacts/publish-win-x64-singlefile-min -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:EnableWindowsTargeting=true -p:DeleteExistingFiles=true`
