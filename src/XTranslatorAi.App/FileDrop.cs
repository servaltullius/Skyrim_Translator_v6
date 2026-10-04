using System.Windows;
using System.Windows.Media;

namespace XTranslatorAi.App;

/// <summary>Reads the files of a drag-and-drop operation and marks the areas that take a file for themselves.</summary>
internal static class FileDrop
{
    /// <summary>Set on an element (the previous-translation row) whose own drop handlers take a dropped file.</summary>
    public static readonly DependencyProperty IsDropZoneProperty =
        DependencyProperty.RegisterAttached("IsDropZone", typeof(bool), typeof(FileDrop), new PropertyMetadata(false));

    public static bool GetIsDropZone(DependencyObject element) => (bool)element.GetValue(IsDropZoneProperty);

    public static void SetIsDropZone(DependencyObject element, bool value) => element.SetValue(IsDropZoneProperty, value);

    public static bool IsInsideDropZone(DependencyObject? element)
    {
        for (var current = element; current != null; current = current is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(current)
                 : LogicalTreeHelper.GetParent(current))
        {
            if (GetIsDropZone(current))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasFiles(IDataObject data) => data.GetDataPresent(DataFormats.FileDrop);

    /// <summary>The dropped file when exactly one file is dragged; null for several files or for text.</summary>
    public static string? SingleFile(IDataObject data)
        => data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files ? files[0] : null;
}
