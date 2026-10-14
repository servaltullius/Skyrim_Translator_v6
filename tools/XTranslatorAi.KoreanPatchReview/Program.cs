using System.Text;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Text;

// Reviews of the community Korean patch the app's game translation dictionary is built from (see README.md).
//   export     <game Data> <patch folder> <new output folder> [--projects <projects folder>] [--chunk N]
//   apply      <game Data> <patch folder> <review.csv> [--write] [--backup <folder>]
//   dictionary <game Data> <patch folder> <output .gz>
if (args.Length < 4)
{
    Console.Error.WriteLine("usage: export|apply|dictionary <game Data> <patch folder> <output or review.csv> [options]");
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
    default:
        Console.Error.WriteLine("unknown command: " + command);
        return 2;
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
    var strings = await KoreanPatchStrings.ReadAsync(data, patch, CancellationToken.None);
    Directory.CreateDirectory(target);
    var groups = strings
        .GroupBy(s => used.Contains(TranslationMemoryKey.NormalizeSource(s.English)) ? "01-used-by-mods" : Category(s.Rec))
        .OrderBy(g => g.Key, StringComparer.Ordinal);
    var index = new StringBuilder("file,rows\n");
    foreach (var group in groups)
    {
        var rows = group.OrderBy(s => s.Rec, StringComparer.Ordinal).ThenBy(s => s.Edid, StringComparer.Ordinal).ToList();
        for (var part = 0; part * chunk < rows.Count; part++)
        {
            var name = $"{group.Key}-{part + 1:00}.csv";
            var csv = new StringBuilder("plugin,table,id,rec,edid,overridden_by,source_en,current_ko\n");
            foreach (var s in rows.Skip(part * chunk).Take(chunk))
            {
                csv.AppendLine(string.Join(',', Csv(s.Plugin), TableName(s.Table), s.Id.ToString("X8"), Csv(s.Rec), Csv(s.Edid ?? ""),
                    Csv(s.OverriddenBy ?? ""), Csv(s.English), Csv(s.Korean)));
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
