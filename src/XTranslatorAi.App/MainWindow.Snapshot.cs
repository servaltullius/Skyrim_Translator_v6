using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace XTranslatorAi.App;

public partial class MainWindow
{
    /// <summary>
    /// Development aid for reviewing the layout (<c>--ui-snapshot DIR</c>): renders every main tab
    /// to a PNG inside the process. Screen capture tools cannot read hardware-rendered WPF windows.
    /// Nothing is opened, saved or translated.
    /// </summary>
    internal async Task SaveTabSnapshotsAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        for (var index = 0; index < MainTabs.Items.Count; index++)
        {
            MainTabs.SelectedIndex = index;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var header = (MainTabs.Items[index] as TabItem)?.Header?.ToString() ?? $"tab{index}";
            SaveSnapshot(Path.Combine(directory, $"{index:00}-{SafeFileName(header)}.png"));
        }
        MainTabs.SelectedIndex = 0;
    }

    private void SaveSnapshot(string path)
    {
        if (Content is not FrameworkElement root) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Background ?? Brushes.White, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            context.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string SafeFileName(string text)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) text = text.Replace(invalid, '_');
        return text.Replace(' ', '_');
    }
}
