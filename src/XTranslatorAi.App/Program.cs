using System;
using System.Runtime.InteropServices;
using System.Text;
using XTranslatorAi.App.Services.AgentMode;

namespace XTranslatorAi.App;

/// <summary>
/// The translator's window, or with "agent" first the agent mode's commands (no window): one program for both, so
/// the agent kit ships no second 81MB program and the two never come from different builds.
/// </summary>
public static class Program
{
    private const int AttachParentProcess = -1;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "agent", StringComparison.OrdinalIgnoreCase))
        {
            return RunAgentCommand(args[1..]);
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    /// <summary>
    /// The window program has no console of its own. Run through agent-kit\tullius.cmd (which waits for it, as
    /// PowerShell and cmd do not for a window program), it writes to the pipe the agent app reads, or to the
    /// console it was started from.
    /// </summary>
    private static int RunAgentCommand(string[] args)
    {
        // Typed at a terminal, the output handle is none (unknown) or the console's: attach to that console. Captured
        // by an agent app it is a pipe, and attaching would send the output past it. Console.IsOutputRedirected
        // counts "none" as redirected, so it kept a typed command silent.
        var output = GetFileType(GetStdHandle(StdOutputHandle));
        if (output is FileTypeUnknown or FileTypeChar)
        {
            AttachConsole(AttachParentProcess);
        }

        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (System.IO.IOException)
        {
            // No console to set the code page of: the pipe gets the writer's UTF-8 as it is.
        }

        return AgentCli.RunAsync(args).GetAwaiter().GetResult();
    }

    private const int StdOutputHandle = -11;
    private const uint FileTypeUnknown = 0;
    private const uint FileTypeChar = 2;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr handle);
}
