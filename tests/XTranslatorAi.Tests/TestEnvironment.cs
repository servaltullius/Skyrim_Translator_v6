using System;
using System.IO;
using System.Runtime.CompilerServices;
using XTranslatorAi.App.Services;

namespace XTranslatorAi.Tests;

internal static class TestEnvironment
{
    /// <summary>
    /// How long a UI test (run on its own STA thread) may take before it fails as hung. It was 20 seconds: on a CI runner
    /// that restored packages about eight times and built about five times slower than usual, twelve UI tests that take
    /// a tenth of a second here failed at 20 seconds one after another, and passed when the run was repeated. Why they
    /// took that long is not known (a slow stretch of the machine, or a stall); a slow stretch now passes, and a hang
    /// still fails, a minute later.
    /// </summary>
    internal static readonly TimeSpan UiTestLimit = TimeSpan.FromSeconds(60);

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

    /// <summary>
    /// The agent commands the tests run read the user's settings.json, and refused to write while the user's translator
    /// window was open: a test passed or failed by the machine. They get default settings (a file that is never written)
    /// and do not look for the window.
    /// </summary>
    [ModuleInitializer]
    internal static void IsolateAgentCommands()
    {
        XTranslatorAi.App.Services.AgentMode.AgentCommand.SettingsPathForTests =
            Path.Combine(Path.GetTempPath(), "xt-tests-settings", Guid.NewGuid().ToString("N"), "settings.json");
        XTranslatorAi.App.Services.AgentMode.AgentCommand.IgnoreRunningAppForTests = true;
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
