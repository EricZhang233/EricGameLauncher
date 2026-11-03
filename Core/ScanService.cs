namespace EricGameLauncher;

public sealed record ScanActionResult(int Changed, IReadOnlyList<string> CompletedPaths);

public static class ScanService
{
    public static async Task<List<ScannedGame>> ScanAllAsync(CancellationToken cancellationToken = default)
    {
        var result = await PlatformScan.RunAsync(token: cancellationToken);
        result.RequireComplete();
        return result.Games;
    }

    public static List<ScannedGame> ScanSteam()
    {
        return SteamHelper.DetectSteamPath() is string root ? PlatformScan.ReadSteam(PlatformScan.SteamLibraries(root)) : [];
    }

    public static List<ScannedGame> ScanEpic()
    {
        return EpicGamesHelper.DetectEpicManifestDir() is string root ? PlatformScan.ReadEpic(root) : [];
    }

    public static List<ScannedGame> ScanWeGame() => WeGameHelper.GetInstalledGames();

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
                string gameNorm = PlatformScan.Identity(game.ExePath);
                exists = gameNorm.Length > 0 && allItems.Any(a =>
                    PlatformScan.Identity(a.ExePath) == gameNorm || WeGameHelper.MatchesLaunchPath(game, a.ExePath));
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
        var scope = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Xbox" };
        if (canValidateSteam) scope.Add("Steam");
        if (canValidateEpic) scope.Add("Epic Games");
        return FindInvalidGames(allItems, new(scannedGames, scope, new Dictionary<string, string>(), true));
    }

    public static List<ScannedGame> FindInvalidGames(List<AppItem> allItems, PlatformScanResult result)
    {
        var scannedGames = result.Games;
        var invalidGames = new List<ScannedGame>();

        foreach (var item in allItems)
        {
            if (IsFileOrDirectoryExists(item.ExePath)) continue;

            string? platformName = item.PlatformName;

            if (platformName == "Steam" || platformName == "Epic Games" || platformName == "Xbox")
            {
                if (!result.Completed.Contains(platformName) || result.Failures.ContainsKey(platformName)) continue;

                if (platformName == "Xbox")
                {
                    if (!StoreHelper.IsAppInstalled(item.ExePath))
                        invalidGames.Add(CreateInvalidGame(item, platformName));
                    continue;
                }

                bool found = scannedGames.Any(game =>
                    game.PlatformBadge == platformName &&
                    PlatformScan.Identity(item.ExePath) == PlatformScan.Identity(game.ExePath));

                if (!found)
                    invalidGames.Add(CreateInvalidGame(item, platformName));
            }
            else
            {
                if (result.IncludeLocal && IsUserLaunchTargetInvalid(item.ExePath))
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
            return canValidateSteam && !steamInstalledUrls.Any(x => PlatformScan.Identity(x) == PlatformScan.Identity(exePath));

        if (item.PlatformName == "Epic Games")
            return canValidateEpic && !epicInstalledUrls.Any(x => PlatformScan.Identity(x) == PlatformScan.Identity(exePath));

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
            ? new HashSet<string>(ScanSteam().Select(x => x.ExePath), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public static HashSet<string> GetEpicInstalledUrls()
    {
        return CanValidateEpic
            ? new HashSet<string>(ScanEpic().Select(x => x.ExePath), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public static int DeleteInvalidGames(List<ScannedGame> games) => DeleteInvalidGamesDetailed(games).Changed;

    public static ScanActionResult DeleteInvalidGamesDetailed(List<ScannedGame> games)
    {
        using (LogService.StartOperation("Scan", "DeleteInvalidGames"))
        {
            var processedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool canValidateSteam = games.Any(x => x.PlatformBadge == "Steam") && CanValidateSteam;
            bool canValidateEpic = games.Any(x => x.PlatformBadge == "Epic Games") && CanValidateEpic;
            var steamInstalledUrls = canValidateSteam ? GetSteamInstalledUrls() : [];
            var epicInstalledUrls = canValidateEpic ? GetEpicInstalledUrls() : [];
            var idsToRemove = new List<string>();

            foreach (var game in games)
            {
                if (string.IsNullOrEmpty(game.ItemId) || !processedIds.Add(game.ItemId)) continue;

                var item = ItemService.FindItem(game.ItemId, null);
                if (item == null) continue;

                if (!IsItemStillInvalid(item, canValidateSteam, canValidateEpic, steamInstalledUrls, epicInstalledUrls))
                    continue;

                idsToRemove.Add(item.Id);
            }

            var removedPaths = new List<string>();
            int removed = ItemService.RemoveItems(idsToRemove, item =>
            {
                var candidate = games.First(x => x.ItemId == item.Id);
                if (PlatformScan.Identity(candidate.ExePath) != PlatformScan.Identity(item.ExePath) ||
                    !IsItemStillInvalid(item, canValidateSteam, canValidateEpic, steamInstalledUrls, epicInstalledUrls)) return false;
                removedPaths.Add(candidate.ExePath);
                return true;
            });
            LogService.Write("Scan", $"DeleteInvalidGames removed={removed}");
            return new(removed, removedPaths);
        }
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
