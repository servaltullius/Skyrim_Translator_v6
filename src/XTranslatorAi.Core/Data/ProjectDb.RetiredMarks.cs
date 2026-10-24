using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace XTranslatorAi.Core.Data;

/// <summary>
/// A translation set aside (a row a partial XML lacked, or a plugin row whose source an update changed) came back
/// without the marks the user put on it: "원문 오류 수정" put the deliberate departure from the source under the
/// ordinary checks again, and "확인함" brought back results already judged. The marks are kept with the translation
/// (<c>Marks</c>, JSON) and restored with it. TM notes describe how the text was made, not a decision, and stay behind.
/// </summary>
public sealed partial class ProjectDb
{
    private sealed record RetiredMark(string Kind, string Message, string UpdatedAt);

    private static bool IsUserMark(string kind)
        => kind == TranslationConstants.SourceFixNoteKind
           || kind.StartsWith(TranslationConstants.LqaReviewedNoteKindPrefix, StringComparison.Ordinal);

    /// <summary>The user's marks among a row's notes as stored with its retired translation, or null when it has none.</summary>
    private static string? SerializeUserMarks(IEnumerable<(string Kind, string Message, string UpdatedAt)>? notes)
    {
        var marks = notes?.Where(note => IsUserMark(note.Kind)).Select(note => new RetiredMark(note.Kind, note.Message, note.UpdatedAt)).ToList();
        return marks is { Count: > 0 } ? JsonSerializer.Serialize(marks) : null;
    }

    private static IReadOnlyList<(string Kind, string Message, string UpdatedAt)> ReadUserMarks(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return Array.Empty<(string, string, string)>();
        try
        {
            return (JsonSerializer.Deserialize<List<RetiredMark>>(reader.GetString(ordinal)) ?? new List<RetiredMark>())
                .Where(mark => mark.Kind != null && IsUserMark(mark.Kind))
                .Select(mark => (mark.Kind, mark.Message ?? "", mark.UpdatedAt ?? "")).ToList();
        }
        catch (JsonException)
        {
            // A damaged value loses only the marks, not the translation they belong to.
            return Array.Empty<(string, string, string)>();
        }
    }

    /// <summary>Migration: the retired tables of an older DB have no Marks column.</summary>
    private async Task EnsureRetiredMarksColumnsAsync(CancellationToken cancellationToken)
    {
        foreach (var table in new[] { "RetiredTranslation", "PluginRetiredTranslation" })
        {
            var present = false;
            await using (var check = _connection.CreateCommand())
            {
                check.CommandText = $"PRAGMA table_info({table});";
                await using var reader = await check.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (string.Equals(reader.GetString(1), "Marks", StringComparison.OrdinalIgnoreCase)) present = true;
                }
            }

            if (present) continue;
            try
            {
                await using var alter = _connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN Marks TEXT;";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException ex) when (
                ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // Another connection added it first.
            }
        }
    }
}
