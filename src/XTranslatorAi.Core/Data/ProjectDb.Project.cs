using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    private const string SelectProjectSql =
        """
        SELECT
          Id,
          InputXmlPath,
          AddonName,
          Franchise,
          SourceLang,
          DestLang,
          XmlVersion,
          XmlHasBom,
          XmlPrologLine,
          ModelName,
          BasePromptText,
          CustomPromptText,
          UseCustomPrompt,
          CreatedAt,
          UpdatedAt
        FROM Project
        WHERE Id = 1
        LIMIT 1;
        """;

    private const string UpsertProjectSql =
        """
        INSERT INTO Project (
          Id, InputXmlPath, AddonName, Franchise, SourceLang, DestLang, XmlVersion,
          XmlHasBom, XmlPrologLine, ModelName, BasePromptText, CustomPromptText, UseCustomPrompt,
          CreatedAt, UpdatedAt
        ) VALUES (
          1, $InputXmlPath, $AddonName, $Franchise, $SourceLang, $DestLang, $XmlVersion,
          $XmlHasBom, $XmlPrologLine, $ModelName, $BasePromptText, $CustomPromptText, $UseCustomPrompt,
          COALESCE((SELECT CreatedAt FROM Project WHERE Id=1), $CreatedAt),
          $UpdatedAt
        )
        ON CONFLICT(Id) DO UPDATE SET
          InputXmlPath=$InputXmlPath,
          AddonName=$AddonName,
          Franchise=$Franchise,
          SourceLang=$SourceLang,
          DestLang=$DestLang,
          XmlVersion=$XmlVersion,
          XmlHasBom=$XmlHasBom,
          XmlPrologLine=$XmlPrologLine,
          ModelName=$ModelName,
          BasePromptText=$BasePromptText,
          CustomPromptText=$CustomPromptText,
          UseCustomPrompt=$UseCustomPrompt,
          UpdatedAt=$UpdatedAt
        ;
        """;

    public async Task<ProjectInfo?> TryGetProjectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = SelectProjectSql;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadProjectInfo(reader);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ProjectInfo ReadProjectInfo(SqliteDataReader reader)
    {
        BethesdaFranchise? franchise = null;
        if (!reader.IsDBNull(3))
        {
            var raw = reader.GetString(3);
            if (Enum.TryParse<BethesdaFranchise>(raw, ignoreCase: true, out var parsed))
            {
                franchise = parsed;
            }
        }

        return new ProjectInfo(
            Id: reader.GetInt64(0),
            InputXmlPath: reader.GetString(1),
            AddonName: reader.IsDBNull(2) ? null : reader.GetString(2),
            Franchise: franchise,
            SourceLang: reader.GetString(4),
            DestLang: reader.GetString(5),
            XmlVersion: reader.GetString(6),
            XmlHasBom: reader.GetInt64(7) != 0,
            XmlPrologLine: reader.GetString(8),
            ModelName: reader.GetString(9),
            BasePromptText: reader.GetString(10),
            CustomPromptText: reader.IsDBNull(11) ? null : reader.GetString(11),
            UseCustomPrompt: reader.GetInt64(12) != 0,
            CreatedAt: ReadUtcTimestamp(reader, 13),
            UpdatedAt: ReadUtcTimestamp(reader, 14)
        );
    }

    private static DateTimeOffset ReadUtcTimestamp(SqliteDataReader reader, int ordinal)
        => DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public async Task<long> UpsertProjectAsync(ProjectInfo project, CancellationToken cancellationToken)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            ConfigureUpsertProjectCommand(cmd, project, updatedAt);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return 1;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ConfigureUpsertProjectCommand(SqliteCommand cmd, ProjectInfo project, DateTimeOffset updatedAt)
    {
        cmd.CommandText = UpsertProjectSql;
        cmd.Parameters.AddWithValue("$InputXmlPath", project.InputXmlPath);
        cmd.Parameters.AddWithValue("$AddonName", (object?)project.AddonName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$Franchise", (object?)project.Franchise?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$SourceLang", project.SourceLang);
        cmd.Parameters.AddWithValue("$DestLang", project.DestLang);
        cmd.Parameters.AddWithValue("$XmlVersion", project.XmlVersion);
        cmd.Parameters.AddWithValue("$XmlHasBom", project.XmlHasBom ? 1 : 0);
        cmd.Parameters.AddWithValue("$XmlPrologLine", project.XmlPrologLine);
        cmd.Parameters.AddWithValue("$ModelName", project.ModelName);
        cmd.Parameters.AddWithValue("$BasePromptText", project.BasePromptText);
        cmd.Parameters.AddWithValue("$CustomPromptText", (object?)project.CustomPromptText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$UseCustomPrompt", project.UseCustomPrompt ? 1 : 0);
        cmd.Parameters.AddWithValue("$CreatedAt", project.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$UpdatedAt", updatedAt.ToString("O"));
    }
}
