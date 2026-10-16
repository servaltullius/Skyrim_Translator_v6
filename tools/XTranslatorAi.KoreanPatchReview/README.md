# Korean patch review

The app ships a game translation dictionary built from the community Korean patch kept in `Skyrim_Latest_Kor`
(see `GameStringDictionary`). The patch is improved by reviews written back into its own string tables, then the
dictionary is rebuilt. A player who installs the improved tables gets the same wording in the game.

```powershell
$data = "G:\SteamLibrary\steamapps\common\Skyrim Special Edition\Data"
$patch = "Skyrim_Latest_Kor\01-main"

# 1. Review batches for Gemini (rows used by local mods first). The output folder must be new.
dotnet run --project tools/XTranslatorAi.KoreanPatchReview -- export $data $patch artifacts/patch-review-<date> --projects "$env:LOCALAPPDATA\XTranslatorAi\Projects"

# Creation Club content: --plugins cc exports the patch's tables for the CC plugins in Data, their English read from each
# plugin's BSA. apply checks a change to such a table against that plugin's English as well.
dotnet run --project tools/XTranslatorAi.KoreanPatchReview -- export $data $patch artifacts/patch-review-<date>-cc --plugins cc

# 2. Check a review CSV (plugin,table,id,current_ko,suggested_ko,...), then write it.
dotnet run --project tools/XTranslatorAi.KoreanPatchReview -- apply $data $patch review.csv
dotnet run --project tools/XTranslatorAi.KoreanPatchReview -- apply $data $patch review.csv --write --backup artifacts/patch-review-<date>/backup

# 3. Rebuild the dictionary the app ships.
dotnet run --project tools/XTranslatorAi.KoreanPatchReview -- dictionary $data $patch src/XTranslatorAi.App/Assets/GameStrings/game-strings-skyrim.gz
```

`apply` skips a change when the patch no longer holds the `current_ko` the reviewer saw, or when the new text loses
the English's tags and placeholders. Read every suggestion before `--write`: reviews from a model can be wrong or
misaligned between rows.

## A mod list's copies of the patch text

A plugin that overrides a base game record carries its own copy of the record's text, shown over the patch's string
tables. `override-scan` follows each base game field to its winning plugin in an MO2 profile and compares it with two
patch releases: `old-patch` is a copy of the older patch's text, `english` with `same_english` is the base game's
English left untranslated, `own` is the mod's own text (not touched: without the mod's English it cannot be judged).
`override-fix` writes those first two kinds with the newer patch's Korean, each plugin to its own folder; compare the
written plugins with their sources before they replace the mod's files.

```powershell
$profile = "G:\TAKEALOOK\profiles\<profile>"
dotnet run --project tools/XTranslatorAi.KoreanPatchReview -- override-scan $data $profile artifacts/<scan> --mods G:\TAKEALOOK\mods --overwrite G:\TAKEALOOK\overwrite --old <older patch folder> --new $patch
dotnet run --project tools/XTranslatorAi.KoreanPatchReview -- override-fix $data artifacts/<scan>/winners.csv artifacts/<fix> --mods G:\TAKEALOOK\mods [--exclude a.esp]
```

`override-english` looks for each `own` field's English in another copy of its plugin (the English release a
translation was installed over) and sorts it: `mod-english` (the mod rewrote the line), `base-english` (the base game's
line, translated in other words), `no-english`. A `base-english` field of an overhaul mod may follow that mod's own
terms; read them before writing any with `override-fix --class base-english`.

`summary.csv` counts each plugin's fields by kind, and `not_utf8_fields` the fields that did not read as UTF-8:
exclude such a plugin, as writing it back would keep the replacement characters. Where the patch's own text differs
from the English in a tag or a number's brackets, `override-fix` checks it against itself and lists it in
`patch-text-as-is.tsv`: the game already shows that text for the record when nothing overrides it.
