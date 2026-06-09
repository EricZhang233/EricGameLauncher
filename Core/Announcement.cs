using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Windows.UI.Text;
using YamlDotNet.Serialization;

namespace EricGameLauncher;

public class Announcement
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "title_cn")]
    public string TitleCn { get; set; } = "";

    [YamlMember(Alias = "title_zh")]
    public string TitleZh { get; set; } = "";

    [YamlMember(Alias = "title_en")]
    public string TitleEn { get; set; } = "";

    [YamlMember(Alias = "body_cn")]
    public string BodyCn { get; set; } = "";

    [YamlMember(Alias = "body_zh")]
    public string BodyZh { get; set; } = "";

    [YamlMember(Alias = "body_en")]
    public string BodyEn { get; set; } = "";

    [YamlMember(Alias = "time")]
    public string Time { get; set; } = "";

    [YamlMember(Alias = "position")]
    public string Position { get; set; } = "";

    [YamlMember(Alias = "visible")]
    public bool Visible { get; set; } = true;

    public string GetDisplayTitle()
    {
        var lang = ConfigService.Language ?? "";
        bool isZhCn = string.Equals(lang, "Zh-CN", StringComparison.OrdinalIgnoreCase) || string.Equals(lang, "zh-cn", StringComparison.OrdinalIgnoreCase);
        string zhText = FirstNonWhiteSpace(TitleCn, TitleZh);
        if (isZhCn)
        {
            return FirstNonWhiteSpace(zhText, TitleEn);
        }
        else
        {
            return FirstNonWhiteSpace(TitleEn, zhText);
        }
    }

    public string GetDisplayBody()
    {
        var lang = ConfigService.Language ?? "";
        bool isZhCn = string.Equals(lang, "Zh-CN", StringComparison.OrdinalIgnoreCase) || string.Equals(lang, "zh-cn", StringComparison.OrdinalIgnoreCase);
        string zhText = FirstNonWhiteSpace(BodyCn, BodyZh);
        if (isZhCn)
        {
            return FirstNonWhiteSpace(zhText, BodyEn);
        }
        else
        {
            return FirstNonWhiteSpace(BodyEn, zhText);
        }
    }

    public string GetPosition()
    {
        string value = (Position ?? "").Trim().ToLowerInvariant();
        if (value == "top" || value == "bottom" || value == "normal")
        {
            return value;
        }

        return "normal";
    }

    public int GetPositionPriority()
    {
        string value = GetPosition();
        if (value == "top") return 0;
        if (value == "bottom") return 2;
        return 1;
    }

    public DateTimeOffset? GetTimeValue()
    {
        if (DateTimeOffset.TryParse(Time, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    private static string FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }
}

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
                OnPropertyChanged(nameof(TitleMaxLines));
                OnPropertyChanged(nameof(TitleTextTrimming));
            }
        }
    }

    public int TitleMaxLines => IsExpanded ? int.MaxValue : 1;
    public TextTrimming TitleTextTrimming => IsExpanded ? TextTrimming.None : TextTrimming.CharacterEllipsis;

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
