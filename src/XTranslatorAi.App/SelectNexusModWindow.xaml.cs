using System.Collections.Generic;
using System.Windows;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.NexusMods;

namespace XTranslatorAi.App;

public partial class SelectNexusModWindow : Window
{
    private readonly IUiInteractionService _uiInteractionService;

    public NexusModSearchResult? SelectedMod { get; private set; }

    public SelectNexusModWindow(IReadOnlyList<NexusModSearchResult> mods, IUiInteractionService? uiInteractionService = null)
    {
        _uiInteractionService = uiInteractionService ?? new WpfUiInteractionService();
        InitializeComponent();
        ModsGrid.ItemsSource = mods ?? new List<NexusModSearchResult>();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ModsGrid.SelectedItem is not NexusModSearchResult selected)
        {
            _uiInteractionService.ShowMessage(
                "모드를 선택하세요.",
                "Select Mod",
                UiMessageBoxButton.Ok,
                UiMessageBoxImage.Information
            );
            return;
        }

        SelectedMod = selected;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
