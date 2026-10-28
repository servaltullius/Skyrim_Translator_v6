using XTranslatorAi.App.Services;

namespace XTranslatorAi.Tests;

/// <summary>app.log only ever grew; it is rotated by size, keeping one previous file.</summary>
public sealed class AppLogTests
{
    [Fact]
    public void Write_MovesAFullLogAsideAndStartsANewOne()
    {
        // The test run's own log folder (TestEnvironment), never the user's. Written through AppLog so other
        // tests logging at the same time share its lock.
        AppLog.Write(new string('x', (int)AppLog.MaxLogBytes));

        AppLog.Write("after rotation");

        Assert.True(new FileInfo(AppLog.PreviousPathForUser).Length >= AppLog.MaxLogBytes);
        var current = new FileInfo(AppLog.PathForUser);
        Assert.True(current.Length < AppLog.MaxLogBytes / 5, $"{current.Length} bytes");
        Assert.Contains("after rotation", ReadShared(AppLog.PathForUser));
    }

    // The window and an agent process write the same log: the one that found it held lost its line.
    [Fact]
    public async Task Write_WaitsOutAnotherProcessHoldingTheLog()
    {
        AppLog.Write("before the hold");
        var marker = $"held-{Guid.NewGuid():N}";
        // Another test class may be writing this very moment.
        FileStream? held = null;
        for (var attempt = 0; held == null; attempt++)
        {
            try { held = new FileStream(AppLog.PathForUser, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { Thread.Sleep(10); }
        }

        var writer = Task.Run(() => AppLog.Write(marker));
        Thread.Sleep(50);
        held.Dispose();

        await writer;
        Assert.Contains(marker, ReadShared(AppLog.PathForUser));
    }

    // Other tests may be appending right now.
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
