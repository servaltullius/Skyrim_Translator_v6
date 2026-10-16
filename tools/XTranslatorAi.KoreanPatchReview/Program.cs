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
//   plugin-write <english plugin> <review.csv with id = field key, suggested_ko> <new output folder>
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

    var result = await PluginWriter.ExportAsync(english, translations, new PluginExportOptions(target), CancellationToken.None);
    Console.WriteLine($"{result.PluginPath}: 바뀐 필드 {result.ChangedFields:N0}, sha256 {result.Sha256}");
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
