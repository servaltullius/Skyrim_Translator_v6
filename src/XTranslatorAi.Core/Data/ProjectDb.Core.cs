using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private const string SchemaSql =
        """
        CREATE TABLE IF NOT EXISTS Project (
          Id INTEGER PRIMARY KEY,
          InputXmlPath TEXT NOT NULL,
          AddonName TEXT,
          Franchise TEXT,
          SourceLang TEXT NOT NULL,
          DestLang TEXT NOT NULL,
          XmlVersion TEXT NOT NULL,
          XmlHasBom INTEGER NOT NULL,
          XmlPrologLine TEXT NOT NULL,
          ModelName TEXT NOT NULL,
          BasePromptText TEXT NOT NULL,
          CustomPromptText TEXT,
          UseCustomPrompt INTEGER NOT NULL,
          CreatedAt TEXT NOT NULL,
          UpdatedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS StringEntry (
          Id INTEGER PRIMARY KEY,
          OrderIndex INTEGER NOT NULL,
          ListAttr TEXT,
          PartialAttr TEXT,
          AttributesJson TEXT,
          EDID TEXT,
          REC TEXT,
          SourceText TEXT NOT NULL,
          DestText TEXT NOT NULL,
          Status INTEGER NOT NULL,
          ErrorMessage TEXT,
          RawStringXml TEXT NOT NULL,
          UpdatedAt TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_StringEntry_OrderIndex ON StringEntry(OrderIndex);
        CREATE INDEX IF NOT EXISTS IX_StringEntry_Status ON StringEntry(Status);

        CREATE TABLE IF NOT EXISTS Glossary (
          Id INTEGER PRIMARY KEY,
          Category TEXT,
          SrcTerm TEXT NOT NULL,
          DstTerm TEXT NOT NULL,
          Enabled INTEGER NOT NULL,
          MatchMode INTEGER NOT NULL,
          ForceMode INTEGER NOT NULL,
          Priority INTEGER NOT NULL,
          Note TEXT
        );

        CREATE TABLE IF NOT EXISTS TranslationMemory (
          Id INTEGER PRIMARY KEY,
          SourceLangKey TEXT NOT NULL,
          DestLangKey TEXT NOT NULL,
          SrcKey TEXT NOT NULL,
          SrcText TEXT NOT NULL,
          DstText TEXT NOT NULL,
          UpdatedAt TEXT NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS UX_TranslationMemory_Key
          ON TranslationMemory (SourceLangKey, DestLangKey, SrcKey);

        CREATE TABLE IF NOT EXISTS NexusContext (
          Id INTEGER PRIMARY KEY,
          GameDomain TEXT NOT NULL,
          ModId INTEGER NOT NULL,
          ModUrl TEXT,
          ModName TEXT,
          Summary TEXT,
          ContextText TEXT NOT NULL,
          UpdatedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS ProjectContext (
          Id INTEGER PRIMARY KEY,
          ContextText TEXT NOT NULL,
          UpdatedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS StringNote (
          StringId INTEGER NOT NULL,
          Kind TEXT NOT NULL,
          Message TEXT NOT NULL,
          UpdatedAt TEXT NOT NULL,
          PRIMARY KEY (StringId, Kind)
        );
        """;

    private ProjectDb(SqliteConnection connection)
    {
        _connection = connection;
    }

    internal SqliteConnection Connection => _connection;

    /// @critical: Opens SQLite project DB and ensures schema.
    public static async Task<ProjectDb> OpenOrCreateAsync(string dbPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);

        var connString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        var connection = new SqliteConnection(connString);
        await connection.OpenAsync(cancellationToken);

        await ExecPragmaAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken);
        try
        {
            await ExecPragmaAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);
        }
        catch (SqliteException)
        {
            await ExecPragmaAsync(connection, "PRAGMA journal_mode=DELETE;", cancellationToken);
        }

        await ExecPragmaAsync(connection, "PRAGMA synchronous=NORMAL;", cancellationToken);
        await ExecPragmaAsync(connection, "PRAGMA temp_store=MEMORY;", cancellationToken);

        var db = new ProjectDb(connection);
        await db.EnsureSchemaAsync(cancellationToken);
        return db;
    }

    private static async Task ExecPragmaAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = SchemaSql;

            await cmd.ExecuteNonQueryAsync(cancellationToken);

            // Migration: older DBs won't have the Category column.
            await EnsureGlossaryCategoryColumnAsync(cancellationToken);

            // Migration: older DBs won't have the Franchise column.
            await EnsureProjectFranchiseColumnAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureGlossaryCategoryColumnAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var check = _connection.CreateCommand();
            check.CommandText = "PRAGMA table_info(Glossary);";
            await using var reader = await check.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "Category", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            await using var alter = _connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Glossary ADD COLUMN Category TEXT;";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (
            ex.SqliteErrorCode == 1
            && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase)
        )
        {
            // Ignore.
        }
    }

    private async Task EnsureProjectFranchiseColumnAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var check = _connection.CreateCommand();
            check.CommandText = "PRAGMA table_info(Project);";
            await using var reader = await check.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "Franchise", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            await using var alter = _connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Project ADD COLUMN Franchise TEXT;";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (
            ex.SqliteErrorCode == 1
            && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase)
        )
        {
            // Ignore.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            await _connection.DisposeAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    public static string ToAttributesJson(Dictionary<string, string> attrs)
        => JsonSerializer.Serialize(attrs, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
