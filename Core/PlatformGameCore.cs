using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Management.Deployment;

namespace EricGameLauncher;

public class ScannedGame
{
    public string Title { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public string PlatformBadge { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public string? ItemId { get; set; }
}

public class GamePlatformInfo
{
    public string PlatformName { get; set; } = "";
    public string? DefaultLauncherPath { get; set; }
    public string UrlProtocol { get; set; } = "";
}

public class SteamGameInfo
{
    public int AppId { get; set; }
    public string? Name { get; set; }
    public string? InstallDir { get; set; }
    public string? Executable { get; set; }
    public string? FullExePath { get; set; }
}

public static class GamePlatformHelper
{
    private static readonly Dictionary<string, GamePlatformInfo> PlatformRegistry = new()
    {
        [LauncherConstants.SteamProtocol] = new GamePlatformInfo { PlatformName = "Steam", DefaultLauncherPath = "steam://open/main", UrlProtocol = LauncherConstants.SteamProtocol },
        [LauncherConstants.EpicProtocol] = new GamePlatformInfo { PlatformName = "Epic Games", DefaultLauncherPath = LauncherConstants.EpicProtocol, UrlProtocol = LauncherConstants.EpicProtocol },
        [LauncherConstants.XboxProtocol] = new GamePlatformInfo { PlatformName = "Xbox", DefaultLauncherPath = LauncherConstants.XboxProtocol, UrlProtocol = LauncherConstants.XboxProtocol }
    };

    public static GamePlatformInfo? DetectPlatform(string url)
    {
        LogService.Write("Platform", $"DetectPlatform Start url={url}");
        if (string.IsNullOrEmpty(url))
        {
            LogService.Write("Platform", "DetectPlatform aborted: empty url");
            return null;
        }

        foreach (var kvp in PlatformRegistry)
        {
            if (url.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                LogService.Write("Platform", $"DetectPlatform matched={kvp.Key}");
                return kvp.Value;
            }
        }

        LogService.Write("Platform", "DetectPlatform no match");
        return null;
    }

    public static async Task<GamePlatformInfo?> DetectPlatformAsync(string url)
    {
        using (LogService.StartOperation("Platform", "DetectPlatformAsync"))
        {
            LogService.Write("Platform", $"DetectPlatformAsync Start url={url}");
            if (string.IsNullOrEmpty(url))
            {
                LogService.Write("Platform", "DetectPlatformAsync aborted: empty url");
                return null;
            }

            if (url.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                bool isGame = false;
                try
                {
                    isGame = await StoreHelper.IsGameAsync(url);
                }
                catch (Exception ex)
                {
                    LogService.Write("Platform", "DetectPlatformAsync StoreHelper.IsGameAsync failed", ex);
                }
                LogService.Write("Platform", $"DetectPlatformAsync IsGameAsync result={isGame}");
                if (isGame)
                {
                    return new GamePlatformInfo
                    {
                        PlatformName = "Xbox",
                        UrlProtocol = LauncherConstants.UwpAppsFolderPrefix,
                        DefaultLauncherPath = LauncherConstants.XboxProtocol
                    };
                }
                return null;
            }

            var platform = DetectPlatform(url);
            LogService.Write("Platform", $"DetectPlatformAsync detected={(platform?.PlatformName ?? "none")}");
            return platform;
        }
    }

    public static bool IsSupportedPlatformUrl(string url)
    {
        try { LogService.Write("Platform", $"IsSupportedPlatformUrl called url={url}"); } catch { }
        return DetectPlatform(url) != null;
    }


    public static string? GetRuntimeManagerPath(string? mgrPath, string? exePath)
    {
        LogService.Write("Platform", $"GetRuntimeManagerPath Start mgrPath={mgrPath} exePath={exePath}");
        if (!string.IsNullOrEmpty(mgrPath))
        {
            LogService.Write("Platform", "GetRuntimeManagerPath returning custom mgrPath");
            return mgrPath;
        }
        if (string.IsNullOrEmpty(exePath))
        {
            LogService.Write("Platform", "GetRuntimeManagerPath aborted: empty exePath");
            return null;
        }

        if (exePath.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var cachedPlatform = DetectPlatform(exePath);
            if (cachedPlatform != null) return cachedPlatform.DefaultLauncherPath;
            return null;
        }

        var platform = DetectPlatform(exePath);
        if (platform != null && !string.IsNullOrEmpty(platform.DefaultLauncherPath)) return platform.DefaultLauncherPath;

        return null;
    }

    public static string? GetPlatformDisplayName(string url)
    {
        try { LogService.Write("Platform", $"GetPlatformDisplayName called url={url}"); } catch { }
        return DetectPlatform(url)?.PlatformName;
    }

}

public static class SteamHelper
{
    private static readonly string[] DefaultSteamPaths = new[] { "Program Files (x86)\\Steam", "Program Files\\Steam", "Steam" };
    private static string? _cachedSteamPath;
    private static List<string>? _cachedLibraryFolders;

    public static int? ExtractAppIdFromUrl(string url)
    {
        try { LogService.Write("Platform", $"ExtractAppIdFromUrl called url={url}"); } catch { }
        if (string.IsNullOrEmpty(url)) return null;
        var match = Regex.Match(url, @"steam://(?:rungameid|run)/(\d+)", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int appId))
        {
            try { LogService.Write("Platform", $"ExtractAppIdFromUrl parsed appId={appId}"); } catch { }
            return appId;
        }
        return null;
    }

    public static string? DetectSteamPath()
    {
        if (_cachedSteamPath != null) return _cachedSteamPath;
        LogService.Write("Platform", "DetectSteamPath Start");
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\\Valve\\Steam");
            if (key?.GetValue("SteamPath") is string regPath && !string.IsNullOrEmpty(regPath))
            {
                string normalizedPath = regPath.Replace("/", "\\");
                if (File.Exists(Path.Combine(normalizedPath, "steam.exe")))
                {
                    LogService.Write("Platform", $"DetectSteamPath found in registry path={normalizedPath}");
                    return _cachedSteamPath = normalizedPath;
                }
            }
        }
        catch (Exception ex) { LogService.Write("Platform", "DetectSteamPath registry read failed", ex); }
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed) continue;
            foreach (var defaultPath in DefaultSteamPaths)
            {
                string fullPath = Path.Combine(drive.Name, defaultPath);
                if (File.Exists(Path.Combine(fullPath, "steam.exe")))
                {
                    LogService.Write("Platform", $"DetectSteamPath found on drive path={fullPath}");
                    return _cachedSteamPath = fullPath;
                }
            }
        }
        LogService.Write("Platform", "DetectSteamPath not found");
        return null;
    }

    public static List<string> GetLibraryFolders()
    {
        using (LogService.StartOperation("Platform", "GetLibraryFolders"))
        {
            if (_cachedLibraryFolders != null) return _cachedLibraryFolders;
            LogService.Write("Platform", "GetLibraryFolders Start");
            var folders = new List<string>();
        string? steamPath = DetectSteamPath();
        if (string.IsNullOrEmpty(steamPath))
        {
            LogService.Write("Platform", "GetLibraryFolders no steam path detected");
                return folders;
        }
        folders.Add(steamPath);
        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
        {
            LogService.Write("Platform", $"GetLibraryFolders libraryfolders.vdf not found at {vdfPath}");
            return _cachedLibraryFolders = folders;
        }
        try
        {
            string content = File.ReadAllText(vdfPath);
            var pathRegex = new Regex("\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            foreach (Match match in pathRegex.Matches(content))
            {
                string path = match.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase)) folders.Add(path);
            }
            LogService.Write("Platform", $"GetLibraryFolders parsed folders={folders.Count}");
            }
            catch (Exception ex) { LogService.Write("Platform", "GetLibraryFolders parse failed", ex); }
            return _cachedLibraryFolders = folders;
        }
    }

    public static SteamGameInfo? GetGameInfo(int appId)
    {
        using (LogService.StartOperation("Platform", $"GetGameInfo {appId}"))
        {
            LogService.Write("Platform", $"GetGameInfo Start appId={appId}");
            var libraryFolders = GetLibraryFolders();
            string? installDir = null, gameName = null, libraryPath = null;
        foreach (var libFolder in libraryFolders)
        {
            string manifestPath = Path.Combine(libFolder, "steamapps", $"appmanifest_{appId}.acf");
            if (File.Exists(manifestPath))
            {
                try
                {
                    string content = File.ReadAllText(manifestPath);
                    var installDirMatch = Regex.Match(content, "\"installdir\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    if (installDirMatch.Success) { installDir = installDirMatch.Groups[1].Value; libraryPath = libFolder; }
                    var nameMatch = Regex.Match(content, "\"name\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    if (nameMatch.Success) gameName = nameMatch.Groups[1].Value;
                    if (!string.IsNullOrEmpty(installDir)) break;
                }
                catch (Exception ex) { LogService.Write("Platform", "GetGameInfo manifest parse failed", ex); }
            }
        }
            LogService.Write("Platform", $"GetGameInfo parsed installDir={installDir} gameName={gameName} libraryPath={libraryPath}");
            if (string.IsNullOrEmpty(installDir) || string.IsNullOrEmpty(libraryPath)) return null;
        string gameDir = Path.Combine(libraryPath, "steamapps", "common", installDir);

        string? fullExePath = FindMainExecutable(gameDir, gameName);

        if (string.IsNullOrEmpty(fullExePath) || !File.Exists(fullExePath))
        {
            string? steamPath = DetectSteamPath();
            if (!string.IsNullOrEmpty(steamPath))
            {
                string backupIcon = Path.Combine(steamPath, "appcache", "librarycache", $"{appId}_icon.jpg");
                if (File.Exists(backupIcon)) fullExePath = backupIcon;
            }
        }

            LogService.Write("Platform", $"GetGameInfo result fullExePath={fullExePath}");
            return new SteamGameInfo { AppId = appId, Name = gameName, InstallDir = installDir, Executable = string.IsNullOrEmpty(fullExePath) ? null : Path.GetFileName(fullExePath), FullExePath = fullExePath };
        }
    }

    public static string? GetExecutableFromSteamUrl(string steamUrl)
    {
        try { LogService.Write("Platform", $"GetExecutableFromSteamUrl called steamUrl={steamUrl}"); } catch { }
        var appId = ExtractAppIdFromUrl(steamUrl);
        var res = appId == null ? null : GetGameInfo(appId.Value)?.FullExePath;
        try { LogService.Write("Platform", $"GetExecutableFromSteamUrl result={res}"); } catch { }
        return res;
    }

    private static string? FindMainExecutable(string gameDir, string? gameName)
    {
        using (LogService.StartOperation("Platform", "FindMainExecutable"))
        {
            try
            {
                LogService.Write("Platform", $"FindMainExecutable Start gameDir={gameDir} gameName={gameName}");
                if (!Directory.Exists(gameDir))
                {
                    LogService.Write("Platform", "FindMainExecutable aborted: gameDir does not exist");
                    return null;
                }

            var exeFiles = Directory.EnumerateFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly).ToList();
            foreach (var subDir in Directory.EnumerateDirectories(gameDir))
            {
                try
                {
                    string dirName = Path.GetFileName(subDir).ToLower();
                    if (dirName == "engine" || dirName == "redist") continue;
                    exeFiles.AddRange(Directory.EnumerateFiles(subDir, "*.exe", SearchOption.TopDirectoryOnly));
                }
                catch (Exception ex) { LogService.Write("Platform", "Enumerate subdir failed", ex); }
            }

            LogService.Write("Platform", $"FindMainExecutable discovered exeCount={exeFiles.Count}");
            if (exeFiles.Count == 0) return null;

            var excludePatterns = new[] { "unins", "uninst", "setup", "install", "crash", "report", "update", "launcher", "redist", "vcredist", "dxsetup", "ue4prereq", "dotnet", "directx", "easyanticheat", "eac_launcher" };

            string normalizedGameName = string.IsNullOrEmpty(gameName) ? "" : Regex.Replace(gameName.ToLower(), @"[^a-z0-9]", "");
            string normalizedDirName = Regex.Replace(Path.GetFileName(gameDir).ToLower(), @"[^a-z0-9]", "");

            var scored = exeFiles.Select(f =>
            {
                int score = 0;
                string fileName = Path.GetFileNameWithoutExtension(f).ToLower();
                string normalizedFileName = Regex.Replace(fileName, @"[^a-z0-9]", "");
                if (excludePatterns.Any(p => fileName.Contains(p))) score -= 1000;
                if (normalizedFileName == normalizedGameName || normalizedFileName == normalizedDirName) score += 50;
                else if (!string.IsNullOrEmpty(normalizedGameName) && (normalizedFileName.Contains(normalizedGameName) || normalizedGameName.Contains(normalizedFileName))) score += 30;
                long length = 0;
                try { length = new FileInfo(f).Length; } catch { }
                if (length > 10 * 1024 * 1024) score += 30; else if (length > 1 * 1024 * 1024) score += 15;
                try { if (Path.GetDirectoryName(f)?.Equals(gameDir, StringComparison.OrdinalIgnoreCase) == true) score += 20; } catch { }
                return new { Path = f, Score = score, Length = length };
            });

            var chosen = scored.OrderByDescending(s => s.Score).ThenByDescending(s => s.Length).FirstOrDefault()?.Path;
                LogService.Write("Platform", $"FindMainExecutable chosen={chosen}");
                return chosen;
            }
            catch (Exception ex) { LogService.Write("Platform", "FindMainExecutable failed", ex); return null; }
        }
    }

    public static void ClearCache() { _cachedSteamPath = null; _cachedLibraryFolders = null; }

    public static List<ScannedGame> GetAllInstalledGames()
    {
        using (LogService.StartOperation("Platform", "Steam_GetAllInstalledGames"))
        {
            var results = new List<ScannedGame>();
            LogService.Write("Platform", "Steam GetAllInstalledGames Start");
        try
        {
            var libraryFolders = GetLibraryFolders();
            LogService.Write("Platform", $"Steam GetAllInstalledGames libraryFolders={libraryFolders.Count}");
            foreach (var libFolder in libraryFolders)
            {
                string appsDir = Path.Combine(libFolder, "steamapps");
                if (!Directory.Exists(appsDir)) continue;

                try
                {
                    var manifestFiles = Directory.GetFiles(appsDir, "appmanifest_*.acf");
                    LogService.Write("Platform", $"Steam GetAllInstalledGames scanning manifests count={manifestFiles.Length} in {appsDir}");
                    foreach (var file in manifestFiles)
                    {
                        try
                        {
                            string content = File.ReadAllText(file);
                            var nameMatch = Regex.Match(content, "\"name\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                            var idMatch = Regex.Match(file, @"appmanifest_(\d+)\\.acf", RegexOptions.IgnoreCase);
                            if (nameMatch.Success && idMatch.Success && int.TryParse(idMatch.Groups[1].Value, out int appId))
                            {
                                string displayName = nameMatch.Groups[1].Value;
                                string exeUrl = $"steam://rungameid/{appId}";
                                results.Add(new ScannedGame { Title = displayName, ExePath = exeUrl, PlatformBadge = "Steam" });
                            }
                        }
                        catch (Exception ex) { LogService.Write("Platform", "Steam GetAllInstalledGames inner loop failed", ex); }
                    }
                }
                catch (Exception ex) { LogService.Write("Platform", "Steam GetAllInstalledGames failed for folder", ex); }
            }
            }
            catch (Exception ex) { LogService.Write("Platform", "Steam GetAllInstalledGames failed", ex); }
            LogService.Write("Platform", $"Steam GetAllInstalledGames resultCount={results.Count}");
            return results;
        }
    }
}

public static class EpicGamesHelper
{
    private static string? _cachedEpicManifestDir;

    public static string? DetectEpicManifestDir()
    {
        try { LogService.Write("Platform", "DetectEpicManifestDir called"); } catch { }
        if (_cachedEpicManifestDir != null) return _cachedEpicManifestDir;
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string manifestDir = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (Directory.Exists(manifestDir)) { _cachedEpicManifestDir = manifestDir; try { LogService.Write("Platform", $"DetectEpicManifestDir found={manifestDir}"); } catch { } return manifestDir; }
        try { LogService.Write("Platform", "DetectEpicManifestDir not found"); } catch { }
        return null;
    }

    public static string? GetExecutableFromEpicUrl(string url)
    {
        try { LogService.Write("Platform", $"GetExecutableFromEpicUrl called url={url}"); } catch { }
        if (string.IsNullOrEmpty(url) || !url.StartsWith(LauncherConstants.EpicAppsProtocol, StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            int prefixLen = LauncherConstants.EpicAppsProtocol.Length;
            int queryIndex = url.IndexOf('?');
            string rawId = (queryIndex > prefixLen) ? url.Substring(prefixLen, queryIndex - prefixLen) : url.Substring(prefixLen);
            if (string.IsNullOrEmpty(rawId)) return null;
            string decodedId = Uri.UnescapeDataString(rawId);
            string appName = decodedId.Contains(':') ? decodedId.Split(':').Last() : decodedId;

            string? manifestDir = DetectEpicManifestDir();
            if (string.IsNullOrEmpty(manifestDir)) return null;

            var manifestFiles = Directory.GetFiles(manifestDir, "*.item");
            foreach (var file in manifestFiles)
            {
                try
                {
                    string content = File.ReadAllText(file);
                            var match = Regex.Match(content, "\"AppName\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    if (match.Success && string.Equals(match.Groups[1].Value, appName, StringComparison.OrdinalIgnoreCase))
                    {
                        return ExtractExePathFromManifest(content);
                    }
                }
                catch (Exception ex) { LogService.Write("Platform", "GetExecutableFromEpicUrl manifest loop failed", ex); }
            }
        }
        catch (Exception ex) { LogService.Write("Platform", "GetExecutableFromEpicUrl failed", ex); }
        return null;
    }

    private static string? ExtractExePathFromManifest(string jsonContent)
    {
        try
        {
            var installLocMatch = Regex.Match(jsonContent, "\"InstallLocation\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            var launchExeMatch = Regex.Match(jsonContent, "\"LaunchExecutable\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (installLocMatch.Success && launchExeMatch.Success)
            {
                string installDir = installLocMatch.Groups[1].Value.Replace("\\\\", "\\").Replace("/", "\\");
                string launchExe = launchExeMatch.Groups[1].Value.Replace("\\\\", "\\").Replace("/", "\\");
                string fullPath = Path.Combine(installDir, launchExe);
                return fullPath;
            }
        }
        catch (Exception ex) { LogService.Write("Platform", "ExtractExePathFromManifest failed", ex); }
        return null;
    }

    public static List<ScannedGame> GetAllInstalledGames()
    {
        var results = new List<ScannedGame>();
        try
        {
            string? manifestDir = DetectEpicManifestDir();
            if (string.IsNullOrEmpty(manifestDir)) return results;
            LogService.Write("Platform", $"EpicGames GetAllInstalledGames manifestDir={manifestDir}");
            var manifestFiles = Directory.GetFiles(manifestDir, "*.item");
            foreach (var file in manifestFiles)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    var nameMatch = Regex.Match(content, "\"DisplayName\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    var appNameMatch = Regex.Match(content, "\"AppName\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    var catalogNsMatch = Regex.Match(content, "\"CatalogNamespace\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    var catalogItemMatch = Regex.Match(content, "\"CatalogItemId\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    string? exePath = ExtractExePathFromManifest(content);
                    if (!string.IsNullOrEmpty(exePath) && appNameMatch.Success)
                    {
                        string displayName = nameMatch.Success ? nameMatch.Groups[1].Value : appNameMatch.Groups[1].Value;
                        string appName = appNameMatch.Groups[1].Value;
                        string catalogNs = catalogNsMatch.Success ? catalogNsMatch.Groups[1].Value : "";
                        string catalogItemId = catalogItemMatch.Success ? catalogItemMatch.Groups[1].Value : "";
                        string fullEpicId = appName;
                        if (!string.IsNullOrEmpty(catalogNs) && !string.IsNullOrEmpty(catalogItemId)) fullEpicId = $"{catalogNs}:{catalogItemId}:{appName}";
                        string epicUrl = $"{LauncherConstants.EpicAppsProtocol}{Uri.EscapeDataString(fullEpicId)}?action=launch&silent=true";
                        results.Add(new ScannedGame { Title = displayName, ExePath = epicUrl, PlatformBadge = "Epic Games" });
                    }
                }
                catch (Exception ex) { LogService.Write("Platform", "EpicGames GetAllInstalledGames inner loop failed", ex); }
            }
        }
        catch (Exception ex) { LogService.Write("Platform", "EpicGames GetAllInstalledGames failed", ex); }
        LogService.Write("Platform", $"EpicGames GetAllInstalledGames resultCount={results.Count}");
        return results;
    }
}

public static class StoreHelper
{
    private static readonly PackageManager _packageManager = new PackageManager();
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    public static bool IsAppInstalled(string? path)
    {
        using (LogService.StartOperation("Platform", "IsAppInstalled"))
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!path.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                LogService.Write("Platform", $"IsAppInstalled check path={path}");
                string aumid = path.Substring(LauncherConstants.UwpAppsFolderPrefix.Length);
                string pfn = aumid.Contains('!') ? aumid.Split('!')[0] : aumid;
                var package = _packageManager.FindPackageForUser("", pfn);
                bool installed = package != null;
                LogService.Write("Platform", $"IsAppInstalled result pfn={pfn} installed={installed}");
                return installed;
            }
            catch (Exception ex) { LogService.Write("Platform", "IsAppInstalled failed", ex); return false; }
        }
    }

    public static async Task<bool> IsGameAsync(string path)
    {
        using (LogService.StartOperation("Platform", "Store_IsGameAsync"))
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!path.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            string aumid = path.Substring(LauncherConstants.UwpAppsFolderPrefix.Length);
            string pfn = aumid.Contains('!') ? aumid.Split('!')[0] : aumid;
            try
            {
                if (await CheckManifestForGameMarkersAsync(aumid)) return true;
                return await IsGameOnlineAsync(pfn);
            }
            catch (Exception ex) { LogService.Write("Platform", "IsGameAsync failed", ex); return false; }
        }
    }

    private static async Task<bool> IsGameOnlineAsync(string pfn)
    {
        using (LogService.StartOperation("Network", "IsGameOnlineAsync"))
        {
            try
            {
                string url = $"https://displaycatalog.md.mp.microsoft.com/v7.0/products/lookup?market=US&languages=en-US&alternateId=PackageFamilyName&value={pfn}";
                var response = await _httpClient.GetFromJsonAsync<System.Text.Json.JsonElement>(url);
                if (response.ValueKind == System.Text.Json.JsonValueKind.Object && response.TryGetProperty("Products", out var products) && products.GetArrayLength() > 0)
                {
                    var product = products[0];
                    if (product.TryGetProperty("ProductFamily", out var family))
                    {
                        string familyStr = family.GetString() ?? "";
                        if (string.Equals(familyStr, "Games", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    if (product.TryGetProperty("ProductKind", out var kind))
                    {
                        string kindStr = kind.GetString() ?? "";
                        if (string.Equals(kindStr, "Game", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
            }
            catch (Exception ex) { LogService.Write("Network", "IsGameOnlineAsync failed", ex); }
            return false;
        }
    }

    private static async Task<bool> CheckManifestForGameMarkersAsync(string aumid)
    {
        using (LogService.StartOperation("Platform", "Store_CheckManifestForGameMarkersAsync"))
        {
            return await Task.Run(() =>
            {
                try
                {
                    string pfn = aumid.Contains('!') ? aumid.Split('!')[0] : aumid;
                    var package = _packageManager.FindPackageForUser("", pfn);
                    if (package == null) return false;

                    string manifestPath = Path.Combine(package.InstalledLocation.Path, "AppxManifest.xml");
                    var doc = new System.Xml.XmlDocument();
                    doc.Load(manifestPath);

                    var nsmgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
                    nsmgr.AddNamespace("uap", "http://schemas.microsoft.com/appx/manifest/uap/windows10");
                    nsmgr.AddNamespace("res", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");

                    var protocols = doc.SelectNodes("//*[local-name()='Protocol']", nsmgr);
                    if (protocols != null)
                    {
                        foreach (System.Xml.XmlNode protocol in protocols)
                        {
                            string? protoName = protocol.Attributes?["Name"]?.Value;
                            if (protoName != null && (protoName.Contains("xbox", StringComparison.OrdinalIgnoreCase) || protoName.StartsWith("ms-xbl-", StringComparison.OrdinalIgnoreCase)))
                                return true;
                        }
                    }

                    var capabilities = doc.SelectNodes("//*[local-name()='Capability' or local-name()='uap:Capability' or local-name()='resCap:Capability']", nsmgr);
                    if (capabilities != null)
                    {
                        foreach (System.Xml.XmlNode cap in capabilities)
                        {
                            string? capName = cap.Attributes?["Name"]?.Value;
                            if (capName != null && (capName.Contains("xbox", StringComparison.OrdinalIgnoreCase) || capName.Equals("gameList", StringComparison.OrdinalIgnoreCase)))
                                return true;
                        }
                    }

                    var extensions = doc.SelectNodes("//*[local-name()='Extension']", nsmgr);
                    if (extensions != null)
                    {
                        foreach (System.Xml.XmlNode ext in extensions)
                        {
                            string? category = ext.Attributes?["Category"]?.Value;
                            if (category != null && category.Contains("gameBar", StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                    }
                }
                catch (Exception ex) { LogService.Write("Platform", "CheckManifestForGameMarkersAsync failed", ex); }
                return false;
            });
        }
    }

    public static async Task<List<ScannedGame>> GetAllInstalledGamesAsync()
    {
        using (LogService.StartOperation("Platform", "GetAllInstalledGamesAsync"))
        {
            var results = new List<ScannedGame>();
            try
            {
                var packages = _packageManager.FindPackagesForUser(string.Empty);
                var tasks = new List<Task<ScannedGame?>>();

                foreach (var package in packages)
                {
                    if (package.IsFramework || package.IsResourcePackage) continue;

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var entries = await package.GetAppListEntriesAsync();
                            if (entries == null || entries.Count == 0) return null;

                            var entry = entries[0];
                            string aumid = entry.AppUserModelId;
                            string pfn = package.Id.FamilyName;

                            if (await IsGameAsync(LauncherConstants.UwpAppsFolderPrefix + aumid))
                            {
                                return new ScannedGame
                                {
                                    Title = entry.DisplayInfo.DisplayName ?? package.DisplayName,
                                    ExePath = LauncherConstants.UwpAppsFolderPrefix + aumid,
                                    PlatformBadge = "Xbox"
                                };
                            }
                        }
                        catch (Exception ex) { LogService.Write("Platform", "GetAllInstalledGames package task failed", ex); }
                        return null;
                    }));
                }

                var games = await Task.WhenAll(tasks);
                results.AddRange(games.Where(g => g != null)!);
            }
            catch (Exception ex) { LogService.Write("Platform", "GetAllInstalledGames failed", ex); }

            return results;
        }
    }
}
