using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    private const int MaxIdChunkSize = 900;

    public async Task<IReadOnlyList<StringEntry>> GetStringsAsync(int limit, int offset, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = new List<StringEntry>(capacity: limit);
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                SELECT
                  Id, OrderIndex, ListAttr, PartialAttr, AttributesJson, EDID, REC,
                  SourceText, DestText, Status, ErrorMessage, UpdatedAt
                FROM StringEntry
                ORDER BY OrderIndex
                LIMIT $Limit OFFSET $Offset;
                """;

            cmd.Parameters.AddWithValue("$Limit", limit);
            cmd.Parameters.AddWithValue("$Offset", offset);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(
                    new StringEntry(
                        Id: reader.GetInt64(0),
                        OrderIndex: reader.GetInt32(1),
                        ListAttr: reader.IsDBNull(2) ? null : reader.GetString(2),
                        PartialAttr: reader.IsDBNull(3) ? null : reader.GetString(3),
                        AttributesJson: reader.IsDBNull(4) ? null : reader.GetString(4),
                        Edid: reader.IsDBNull(5) ? null : reader.GetString(5),
                        Rec: reader.IsDBNull(6) ? null : reader.GetString(6),
                        SourceText: reader.GetString(7),
                        DestText: reader.GetString(8),
                        Status: (StringEntryStatus)reader.GetInt32(9),
                        ErrorMessage: reader.IsDBNull(10) ? null : reader.GetString(10),
                        UpdatedAt: DateTimeOffset.Parse(reader.GetString(11))
                    )
                );
            }

            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<(long Id, int OrderIndex, string DestText, string RawStringXml)>> GetStringsForExportAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = new List<(long, int, string, string)>(capacity: limit);
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                SELECT
                  Id, OrderIndex, DestText, RawStringXml
                FROM StringEntry
                ORDER BY OrderIndex
                LIMIT $Limit OFFSET $Offset;
                """;

            cmd.Parameters.AddWithValue("$Limit", limit);
            cmd.Parameters.AddWithValue("$Offset", offset);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
            }

            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> GetStringCountAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM StringEntry;";
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<long>> GetStringIdsByStatusAsync(
        IReadOnlyList<StringEntryStatus> statuses,
        CancellationToken cancellationToken
    )
    {
        if (statuses.Count == 0)
        {
            return Array.Empty<long>();
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            var placeholders = new List<string>(statuses.Count);
            for (var i = 0; i < statuses.Count; i++)
            {
                var name = $"$s{i}";
                placeholders.Add(name);
                cmd.Parameters.AddWithValue(name, (int)statuses[i]);
            }

            cmd.CommandText =
                $"SELECT Id FROM StringEntry WHERE Status IN ({string.Join(",", placeholders)}) ORDER BY OrderIndex;";

            var list = new List<long>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(reader.GetInt64(0));
            }

            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<(long Id, string SourceText, StringEntryStatus Status)>> GetStringSourcesByStatusAsync(
        IReadOnlyList<StringEntryStatus> statuses,
        CancellationToken cancellationToken
    )
    {
        if (statuses.Count == 0)
        {
            return Array.Empty<(long, string, StringEntryStatus)>();
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            var placeholders = new List<string>(statuses.Count);
            for (var i = 0; i < statuses.Count; i++)
            {
                var name = $"$s{i}";
                placeholders.Add(name);
                cmd.Parameters.AddWithValue(name, (int)statuses[i]);
            }

            cmd.CommandText =
                $"SELECT Id, SourceText, Status FROM StringEntry WHERE Status IN ({string.Join(",", placeholders)}) ORDER BY OrderIndex;";

            var list = new List<(long, string, StringEntryStatus)>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add((reader.GetInt64(0), reader.GetString(1), (StringEntryStatus)reader.GetInt32(2)));
            }

            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<(long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status)>> GetStringSourceContextsByStatusAsync(
        IReadOnlyList<StringEntryStatus> statuses,
        CancellationToken cancellationToken
    )
    {
        if (statuses.Count == 0)
        {
            return Array.Empty<(long, string, string?, string?, StringEntryStatus)>();
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            var placeholders = new List<string>(statuses.Count);
            for (var i = 0; i < statuses.Count; i++)
            {
                var name = $"$s{i}";
                placeholders.Add(name);
                cmd.Parameters.AddWithValue(name, (int)statuses[i]);
            }

            cmd.CommandText =
                $"SELECT Id, SourceText, Rec, Edid, Status FROM StringEntry WHERE Status IN ({string.Join(",", placeholders)}) ORDER BY OrderIndex;";

            var list = new List<(long, string, string?, string?, StringEntryStatus)>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(
                    (
                        reader.GetInt64(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        (StringEntryStatus)reader.GetInt32(4)
                    )
                );
            }

            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(long Id, string SourceText, StringEntryStatus Status)> GetStringTranslationStateAsync(
        long id,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Id, SourceText, Status FROM StringEntry WHERE Id=$Id;";
            cmd.Parameters.AddWithValue("$Id", id);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"Missing row id={id}");
            }

            return (reader.GetInt64(0), reader.GetString(1), (StringEntryStatus)reader.GetInt32(2));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status)> GetStringTranslationContextAsync(
        long id,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Id, SourceText, REC, EDID, Status FROM StringEntry WHERE Id=$Id;";
            cmd.Parameters.AddWithValue("$Id", id);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"Missing row id={id}");
            }

            return (
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                (StringEntryStatus)reader.GetInt32(4)
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<long, (long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status)>> GetStringTranslationContextsByIdsAsync(
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken
    )
    {
        var map = new Dictionary<long, (long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status)>(capacity: ids.Count);
        if (ids.Count == 0)
        {
            return map;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            for (var offset = 0; offset < ids.Count; offset += MaxIdChunkSize)
            {
                var count = Math.Min(MaxIdChunkSize, ids.Count - offset);

                await using var cmd = _connection.CreateCommand();
                var placeholders = new List<string>(count);
                for (var i = 0; i < count; i++)
                {
                    var name = $"$id{i}";
                    placeholders.Add(name);
                    cmd.Parameters.AddWithValue(name, ids[offset + i]);
                }

                cmd.CommandText = $"SELECT Id, SourceText, REC, EDID, Status FROM StringEntry WHERE Id IN ({string.Join(",", placeholders)});";

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var id = reader.GetInt64(0);
                    map[id] = (
                        id,
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        (StringEntryStatus)reader.GetInt32(4)
                    );
                }
            }

            return map;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<long, StringEntryStatus>> GetStringStatusesByIdsAsync(
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken
    )
    {
        var map = new Dictionary<long, StringEntryStatus>(capacity: ids.Count);
        if (ids.Count == 0)
        {
            return map;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            for (var offset = 0; offset < ids.Count; offset += MaxIdChunkSize)
            {
                var count = Math.Min(MaxIdChunkSize, ids.Count - offset);

                await using var cmd = _connection.CreateCommand();
                var placeholders = new List<string>(count);
                for (var i = 0; i < count; i++)
                {
                    var name = $"$id{i}";
                    placeholders.Add(name);
                    cmd.Parameters.AddWithValue(name, ids[offset + i]);
                }

                cmd.CommandText = $"SELECT Id, Status FROM StringEntry WHERE Id IN ({string.Join(",", placeholders)});";

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    map[reader.GetInt64(0)] = (StringEntryStatus)reader.GetInt32(1);
                }
            }

            return map;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> GetRawStringXmlAsync(long id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT RawStringXml FROM StringEntry WHERE Id=$Id;";
            cmd.Parameters.AddWithValue("$Id", id);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return Convert.ToString(result) ?? throw new InvalidDataException("Missing RawStringXml.");
        }
        finally
        {
            _gate.Release();
        }
    }
}
