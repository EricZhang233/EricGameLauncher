using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI.Text;

namespace EricGameLauncher;

public sealed class AnnouncementListItem : INotifyPropertyChanged
{
    private bool _isRead;
    private bool _isExpanded;

    public Announcement Source { get; }
    public string Id { get; }
    public string Title { get; }
    public string Body { get; }
    public string TimeText { get; }
    public bool HasMarker { get; }
    public string MarkerGlyph { get; }

    public bool IsRead
    {
        get => _isRead;
        set
        {
            if (_isRead != value)
            {
                _isRead = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsUnread));
                OnPropertyChanged(nameof(ReadStatusText));
                OnPropertyChanged(nameof(TitleWeight));
            }
        }
    }

    public bool IsUnread => !IsRead;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public string ReadStatusText => IsRead ? I18n.T("Announcements_Read") : I18n.T("Announcements_Unread");
    public FontWeight TitleWeight => Microsoft.UI.Text.FontWeights.Normal;

    public event PropertyChangedEventHandler? PropertyChanged;

    private AnnouncementListItem(Announcement source, string id, string title, string body, string timeText, bool isRead, bool hasMarker, string markerGlyph)
    {
        Source = source;
        Id = id;
        Title = title;
        Body = body;
        TimeText = timeText;
        _isRead = isRead;
        _isExpanded = false;
        HasMarker = hasMarker;
        MarkerGlyph = markerGlyph;
    }

    public static AnnouncementListItem FromAnnouncement(Announcement announcement)
    {
        bool isRead = ServerConfigManager.IsRead(announcement.Id);
        string position = announcement.GetPosition();
        bool hasMarker = position == "top" || position == "bottom";
        string markerGlyph = position == "bottom" ? "\u21A7" : "\uE718";

        return new AnnouncementListItem(
            announcement,
            announcement.Id,
            announcement.GetDisplayTitle(),
            announcement.GetDisplayBody(),
            FormatTime(announcement.Time),
            isRead,
            hasMarker,
            markerGlyph
        );
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string FormatTime(string value)
    {
        if (DateTime.TryParse(value, out var parsed))
        {
            return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        return value ?? string.Empty;
    }
}
