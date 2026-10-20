using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace EricGameLauncher;

public sealed partial class AnnouncementFlyoutControl : UserControl
{
    private readonly AnnouncementListState _state = new();
    private ObservableCollection<AnnouncementListItem> _items => _state.Items;

    public AnnouncementFlyoutControl()
    {
        InitializeComponent();
        AnnouncementsListView.ItemsSource = _items;
    }

    public void ApplyLocalization()
    {
        AnnouncementsTitleText.Text = Text.T("Announcements_Title");
        AnnouncementsEmptyText.Text = Text.T("Announcements_None");
        ToolTipService.SetToolTip(AnnouncementButton, Text.T("Menu_Announcements"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AnnouncementButton, Text.T("Menu_Announcements"));
    }

    public void Refresh()
    {
        try
        {
            var activeAnnouncements = ServerConfigManager.GetActiveAnnouncements();
            bool changed = _state.Refresh(activeAnnouncements);
            AnnouncementsListView.Visibility = _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AnnouncementsEmptyText.Visibility = _items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            UpdateIndicator();
            if (!changed) return;
            LogService.Write("Announcement", $"Announcement list refreshed total={_items.Count} unread={_items.Count(item => !item.IsRead)}");
        }
        catch (Exception ex) { LogService.Write("Announcement", "AnnouncementFlyoutControl refresh failed", ex); }
    }

    private void AnnouncementFlyout_Opened(object sender, object e)
    {
        Refresh();
        LogService.Write("Announcement", "Announcement flyout opened");
    }

    private void AnnouncementsListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AnnouncementListItem item) return;
        foreach (var row in _items)
            if (!ReferenceEquals(row, item)) row.IsExpanded = false;
        item.IsExpanded = !item.IsExpanded;
        if (!item.IsRead)
        {
            ServerConfigManager.MarkAsRead(item.Id, notify: false);
            item.IsRead = true;
        }
        UpdateIndicator();
        LogService.Write("Announcement", $"Announcement clicked id={item.Id}");
    }

    private void UpdateIndicator()
    {
        if (_items.Any(item => !item.IsRead))
            AnnouncementButtonIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28));
        else
            AnnouncementButtonIcon.ClearValue(IconElement.ForegroundProperty);
    }

    private void BodyRichText_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not RichTextBlock rich || rich.DataContext is not AnnouncementListItem item) return;
            rich.Blocks.Clear();
            string text = item.Body;
            var paragraph = new Paragraph();
            int lastIndex = 0;
            foreach (Match match in Regex.Matches(text, @"https?://[^\s<>]+"))
            {
                AddText(paragraph, text[lastIndex..match.Index]);
                if (Uri.TryCreate(match.Value, UriKind.Absolute, out var uri))
                {
                    var link = new Hyperlink();
                    link.Inlines.Add(new Run { Text = match.Value });
                    link.Click += async (_, _) =>
                    {
                        try { await Windows.System.Launcher.LaunchUriAsync(uri); }
                        catch (Exception ex) { LogService.Write("Announcement", "Open link failed", ex); }
                    };
                    paragraph.Inlines.Add(link);
                }
                else AddText(paragraph, match.Value);
                lastIndex = match.Index + match.Length;
            }
            AddText(paragraph, text[lastIndex..]);
            rich.Blocks.Add(paragraph);
        }
        catch (Exception ex) { LogService.Write("Announcement", "Announcement body rendering failed", ex); }
    }

    private static void AddText(Paragraph paragraph, string text)
    {
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            if (index > 0) paragraph.Inlines.Add(new LineBreak());
            if (lines[index].Length > 0) paragraph.Inlines.Add(new Run { Text = lines[index] });
        }
    }

}
