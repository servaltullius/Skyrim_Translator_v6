using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.Core.Data;

/// <summary>
/// The speech level decided for what a speaker says to a listener (<see cref="Translation.SpeakerStyles"/>): the
/// speaker's FormID as the speaker groups have it ("%08X", or "player"), the listener's ("" the player, "00000000"
/// another character, "*" anyone), and the level (합쇼체, 해요체, 하오체, 하게체, 반말).
/// </summary>
public sealed record SpeakerStyle(string Speaker, string Listener, string Level, string? Note = null);

public sealed partial class ProjectDb
{
    /// <summary>
    /// The decided speech levels. Meridia's Order's Laracin was decided to speak 합쇼체 to the Champion and the
    /// translators wrote 반말 in most of his lines; the check, comparing his lines with each other, flagged the right ones.
    /// </summary>
    public async Task<IReadOnlyList<SpeakerStyle>> GetSpeakerStylesAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var styles = new List<SpeakerStyle>();
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Speaker, Listener, Level, Note FROM SpeakerStyle ORDER BY Speaker, Listener;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                styles.Add(new SpeakerStyle(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
            }

            return styles;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Decides a speaker's level to a listener; an empty level takes the decision back. False when there was
    /// nothing to take back.</summary>
    public async Task<bool> SetSpeakerStyleAsync(SpeakerStyle style, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            if (string.IsNullOrWhiteSpace(style.Level))
            {
                cmd.CommandText = "DELETE FROM SpeakerStyle WHERE Speaker=$Speaker AND Listener=$Listener;";
            }
            else
            {
                cmd.CommandText = "INSERT INTO SpeakerStyle (Speaker, Listener, Level, Note, UpdatedAt) VALUES ($Speaker, $Listener, $Level, $Note, $UpdatedAt) "
                                  + "ON CONFLICT(Speaker, Listener) DO UPDATE SET Level=excluded.Level, Note=excluded.Note, UpdatedAt=excluded.UpdatedAt;";
                cmd.Parameters.AddWithValue("$Level", style.Level);
                cmd.Parameters.AddWithValue("$Note", (object?)style.Note ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
            }

            cmd.Parameters.AddWithValue("$Speaker", style.Speaker);
            cmd.Parameters.AddWithValue("$Listener", style.Listener);
            return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The styles name their speaker and listener by FormID, whose first byte is a master's position: an update that
    /// added or reordered masters renumbered the NPC (01000800 → 02000800), and the styles stayed on the old number, so
    /// the requests and the check no longer had the decided level (and the old number could be another NPC's). They are
    /// renumbered with the rows. One whose master left is kept under that master's name ("@Dawnguard.esm|01001234"),
    /// on no NPC, and numbered again when the master is back.
    /// </summary>
    private async Task RemapSpeakerStylesAsync(
        SqliteTransaction tx, IReadOnlyList<string> fromMasters, IReadOnlyList<string> toMasters, CancellationToken ct)
    {
        var rows = new List<(string Speaker, string Listener, string Level, object Note, string UpdatedAt)>();
        await using (var query = _connection.CreateCommand())
        {
            query.Transaction = tx;
            query.CommandText = "SELECT Speaker, Listener, Level, Note, UpdatedAt FROM SpeakerStyle;";
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? DBNull.Value : reader.GetString(3), reader.GetString(4)));
            }
        }

        if (rows.Count == 0) return;
        string? Map(string id)
        {
            if (id.StartsWith('@'))
            {
                return PreviousTranslationMatcher.TryAttachFormId(id, toMasters, out var attached) ? attached : id;
            }

            // "player", "" (the player), "*" (anyone) and "00000000" (another character) name no record.
            if (id.Length != 8 || id == "00000000"
                || !uint.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            {
                return id;
            }

            return PreviousTranslationMatcher.TryMapFormId(id, fromMasters, toMasters, out var mapped)
                ? mapped
                : PreviousTranslationMatcher.DetachFormId(id, fromMasters);
        }

        await using (var clear = _connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM SpeakerStyle;";
            await clear.ExecuteNonQueryAsync(ct);
        }

        await using var insert = _connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = "INSERT OR REPLACE INTO SpeakerStyle (Speaker, Listener, Level, Note, UpdatedAt) VALUES ($Speaker, $Listener, $Level, $Note, $UpdatedAt);";
        var speaker = insert.Parameters.Add("$Speaker", SqliteType.Text);
        var listener = insert.Parameters.Add("$Listener", SqliteType.Text);
        var level = insert.Parameters.Add("$Level", SqliteType.Text);
        var note = insert.Parameters.Add("$Note", SqliteType.Text);
        var updatedAt = insert.Parameters.Add("$UpdatedAt", SqliteType.Text);
        foreach (var row in rows)
        {
            // A plugin's own record past 0xFF masters has no number left: the style is dropped, not put on another NPC.
            if (Map(row.Speaker) is not { } newSpeaker || Map(row.Listener) is not { } newListener) continue;
            (speaker.Value, listener.Value, level.Value, note.Value, updatedAt.Value) = (newSpeaker, newListener, row.Level, row.Note, row.UpdatedAt);
            await insert.ExecuteNonQueryAsync(ct);
        }
    }
}
