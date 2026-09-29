using Microsoft.Data.Sqlite;

namespace XTranslatorAi.Tests.TestSupport;

internal static class TestDbHelper
{
    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    public static void TryDeleteDbFiles(string path)
    {
        TryDelete(path);
        TryDelete(path + "-wal");
        TryDelete(path + "-shm");
    }

    // Call after disposing every ProjectDb using this path. Match its exact
    // connection string so parallel tests using other databases keep their pools.
    public static void ReleaseProjectPoolAndDeleteDbFiles(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        SqliteConnection.ClearPool(connection);
        TryDeleteDbFiles(path);
    }
}
