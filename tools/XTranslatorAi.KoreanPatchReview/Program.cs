using System.Text;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Text;

// Reviews of the community Korean patch the app's game translation dictionary is built from (see README.md).
//   export     <game Data> <patch folder> <new output folder> [--projects <projects folder>] [--chunk N] [--plugins cc|a.esl,b.esm]
//   apply      <game Data> <patch folder> <review.csv> [--write] [--backup <folder>]
//   dictionary <game Data> <patch folder> <output .gz>
// A patch shipped as a translated plugin (USSEP) instead of string tables:
//   plugin-pair  <english plugin> <translated plugin of an earlier release> <new output folder> [--older-english <its English>]
//                [--dictionary <game strings .gz>] [--game-data <game Data>] [--chunk N]
//   plugin-write <plugin> <review.csv with id = field key, suggested_ko> <new output folder> [--english <its English plugin>]
//                (--english: the plugin is itself a translation, written over; texts are checked against that English)
// A mod list's plugins that override base game records with their own copy of the patch text:
//   override-scan <game Data> <MO2 profile folder> <new output folder> --mods <MO2 mods folder> --old <patch folder> --new <patch folder>
//                 [--overwrite <MO2 overwrite folder>]
//   override-fix  <game Data> <winners.csv from override-scan> <new output folder> --mods <MO2 mods folder> [--exclude a.esp,b.esp]
//                 (writes each plugin with its older-patch text, and its base game English, replaced by the patch's Korean)
if (args.Length < 4)
{
    Console.Error.WriteLine("usage: export|apply|dictionary|plugin-pair|plugin-write <game Data or plugin> <patch folder or plugin> <output or review.csv> [options]");
    return 2;
}

var (command, data, patch, target) = (args[0], args[1], args[2], args[3]);
string? Option(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
switch (command)
{
    case "export":
        return await Export();
    case "apply":
        return await Apply();
    case "dictionary":
    {
        var (dictionary, report) = await GameStringDictionaryBuilder.BuildAsync(data, patch, CancellationToken.None);
        await using (var stream = File.Create(target))
        {
            dictionary.Save(stream);
        }

        Console.WriteLine($"{target}: 레코드 {report.FieldCount:N0}, 문장 {report.TextCount:N0} ({string.Join(", ", report.Plugins)})");
        return 0;
    }
    case "plugin-pair":
        return await PluginPair();
    case "plugin-write":
        return await PluginWrite();
    case "override-scan":
        return await OverrideScan();
    case "override-fix":
        return await OverrideFix();
    default:
        Console.Error.WriteLine("unknown command: " + command);
        return 2;
}

// Pairs a plugin's English with the translated plugin of an earlier release, field by field (the app's previous
// translation matching: FormIDs through the master lists, EditorIDs and subrecord counts checked). With the earlier
// release's English, a field whose English changed since then is marked: its old translation may say something else.
async Task<int> PluginPair()
{
    if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
    {
        Console.Error.WriteLine("새 빈 폴더를 지정하세요: " + target);
        return 2;
    }

    var english = await PluginReader.ReadAsync(data, new PluginReadOptions(), CancellationToken.None);
    var translated = await PluginReader.ReadAsync(patch, new PluginReadOptions(), CancellationToken.None);
    var match = PreviousTranslationMatcher.Match(english.Fields, english.Info.Masters, translated.Fields, translated.Info.Masters, requireHangul: true);
    Dictionary<string, string>? older = null;
    if (Option("--older-english") is { } olderPath)
    {
        var olderDocument = await PluginReader.ReadAsync(olderPath, new PluginReadOptions(), CancellationToken.None);
        older = olderDocument.Fields.GroupBy(f => f.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().SourceText, StringComparer.Ordinal);
    }

    // A field that overrides a base game record with the same English shows the plugin's text over the base game's:
    // the reviewed patch text for that record goes next to it.
    GameStringDictionary? dictionary = null;
    if (Option("--dictionary") is { } dictionaryPath)
    {
        await using var stream = File.OpenRead(dictionaryPath);
        dictionary = GameStringDictionary.Load(stream);
    }

    // The base game's English of each record field, to show what a patch changed in a vanilla line.
    var baseEnglish = new Dictionary<string, string>(StringComparer.Ordinal);
    if (Option("--game-data") is { } gameData)
    {
        foreach (var plugin in GameStringDictionaryBuilder.BaseGamePlugins)
        {
            var document = await PluginReader.ReadAsync(Path.Combine(gameData, plugin), new PluginReadOptions(), CancellationToken.None);
            foreach (var field in document.Fields)
            {
                if (GameStringDictionary.FieldIdentity(field.Key, document.Info.Masters, plugin) is { } identity)
                {
                    baseEnglish[identity] = field.SourceText;
                }
            }
        }
    }

    var chunk = int.TryParse(Option("--chunk"), out var size) ? size : 2000;
    var name = Path.GetFileName(data);
    var status = new Dictionary<string, int>(StringComparer.Ordinal);
    var rows = new List<(string Group, string Line)>();
    foreach (var field in english.Fields.Where(f => !string.IsNullOrWhiteSpace(f.SourceText)))
    {
        var korean = match.TextByFieldKey.GetValueOrDefault(field.Key);
        var state = korean == null ? "new" : older == null ? "paired"
            : !older.TryGetValue(field.Key, out var before) ? "paired, not in the older English"
            : string.Equals(before, field.SourceText, StringComparison.Ordinal) ? "paired" : "English changed since: " + before;
        var key = state.StartsWith("English changed", StringComparison.Ordinal) ? "English changed" : state;
        status[key] = status.GetValueOrDefault(key) + 1;
        // game_ko is the base game's Korean for the record; where the plugin changed its English, the status says so.
        var identity = GameStringDictionary.FieldIdentity(field.Key, english.Info.Masters, name);
        var record = dictionary?.FindRecord(identity, field.SourceText);
        var game = record?.Korean ?? "";
        if (record is { SameEnglish: false })
        {
            state += " | base English differs";
        }

        rows.Add((Category(field.Rec), string.Join(',', Csv(name), "PLUGIN", Csv(field.Key), Csv(field.Rec), Csv(field.EditorId ?? ""), Csv(game),
            Csv(field.SourceText), Csv(korean ?? ""), Csv(state), Csv(identity != null ? baseEnglish.GetValueOrDefault(identity) ?? "" : ""))));
    }

    Directory.CreateDirectory(target);
    var index = new StringBuilder("file,rows\n");
    foreach (var group in rows.GroupBy(r => r.Group).OrderBy(g => g.Key, StringComparer.Ordinal))
    {
        var lines = group.Select(r => r.Line).ToList();
        for (var part = 0; part * chunk < lines.Count; part++)
        {
            var file = $"{group.Key}-{part + 1:00}.csv";
            var csv = new StringBuilder("plugin,table,id,rec,edid,game_ko,source_en,current_ko,other_ko,base_en\n");
            foreach (var line in lines.Skip(part * chunk).Take(chunk))
            {
                csv.AppendLine(line);
            }

            await File.WriteAllTextAsync(Path.Combine(target, file), csv.ToString(), new UTF8Encoding(true));
            index.AppendLine($"{file},{Math.Min(chunk, lines.Count - part * chunk)}");
        }
    }

    await File.WriteAllTextAsync(Path.Combine(target, "index.csv"), index.ToString(), new UTF8Encoding(true));
    Console.WriteLine($"{rows.Count:N0} fields → {target}: {string.Join(", ", status.Select(s => $"{s.Key} {s.Value:N0}"))}");
    Console.WriteLine($"not in the earlier release {match.NotInPreviousRelease:N0}, same as English {match.SameAsSource:N0}, not translated {match.NotTranslated:N0}, record changed {match.ChangedRecord:N0}");
    return 0;
}

// Writes the translated plugin: the English plugin with every field of the CSV (id = field key) in Korean, into a new
// folder. Nothing in the game or the mod manager is touched.
async Task<int> PluginWrite()
{
    var english = await PluginReader.ReadAsync(data, new PluginReadOptions(), CancellationToken.None);
    var rows = ReadCsv(await File.ReadAllTextAsync(patch));
    var header = rows[0].Select(h => h.Trim().TrimStart('﻿')).ToList();
    var (id, suggested) = (header.IndexOf("id"), header.IndexOf("suggested_ko"));
    var keys = english.Fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
    var translations = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var row in rows.Skip(1).Where(r => r.Count > suggested && !string.IsNullOrWhiteSpace(r[suggested])))
    {
        if (!keys.Contains(row[id]))
        {
            Console.Error.WriteLine("이 플러그인에 없는 필드: " + row[id]);
            return 2;
        }

        translations[row[id]] = row[suggested];
    }

    // The writer stops at the first text that loses the English's tags; list them all first.
    IReadOnlyDictionary<string, string>? checkAgainst = null;
    if (Option("--english") is { } englishPath)
    {
        var source = await PluginReader.ReadAsync(englishPath, new PluginReadOptions(), CancellationToken.None);
        checkAgainst = source.Fields.GroupBy(f => f.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().SourceText, StringComparer.Ordinal);
    }

    var broken = PluginWriter.FindInvalidTranslations(english, translations, allowDroppedInitialsAndPronouns: true, checkAgainst).ToList();

    if (broken.Count > 0)
    {
        broken.ForEach(Console.Error.WriteLine);
        Console.Error.WriteLine($"태그가 영어와 다른 번역 {broken.Count:N0}개");
        return 2;
    }

    var result = await PluginWriter.ExportAsync(english, translations, new PluginExportOptions(target, AllowDroppedInitialsAndPronouns: true, CheckAgainst: checkAgainst), CancellationToken.None);
    Console.WriteLine($"{result.PluginPath}: 바뀐 필드 {result.ChangedFields:N0}, sha256 {result.Sha256}");
    return 0;
}

// A plugin that overrides a base game record carries its own copy of the record's text, which the game shows over the
// patch's string tables. A Korean mod list's plugins often copied the patch text of their time: where the record that
// wins in the load order holds exactly the older patch's Korean, and the patch has since changed it, the player still
// sees the older text. Each base game field is followed to its winning plugin and compared with both patches.
async Task<int> OverrideScan()
{
    var (profile, output) = (patch, target);
    if (Option("--mods") is not { } modsFolder || Option("--old") is not { } oldPatch || Option("--new") is not { } newPatch)
    {
        Console.Error.WriteLine("--mods, --old, --new를 지정하세요.");
        return 2;
    }

    if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    {
        Console.Error.WriteLine("새 빈 폴더를 지정하세요: " + output);
        return 2;
    }

    Directory.CreateDirectory(output);
    var (oldDictionary, _) = await GameStringDictionaryBuilder.BuildAsync(data, oldPatch, CancellationToken.None);
    var (newDictionary, _) = await GameStringDictionaryBuilder.BuildAsync(data, newPatch, CancellationToken.None);

    // Where MO2 finds each plugin: the game's Data, then the enabled mods from the lowest priority up (modlist.txt
    // lists the highest first), then the overwrite folder.
    var pathByName = new Dictionary<string, (string Path, string Mod)>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in Directory.EnumerateFiles(data))
    {
        pathByName[Path.GetFileName(file)] = (file, "(Data)");
    }

    var mods = File.ReadAllLines(Path.Combine(profile, "modlist.txt"))
        .Where(line => line.StartsWith('+'))
        .Select(line => line[1..])
        .Reverse();
    foreach (var mod in mods)
    {
        var folder = Path.Combine(modsFolder, mod);
        if (!Directory.Exists(folder))
        {
            continue;
        }

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            pathByName[Path.GetFileName(file)] = (file, mod);
        }
    }

    if (Option("--overwrite") is { } overwrite && Directory.Exists(overwrite))
    {
        foreach (var file in Directory.EnumerateFiles(overwrite))
        {
            pathByName[Path.GetFileName(file)] = (file, "(overwrite)");
        }
    }

    // plugins.txt marks the enabled plugins with *; the base game and Creation Club plugins are not listed and always load.
    var listed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in File.ReadAllLines(Path.Combine(profile, "plugins.txt")))
    {
        if (line.Length > 0 && line[0] != '#')
        {
            listed[line.TrimStart('*')] = line[0] == '*';
        }
    }

    var loadOrder = File.ReadAllLines(Path.Combine(profile, "loadorder.txt"))
        .Where(line => line.Length > 0 && line[0] != '#')
        .Where(name => !listed.TryGetValue(name, out var enabled) || enabled)
        .ToList();

    var skipped = new HashSet<string>(GameStringDictionaryBuilder.BaseGamePlugins, StringComparer.OrdinalIgnoreCase);
    var winners = new Dictionary<string, (string Plugin, string Mod, PluginField Field)>(StringComparer.Ordinal);
    var failures = new List<string>();
    var brokenText = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var read = 0;
    foreach (var name in loadOrder)
    {
        if (skipped.Contains(name) || !pathByName.TryGetValue(name, out var location))
        {
            continue;
        }

        PluginDocument document;
        try
        {
            document = await PluginReader.ReadAsync(location.Path, new PluginReadOptions(SourceLanguage: "korean"), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add($"{name}\t{ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}");
            continue;
        }

        read++;
        // Text that is not UTF-8 (CP949) reads with replacement characters, and writing the plugin back would keep them.
        if (document.Fields.Count(field => field.SourceText.Contains('�')) is var broken and > 0)
        {
            brokenText[name] = broken;
        }

        foreach (var field in document.Fields)
        {
            if (GameStringDictionary.FieldIdentity(field.Key, document.Info.Masters, name) is { } identity
                && newDictionary.FindRecord(identity, field.SourceText) != null)
            {
                winners[identity] = (name, location.Mod, field);
            }
        }
    }

    static string Normalize(string text) => text.ReplaceLineEndings("\n").Trim();
    // same_english: the winning text is the base game's English itself, so the patch's Korean fits it as it is.
    var rows = new List<string> { "class,same_english,plugin,mod,field_key,identity,rec,edid,text,old_ko,new_ko" };
    var counts = new Dictionary<(string Plugin, string Class), int>();
    foreach (var (identity, (plugin, mod, field)) in winners.OrderBy(pair => pair.Value.Plugin, StringComparer.OrdinalIgnoreCase).ThenBy(pair => pair.Value.Field.Key, StringComparer.Ordinal))
    {
        var text = Normalize(field.SourceText);
        var found = newDictionary.FindRecord(identity, field.SourceText)!.Value;
        var newKo = Normalize(found.Korean);
        var oldKo = oldDictionary.FindRecord(identity, field.SourceText) is { } old ? Normalize(old.Korean) : "";
        var kind = text == newKo ? "same"
            : text == oldKo ? "old-patch"
            : !text.Any(c => c is >= '가' and <= '힣') ? "english"
            : "own";
        counts[(plugin, kind)] = counts.GetValueOrDefault((plugin, kind)) + 1;
        if (kind != "same")
        {
            rows.Add(string.Join(',', new[] { kind, found.SameEnglish ? "1" : "", plugin, mod, field.Key, identity, field.Rec, field.EditorId ?? "", text, oldKo, newKo }.Select(Csv)));
        }
    }

    await File.WriteAllLinesAsync(Path.Combine(output, "winners.csv"), rows, new UTF8Encoding(true));
    var summary = new List<string> { "plugin,same,old_patch,own,english,not_utf8_fields" };
    foreach (var plugin in counts.Keys.Select(key => key.Plugin).Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(p => counts.GetValueOrDefault((p, "old-patch"))))
    {
        summary.Add(string.Join(',', Csv(plugin), counts.GetValueOrDefault((plugin, "same")), counts.GetValueOrDefault((plugin, "old-patch")),
            counts.GetValueOrDefault((plugin, "own")), counts.GetValueOrDefault((plugin, "english")), brokenText.GetValueOrDefault(plugin)));
    }

    await File.WriteAllLinesAsync(Path.Combine(output, "summary.csv"), summary, new UTF8Encoding(true));
    await File.WriteAllLinesAsync(Path.Combine(output, "failures.tsv"), failures, new UTF8Encoding(true));
    Console.WriteLine($"플러그인 {read:N0}개 읽음(실패 {failures.Count}), 기본 게임 필드를 덮는 승자 {winners.Count:N0}개: "
        + string.Join(", ", counts.GroupBy(pair => pair.Key.Class).Select(group => $"{group.Key} {group.Sum(pair => pair.Value):N0}")));
    return 0;
}

// Writes the fields override-scan found: a plugin's copy of the older patch text ("old-patch"), and a base game English
// it left untranslated ("english" with same_english), take the patch's Korean. Only the text of those fields changes;
// each plugin is written to <output>/<mod folder>/<plugin name>/<plugin> for checking before it replaces the mod's file.
async Task<int> OverrideFix()
{
    var (winnersCsv, output) = (patch, target);
    if (Option("--mods") is not { } modsFolder)
    {
        Console.Error.WriteLine("--mods를 지정하세요.");
        return 2;
    }

    if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    {
        Console.Error.WriteLine("새 빈 폴더를 지정하세요: " + output);
        return 2;
    }

    var excluded = (Option("--exclude") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var rows = ReadCsv(await File.ReadAllTextAsync(winnersCsv));
    var header = rows[0].Select(h => h.Trim().TrimStart('\uFEFF')).ToList();
    int Col(string name) => header.IndexOf(name);
    var (kindCol, sameCol, pluginCol, modCol, keyCol, identityCol, textCol, newCol) =
        (Col("class"), Col("same_english"), Col("plugin"), Col("mod"), Col("field_key"), Col("identity"), Col("text"), Col("new_ko"));
    var targets = rows.Skip(1)
        .Where(r => r.Count > newCol && (r[kindCol] == "old-patch" || (r[kindCol] == "english" && r[sameCol] == "1")))
        .Where(r => !excluded.Contains(r[pluginCol]))
        .GroupBy(r => (Mod: r[modCol], Plugin: r[pluginCol]))
        .ToList();

    // The base game English of each field, which the new Korean is checked against: the plugin's own text is the
    // older Korean (or that English), not the source.
    var englishByIdentity = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var plugin in GameStringDictionaryBuilder.BaseGamePlugins)
    {
        var document = await PluginReader.ReadAsync(Path.Combine(data, plugin), new PluginReadOptions(), CancellationToken.None);
        foreach (var field in document.Fields)
        {
            if (GameStringDictionary.FieldIdentity(field.Key, document.Info.Masters, plugin) is { } identity)
            {
                englishByIdentity[identity] = field.SourceText;
            }
        }
    }

    static string Normalize(string text) => text.ReplaceLineEndings("\n").Trim();
    // The new text in the field's own frame: its line-ending style and its leading and trailing blank space.
    static string InFrame(string raw, string core)
    {
        var newline = raw.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lead = raw[..(raw.Length - raw.TrimStart().Length)];
        var trail = raw[raw.TrimEnd().Length..];
        return lead + Normalize(core).Replace("\n", newline, StringComparison.Ordinal) + trail;
    }

    Directory.CreateDirectory(output);
    var manifest = new List<string> { "mod,plugin,source,output,planned,changed,skipped,sha256" };
    var problems = new List<string>();
    var patchTextAsIs = new List<string>();
    int written = 0, changedTotal = 0;
    foreach (var group in targets)
    {
        var source = Path.Combine(modsFolder, group.Key.Mod, group.Key.Plugin);
        if (!File.Exists(source))
        {
            problems.Add($"{group.Key.Plugin}\t파일 없음: {source}");
            continue;
        }

        var document = await PluginReader.ReadAsync(source, new PluginReadOptions(SourceLanguage: "korean"), CancellationToken.None);
        if (document.Info.IsLocalized)
        {
            problems.Add($"{group.Key.Plugin}\t문자열 파일을 쓰는 플러그인이라 건너뜀");
            continue;
        }

        var fields = document.Fields.ToDictionary(f => f.Key, StringComparer.Ordinal);
        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        var checkAgainst = new Dictionary<string, string>(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var row in group)
        {
            if (!fields.TryGetValue(row[keyCol], out var field) || Normalize(field.SourceText) != row[textCol]
                || !englishByIdentity.TryGetValue(row[identityCol], out var english))
            {
                problems.Add($"{group.Key.Plugin}\t{row[keyCol]}\t스캔 뒤 글자가 바뀌었거나 영어를 찾지 못함");
                skipped++;
                continue;
            }

            translations[field.Key] = InFrame(field.SourceText, row[newCol]);
            checkAgainst[field.Key] = InFrame(field.SourceText, english);
        }

        // The patch's own text differs from the English in a tag or a number's brackets ("3%" as "<3>%") in some fields.
        // The game shows that very text for the base record when nothing overrides it, so it is checked against itself
        // here, and listed.
        foreach (var broken in PluginWriter.FindInvalidTranslations(document, translations, allowDroppedInitialsAndPronouns: true, checkAgainst))
        {
            var match = System.Text.RegularExpressions.Regex.Match(broken, @"plugin:([A-Z_0-9]{4}/[0-9A-F]{8}/\d+/[A-Z_0-9]{4}/\d+)");
            if (match.Success && translations.TryGetValue(match.Groups[1].Value, out var text))
            {
                checkAgainst[match.Groups[1].Value] = text;
                patchTextAsIs.Add($"{group.Key.Plugin}\t{broken.ReplaceLineEndings(" ")}");
            }
            else
            {
                problems.Add($"{group.Key.Plugin}\t{broken.ReplaceLineEndings(" ")}");
            }
        }

        if (translations.Count == 0)
        {
            continue;
        }

        var folder = Path.Combine(output, group.Key.Mod, Path.GetFileNameWithoutExtension(group.Key.Plugin));
        var result = await PluginWriter.ExportAsync(document, translations,
            new PluginExportOptions(folder, AllowDroppedInitialsAndPronouns: true, CheckAgainst: checkAgainst), CancellationToken.None);
        written++;
        changedTotal += result.ChangedFields;
        manifest.Add(string.Join(',', new[] { group.Key.Mod, group.Key.Plugin, source, result.PluginPath,
            translations.Count.ToString(), result.ChangedFields.ToString(), skipped.ToString(), result.Sha256 }.Select(Csv)));
    }

    await File.WriteAllLinesAsync(Path.Combine(output, "manifest.csv"), manifest, new UTF8Encoding(true));
    await File.WriteAllLinesAsync(Path.Combine(output, "problems.tsv"), problems, new UTF8Encoding(true));
    await File.WriteAllLinesAsync(Path.Combine(output, "patch-text-as-is.tsv"), patchTextAsIs, new UTF8Encoding(true));
    Console.WriteLine($"플러그인 {written}개 씀, 바뀐 필드 {changedTotal:N0}, 영어와 태그가 다른 패치 글자 그대로 {patchTextAsIs.Count}, 문제 {problems.Count}");
    return 0;
}

async Task<int> Export()
{
    if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
    {
        Console.Error.WriteLine("새 빈 폴더를 지정하세요: " + target);
        return 2;
    }

    var chunk = int.TryParse(Option("--chunk"), out var size) ? size : 2000;
    var used = Option("--projects") is { } projects ? ReadProjectSources(projects) : new HashSet<string>();
    var strings = await KoreanPatchStrings.ReadAsync(data, patch, CancellationToken.None, Option("--plugins") switch
    {
        null => null,
        "cc" => CreationClubPlugins(data, patch),
        var list => list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    });
    Directory.CreateDirectory(target);
    var groups = strings
        .GroupBy(s => used.Contains(TranslationMemoryKey.NormalizeSource(s.English)) ? "01-used-by-mods" : Category(s.Rec))
        .OrderBy(g => g.Key, StringComparer.Ordinal);
    // The patch's other translations of the same English, in every file: a batch alone does not show that a name is
    // 서리 숨결 in 17 strings elsewhere. Short lines only; a book differs from its copies for other reasons.
    var byEnglish = strings.Where(s => s.English.Length <= 200 && s.English.IndexOf('\n') < 0)
        .GroupBy(s => s.English, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.GroupBy(s => s.Korean, StringComparer.Ordinal)
            .Select(k => (Korean: k.Key, Count: k.Count(), Hidden: k.All(s => s.OverriddenBy != null)))
            .OrderByDescending(k => k.Count).ToList(), StringComparer.Ordinal);
    string OtherKo(KoreanPatchString s) => byEnglish.TryGetValue(s.English, out var variants)
        ? string.Join(" | ", variants.Where(v => v.Korean != s.Korean).Take(5)
            .Select(v => $"{v.Korean} ({v.Count}{(v.Hidden ? ", 가려짐" : "")})"))
        : "";
    var index = new StringBuilder("file,rows\n");
    foreach (var group in groups)
    {
        var rows = group.OrderBy(s => s.Rec, StringComparer.Ordinal).ThenBy(s => s.Edid, StringComparer.Ordinal).ToList();
        for (var part = 0; part * chunk < rows.Count; part++)
        {
            var name = $"{group.Key}-{part + 1:00}.csv";
            var csv = new StringBuilder("plugin,table,id,rec,edid,overridden_by,source_en,current_ko,other_ko\n");
            foreach (var s in rows.Skip(part * chunk).Take(chunk))
            {
                csv.AppendLine(string.Join(',', Csv(s.Plugin), TableName(s.Table), s.Id.ToString("X8"), Csv(s.Rec), Csv(s.Edid ?? ""),
                    Csv(s.OverriddenBy ?? ""), Csv(s.English), Csv(s.Korean), Csv(OtherKo(s))));
            }

            await File.WriteAllTextAsync(Path.Combine(target, name), csv.ToString(), new UTF8Encoding(true));
            index.AppendLine($"{name},{Math.Min(chunk, rows.Count - part * chunk)}");
        }
    }

    await File.WriteAllTextAsync(Path.Combine(target, "index.csv"), index.ToString(), new UTF8Encoding(true));
    File.Copy(Path.Combine(AppContext.BaseDirectory, "REVIEW_GUIDE.md"), Path.Combine(target, "REVIEW_GUIDE.md"));
    Console.WriteLine($"{strings.Count:N0} strings, {used.Count:N0} mod texts → {target}");
    Console.Write(index);
    return 0;
}

async Task<int> Apply()
{
    var rows = ReadCsv(await File.ReadAllTextAsync(target));
    var header = rows[0].Select(h => h.Trim().TrimStart('﻿')).ToList();
    int Column(string name) => header.IndexOf(name) is var i and >= 0 ? i : throw new InvalidDataException("열이 없습니다: " + name);
    var (plugin, table, id, current, suggested) = (Column("plugin"), Column("table"), Column("id"), Column("current_ko"), Column("suggested_ko"));
    var changes = rows.Skip(1)
        .Where(r => r.Count > suggested && !string.IsNullOrWhiteSpace(r[suggested]))
        .Select(r => new KoreanPatchChange(r[plugin], ParseTable(r[table]), Convert.ToUInt32(r[id], 16), r[current], r[suggested]))
        .ToList();
    var write = args.Contains("--write");
    var result = await KoreanPatchStrings.ApplyAsync(data, patch, changes, write, Option("--backup"), CancellationToken.None);
    Console.WriteLine($"{(write ? "반영" : "확인만")}: 적용 {result.Applied.Count}, 건너뜀 {result.Skipped.Count}");
    foreach (var (change, reason) in result.Skipped)
    {
        Console.WriteLine($"  건너뜀 {change.Plugin} {TableName(change.Table)} {change.Id:X8}: {reason}");
    }

    return 0;
}

// The Creation Club plugins the patch translates: every table that is not the base game's, matched to its plugin in the
// game's Data folder (the English is in that plugin's BSA).
static IReadOnlyList<string> CreationClubPlugins(string data, string patch)
{
    var folder = Directory.GetDirectories(patch).FirstOrDefault(d => string.Equals(Path.GetFileName(d), "Strings", StringComparison.OrdinalIgnoreCase)) ?? patch;
    var stems = Directory.EnumerateFiles(folder).Select(f => Path.GetFileNameWithoutExtension(f))
        .Where(n => n.EndsWith("_english", StringComparison.OrdinalIgnoreCase)).Select(n => n[..^"_english".Length])
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    stems.ExceptWith(GameStringDictionaryBuilder.BaseGamePlugins.Select(Path.GetFileNameWithoutExtension)!);
    return Directory.EnumerateFiles(data)
        .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".esm" or ".esl" or ".esp" && stems.Contains(Path.GetFileNameWithoutExtension(f)))
        .Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()!;
}

static HashSet<string> ReadProjectSources(string folder)
{
    var texts = new HashSet<string>(StringComparer.Ordinal);
    foreach (var db in Directory.EnumerateFiles(folder, "*.sqlite", SearchOption.AllDirectories))
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={db};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT SourceText FROM StringEntry";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                texts.Add(TranslationMemoryKey.NormalizeSource(reader.GetString(0)));
            }
        }
        catch (SqliteException)
        {
            // Not a project database (the global glossary, a broken copy).
        }
    }

    return texts;
}

static string Category(string rec)
{
    var (type, sub) = (rec.Split(':')[0], rec.Split(':').ElementAtOrDefault(1) ?? "");
    return type switch
    {
        "INFO" or "DIAL" => "06-dialogue",
        "BOOK" when sub == "DESC" => "05-books",
        "QUST" => "04-quests",
        _ when sub is "FULL" or "SHRT" or "ITXT" => "02-names",
        _ when sub is "DESC" or "DNAM" or "CNAM" => "03-descriptions",
        _ => "07-other",
    };
}

static string TableName(PluginStringTableKind kind) => kind switch
{
    PluginStringTableKind.DlStrings => "DLSTRINGS",
    PluginStringTableKind.IlStrings => "ILSTRINGS",
    _ => "STRINGS",
};

static PluginStringTableKind ParseTable(string name) => name.Trim().ToUpperInvariant() switch
{
    "DLSTRINGS" => PluginStringTableKind.DlStrings,
    "ILSTRINGS" => PluginStringTableKind.IlStrings,
    "STRINGS" => PluginStringTableKind.Strings,
    _ => throw new InvalidDataException("알 수 없는 테이블: " + name),
};

static string Csv(string value)
    => value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";

// RFC 4180: quoted fields may hold commas, quotes ("") and line breaks.
static List<List<string>> ReadCsv(string text)
{
    var rows = new List<List<string>>();
    var row = new List<string>();
    var field = new StringBuilder();
    var quoted = false;
    for (var i = 0; i < text.Length; i++)
    {
        var c = text[i];
        if (quoted)
        {
            if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
            {
                field.Append('"');
                i++;
            }
            else if (c == '"')
            {
                quoted = false;
            }
            else
            {
                field.Append(c);
            }
        }
        else if (c == '"')
        {
            quoted = true;
        }
        else if (c == ',')
        {
            row.Add(field.ToString());
            field.Clear();
        }
        else if (c == '\n' || c == '\r')
        {
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            row.Add(field.ToString());
            field.Clear();
            if (row.Any(f => f.Length > 0))
            {
                rows.Add(row);
            }

            row = new List<string>();
        }
        else
        {
            field.Append(c);
        }
    }

    row.Add(field.ToString());
    if (row.Any(f => f.Length > 0))
    {
        rows.Add(row);
    }

    return rows;
}
