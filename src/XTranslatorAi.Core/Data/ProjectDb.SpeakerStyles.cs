using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

    /// <summary>Decides a speaker's level to a listener; an empty level takes the decision back.</summary>
    public async Task SetSpeakerStyleAsync(SpeakerStyle style, CancellationToken cancellationToken)
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
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
