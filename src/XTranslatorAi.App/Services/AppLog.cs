using System;
using System.IO;
using System.Text;

namespace XTranslatorAi.App.Services;

public static class AppLog
{
    private static readonly string LogPath = ResolveLogPath();

    public static string PathForUser => LogPath;

    public static void Write(string message)
    {
        try
        {
            File.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}{Environment.NewLine}",
                Encoding.UTF8
            );
        }
        catch
        {
            // ignore
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

