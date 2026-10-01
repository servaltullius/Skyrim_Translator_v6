using System;
using System.IO;
using System.Runtime.CompilerServices;
using XTranslatorAi.App.Services;

namespace XTranslatorAi.Tests;

internal static class TestEnvironment
{
    /// <summary>
    /// Error-path tests log through <see cref="AppLog"/>. Without this, every test run appended
    /// fake plugin and SQLite errors to the user's real %LOCALAPPDATA%\TulliusTranslator\logs\app.log.
    /// </summary>
    [ModuleInitializer]
    internal static void RedirectAppLog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xt-tests-logs");
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable(AppLog.LogDirectoryVariable, dir);
    }
}

public class TestEnvironmentTests
{
    [Xunit.Fact]
    public void AppLog_WritesOutsideTheUsersAppData()
    {
        var userLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TulliusTranslator");
        Xunit.Assert.False(AppLog.PathForUser.StartsWith(userLogs, StringComparison.OrdinalIgnoreCase), AppLog.PathForUser);
    }
}
