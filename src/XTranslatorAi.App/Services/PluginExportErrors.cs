using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.App.Services;

/// <summary>
/// The rows an export refusal names. The plugin writer names a field by its key ("plugin:INFO/00001003/0/NAM1/0"), by its
/// record ("WEAP:FULL/00000800") or by the fields' 순번 ("관련 행: #11, #12"), by which the agent, who knows rows by id,
/// finds none: its export turned the first into ids alone, and the GUI's said a token rule was broken, as of a model's
/// answer, naming no row.
/// </summary>
public static class PluginExportErrors
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    // A key as the reader writes it ({Type}/{FormId}/{occurrence}/{Sub}/{ordinal}): "\S+" took the sentence's closing
    // period with it ("…/NAM1/0."), and no row had that key.
    private static readonly Regex FieldKey = new(@"plugin:([^/\s]{4}/[0-9A-Fa-f]{8}/\d+/[^/\s]{4}/\d+)", RegexOptions.CultureInvariant, Timeout);

    // Only the two refusals of a translated field by its record: the same pattern names a record in the writer's own
    // checks ("비번역 데이터가 변경되었습니다: INFO:CTDA/…"), which no edit fixes.
    private static readonly Regex RecordField = new(
        @"^(?<what>비어 있는 번역을 저장할 수 없습니다|출력 인코딩으로 표현할 수 없는 문자가 있습니다): (?<type>[^/\s:]{4}):(?<sub>[^/\s]{4})/(?<form>[0-9A-F]{8})(?: U\+(?<ch>[0-9A-F]{4,6}))?$",
        RegexOptions.CultureInvariant, Timeout);

    private static readonly Regex SharedString = new(
        @"^여러 필드가 같은 (?<table>\w+) StringID (?<id>\d+)를 공유하지만 번역이 다릅니다\..*관련 행: (?<rows>#\d+(?:, #\d+)*)$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline, Timeout);

    /// <summary>
    /// The ids of the rows the refusal names. The writer stops at the first translation that loses a tag; all of them are
    /// named, so one round of edits fixes them. Of a record's rows, only those whose translation the refusal is about.
    /// </summary>
    public static async Task<IReadOnlyList<long>> RowIdsAsync(ProjectDb db, PluginDocument document, string message, CancellationToken ct)
    {
        if (FieldKey.IsMatch(message))
        {
            var problems = PluginWriter.FindInvalidTranslations(document, await db.GetPluginTranslationsForExportAsync(document.Info.Sha256, ct));
            var keys = new[] { message }.Concat(problems).SelectMany(text => FieldKey.Matches(text)).Select(m => m.Groups[1].Value).Distinct().ToList();
            var idByKey = (await db.GetPluginFieldKeysByStringIdAsync(ct)).ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
            return keys.Select(key => idByKey.TryGetValue(key, out var id) ? id : (long?)null).OfType<long>().ToList();
        }

        if (RecordField.Match(message) is { Success: true } field)
        {
            var (type, sub, formId) = (field.Groups["type"].Value, field.Groups["sub"].Value, field.Groups["form"].Value);
            var ids = (await db.GetPluginFieldKeysByStringIdAsync(ct))
                .Where(pair => pair.Value.Split('/') is [var t, var f, _, var s, _] && t == type && s == sub && string.Equals(f, formId, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key).ToHashSet();
            var written = (await RowsAsync(db, ct)).Where(row => ids.Contains(row.Id)).ToList();
            bool Written(StringEntry row) => row.Status is StringEntryStatus.Done or StringEntryStatus.Edited;
            if (!field.Groups["ch"].Success)
            {
                return written.Where(row => Written(row) && string.IsNullOrWhiteSpace(row.DestText)).Select(row => row.Id).ToList();
            }

            // The writer encodes the translation, or the English of a row it does not write.
            var ch = char.ConvertFromUtf32(Convert.ToInt32(field.Groups["ch"].Value, 16));
            return written.Where(row => ((Written(row) ? row.DestText : row.SourceText) ?? "").Contains(ch, StringComparison.Ordinal))
                .Select(row => row.Id).ToList();
        }

        if (SharedString.Match(message) is { Success: true } shared)
        {
            var orders = shared.Groups["rows"].Value.Split(", ").Select(n => int.Parse(n[1..])).ToList();
            var byOrder = (await RowsAsync(db, ct)).ToDictionary(row => row.OrderIndex, row => row.Id);
            return orders.Select(order => byOrder.TryGetValue(order, out var id) ? id : (long?)null).OfType<long>().ToList();
        }

        return Array.Empty<long>();
    }

    /// <summary>
    /// The refusal said for a reader who knows rows by id: the shared-string one's search hint and 순번 are the GUI's,
    /// and an agent told "관련 행: #11" edited the row whose id is 11.
    /// </summary>
    public static string Reason(string message)
        => SharedString.Match(message) is { Success: true } shared
            ? $"플러그인의 같은 문자열({shared.Groups["table"].Value} StringID {shared.Groups["id"].Value})을 함께 쓰는 줄인데 번역이 다릅니다. 같은 번역으로 맞추세요."
            : message;

    private static async Task<List<StringEntry>> RowsAsync(ProjectDb db, CancellationToken ct)
    {
        var rows = new List<StringEntry>();
        for (var offset = 0; ; offset += 1000)
        {
            var page = await db.GetStringsAsync(1000, offset, ct);
            rows.AddRange(page);
            if (page.Count < 1000) break;
        }

        return rows;
    }
}
