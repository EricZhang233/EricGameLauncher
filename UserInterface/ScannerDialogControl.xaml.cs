using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;

namespace EricGameLauncher;

public sealed partial class ScannerDialogControl : ContentDialog
{
    private readonly ObservableCollection<ScannedGame> _newGames = new();
    private readonly ObservableCollection<ScannedGame> _existingGames = new();
    private readonly ObservableCollection<ScannedGame> _invalidGames = new();

    public event Action<IReadOnlyList<ScannedGame>>? ImportRequested;
    public event Action<IReadOnlyList<ScannedGame>>? DeleteInvalidRequested;

    public ScannerDialogControl()
    {
        InitializeComponent();
        ScannerNewGamesList.ItemsSource = _newGames;
        ScannerExistingGamesList.ItemsSource = _existingGames;
        ScannerInvalidGamesList.ItemsSource = _invalidGames;
        ApplyLocalization();
    }

    public void ApplyLocalization()
    {
        ScannerDialogTitle.Text = Text.T("Scanner_Title");
        ToolTipService.SetToolTip(ScannerDialogCloseBtn, Text.T("Property_Close"));
        ScannerImportSelectedBtn.Content = Text.T("Scanner_ImportSelected");
        ScannerNewSelectAllBtn.Content = IsAllSelected(ScannerNewGamesList) ? Text.T("Scanner_DeselectAll") : Text.T("Scanner_SelectAll");
        ScannerInvalidSelectAllBtn.Content = IsAllSelected(ScannerInvalidGamesList) ? Text.T("Scanner_DeselectAll") : Text.T("Scanner_SelectAll");
        ScannerLoadingText.Text = Text.T("Scanner_Loading");
        ScannerDescriptionText.Text = Text.T("Scanner_Description");
    }

    public void ShowLoading()
    {
        ScannerResultPanel.Visibility = Visibility.Collapsed;
        ScannerLoadingPanel.Visibility = Visibility.Visible;
        _newGames.Clear();
        _existingGames.Clear();
        _invalidGames.Clear();
    }

    public void ShowResults(IEnumerable<ScannedGame> newGames, IEnumerable<ScannedGame> existingGames, IEnumerable<ScannedGame> invalidGames)
    {
        _newGames.Clear();
        _existingGames.Clear();
        _invalidGames.Clear();
        foreach (var game in newGames) _newGames.Add(game);
        foreach (var game in existingGames) _existingGames.Add(game);
        foreach (var game in invalidGames) _invalidGames.Add(game);

        ScannerNewGamesHeader.Text = string.Format(Text.T("Scanner_NewGames"), _newGames.Count);
        ScannerExistingGamesHeader.Text = string.Format(Text.T("Scanner_ExistingGames"), _existingGames.Count);
        ScannerInvalidGamesHeader.Text = string.Format(Text.T("Scanner_InvalidGames"), _invalidGames.Count);
        ScannerDeleteInvalidBtn.Content = Text.T("Scanner_DeleteInvalid");

        ScannerNewGamesSection.Visibility = _newGames.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ScannerExistingGamesSection.Visibility = _existingGames.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ScannerInvalidGamesSection.Visibility = _invalidGames.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        ScannerLoadingPanel.Visibility = Visibility.Collapsed;
        ScannerResultPanel.Visibility = Visibility.Visible;
        LogService.Write("Scan", $"Scanner dialog results new={_newGames.Count} existing={_existingGames.Count} invalid={_invalidGames.Count}");
    }

    private static bool IsAllSelected(ListView list) =>
        list.Items.Count > 0 && list.SelectedItems.Count == list.Items.Count;

    private void ScannerDialogCloseBtn_Click(object sender, RoutedEventArgs e) => Hide();

    private void ScannerImportSelectedBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedItems = ScannerNewGamesList.SelectedItems.Cast<ScannedGame>().ToList();
            if (selectedItems.Count == 0) return;

            ImportRequested?.Invoke(selectedItems);

            foreach (var item in selectedItems) _newGames.Remove(item);
            ScannerNewGamesHeader.Text = string.Format(Text.T("Scanner_NewGames"), _newGames.Count);
            if (_newGames.Count == 0) ScannerNewGamesSection.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { LogService.Write("Scan", "Scanner import selected failed", ex); }
    }

    private void ScannerDeleteInvalidBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedInvalid = ScannerInvalidGamesList.SelectedItems.Cast<ScannedGame>().ToList();
            if (selectedInvalid.Count == 0) return;

            DeleteInvalidRequested?.Invoke(selectedInvalid);

            foreach (var item in selectedInvalid) _invalidGames.Remove(item);
            ScannerInvalidGamesHeader.Text = string.Format(Text.T("Scanner_InvalidGames"), _invalidGames.Count);
            if (_invalidGames.Count == 0) ScannerInvalidGamesSection.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { LogService.Write("Scan", "Scanner delete invalid failed", ex); }
    }

    private void ScannerNewGamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ScannerNewSelectAllBtn.Content = IsAllSelected(ScannerNewGamesList) ? Text.T("Scanner_DeselectAll") : Text.T("Scanner_SelectAll");
    }

    private void ScannerNewSelectAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsAllSelected(ScannerNewGamesList)) ScannerNewGamesList.SelectedItems.Clear();
        else ScannerNewGamesList.SelectAll();
    }

    private void ScannerInvalidGamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ScannerInvalidSelectAllBtn.Content = IsAllSelected(ScannerInvalidGamesList) ? Text.T("Scanner_DeselectAll") : Text.T("Scanner_SelectAll");
    }

    private void ScannerInvalidSelectAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsAllSelected(ScannerInvalidGamesList)) ScannerInvalidGamesList.SelectedItems.Clear();
        else ScannerInvalidGamesList.SelectAll();
    }
}
