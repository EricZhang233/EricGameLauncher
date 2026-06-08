using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
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
    private static readonly string[] DefaultSteamPaths = ["Program Files (x86)\\Steam", "Program Files\\Steam", "Steam"];
    private static string? _cachedSteamPath;
    private static List<string>? _cachedLibraryFolders;

    public static int? ExtractAppIdFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var match = Regex.Match(url, @"steam://(?:rungameid|run)/(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out int appId) ? appId : null;
    }

    public static string? DetectSteamPath()
    {
        if (_cachedSteamPath != null) return _cachedSteamPath;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string regPath && !string.IsNullOrEmpty(regPath))
            {
                string normalizedPath = regPath.Replace("/", "\\");
                if (File.Exists(Path.Combine(normalizedPath, "steam.exe"))) return _cachedSteamPath = normalizedPath;
            }
        }
        catch (Exception) { }
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed) continue;
            foreach (var defaultPath in DefaultSteamPaths)
            {
                string fullPath = Path.Combine(drive.Name, defaultPath);
                if (File.Exists(Path.Combine(fullPath, "steam.exe"))) return _cachedSteamPath = fullPath;
            }
        }
        return null;
    }

    public static List<string> GetLibraryFolders()
    {
        if (_cachedLibraryFolders != null) return _cachedLibraryFolders;
        var folders = new List<string>();
        string? steamPath = DetectSteamPath();
        if (string.IsNullOrEmpty(steamPath)) return folders;
        folders.Add(steamPath);
        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath)) return _cachedLibraryFolders = folders;
        try
        {
            string content = File.ReadAllText(vdfPath);
            var pathRegex = new Regex("\"path\"\\s*\"([^\"]+)\"");
            foreach (Match match in pathRegex.Matches(content))
            {
                string path = match.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase)) folders.Add(path);
            }
        }
        catch (Exception) { }
        return _cachedLibraryFolders = folders;
    }

    public static SteamGameInfo? GetGameInfo(int appId)
    {
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
                    var installDirMatch = Regex.Match(content, "\"installdir\"\\s*\"([^\"]+)\"");
                    if (installDirMatch.Success) { installDir = installDirMatch.Groups[1].Value; libraryPath = libFolder; }
                    var nameMatch = Regex.Match(content, "\"name\"\\s*\"([^\"]+)\"");
                    if (nameMatch.Success) gameName = nameMatch.Groups[1].Value;
                    if (!string.IsNullOrEmpty(installDir)) break;
                }
                catch (Exception) { }
            }
        }
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

        return new SteamGameInfo { AppId = appId, Name = gameName, InstallDir = installDir, Executable = Path.GetFileName(fullExePath), FullExePath = fullExePath };
    }

    public static string? GetExecutableFromSteamUrl(string steamUrl)
    {
        var appId = ExtractAppIdFromUrl(steamUrl);
        return appId == null ? null : GetGameInfo(appId.Value)?.FullExePath;
    }

    private static string ReadCString(BinaryReader reader)
    {
        const int MaxLen = 4096;
        var bytes = new List<byte>(64);
        byte b;
        while (bytes.Count < MaxLen && (b = reader.ReadByte()) != 0) bytes.Add(b);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static string? FindMainExecutable(string gameDir, string? gameName)
    {
        try
        {
            if (!Directory.Exists(gameDir)) return null;

            var exeFiles = new List<string>();
            foreach (var file in Directory.EnumerateFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly)) exeFiles.Add(file);
            foreach (var subDir in Directory.EnumerateDirectories(gameDir))
            {
                try
                {
                    string dirName = Path.GetFileName(subDir).ToLower();
                    if (dirName == "engine" || dirName == "redist") continue;
                    foreach (var file in Directory.EnumerateFiles(subDir, "*.exe", SearchOption.TopDirectoryOnly)) exeFiles.Add(file);
                }
                catch { }
            }

            if (exeFiles.Count == 0) return null;

            var excludePatterns = new[] { "unins", "uninst", "setup", "install", "crash", "report", "update", "launcher", "redist", "vcredist", "dxsetup", "ue4prereq", "dotnet", "directx", "easyanticheat", "eac_launcher" };

            string normalizedGameName = string.IsNullOrEmpty(gameName) ? "" : Regex.Replace(gameName.ToLower(), @"[^a-z0-9]", "");
            string normalizedDirName = Regex.Replace(Path.GetFileName(gameDir).ToLower(), @"[^a-z0-9]", "");

            var scoredCandidates = exeFiles.Select(f =>
            {
                int score = 0;
                string fileName = Path.GetFileNameWithoutExtension(f).ToLower();
                string normalizedFileName = Regex.Replace(fileName, @"[^a-z0-9]", "");

                if (excludePatterns.Any(p => fileName.Contains(p))) score -= 1000;

                if (normalizedFileName == normalizedGameName || normalizedFileName == normalizedDirName) score += 50;
                else if (normalizedFileName.Contains(normalizedGameName) || normalizedGameName.Contains(normalizedFileName)) score += 30;

                long length = new FileInfo(f).Length;
                if (length > 10 * 1024 * 1024) score += 30;
                else if (length > 1 * 1024 * 1024) score += 15;

                string? dir = Path.GetDirectoryName(f);
                if (dir != null && dir.Equals(gameDir, StringComparison.OrdinalIgnoreCase)) score += 20;

                return new { Path = f, Score = score, Length = length };
            });

            return scoredCandidates
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.Length)
                .FirstOrDefault()?.Path;
        }
        catch (Exception) { return null; }
    }

    public static void ClearCache() { _cachedSteamPath = null; _cachedLibraryFolders = null; }

    public static List<ScannedGame> GetAllInstalledGames()
    {
        var results = new List<ScannedGame>();
        try
        {
            var libraryFolders = GetLibraryFolders();
            foreach (var libFolder in libraryFolders)
            {
                string appsDir = Path.Combine(libFolder, "steamapps");
                if (!Directory.Exists(appsDir)) continue;

                try
                {
                    var manifestFiles = Directory.GetFiles(appsDir, "appmanifest_*.acf");
                    foreach (var mf in manifestFiles)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(mf);
                        if (fileName.StartsWith("appmanifest_") && int.TryParse(fileName.Substring(12), out int appId))
                        {
                            if (appId == 228980 || appId == 1070560 || appId == 1391110 || appId == 1628350)
                                continue;

                            var info = GetGameInfo(appId);
                            if (info != null)
                            {
                                results.Add(new ScannedGame
                                {
                                    Title = info.Name ?? $"Steam Game {appId}",
                                    ExePath = $"steam://rungameid/{appId}",
                                    PlatformBadge = "Steam"
                                });
                            }
                        }
                    }
                }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
        return results;
    }
}

public static class EpicGamesHelper
{
    private static string? _cachedEpicManifestDir;

    public static string? DetectEpicManifestDir()
    {
        if (_cachedEpicManifestDir != null) return _cachedEpicManifestDir;

        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string manifestDir = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (Directory.Exists(manifestDir))
        {
            _cachedEpicManifestDir = manifestDir;
            return manifestDir;
        }
        return null;
    }

    public static string? GetExecutableFromEpicUrl(string url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith(LauncherConstants.EpicAppsProtocol, StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            int prefixLen = LauncherConstants.EpicAppsProtocol.Length;
            int queryIndex = url.IndexOf('?');
            string rawId = (queryIndex > prefixLen)
                ? url.Substring(prefixLen, queryIndex - prefixLen)
                : url.Substring(prefixLen);

            if (string.IsNullOrEmpty(rawId)) return null;

            string decodedId = Uri.UnescapeDataString(rawId);
            string appName = decodedId;
            if (decodedId.Contains(':'))
            {
                var parts = decodedId.Split(':');
                if (parts.Length > 0)
                {
                    appName = parts.Last();
                }
            }

            string? manifestDir = DetectEpicManifestDir();
            if (string.IsNullOrEmpty(manifestDir)) return null;

            var manifestFiles = Directory.GetFiles(manifestDir, "*.item");
            foreach (var file in manifestFiles)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    var match = Regex.Match(content, "\"AppName\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    if (match.Success && match.Groups[1].Value.Equals(appName, StringComparison.OrdinalIgnoreCase))
                    {
                        return ExtractExePathFromManifest(content);
                    }
                }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
        return null;
    }

    private static string? ExtractExePathFromManifest(string jsonContent)
    {
        try
        {
            var installLocMatch = Regex.Match(jsonContent, "\"InstallLocation\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            var launchExeMatch = Regex.Match(jsonContent, "\"LaunchExecutable\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);

            if (installLocMatch.Success && launchExeMatch.Success)
            {
                string installDir = installLocMatch.Groups[1].Value.Replace("\\\\", "\\").Replace("/", "\\");
                string launchExe = launchExeMatch.Groups[1].Value.Replace("\\\\", "\\").Replace("/", "\\");

                string fullPath = Path.Combine(installDir, launchExe);
                return fullPath;
            }
        }
        catch (Exception) { }
        return null;
    }

    public static List<ScannedGame> GetAllInstalledGames()
    {
        var results = new List<ScannedGame>();
        string? manifestDir = DetectEpicManifestDir();
        if (string.IsNullOrEmpty(manifestDir)) return results;

        try
        {
            var manifestFiles = Directory.GetFiles(manifestDir, "*.item");
            foreach (var file in manifestFiles)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    var nameMatch = Regex.Match(content, "\"DisplayName\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    var appNameMatch = Regex.Match(content, "\"AppName\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    var catalogNsMatch = Regex.Match(content, "\"CatalogNamespace\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    var catalogItemMatch = Regex.Match(content, "\"CatalogItemId\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);

                    string? exePath = ExtractExePathFromManifest(content);
                    if (!string.IsNullOrEmpty(exePath) && appNameMatch.Success)
                    {
                        string displayName = nameMatch.Success ? nameMatch.Groups[1].Value : appNameMatch.Groups[1].Value;
                        string appName = appNameMatch.Groups[1].Value;
                        string catalogNs = catalogNsMatch.Success ? catalogNsMatch.Groups[1].Value : "";
                        string catalogItemId = catalogItemMatch.Success ? catalogItemMatch.Groups[1].Value : "";

                        string fullEpicId = appName;
                        if (!string.IsNullOrEmpty(catalogNs) && !string.IsNullOrEmpty(catalogItemId))
                        {
                            fullEpicId = $"{catalogNs}:{catalogItemId}:{appName}";
                        }

                        string epicUrl = $"{LauncherConstants.EpicAppsProtocol}{Uri.EscapeDataString(fullEpicId)}?action=launch&silent=true";

                        results.Add(new ScannedGame
                        {
                            Title = displayName,
                            ExePath = epicUrl,
                            PlatformBadge = "Epic Games"
                        });
                    }
                }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
        return results;
    }
}

public static class StoreHelper
{
    private static readonly PackageManager _packageManager = new PackageManager();
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    public static bool IsAppInstalled(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (!path.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            string aumid = path.Substring(LauncherConstants.UwpAppsFolderPrefix.Length);
            string pfn = aumid.Contains("!") ? aumid.Split('!')[0] : aumid;
            var package = _packageManager.FindPackageForUser("", pfn);
            if (package != null) return true;

            int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out var shellItem);
            if (hr == 0 && shellItem != null)
            {
                Marshal.ReleaseComObject(shellItem);
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        [In] IntPtr pbc,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }

    public static async Task<bool> IsGameAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (!path.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        string aumid = path.Substring(LauncherConstants.UwpAppsFolderPrefix.Length);
        string pfn = aumid.Contains("!") ? aumid.Split('!')[0] : aumid;

        try
        {
            if (await CheckManifestForGameMarkersAsync(aumid)) return true;
            return await IsGameOnlineAsync(pfn);
        }
        catch { return false; }
    }

    private static async Task<bool> IsGameOnlineAsync(string pfn)
    {
        try
        {
            string url = $"https://displaycatalog.md.mp.microsoft.com/v7.0/products/lookup?market=US&languages=en-US&alternateId=PackageFamilyName&value={pfn}";
            var response = await _httpClient.GetFromJsonAsync<System.Text.Json.JsonElement>(url);

            if (response.TryGetProperty("Products", out var products) && products.GetArrayLength() > 0)
            {
                var product = products[0];
                if (product.TryGetProperty("ProductFamily", out var family))
                {
                    string familyStr = family.GetString() ?? "";
                    if (string.Equals(familyStr, "Games", StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                if (product.TryGetProperty("ProductKind", out var kind))
                {
                    string kindStr = kind.GetString() ?? "";
                    if (string.Equals(kindStr, "Game", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static async Task<bool> CheckManifestForGameMarkersAsync(string aumid)
    {
        return await Task.Run(() =>
        {
            try
            {
                string pfn = aumid.Contains("!") ? aumid.Split('!')[0] : aumid;
                var package = _packageManager.FindPackageForUser("", pfn);
                if (package == null) return false;

                string manifestPath = Path.Combine(package.InstalledLocation.Path, "AppxManifest.xml");
                XmlDocument doc = new XmlDocument();
                doc.Load(manifestPath);

                XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("uap", "http://schemas.microsoft.com/appx/manifest/uap/windows10");
                nsmgr.AddNamespace("res", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");

                var protocols = doc.SelectNodes("//*[local-name()='Protocol']", nsmgr);
                if (protocols != null)
                {
                    foreach (XmlNode protocol in protocols)
                    {
                        string? protoName = protocol.Attributes?["Name"]?.Value;
                        if (protoName != null && (protoName.Contains("xbox", StringComparison.OrdinalIgnoreCase) || protoName.StartsWith("ms-xbl-", StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }
                }

                var capabilities = doc.SelectNodes("//*[local-name()='Capability' or local-name()='uap:Capability' or local-name()='resCap:Capability']", nsmgr);
                if (capabilities != null)
                {
                    foreach (XmlNode cap in capabilities)
                    {
                        string? capName = cap.Attributes?["Name"]?.Value;
                        if (capName != null && (capName.Contains("xbox", StringComparison.OrdinalIgnoreCase) || capName.Equals("gameList", StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }
                }

                var extensions = doc.SelectNodes("//*[local-name()='Extension']", nsmgr);
                if (extensions != null)
                {
                    foreach (XmlNode ext in extensions)
                    {
                        string? category = ext.Attributes?["Category"]?.Value;
                        if (category != null && category.Contains("gameBar", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        });
    }

    public static async Task<List<ScannedGame>> GetAllInstalledGamesAsync()
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
                    catch { }
                    return null;
                }));
            }

            var games = await Task.WhenAll(tasks);
            results.AddRange(games.Where(g => g != null)!);
        }
        catch (Exception) { }

        return results;
    }
}
