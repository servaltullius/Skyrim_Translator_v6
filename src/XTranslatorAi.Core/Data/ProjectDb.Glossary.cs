using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Data;

public readonly record struct GlossaryUpsertRequest(
    string? Category,
    string SourceTerm,
    string TargetTerm,
    bool Enabled,
    int Priority,
    GlossaryMatchMode MatchMode,
    GlossaryForceMode ForceMode,
    string? Note
);

public sealed partial class ProjectDb
{
    private readonly record struct BulkGlossaryInsertCommand(
        SqliteParameter Category,
        SqliteParameter SourceTerm,
        SqliteParameter TargetTerm,
        SqliteParameter Enabled,
        SqliteParameter MatchMode,
        SqliteParameter ForceMode,
        SqliteParameter Priority,
        SqliteParameter Note
    );

    private readonly record struct BulkGlossaryUpdateCommand(
        SqliteParameter Id,
        SqliteParameter Category,
        SqliteParameter SourceTerm,
        SqliteParameter TargetTerm,
        SqliteParameter Enabled,
        SqliteParameter MatchMode,
        SqliteParameter ForceMode,
        SqliteParameter Priority,
        SqliteParameter Note
    );

    public async Task<IReadOnlyList<GlossaryEntry>> GetGlossaryAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = new List<GlossaryEntry>();
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                SELECT Id, Category, SrcTerm, DstTerm, Enabled, MatchMode, ForceMode, Priority, Note
                FROM Glossary
                ORDER BY Priority DESC, LENGTH(SrcTerm) DESC, Id ASC;
                """;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(
                    new GlossaryEntry(
                        Id: reader.GetInt64(0),
                        Category: reader.IsDBNull(1) ? null : reader.GetString(1),
                        SourceTerm: reader.GetString(2),
                        TargetTerm: reader.GetString(3),
                        Enabled: reader.GetInt32(4) != 0,
                        MatchMode: (GlossaryMatchMode)reader.GetInt32(5),
                        ForceMode: (GlossaryForceMode)reader.GetInt32(6),
                        Priority: reader.GetInt32(7),
                        Note: reader.IsDBNull(8) ? null : reader.GetString(8)
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

    public async Task UpsertGlossaryAsync(GlossaryUpsertRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO Glossary (Category, SrcTerm, DstTerm, Enabled, MatchMode, ForceMode, Priority, Note)
                VALUES ($Category, $SrcTerm, $DstTerm, $Enabled, $MatchMode, $ForceMode, $Priority, $Note);
                """;

            cmd.Parameters.AddWithValue("$Category", (object?)request.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$SrcTerm", request.SourceTerm);
            cmd.Parameters.AddWithValue("$DstTerm", request.TargetTerm);
            cmd.Parameters.AddWithValue("$Enabled", request.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$MatchMode", (int)request.MatchMode);
            cmd.Parameters.AddWithValue("$ForceMode", (int)request.ForceMode);
            cmd.Parameters.AddWithValue("$Priority", request.Priority);
            cmd.Parameters.AddWithValue("$Note", (object?)request.Note ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TryInsertGlossaryIfMissingAsync(GlossaryUpsertRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO Glossary (Category, SrcTerm, DstTerm, Enabled, MatchMode, ForceMode, Priority, Note)
                SELECT $Category, $SrcTerm, $DstTerm, $Enabled, $MatchMode, $ForceMode, $Priority, $Note
                WHERE NOT EXISTS (
                  SELECT 1 FROM Glossary WHERE LOWER(SrcTerm) = LOWER($SrcTerm)
                );
                """;

            cmd.Parameters.AddWithValue("$Category", (object?)request.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$SrcTerm", request.SourceTerm);
            cmd.Parameters.AddWithValue("$DstTerm", request.TargetTerm);
            cmd.Parameters.AddWithValue("$Enabled", request.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$MatchMode", (int)request.MatchMode);
            cmd.Parameters.AddWithValue("$ForceMode", (int)request.ForceMode);
            cmd.Parameters.AddWithValue("$Priority", request.Priority);
            cmd.Parameters.AddWithValue("$Note", (object?)request.Note ?? DBNull.Value);

            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            return rows > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task BulkInsertGlossaryAsync(
        IEnumerable<(string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note)> rows,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using var cmd = _connection.CreateCommand();
            var ps = ConfigureBulkInsertGlossaryCommand(cmd, tx);

            foreach (var row in rows)
            {
                ps.Category.Value = (object?)row.Category ?? DBNull.Value;
                ps.SourceTerm.Value = row.SourceTerm;
                ps.TargetTerm.Value = row.TargetTerm;
                ps.Enabled.Value = row.Enabled ? 1 : 0;
                ps.MatchMode.Value = row.MatchMode;
                ps.ForceMode.Value = row.ForceMode;
                ps.Priority.Value = row.Priority;
                ps.Note.Value = (object?)row.Note ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static BulkGlossaryInsertCommand ConfigureBulkInsertGlossaryCommand(SqliteCommand cmd, SqliteTransaction tx)
    {
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO Glossary (Category, SrcTerm, DstTerm, Enabled, MatchMode, ForceMode, Priority, Note)
            VALUES ($Category, $SrcTerm, $DstTerm, $Enabled, $MatchMode, $ForceMode, $Priority, $Note);
            """;

        return new BulkGlossaryInsertCommand(
            Category: CreateParameter(cmd, "$Category"),
            SourceTerm: CreateParameter(cmd, "$SrcTerm"),
            TargetTerm: CreateParameter(cmd, "$DstTerm"),
            Enabled: CreateParameter(cmd, "$Enabled"),
            MatchMode: CreateParameter(cmd, "$MatchMode"),
            ForceMode: CreateParameter(cmd, "$ForceMode"),
            Priority: CreateParameter(cmd, "$Priority"),
            Note: CreateParameter(cmd, "$Note")
        );
    }

    private static SqliteParameter CreateParameter(SqliteCommand cmd, string name)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        cmd.Parameters.Add(p);
        return p;
    }

    public async Task BulkUpdateGlossaryAsync(
        IEnumerable<(long Id, string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note)> rows,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            var p = ConfigureBulkUpdateGlossaryCommand(cmd);
            await ExecuteBulkUpdateGlossaryAsync(cmd, p, rows, cancellationToken);

            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static BulkGlossaryUpdateCommand ConfigureBulkUpdateGlossaryCommand(SqliteCommand cmd)
    {
        cmd.CommandText =
            """
            UPDATE Glossary
            SET Category=$Category,
                SrcTerm=$SrcTerm,
                DstTerm=$DstTerm,
                Enabled=$Enabled,
                MatchMode=$MatchMode,
                ForceMode=$ForceMode,
                Priority=$Priority,
                Note=$Note
            WHERE Id=$Id;
            """;

        return new BulkGlossaryUpdateCommand(
            Id: CreateParameter(cmd, "$Id"),
            Category: CreateParameter(cmd, "$Category"),
            SourceTerm: CreateParameter(cmd, "$SrcTerm"),
            TargetTerm: CreateParameter(cmd, "$DstTerm"),
            Enabled: CreateParameter(cmd, "$Enabled"),
            MatchMode: CreateParameter(cmd, "$MatchMode"),
            ForceMode: CreateParameter(cmd, "$ForceMode"),
            Priority: CreateParameter(cmd, "$Priority"),
            Note: CreateParameter(cmd, "$Note")
        );
    }

    private static async Task ExecuteBulkUpdateGlossaryAsync(
        SqliteCommand cmd,
        BulkGlossaryUpdateCommand p,
        IEnumerable<(long Id, string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note)> rows,
        CancellationToken cancellationToken
    )
    {
        foreach (var row in rows)
        {
            p.Id.Value = row.Id;
            p.Category.Value = (object?)row.Category ?? DBNull.Value;
            p.SourceTerm.Value = row.SourceTerm;
            p.TargetTerm.Value = row.TargetTerm;
            p.Enabled.Value = row.Enabled ? 1 : 0;
            p.MatchMode.Value = row.MatchMode;
            p.ForceMode.Value = row.ForceMode;
            p.Priority.Value = row.Priority;
            p.Note.Value = (object?)row.Note ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task DeleteGlossaryEntryAsync(long id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Glossary WHERE Id=$Id;";
            cmd.Parameters.AddWithValue("$Id", id);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
