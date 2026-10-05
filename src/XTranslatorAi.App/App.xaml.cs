using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using XTranslatorAi.App.Services;

namespace XTranslatorAi.App;

public partial class App : Application
{
    private readonly StartupLog _startupLog = StartupLog.Create();
    private readonly IUiInteractionService _uiInteractionService = new WpfUiInteractionService();

    // Two windows wrote over each other's project DBs and settings (one deleted the API keys the other saved) and paid
    // for the same rows twice, so a second start brings the running window forward instead.
    private const string SingleInstanceName = @"Local\TulliusTranslator.SingleInstance";
    private Mutex? _singleInstance;

    // Set once the main window is shown: after that an unexpected UI exception is reported and the app keeps running.
    private bool _started;
    private bool _shownRuntimeError;

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

        var snapshot = TryGetSnapshotDirectory(e.Args, out var snapshotDirectory);
        if (!snapshot && !TryClaimSingleInstance())
        {
            _startupLog.Write("Another window is already running; bringing it forward.");
            BringRunningWindowForward();
            Shutdown(0);
            return;
        }

        try
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            _startupLog.Write("Creating MainWindow...");
            var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            var appSettings = new AppSettingsStore();
            var apiCallLogs = new ApiCallLogService();
            var systemPromptBuilder = new SystemPromptBuilder();
            var builtInGlossaryService = new BuiltInGlossaryService();
            var bundledFranchiseTmSeedService = new BundledFranchiseTmSeedService();
            var globalProjectDbService = new GlobalProjectDbService(builtInGlossaryService);
            var glossaryFileService = new GlossaryFileService();
            var glossaryImportService = new GlossaryImportService(glossaryFileService);
            var projectGlossaryService = new ProjectGlossaryService(glossaryImportService);
            var globalGlossaryService = new GlobalGlossaryService(globalProjectDbService, projectGlossaryService);
            var franchiseTranslationMemoryService = new FranchiseTranslationMemoryService(globalProjectDbService);
            var projectWorkspaceService = new ProjectWorkspaceService(globalProjectDbService);
            var translationRunnerService = new TranslationRunnerService(globalProjectDbService);
            var compareTranslationService = new CompareTranslationService(projectGlossaryService);
            var services = new MainViewModelServices(
                AppSettings: appSettings,
                ApiCallLogService: apiCallLogs,
                SystemPromptBuilder: systemPromptBuilder,
                UiInteractionService: _uiInteractionService,
                BundledFranchiseTmSeedService: bundledFranchiseTmSeedService,
                GlobalProjectDbService: globalProjectDbService,
                ProjectGlossaryService: projectGlossaryService,
                GlobalGlossaryService: globalGlossaryService,
                FranchiseTranslationMemoryService: franchiseTranslationMemoryService,
                ProjectWorkspaceService: projectWorkspaceService,
                TranslationRunnerService: translationRunnerService,
                CompareTranslationService: compareTranslationService
            );
            var vm = new ViewModels.MainViewModel(httpClient, services);
            var window = new MainWindow(vm);
            MainWindow = window;

            _startupLog.Write("Showing MainWindow...");
            window.Show();
            _startupLog.Write("MainWindow shown.");
            _started = true;

            if (snapshot)
            {
                window.ContentRendered += async (_, _) =>
                {
                    try
                    {
                        await window.SaveTabSnapshotsAsync(snapshotDirectory);
                        _startupLog.Write($"UI snapshots saved: {snapshotDirectory}");
                        Shutdown(0);
                    }
                    catch (Exception ex)
                    {
                        // An automated run must not wait behind a message box.
                        _startupLog.Write(ex, "UI snapshots failed");
                        Shutdown(-1);
                    }
                };
            }
        }
        catch (Exception ex)
        {
            _startupLog.Write(ex, "Fatal exception during startup");
            TryShowFatalError(ex);
            Shutdown(-1);
        }
    }

    private static bool TryGetSnapshotDirectory(string[]? args, out string directory)
    {
        directory = "";
        if (args == null) return false;
        var index = Array.IndexOf(args, "--ui-snapshot");
        if (index < 0 || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) return false;
        directory = Path.GetFullPath(args[index + 1]);
        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private bool TryClaimSingleInstance()
    {
        try
        {
            _singleInstance = new Mutex(initiallyOwned: false, SingleInstanceName, out var createdNew);
            return createdNew;
        }
        catch (Exception ex)
        {
            // Never keep the app from starting because of the check itself.
            _startupLog.Write(ex, "Single-instance check failed");
            return true;
        }
    }

    private void BringRunningWindowForward()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            foreach (var other in Process.GetProcessesByName(current.ProcessName))
            {
                using (other)
                {
                    if (other.Id != current.Id && other.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(other.MainWindowHandle, SwRestore);
                        SetForegroundWindow(other.MainWindowHandle);
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _startupLog.Write(ex, "Could not bring the running window forward");
        }
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// Before the window is shown an exception means the app cannot start. After that, one escaping a binding, a
    /// converter or a command used to close the app as a "start" failure and lose the unsaved edit; it is logged and
    /// reported once, and the app keeps running.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _startupLog.Write(e.Exception, "DispatcherUnhandledException");
        e.Handled = true;
        if (!_started)
        {
            TryShowFatalError(e.Exception);
            Shutdown(-1);
            return;
        }

        AppLog.Write($"ERROR 처리하지 못한 화면 오류: {e.Exception.GetType().Name}: {e.Exception.Message}");
        if (_shownRuntimeError)
        {
            return;
        }

        _shownRuntimeError = true;
        try
        {
            _uiInteractionService.ShowMessage(
                "예상하지 못한 오류가 났지만 앱은 계속 실행합니다. 저장하지 않은 수정이 있다면 저장하고, 이상하면 앱을 다시 시작하세요."
                + Environment.NewLine + Environment.NewLine + $"{e.Exception.GetType().Name}: {e.Exception.Message}"
                + Environment.NewLine + $"로그: {_startupLog.LogPath}",
                "Tullius Translator - 오류",
                UiMessageBoxButton.Ok,
                UiMessageBoxImage.Warning);
        }
        catch
        {
            // Reporting must not throw again.
        }
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
            message.AppendLine("Tullius Translator를 시작하지 못했습니다.");
            message.AppendLine();
            message.AppendLine($"{ex.GetType().FullName}: {ex.Message}");
            message.AppendLine();
            message.AppendLine($"로그: {_startupLog.LogPath}");
            message.AppendLine();
            message.AppendLine(ex.ToString());

            _uiInteractionService.ShowMessage(
                message.ToString(),
                "Tullius Translator - 시작 오류",
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
