# Korean patch review

The app ships a game translation dictionary built from the community Korean patch kept in `Skyrim_Latest_Kor`
(see `GameStringDictionary`). The patch is improved by reviews written back into its own string tables, then the
dictionary is rebuilt. A player who installs the improved tables gets the same wording in the game.

```powershell
$data = "G:\SteamLibrary\steamapps\common\Skyrim Special Edition\Data"
$patch = "Skyrim_Latest_Kor\01-main"

# 1. Review batches for Gemini (rows used by local mods first). The output folder must be new.
dotnet run --project tools/XTranslatorAi.KoreanPatchReview -- export $data $patch artifacts/patch-review-<date> --projects "$env:LOCALAPPDATA\XTranslatorAi\Projects"

# 2. Check a review CSV (plugin,table,id,current_ko,suggested_ko,...), then write it.
dotnet run --project tools/XTranslatorAi.KoreanPatchReview -- apply $data $patch review.csv
dotnet run --project tools/XTranslatorAi.KoreanPatchReview -- apply $data $patch review.csv --write --backup artifacts/patch-review-<date>/backup

# 3. Rebuild the dictionary the app ships.
dotnet run --project tools/XTranslatorAi.KoreanPatchReview -- dictionary $data $patch src/XTranslatorAi.App/Assets/GameStrings/game-strings-skyrim.gz
```

`apply` skips a change when the patch no longer holds the `current_ko` the reviewer saw, or when the new text loses
the English's tags and placeholders. Read every suggestion before `--write`: reviews from a model can be wrong or
misaligned between rows.
