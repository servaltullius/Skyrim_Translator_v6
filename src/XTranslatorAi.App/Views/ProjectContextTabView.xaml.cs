using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XTranslatorAi.App.ViewModels.Tabs;

namespace XTranslatorAi.App.Views;

public partial class ProjectContextTabView : UserControl
{
    private Thickness? _restingThickness;
    private Brush? _restingBrush;

    public ProjectContextTabView()
    {
        InitializeComponent();
    }

    private ProjectContextTabViewModel? Vm => DataContext as ProjectContextTabViewModel;

    // The row takes only a plugin it can link; anything else goes on to the window, which opens files as projects.
    private void PreviousTranslationDropZone_OnDragOver(object sender, DragEventArgs e)
    {
        var path = FileDrop.SingleFile(e.Data);
        if (Vm?.CanLinkDroppedPreviousTranslation(path) != true)
        {
            SetHighlight(false);
            return;
        }

        e.Effects = DragDropEffects.Link;
        e.Handled = true;
        SetHighlight(true);
    }

    private void PreviousTranslationDropZone_OnDragLeave(object sender, DragEventArgs e) => SetHighlight(false);

    private async void PreviousTranslationDropZone_OnDrop(object sender, DragEventArgs e)
    {
        SetHighlight(false);
        var vm = Vm;
        var path = FileDrop.SingleFile(e.Data);
        if (vm?.CanLinkDroppedPreviousTranslation(path) != true)
        {
            return;
        }

        e.Handled = true;
        try
        {
            await vm.LinkDroppedPreviousTranslationAsync(path!);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "이전 번역 연결 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetHighlight(bool on)
    {
        _restingThickness ??= PreviousTranslationDropZone.BorderThickness;
        _restingBrush ??= PreviousTranslationDropZone.BorderBrush;
        PreviousTranslationDropZone.BorderThickness = on ? new Thickness(2) : _restingThickness.Value;
        PreviousTranslationDropZone.BorderBrush = on ? (Brush)FindResource("XT.AccentBrush") : _restingBrush;
    }
}
