using System.Text.Json;
using System.Text.RegularExpressions;

namespace EricGameLauncher;

public sealed record PlatformScanResult(List<ScannedGame> Games, IReadOnlySet<string> Completed, IReadOnlyDictionary<string, string> Failures, bool IncludeLocal)
{
    public IReadOnlySet<string> Requested { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IEnumerable<string> Unavailable => Requested.Except(Completed, StringComparer.OrdinalIgnoreCase).Except(Failures.Keys, StringComparer.OrdinalIgnoreCase);
    public void RequireComplete()
    {
        if (Failures.Count > 0) throw new IOException(string.Join(Environment.NewLine, Failures.Select(x => x.Key + ": " + x.Value)));
    }
}

public static class PlatformScan
{
    public static readonly string[] Platforms = ["Steam", "Epic Games", "Xbox", "Google Play Games", "WeGame"];

    public static string Identity(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var steam = path.StartsWith(LauncherConstants.SteamProtocol, StringComparison.OrdinalIgnoreCase) ? SteamHelper.ExtractAppIdFromUrl(path) : null;
        if (steam != null) return "Steam:" + steam;
        if (path.StartsWith(LauncherConstants.EpicAppsProtocol, StringComparison.OrdinalIgnoreCase))
            return "Epic:" + Uri.UnescapeDataString(path[LauncherConstants.EpicAppsProtocol.Length..].Split('?', '#')[0]).Split(':').Last().ToUpperInvariant();
        return PathHashHelper.PathKey(path);
    }

    public static async Task<PlatformScanResult> RunAsync(IEnumerable<string>? requested = null, CancellationToken token = default,
        Func<string, Task<List<ScannedGame>>>? scan = null)
    {
        string[] scope = (requested ?? Platforms).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var games = new List<ScannedGame>();
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string platform in scope)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var found = await (scan != null ? scan(platform) : Task.Run(async () => platform switch
                {
                    "Steam" => SteamHelper.DetectSteamPath() is string root ? ReadSteam(SteamLibraries(root)) : [],
                    "Epic Games" => EpicGamesHelper.DetectEpicManifestDir() is string manifests ? ReadEpic(manifests) : [],
                    "Xbox" => await ScanService.ScanXboxAsync(),
                    "Google Play Games" => GooglePlayGamesHelper.GetInstalledGames(),
                    "WeGame" => ScanService.ScanWeGame(),
                    _ => throw new ArgumentOutOfRangeException(nameof(requested))
                }, token)).WaitAsync(token);
                games.AddRange(found);
                if (scan != null || platform != "Steam" && platform != "Epic Games" ||
                    platform == "Steam" && SteamHelper.DetectSteamPath() != null || platform == "Epic Games" && EpicGamesHelper.DetectEpicManifestDir() != null)
                    completed.Add(platform);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { failures[platform] = ex.Message; LogService.Write("Scan", platform + " incomplete", ex); }
        }
        LogService.Write("Scan", $"Requested={string.Join(',', scope)} completed={string.Join(',', completed)} failed={string.Join(',', failures.Keys)}");
        return new(games, completed, failures, requested == null) { Requested = scope.ToHashSet(StringComparer.OrdinalIgnoreCase) };
    }

    internal static IEnumerable<string> SteamLibraries(string root)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        string file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        string content = File.ReadAllText(file);
        if (!content.Contains("libraryfolders", StringComparison.OrdinalIgnoreCase) || content.Count(x => x == '{') != content.Count(x => x == '}')) throw new InvalidDataException(file);
        var paths = Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\"");
        int blocks = Regex.Matches(content, "\"[0-9]+\"\\s*\\{").Count;
        if (blocks != paths.Count) throw new InvalidDataException(file);
        foreach (Match match in paths) libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
        if (blocks == 0)
            foreach (Match match in Regex.Matches(content, "\"[0-9]+\"\\s*\"([^\"]+)\""))
            {
                string path = match.Groups[1].Value.Replace("\\\\", "\\");
                if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException(file);
                libraries.Add(path);
            }
        return libraries;
    }

    internal static List<ScannedGame> ReadSteam(IEnumerable<string> libraries)
    {
        var games = new List<ScannedGame>();
        foreach (string library in libraries)
            foreach (string file in Directory.GetFiles(Path.Combine(library, "steamapps"), "appmanifest_*.acf"))
            {
                string content = File.ReadAllText(file);
                string Field(string key) => Regex.Match(content, "\"" + key + "\"\\s*\"([^\"]+)\"").Groups[1].Value;
                if (!int.TryParse(Field("appid"), out int id) || id <= 0 || Path.GetFileNameWithoutExtension(file) != "appmanifest_" + id || Field("name").Length == 0 || Field("installdir").Length == 0 ||
                    content.Count(x => x == '{') != content.Count(x => x == '}')) throw new InvalidDataException(file);
                if (id is 228980 or 1070560 or 1391110 or 1628350) continue;
                games.Add(new() { Title = Field("name"), ExePath = "steam://rungameid/" + id, PlatformBadge = "Steam" });
            }
        return games;
    }

    internal static List<ScannedGame> ReadEpic(string directory)
    {
        var games = new List<ScannedGame>();
        foreach (string file in Directory.GetFiles(directory, "*.item"))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            string Field(string key) => json.RootElement.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
            string app = Field("AppName");
            if (app.Length == 0 || Field("InstallLocation").Length == 0) throw new InvalidDataException(file);
            string ns = Field("CatalogNamespace"), item = Field("CatalogItemId");
            string id = ns.Length > 0 && item.Length > 0 ? ns + ":" + item + ":" + app : app;
            games.Add(new() { Title = Field("DisplayName") is { Length: > 0 } title ? title : app,
                ExePath = LauncherConstants.EpicAppsProtocol + Uri.EscapeDataString(id) + "?action=launch&silent=true", PlatformBadge = "Epic Games" });
        }
        return games;
    }
}
