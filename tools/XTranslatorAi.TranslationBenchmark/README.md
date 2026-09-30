# Fresh Gemini 3.8 Flash plugin comparison

This console tool uses the production `TranslationService`, `GeminiClient`, plugin
importer and writer. It accepts only an original plugin and an isolated experiment
folder. Existing Korean translations are separate inputs to `compare.py`; they
are never inputs to the translation runner.

```powershell
dotnet build tools/XTranslatorAi.TranslationBenchmark -c Release
dotnet run --project tools/XTranslatorAi.TranslationBenchmark -c Release -- prepare original.esp NEW-EXPERIMENT-FOLDER
dotnet run --project tools/XTranslatorAi.TranslationBenchmark -c Release -- estimate original.esp NEW-EXPERIMENT-FOLDER
dotnet run --project tools/XTranslatorAi.TranslationBenchmark -c Release -- translate original.esp NEW-EXPERIMENT-FOLDER
```

`prepare` is offline: it imports source strings as Pending into a new database,
checks empty glossary/TM, and records the source hash and system prompt hash.
`translate` reads `GEMINI_API_KEY` in memory, verifies access to the exact
`gemini-3.8-flash` model and uses Low thinking. No model substitutions, quality
escalation, Batch API, Flex, global glossary or global TM are enabled.

The translation settings match the app defaults: 12 rows, 15,000 source characters,
two parallel requests, the model output ceiling, REC hints, placeholder protection,
soft semantic repair, dialogue context and newly learned session terms. Project
context generation and explicit prompt caching are disabled for this experiment.
This difference from the default app configuration is recorded in the console
output. Only the standard API is used. The bundled Skyrim base system prompt and
the app's final priority guard are used without a custom prompt.

`estimate` checks the exact model and counts input tokens with the production cost
estimator. It generates no sample translation and exports no ESP. The output-token
range is a heuristic, not a spending cap; thinking, session context, repairs and
retries can add cost. Use the standard estimate without prompt caching. This check
does not determine the account's Free Tier or billing eligibility.

Interrupted runs save completed rows in `translation.stproj.sqlite` and `rows.json`.
Resuming requires the same source hash, model and prompt hash. Results generated
within this experiment may supply its own session terms/TM; no older translation
database is read. Incomplete runs do not export an ESP. `export` exports an already
completed database without API access, into a new subfolder.

`usage.jsonl` retains token counts and request purposes across all invocations,
including unsuccessful generations when usage is reported. Cost estimates use
the project's dated pricing table, not an account billing receipt. Unknown usage
must be reported separately. `requests.jsonl` retains model input bodies for
checking provenance. HTTP headers, key masks and credential-bearing URLs are not
written. The HTTP handler moves the key from the client URL into the Google API
header before transmission.

```powershell
python tools/XTranslatorAi.TranslationBenchmark/compare.py experiment-inputs.json REPORT-FOLDER
```

The comparison configuration is an array:

```json
[
  {
    "name": "EldenSkyrim.esp",
    "old_source": "old-original-manifest.json",
    "baseline": "installed-korean-manifest.json",
    "latest_source": "latest-original-manifest.json",
    "translated_rows": "NEW-EXPERIMENT-FOLDER/rows.json"
  }
]
```

Manifests have the `Info` and `Fields` schema produced by `XTranslatorAi.Validate
--plugin ... --report ...`. Without `latest_source` or `translated_rows`, the tool
produces a preparation report that explicitly says the latest download or new
translation is pending.

Field identity uses the actual master/plugin owner, local FormID, record type and
repeated field occurrence. It does not compare the load-order byte or source text
alone. Changed EDIDs and changed source strings are separated from the unchanged
English comparison subset. Ambiguous identities are rejected. Counts of different
Korean strings are descriptive, not quality scores; flags such as English text
remaining require review because internal names and product names can be valid.

The HTML report is standalone and searchable. Source and translation content is
inserted as text, with embedded JSON escaped to prevent executable markup.

Validation before credentials/download access: Release build passed with zero
warnings/errors; offline imports of the original Elden Rim Base 3.7.2 (985 fields)
and Weapon Art 3.2.2 (2,852 fields) retained all fields as Pending with zero initial
glossary/TM. This does **not** verify a real API translation or game behavior.
Six comparison tests passed for moving master slots, different record owners,
changed English, changed EDIDs, unchanged-source comparisons and rejecting
translations from a different source. The generated 3,837-row preparation HTML
was opened in the browser: EDID search returned the corresponding two fields,
and the quality comparison filter correctly returned zero unrun rows.
