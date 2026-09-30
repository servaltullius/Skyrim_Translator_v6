# Independent Mutagen string inventory

This console references only `Mutagen.Bethesda.Skyrim` **0.53.1**. It does not
reference the application, Core, or the production field registry. It imports one
Skyrim SE plugin as a read-only overlay, enumerates all major records, and visits
their canonical getter interfaces. It records every encountered
`ITranslatedStringGetter`, including empty values, separately from ordinary
`string` properties. It never resolves links, loads an active game environment,
uses a writer, or changes a plugin or string table.

```powershell
dotnet build tools/XTranslatorAi.PluginInventory -c Release
dotnet --roll-forward Major tools/XTranslatorAi.PluginInventory/bin/Release/net8.0/XTranslatorAi.PluginInventory.dll `
  --plugin "G:\툴리우스번역기\Skyrim_Latest_Kor\03-USSEP\unofficial skyrim special edition patch.esp" `
  --strings-folder "G:\툴리우스번역기\artifacts\direct-plugin-20260929\xedit-data\Strings" `
  --source-encoding utf-8 --tables-encoding utf-8 --metadata-encoding windows-1252 `
  --language English `
  --manifest "G:\툴리우스번역기\artifacts\direct-plugin-20260929\ussep-preview2-inspect.json" `
  --output "G:\툴리우스번역기\artifacts\direct-plugin-20260929\ussep-mutagen-inventory.json"
```

Build/restore and execution are separate owner-controlled steps. Output must be a
new JSON file; the console refuses overwrites. Use the same command with the
explicit original `Skyrim.esm` and its matching manifest for the base game.
The example allows the net8.0 validation tool to run with the workspace's .NET 10
runtime. This does not add Mutagen to the application or its published package.

Start a diagnostic run by adding `--max-records 100` and choosing a distinct new
output name. Reaching this limit intentionally exits nonzero with
`StopCode: record_limit`, `TraversalCompleted: false`, and a partial inventory;
the full-manifest comparison is skipped. Then inspect diagnostics and progress
before removing the record limit for a full run.

The console prints record count, visited nodes, retained strings, diagnostics,
memory, and the current getter path to **stderr** about every two seconds. Hard
traversal limits throw a dedicated exception that escapes every per-property and
per-record handler, preserving a failed partial JSON report. Defaults are:

| Option | Default |
| --- | ---: |
| `--max-visited` | 5,000,000 nodes |
| `--max-diagnostics` | 1,000 entries (4,096 characters per detail) |
| `--max-strings` | 500,000 values |
| `--max-text-chars` | 64,000,000 retained text characters |
| `--max-memory-mb` | 1,024 MiB observed working set or managed heap |
| `--max-depth` | 48 |

Managed heap size is checked before each record and every 128 traversal
checkpoints. Working set is sampled at those checkpoints no more than once per
100 ms; explicit import/comparison phase checks sample immediately. Between
samples, the latest working-set observation is reused. A brief native-memory
spike or a single allocation inside a third-party getter may cross the observed
limit before another checkpoint, or may disappear between samples; the reported
peak is an observed peak, not a guaranteed process peak. This is not an OS-enforced
memory quota. Visit, depth, diagnostic, string-count, and text-size limits remain
independent of sampling. A stopped traversal skips the full-manifest comparison.

The sampling change avoids repeatedly invalidating and rebuilding native process
information. In [.NET's Process implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.cs),
`Refresh()` clears cached process information and `WorkingSet64` retrieves it
again. The Windows path ultimately calls
[`NtQuerySystemInformation`](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/ProcessManager.Win32.cs)
before filtering the process snapshot. In the local USSEP runs, completion of the
comparison fell from 95.5 seconds (v2) to 4.9 seconds (v3), with all 152,741
inventory rows and comparison results identical. This measures the validation
tool, not application translation speed; evidence is
`ussep-inventory-sampling-regression.json`.

Numeric-only value types are pruned before invoking their getters, based on their
declared instance storage field types, as are collections of such values. The
exact `System.Drawing.Color` RGBA type is also a scalar. All pruned types and
counts are reported. This avoids calculated value cycles such as
`Noggog.P3Float.Normalized` / `.Absolute` and `Percent.Inverse`; it does not prune
string-bearing structs, translated strings, or abstract/interface-typed record
containers. Terminal strings are captured before object-cycle checks.

`string` and `ITranslatedStringGetter` are explicitly excluded from numeric
sequence detection (`string` implements `IEnumerable<char>`). Noggog's exact
`IReadOnlyArray2d<T>` contract is pruned only for proven numeric `T`; this covers
byte height maps without visiting millions of coordinate/value pairs. See the
pinned [IArray2d contract](https://github.com/Noggog/CSharpExt/blob/de16c021f62802746a4ebb7a51a2cde676fb37d7/Noggog.CSharpExt/Containers/Interfaces/IArray2d.cs).

Finite non-flags enum indexers are recorded in `EnumIndexerSchemas`. The pinned
`IWeatherImageSpacesGetter` and `IWeatherVolumetricLightingGetter` expose four
`TimeOfDay` entries; their named properties are known aliases and are skipped so
each slot is traversed once. Unknown potential named/indexer aliases remain an
error rather than silently double-counting strings.

The exact canonical `IWeatherAmbientColorSetGetter` is a **declared numeric schema
exclusion**. Its four `IAmbientColorsGetter` members contain seven RGBA colors,
float Scale, and enum Versioning only. The pinned overlay has unimplemented
accessors, so this is explicitly not reported as successful value decoding.
`DeclaredNumericSchemaExclusions` records the type, count, reason, source files,
and package commit. No other unknown or string-bearing class is excluded this way.
Sources: [ambient color set](https://github.com/Mutagen-Modding/Mutagen/blob/bdbb6ff6faad3b4c3b0689e98e889ecd205068c8/Mutagen.Bethesda.Skyrim/Records/Major%20Records/WeatherAmbientColorSet_Generated.cs),
[ambient color values](https://github.com/Mutagen-Modding/Mutagen/blob/bdbb6ff6faad3b4c3b0689e98e889ecd205068c8/Mutagen.Bethesda.Skyrim/Records/Common%20Subrecords/AmbientColors_Generated.cs),
[image-space aliases](https://github.com/Mutagen-Modding/Mutagen/blob/bdbb6ff6faad3b4c3b0689e98e889ecd205068c8/Mutagen.Bethesda.Skyrim/Records/Major%20Records/WeatherImageSpaces.cs),
[volumetric-lighting aliases](https://github.com/Mutagen-Modding/Mutagen/blob/bdbb6ff6faad3b4c3b0689e98e889ecd205068c8/Mutagen.Bethesda.Skyrim/Records/Major%20Records/WeatherVolumetricLighting.cs).

Encoding options are required and use exception fallbacks. `--source-encoding`
controls embedded translated strings, `--metadata-encoding` controls ordinary
strings such as Editor IDs, and `--tables-encoding` controls localized tables.
`--strings-folder` is the folder containing the actual `*_english.STRINGS`,
`DLSTRINGS`, and `ILSTRINGS` files. `--bsa-folder` defaults to that same directory,
so the sample does not search the game or active MO2 installation for archives.
If an archive folder is explicitly supplied, every BSA there is hashed and kept
open read-only. Plugin and selected loose tables are likewise hashed before and
after enumeration with write-denying handles retained on Windows.

The optional comparison uses **(record signature, file-local raw FormID, exact
text) multisets** for nonempty translated values in non-deleted records. Each
inventory row includes the deleted flag and raw record flags. Deleted-record and
empty translated-string counts are reported separately. It lists unmatched entries and
their Mutagen property paths, and notes whether manifest-only text is found in a
plain-string property. These are review candidates, not automatically proven
translation omissions. Plain strings include technical names, scripts, asset
paths, and some display strings; they must be reviewed separately. Empty values
remain in the inventory but do not participate in the comparison.

The manifest is read once into an immutable byte snapshot used for both parsing
and SHA256. Its `Info.Sha256` must match the inventoried plugin's SHA256; a
mismatched input stops the comparison with a failed report.

Translated inventory rows include `StringsKey` and `ValueState`. A nonzero
localized key whose table/language lookup fails remains an error (`unresolved`).
The pinned Mutagen parser maps localized ID 0 to an unkeyed `TranslatedString`
with a null direct value; its `TryLookup` returns false despite this representing
no text. Only a localized, exact `TranslatedString`, with no key, matching target
language, and null `String` receives `localized_unkeyed_null`. This state records
the observed sentinel, not a claimed raw subrecord ID extracted by this tool.
Null canonical translated getters are `absent`; the shared `TranslatedString.Empty`
instance is `empty_default` even when its default target language differs from
the requested language. Successfully resolved empty text is `keyed_empty` or
`unkeyed_empty`, according to the public key metadata.
All these rows remain in the inventory and are excluded from nonempty multiset
comparison. `TranslatedValueStates` summarizes each state.

Primary distinction: [StringBinaryTranslation.Parse](https://github.com/Mutagen-Modding/Mutagen/blob/bdbb6ff6faad3b4c3b0689e98e889ecd205068c8/Mutagen.Bethesda.Core/Plugins/Binary/Translations/StringBinaryTranslation.cs#L164-L197)
and [TranslatedString.TryLookup / StringsKey](https://github.com/Mutagen-Modding/Mutagen/blob/bdbb6ff6faad3b4c3b0689e98e889ecd205068c8/Mutagen.Bethesda.Core/Strings/TranslatedString.cs).

Raw FormIDs are reconstructed from the plugin's own ordered master list and
`FormKey.ModKey`/`FormKey.ID`. These are on-disk Skyrim file-local IDs, not runtime
FE light-plugin IDs. Getter property paths do not imply subrecord signatures or
byte offsets. The prior xDump comparison remains the evidence for exact exposed
subrecord values.

Unknown objects, getter/enumeration exceptions, missing requested languages, and
limits become report diagnostics, with a nonzero exit status. Exit zero means the
implemented traversal completed, **not** that every possible display string was
identified. `LeafTypesWithoutProperties`, unmatched property paths, plain strings,
and lazy-reader behavior still need review. No fallback to the production parser
is used. A failed import may yield a partial report.

Verified local run on 2026-09-29: `ussep-mutagen-inventory-v2.json` completed
57,205 USSEP major records with zero diagnostics. Its 18,870 nonempty translated
values exactly matched the application's 18,870 fields in both directions;
131,884 plain values were retained separately. The maximum observed working set
was about 262 MiB. The earlier unsuffixed inventory is a failed partial run, and
`sample100*` reports deliberately stop at the record limit. See the workspace's
`docs/analysis/2026-09-29-direct-plugin-validation.md` for the evidence and limits.

The final `ussep-mutagen-inventory-v4.json` also records 15,548 absent translated
properties, so its translated-slot count is 36,405; the same 18,870 nonempty values
still match. `ussep-mutagen-inventory-final-summary.json` binds that report and the
current source hashes.

Skyrim.esm original/edited v2 runs reach all 869,687 major records but remain
**failed raw traversals**: four CPTH numeric condition alias getters throw, and
five SNDR nonzero localized references cannot resolve. A separate, explicitly
scoped comparison matches the application's 67,388 exposed fields in both runs;
it excludes SNDR.String and retains its 54 resolved extras plus five unresolved
references. This does not turn the raw reports into successes or establish that
every display field was selected. Shared IDs also change 54 decoded SNDR values
indirectly despite identical plugin bytes; game behavior remains unverified.

Primary API sources, pinned to the package's repository commit:

- [ModFactory.ImportGetter](https://github.com/Mutagen-Modding/Mutagen/blob/bdbb6ff6faad3b4c3b0689e98e889ecd205068c8/Mutagen.Bethesda.Core/Plugins/Records/ModFactory.cs)
- [StringsReadParameters](https://github.com/Mutagen-Modding/Mutagen/blob/bdbb6ff6faad3b4c3b0689e98e889ecd205068c8/Mutagen.Bethesda.Core/Strings/StringsReadParameters.cs)
- [ITranslatedStringGetter](https://github.com/Mutagen-Modding/Mutagen/blob/bdbb6ff6faad3b4c3b0689e98e889ecd205068c8/Mutagen.Bethesda.Core/Strings/ITranslatedString.cs)
- [Generated Skyrim getter and overlay](https://github.com/Mutagen-Modding/Mutagen/blob/bdbb6ff6faad3b4c3b0689e98e889ecd205068c8/Mutagen.Bethesda.Skyrim/Records/SkyrimMod_Generated.cs)
- [AssociatedRecordTypesAttribute](https://github.com/Mutagen-Modding/Mutagen/blob/bdbb6ff6faad3b4c3b0689e98e889ecd205068c8/Mutagen.Bethesda.Core/Plugins/Records/Mapping/AssociatedRecordTypesAttribute.cs)

Loqui's generated `GetNthName`/`GetNthObject` API is not implemented by these
Skyrim classes; the console instead reflects the canonical getter interface from
`ILoquiObject.Registration.GetterType`. Getter aliases such as a plain `Name`
view are deduplicated in favor of the concrete translated property.
