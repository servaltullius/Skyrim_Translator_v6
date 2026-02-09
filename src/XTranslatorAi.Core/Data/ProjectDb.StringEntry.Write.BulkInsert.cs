using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    public async Task BulkInsertStringsAsync(
        IEnumerable<(
            int OrderIndex,
            string? ListAttr,
            string? PartialAttr,
            string? AttributesJson,
            string? Edid,
            string? Rec,
            string SourceText,
            string DestText,
            StringEntryStatus Status,
            string RawStringXml
        )> rows,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using var cmd = CreateBulkInsertStringsCommand(_connection, tx, out var p);
            var now = DateTimeOffset.UtcNow.ToString("O");
            foreach (var row in rows)
            {
                p.BindRow(row, now);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static SqliteCommand CreateBulkInsertStringsCommand(
        SqliteConnection connection,
        SqliteTransaction tx,
        out BulkInsertStringsParameters parameters
    )
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO StringEntry (
              OrderIndex, ListAttr, PartialAttr, AttributesJson, EDID, REC,
              SourceText, DestText, Status, ErrorMessage, RawStringXml, UpdatedAt
            ) VALUES (
              $OrderIndex, $ListAttr, $PartialAttr, $AttributesJson, $EDID, $REC,
              $SourceText, $DestText, $Status, NULL, $RawStringXml, $UpdatedAt
            );
            """;

        parameters = BulkInsertStringsParameters.Create(cmd);
        return cmd;
    }

    private sealed class BulkInsertStringsParameters
    {
        private BulkInsertStringsParameters(
            SqliteParameter orderIndex,
            SqliteParameter listAttr,
            SqliteParameter partialAttr,
            SqliteParameter attributesJson,
            SqliteParameter edid,
            SqliteParameter rec,
            SqliteParameter sourceText,
            SqliteParameter destText,
            SqliteParameter status,
            SqliteParameter rawStringXml,
            SqliteParameter updatedAt
        )
        {
            OrderIndex = orderIndex;
            ListAttr = listAttr;
            PartialAttr = partialAttr;
            AttributesJson = attributesJson;
            Edid = edid;
            Rec = rec;
            SourceText = sourceText;
            DestText = destText;
            Status = status;
            RawStringXml = rawStringXml;
            UpdatedAt = updatedAt;
        }

        public SqliteParameter OrderIndex { get; }
        public SqliteParameter ListAttr { get; }
        public SqliteParameter PartialAttr { get; }
        public SqliteParameter AttributesJson { get; }
        public SqliteParameter Edid { get; }
        public SqliteParameter Rec { get; }
        public SqliteParameter SourceText { get; }
        public SqliteParameter DestText { get; }
        public SqliteParameter Status { get; }
        public SqliteParameter RawStringXml { get; }
        public SqliteParameter UpdatedAt { get; }

        public static BulkInsertStringsParameters Create(SqliteCommand cmd)
        {
            return new BulkInsertStringsParameters(
                orderIndex: AddParam(cmd, "$OrderIndex"),
                listAttr: AddParam(cmd, "$ListAttr"),
                partialAttr: AddParam(cmd, "$PartialAttr"),
                attributesJson: AddParam(cmd, "$AttributesJson"),
                edid: AddParam(cmd, "$EDID"),
                rec: AddParam(cmd, "$REC"),
                sourceText: AddParam(cmd, "$SourceText"),
                destText: AddParam(cmd, "$DestText"),
                status: AddParam(cmd, "$Status"),
                rawStringXml: AddParam(cmd, "$RawStringXml"),
                updatedAt: AddParam(cmd, "$UpdatedAt")
            );
        }

        public void BindRow(
            (
                int OrderIndex,
                string? ListAttr,
                string? PartialAttr,
                string? AttributesJson,
                string? Edid,
                string? Rec,
                string SourceText,
                string DestText,
                StringEntryStatus Status,
                string RawStringXml
            ) row,
            string updatedAt
        )
        {
            OrderIndex.Value = row.OrderIndex;
            ListAttr.Value = (object?)row.ListAttr ?? DBNull.Value;
            PartialAttr.Value = (object?)row.PartialAttr ?? DBNull.Value;
            AttributesJson.Value = (object?)row.AttributesJson ?? DBNull.Value;
            Edid.Value = (object?)row.Edid ?? DBNull.Value;
            Rec.Value = (object?)row.Rec ?? DBNull.Value;
            SourceText.Value = row.SourceText;
            DestText.Value = row.DestText ?? "";
            Status.Value = (int)row.Status;
            RawStringXml.Value = row.RawStringXml;
            UpdatedAt.Value = updatedAt;
        }

        private static SqliteParameter AddParam(SqliteCommand cmd, string name)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            cmd.Parameters.Add(p);
            return p;
        }
    }
}
