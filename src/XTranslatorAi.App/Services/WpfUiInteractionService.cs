using System;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows;

namespace XTranslatorAi.App.Services;

public sealed class WpfUiInteractionService : IUiInteractionService
{
    public UiMessageBoxResult ShowMessage(
        string message,
        string title,
        UiMessageBoxButton button = UiMessageBoxButton.Ok,
        UiMessageBoxImage image = UiMessageBoxImage.None,
        UiMessageBoxResult defaultResult = UiMessageBoxResult.None
    )
    {
        var result = MessageBox.Show(
            message ?? "",
            title ?? "",
            ToWpfButton(button),
            ToWpfImage(image),
            ToWpfResult(defaultResult)
        );

        return ToUiResult(result);
    }

    public string? ShowOpenFileDialog(OpenFileDialogRequest request)
    {
        var dialog = new OpenFileDialog
        {
            Filter = request.Filter,
            Title = request.Title,
            FileName = request.FileName ?? "",
        };

        if (!string.IsNullOrWhiteSpace(request.InitialDirectory))
        {
            dialog.InitialDirectory = request.InitialDirectory;
        }

        return dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName)
            ? dialog.FileName
            : null;
    }

    public string? ShowSaveFileDialog(SaveFileDialogRequest request)
    {
        var dialog = new SaveFileDialog
        {
            Filter = request.Filter,
            Title = request.Title,
            FileName = request.FileName ?? "",
        };

        if (!string.IsNullOrWhiteSpace(request.InitialDirectory))
        {
            dialog.InitialDirectory = request.InitialDirectory;
        }

        return dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName)
            ? dialog.FileName
            : null;
    }

    public bool TryOpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true,
                }
            );
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static MessageBoxButton ToWpfButton(UiMessageBoxButton button) => button switch
    {
        UiMessageBoxButton.YesNo => MessageBoxButton.YesNo,
        UiMessageBoxButton.YesNoCancel => MessageBoxButton.YesNoCancel,
        _ => MessageBoxButton.OK,
    };

    private static MessageBoxImage ToWpfImage(UiMessageBoxImage image) => image switch
    {
        UiMessageBoxImage.Information => MessageBoxImage.Information,
        UiMessageBoxImage.Warning => MessageBoxImage.Warning,
        UiMessageBoxImage.Question => MessageBoxImage.Question,
        UiMessageBoxImage.Error => MessageBoxImage.Error,
        _ => MessageBoxImage.None,
    };

    private static MessageBoxResult ToWpfResult(UiMessageBoxResult result) => result switch
    {
        UiMessageBoxResult.Ok => MessageBoxResult.OK,
        UiMessageBoxResult.Yes => MessageBoxResult.Yes,
        UiMessageBoxResult.No => MessageBoxResult.No,
        UiMessageBoxResult.Cancel => MessageBoxResult.Cancel,
        _ => MessageBoxResult.None,
    };

    private static UiMessageBoxResult ToUiResult(MessageBoxResult result) => result switch
    {
        MessageBoxResult.OK => UiMessageBoxResult.Ok,
        MessageBoxResult.Yes => UiMessageBoxResult.Yes,
        MessageBoxResult.No => UiMessageBoxResult.No,
        MessageBoxResult.Cancel => UiMessageBoxResult.Cancel,
        _ => UiMessageBoxResult.None,
    };
}
