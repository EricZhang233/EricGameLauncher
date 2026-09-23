using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace EricGameLauncher;

public sealed partial class SearchFlyoutControl : UserControl
{
    public event Action<AutoSuggestBox, AutoSuggestBoxTextChangedEventArgs>? QueryChanged;
    public Button Button => SearchButton;
    public string Text => SearchBox.Text ?? string.Empty;

    public SearchFlyoutControl()
    {
        InitializeComponent();
    }

    public void ApplyLocalization()
    {
        ToolTipService.SetToolTip(SearchButton, TextResource("TitleBar_Search"));
        SearchBox.PlaceholderText = TextResource("TitleBar_SearchPlaceholder");
    }

    public void FocusInput()
    {
        SearchBox.Focus(FocusState.Programmatic);
    }

    private string TextResource(string key) => EricGameLauncher.Text.T(key);

    private void SearchFlyout_Opened(object sender, object e)
    {
        FocusInput();
    }

    private void SearchFlyout_Closing(FlyoutBase sender, FlyoutBaseClosingEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(SearchBox.Text)) args.Cancel = true;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        QueryChanged?.Invoke(sender, args);
    }
}
