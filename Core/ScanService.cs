namespace EricGameLauncher;

public static class ScanService
{
    public static async Task<List<ScannedGame>> ScanAllAsync()
    {
        var games = new List<ScannedGame>();
        games.AddRange(ScanSteam());
        games.AddRange(ScanEpic());
        games.AddRange(await ScanXboxAsync());
        return games;
    }

    public static List<ScannedGame> ScanSteam()
    {
        return SteamHelper.GetAllInstalledGames();
    }

    public static List<ScannedGame> ScanEpic()
    {
        return EpicGamesHelper.GetAllInstalledGames();
    }

    public static async Task<List<ScannedGame>> ScanXboxAsync()
    {
        return await StoreHelper.GetAllInstalledGamesAsync();
    }

    public static (List<ScannedGame> newGames, List<ScannedGame> existingGames) Classify(
        List<ScannedGame> scannedGames, List<AppItem> allItems)
    {
        var existingGames = new List<ScannedGame>();
        var newGames = new List<ScannedGame>();

        if (scannedGames == null) return (newGames, existingGames);

        foreach (var game in scannedGames)
        {
            bool exists = false;

            if (game.PlatformBadge == "Xbox")
            {
                string gameId = game.ExePath.Replace(LauncherConstants.UwpAppsFolderPrefix, "");
                exists = allItems.Any(a => !string.IsNullOrEmpty(a.ExePath) &&
                    a.ExePath.Contains(gameId, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                string gameNorm = NormalizePath(game.ExePath);
                exists = allItems.Any(a =>
                    (!string.IsNullOrEmpty(a.ExePath) && NormalizePath(a.ExePath) == gameNorm) ||
                    string.Equals(a.Title, game.Title, StringComparison.OrdinalIgnoreCase));
            }

            if (exists) existingGames.Add(game);
            else newGames.Add(game);
        }

        return (newGames, existingGames);
    }

    public static List<ScannedGame> FindInvalidGames(
        List<AppItem> allItems, List<ScannedGame> scannedGames,
        bool canValidateSteam, bool canValidateEpic)
    {
        var invalidGames = new List<ScannedGame>();

        foreach (var item in allItems)
        {
            if (IsFileOrDirectoryExists(item.ExePath)) continue;

            string? platformName = item.PlatformName;

            if (platformName == "Steam" || platformName == "Epic Games" || platformName == "Xbox")
            {
                if (platformName == "Steam" && !canValidateSteam) continue;
                if (platformName == "Epic Games" && !canValidateEpic) continue;

                if (platformName == "Xbox")
                {
                    if (!StoreHelper.IsAppInstalled(item.ExePath))
                        invalidGames.Add(CreateInvalidGame(item, platformName));
                    continue;
                }

                bool found = scannedGames.Any(game =>
                    game.PlatformBadge == platformName &&
                    ((!string.IsNullOrEmpty(item.ExePath) && NormalizePath(item.ExePath) == NormalizePath(game.ExePath)) ||
                     string.Equals(item.Title, game.Title, StringComparison.OrdinalIgnoreCase)));

                if (!found)
                    invalidGames.Add(CreateInvalidGame(item, platformName));
            }
            else
            {
                if (IsUserLaunchTargetInvalid(item.ExePath))
                    invalidGames.Add(CreateInvalidGame(item, "User"));
            }
        }

        return invalidGames;
    }

    public static bool IsItemStillInvalid(AppItem item, bool canValidateSteam, bool canValidateEpic,
        HashSet<string> steamInstalledUrls, HashSet<string> epicInstalledUrls)
    {
        var exePath = item.ExePath;
        if (string.IsNullOrWhiteSpace(exePath)) return false;

        if (item.PlatformName == "Xbox")
            return !StoreHelper.IsAppInstalled(exePath);

        if (item.PlatformName == "Steam")
            return canValidateSteam && !steamInstalledUrls.Contains(exePath);

        if (item.PlatformName == "Epic Games")
            return canValidateEpic && !epicInstalledUrls.Contains(exePath);

        return IsUserLaunchTargetInvalid(exePath);
    }

    public static bool IsUserLaunchTargetInvalid(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return false;

        string path = Environment.ExpandEnvironmentVariables(rawPath.Trim());

        if (path.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
            return !StoreHelper.IsAppInstalled(path);

        if (path.Contains("://", StringComparison.OrdinalIgnoreCase))
            return false;

        if (File.Exists(path) || Directory.Exists(path))
            return false;

        var (filePath, _) = ProcessRunner.SplitPath(path);
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            string resolved = Environment.ExpandEnvironmentVariables(filePath.Trim());
            if (File.Exists(resolved) || Directory.Exists(resolved))
                return false;
        }

        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            return !File.Exists(path);

        if (path.Contains(".exe", StringComparison.OrdinalIgnoreCase))
        {
            int exeIndex = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) + 4;
            string exeCandidate = path.Substring(0, exeIndex).Trim('\"', ' ', '\'');
            return !File.Exists(exeCandidate);
        }

        return false;
    }

    public static bool CanValidateSteam => !string.IsNullOrEmpty(SteamHelper.DetectSteamPath());
    public static bool CanValidateEpic => !string.IsNullOrEmpty(EpicGamesHelper.DetectEpicManifestDir());

    public static HashSet<string> GetSteamInstalledUrls()
    {
        return CanValidateSteam
            ? new HashSet<string>(SteamHelper.GetAllInstalledGames().Select(x => x.ExePath), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public static HashSet<string> GetEpicInstalledUrls()
    {
        return CanValidateEpic
            ? new HashSet<string>(EpicGamesHelper.GetAllInstalledGames().Select(x => x.ExePath), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? p)
    {
        if (string.IsNullOrEmpty(p)) return string.Empty;
        try { return Path.GetFullPath(p).ToUpperInvariant(); }
        catch { return p.ToUpperInvariant(); }
    }

    private static bool IsFileOrDirectoryExists(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return false;
        try
        {
            string expandedPath = Environment.ExpandEnvironmentVariables(rawPath);
            return File.Exists(expandedPath) || Directory.Exists(expandedPath);
        }
        catch { return false; }
    }

    private static ScannedGame CreateInvalidGame(AppItem i, string? badge)
    {
        return new ScannedGame
        {
            Title = i.Title ?? "Unknown",
            ExePath = i.ExePath ?? string.Empty,
            PlatformBadge = badge ?? "",
            ItemId = i.Id
        };
    }
}
