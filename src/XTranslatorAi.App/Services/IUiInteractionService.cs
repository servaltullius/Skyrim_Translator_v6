namespace XTranslatorAi.App.Services;

public enum UiMessageBoxButton
{
    Ok,
    YesNo,
    YesNoCancel,
}

public enum UiMessageBoxImage
{
    None,
    Information,
    Warning,
    Question,
    Error,
}

public enum UiMessageBoxResult
{
    None,
    Ok,
    Yes,
    No,
    Cancel,
}

public sealed record OpenFileDialogRequest(string Filter, string Title, string? FileName = null, string? InitialDirectory = null);

public sealed record SaveFileDialogRequest(string Filter, string Title, string? FileName = null, string? InitialDirectory = null);

public interface IUiInteractionService
{
    UiMessageBoxResult ShowMessage(
        string message,
        string title,
        UiMessageBoxButton button = UiMessageBoxButton.Ok,
        UiMessageBoxImage image = UiMessageBoxImage.None,
        UiMessageBoxResult defaultResult = UiMessageBoxResult.None
    );

    string? ShowOpenFileDialog(OpenFileDialogRequest request);
    string? ShowSaveFileDialog(SaveFileDialogRequest request);
    bool TryOpenFolder(string path);
}
