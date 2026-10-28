using System;
using System.IO;
using System.Text;
using System.Threading;

namespace XTranslatorAi.App.Services;

public static class AppLog
{
    /// <summary>
    /// The log only ever grew (errors carry full stack traces, every run appends). Past this size it is moved to
    /// <see cref="PreviousPathForUser"/>, replacing the older one, so about two of these are kept.
    /// </summary>
    public const long MaxLogBytes = 5 * 1024 * 1024;

    private static readonly string LogPath = ResolveLogPath();
    private static readonly object Sync = new();

    public static string PathForUser => LogPath;

    public static string PreviousPathForUser => System.IO.Path.ChangeExtension(LogPath, ".old.log");

    public static void Write(string message)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}{Environment.NewLine}";
        lock (Sync)
        {
            // The window and an agent process write here at once, each holding the file for its line: the line of the one
            // that found it held was lost with its error swallowed (a WARN that was the only trace of a skipped dictionary
            // or global DB). The other's moment is waited out, up to a second.
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    RotateIfFull();
                    File.AppendAllText(LogPath, line, Encoding.UTF8);
                    return;
                }
                // Only the other's hold (a sharing or lock violation) passes: a folder gone or a full disk does not, and
                // waiting on it held every line, on the window's thread too, a second each.
                catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33 && waited.ElapsedMilliseconds < 1000)
                {
                    Thread.Sleep(15);
                }
                catch
                {
                    return; // ignore
                }
            }
        }
    }

    private static void RotateIfFull()
    {
        var log = new FileInfo(LogPath);
        if (log.Exists && log.Length >= MaxLogBytes)
        {
            try
            {
                File.Move(LogPath, PreviousPathForUser, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The other process holds it, or app.old.log is open elsewhere: the line goes in as it is, and the move is
                // tried again on a later line.
            }
        }
    }

    public static void WriteError(string code, string operation, Exception ex)
    {
        Write($"ERROR {operation} ({code})");
        Write(ex.ToString());
    }

    /// <summary>Overrides the log folder, e.g. so test runs never write into the user's app.log.</summary>
    public const string LogDirectoryVariable = "TULLIUS_TRANSLATOR_LOG_DIR";

    private static string ResolveLogPath()
    {
        try
        {
            var dir = Environment.GetEnvironmentVariable(LogDirectoryVariable) is { Length: > 0 } custom
                ? custom
                : System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TulliusTranslator",
                    "logs"
                );
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "app.log");
        }
        catch
        {
            return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TulliusTranslator-app.log");
        }
    }
}

