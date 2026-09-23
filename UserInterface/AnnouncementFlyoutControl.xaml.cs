using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace EricGameLauncher;

public sealed partial class AnnouncementFlyoutControl : UserControl
{
    private readonly ObservableCollection<AnnouncementListItem> _items = new();

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
    }

    public void Refresh()
    {
        try
        {
            var activeAnnouncements = ServerConfigManager.GetActiveAnnouncements();
            var dataKey = string.Join("|", activeAnnouncements.Select(item => item.Id));
            if (_items.Count > 0 && dataKey == string.Join("|", _items.Select(item => item.Id))) return;
            _items.Clear();
            foreach (var item in activeAnnouncements)
                _items.Add(AnnouncementListItem.FromAnnouncement(item));
            AnnouncementsListView.Visibility = _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AnnouncementsEmptyText.Visibility = _items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            UpdateIndicator();
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
        AnnouncementButtonIcon.Foreground = _items.Any(item => !item.IsRead)
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28))
            : null;
    }

    private void BodyRichText_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not RichTextBlock rich || rich.DataContext is not AnnouncementListItem item) return;
            rich.Blocks.Clear();
            string text = item.Body ?? string.Empty;
            var paragraph = new Paragraph();
            int lastIndex = 0;
            foreach (Match match in Regex.Matches(text, @"(https?://[\w\-\./?%&=#]+)"))
            {
                if (match.Index > lastIndex)
                    paragraph.Inlines.Add(new Run { Text = text.Substring(lastIndex, match.Index - lastIndex) });
                var link = new Hyperlink();
                link.Inlines.Add(new Run { Text = match.Value });
                string url = match.Value;
                link.Click += async (s, args) =>
                {
                    try { await Windows.System.Launcher.LaunchUriAsync(new Uri(url)); } catch { }
                };
                paragraph.Inlines.Add(link);
                lastIndex = match.Index + match.Length;
            }
            if (lastIndex < text.Length)
                paragraph.Inlines.Add(new Run { Text = text[lastIndex..] });
            rich.Blocks.Add(paragraph);
        }
        catch (Exception ex) { LogService.Write("Announcement", "Announcement body rendering failed", ex); }
    }
}
