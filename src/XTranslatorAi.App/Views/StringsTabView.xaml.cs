using System.Windows.Controls;

namespace XTranslatorAi.App.Views;

public partial class StringsTabView : UserControl
{
    public StringsTabView()
    {
        InitializeComponent();
    }

    /// <summary>Ctrl+F from the window: focus the search box and select its text.</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}
