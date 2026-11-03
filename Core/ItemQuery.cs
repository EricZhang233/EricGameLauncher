using System.Text.Json.Serialization;

namespace EricGameLauncher;

public sealed record ItemQuery
{
    public string Id { get; init; } = "";
    public string? Title { get; init; }
    public string? ExePath { get; init; }
    public bool IsAdmin { get; init; }
    public string? Platform { get; init; }
    public string? PlatformName { get; init; }
    public bool IsPlatform { get; init; }
    public bool Pin { get; init; }
    public long LaunchCount { get; init; }
    public int SortOrder { get; init; }
    public string? IconPath { get; init; }
    public string? RecycleId { get; init; }
    public string? MgrPath { get; init; }
    public bool IsMgrAdmin { get; init; }
    public string? AlternativeLaunchCommand { get; init; }
    public bool UseAlternativeLaunch { get; init; }
    public bool IsAltAdmin { get; init; }
    public string? AlongsideCommand { get; init; }
    public bool RunAlongside { get; init; }
    public bool IsAlongsideAdmin { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    [JsonPropertyName("deletionDeadline")]
    public DateTimeOffset? DeletionDeadline { get; init; }
    [JsonPropertyName("status")]
    public string Status { get; init; } = "normal";
    [JsonPropertyName("customMenuItems")]
    public IReadOnlyList<CustomMenuItem> CustomMenuItems { get; init; } = [];

    public static ItemQuery FromItem(AppItem item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        ExePath = item.ExePath,
        IsAdmin = item.IsAdmin,
        Platform = item.Platform,
        PlatformName = item.PlatformName,
        IsPlatform = item.IsPlatform,
        Pin = item.Pin,
        LaunchCount = item.LaunchCount,
        SortOrder = item.SortOrder,
        IconPath = item.IconPath,
        RecycleId = item.RecycleId,
        MgrPath = item.MgrPath,
        IsMgrAdmin = item.IsMgrAdmin,
        AlternativeLaunchCommand = item.AlternativeLaunchCommand,
        UseAlternativeLaunch = item.UseAlternativeLaunch,
        IsAltAdmin = item.IsAltAdmin,
        AlongsideCommand = item.AlongsideCommand,
        RunAlongside = item.RunAlongside,
        IsAlongsideAdmin = item.IsAlongsideAdmin,
        DeletedAt = item.DeletedAt,
        DeletionDeadline = item.DeletionDeadline,
        Status = item.Status switch { 0 => "normal", 1 => "recycled", 2 => "pending_deletion", _ => "unknown" },
        CustomMenuItems = item.CustomMenuItems.Select(x => new CustomMenuItem { Title = x.Title, Command = x.Command, IsAdmin = x.IsAdmin }).ToArray()
    };
}
