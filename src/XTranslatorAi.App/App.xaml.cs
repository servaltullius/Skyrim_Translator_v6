using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using XTranslatorAi.App.Services;

namespace XTranslatorAi.App;

public partial class App : Application
{
    private readonly StartupLog _startupLog = StartupLog.Create();
    private readonly IUiInteractionService _uiInteractionService = new WpfUiInteractionService();

    public App()
    {
        _startupLog.Write("App ctor");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// @critical: App startup & DI wiring.
    protected override void OnStartup(StartupEventArgs e)
    {
        _startupLog.Write($"OnStartup args: {string.Join(" ", e.Args ?? Array.Empty<string>())}");
        _startupLog.Write($"Version: {typeof(App).Assembly.GetName().Version}");
        _startupLog.Write($"ProcessPath: {GetProcessPathForLog()}");

        base.OnStartup(e);

        try
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            _startupLog.Write("Creating MainWindow...");
            var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            var appSettings = new AppSettingsStore();
            var apiCallLogs = new ApiCallLogService();
            var systemPromptBuilder = new SystemPromptBuilder();
            var builtInGlossaryService = new BuiltInGlossaryService();
            var globalProjectDbService = new GlobalProjectDbService(builtInGlossaryService);
            var glossaryFileService = new GlossaryFileService();
            var glossaryImportService = new GlossaryImportService(glossaryFileService);
            var projectGlossaryService = new ProjectGlossaryService(glossaryImportService);
            var globalGlossaryService = new GlobalGlossaryService(globalProjectDbService, projectGlossaryService);
            var globalTranslationMemoryService = new GlobalTranslationMemoryService(globalProjectDbService);
            var projectWorkspaceService = new ProjectWorkspaceService(globalProjectDbService, builtInGlossaryService);
            var translationRunnerService = new TranslationRunnerService(globalProjectDbService);
            var compareTranslationService = new CompareTranslationService(projectGlossaryService);
            var vm = new ViewModels.MainViewModel(
                httpClient,
                appSettings,
                apiCallLogs,
                systemPromptBuilder,
                _uiInteractionService,
                globalProjectDbService,
                projectGlossaryService,
                globalGlossaryService,
                globalTranslationMemoryService,
                projectWorkspaceService,
                translationRunnerService,
                compareTranslationService
            );
            var window = new MainWindow(vm);
            MainWindow = window;

            _startupLog.Write("Showing MainWindow...");
            window.Show();
            _startupLog.Write("MainWindow shown.");
        }
        catch (Exception ex)
        {
            _startupLog.Write(ex, "Fatal exception during startup");
            TryShowFatalError(ex);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _startupLog.Write(e.Exception, "DispatcherUnhandledException");
        TryShowFatalError(e.Exception);

        e.Handled = true;
        Shutdown(-1);
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _startupLog.Write(ex, $"AppDomain.UnhandledException (IsTerminating={e.IsTerminating})");
            TryShowFatalError(ex);
        }
        else
        {
            _startupLog.Write($"AppDomain.UnhandledException (IsTerminating={e.IsTerminating}) ExceptionObject={e.ExceptionObject}");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _startupLog.Write(e.Exception, "TaskScheduler.UnobservedTaskException");
        e.SetObserved();
    }

    private void TryShowFatalError(Exception ex)
    {
        try
        {
            var message = new StringBuilder();
            message.AppendLine("Tullius Translator failed to start.");
            message.AppendLine();
            message.AppendLine($"{ex.GetType().FullName}: {ex.Message}");
            message.AppendLine();
            message.AppendLine($"Log: {_startupLog.LogPath}");
            message.AppendLine();
            message.AppendLine(ex.ToString());

            _uiInteractionService.ShowMessage(
                message.ToString(),
                "Tullius Translator - Startup Error",
                UiMessageBoxButton.Ok,
                UiMessageBoxImage.Error
            );
        }
        catch
        {
            // Ignore any UI errors while already handling a fatal failure.
        }
    }

    private static string GetProcessPathForLog()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            return p.MainModule?.FileName ?? "(unknown)";
        }
        catch
        {
            return "(unknown)";
        }
    }

    private sealed class StartupLog
    {
        public string LogPath { get; }

        private StartupLog(string logPath)
        {
            LogPath = logPath;
        }

        public static StartupLog Create()
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TulliusTranslator",
                    "logs"
                );
                Directory.CreateDirectory(dir);

                var logPath = Path.Combine(dir, "startup.log");
                return new StartupLog(logPath);
            }
            catch
            {
                return new StartupLog(Path.Combine(Path.GetTempPath(), "TulliusTranslator-startup.log"));
            }
        }

        public void Write(string message)
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
                // Ignore logging failures (avoid crashing while trying to log a crash).
            }
        }

        public void Write(Exception ex, string message)
        {
            Write($"{message}{Environment.NewLine}{ex}");
        }
    }
}

