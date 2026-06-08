using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EricGameLauncher;

public class AppSettings
{
    [YamlMember(Alias = "launchMode")]
    public string LaunchMode { get; set; } = "single";

    [YamlMember(Alias = "closeAfterLaunch")]
    public bool CloseAfterLaunch { get; set; } = false;

    [YamlMember(Alias = "iconSize")]
    public double IconSize { get; set; } = 118;

    [YamlMember(Alias = "language")]
    public string Language { get; set; } = "";

    [YamlMember(Alias = "updateChannel")]
    public string UpdateChannel { get; set; } = "stable";

    [YamlMember(Alias = "window")]
    public WindowBoundsInfo Window { get; set; } = new();
}

public class WindowBoundsInfo
{
    [YamlMember(Alias = "x")]
    public int X { get; set; } = -1;
    [YamlMember(Alias = "y")]
    public int Y { get; set; } = -1;
    [YamlMember(Alias = "width")]
    public int Width { get; set; } = 950;
    [YamlMember(Alias = "height")]
    public int Height { get; set; } = 650;
}

public class ConfigData
{
    [YamlMember(Alias = "version")]
    public int Version { get; set; } = ConfigService.CurrentConfigVersion;

    [YamlMember(Alias = "settings")]
    public AppSettings Settings { get; set; } = new();

    [YamlMember(Alias = "items")]
    public List<AppItemDto> Items { get; set; } = [];

    [YamlMember(Alias = "recycleBin")]
    public List<AppItemDto> RecycleBinItems { get; set; } = [];
}

public class ServerConfigInfo
{
    [YamlMember(Alias = "forceUpdate")]
    public ForceUpdateInfo? ForceUpdate { get; set; }

    [YamlMember(Alias = "announcements")]
    public List<Announcement>? Announcements { get; set; }
}

public class ForceUpdateInfo
{
    [YamlMember(Alias = "minVersion")]
    public string MinVersion { get; set; } = "";
}

public static class ServerConfigManager
{
    private static readonly HttpClient client = new HttpClient();
    public static ServerConfigInfo? CurrentConfig { get; private set; }
    public static event Action? AnnouncementsUpdated;
    private static readonly string ReadIdsFileName = "announcements.read";
    private static HashSet<string> _readIds = new(StringComparer.OrdinalIgnoreCase);

    public static void LoadReadIds()
    {
        try
        {
            string cacheDir = ConfigService.SystemCachePath;
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
            string path = Path.Combine(cacheDir, ReadIdsFileName);
            if (File.Exists(path))
            {
                var lines = File.ReadAllLines(path);
                _readIds = new HashSet<string>(lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()), StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                _readIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { _readIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    public static void SaveReadIds()
    {
        try
        {
            string cacheDir = ConfigService.SystemCachePath;
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
            string path = Path.Combine(cacheDir, ReadIdsFileName);
            File.WriteAllLines(path, _readIds);
        }
        catch { }
    }

    public static bool IsRead(string id) => !string.IsNullOrEmpty(id) && _readIds.Contains(id);

    public static void MarkAsRead(string id, bool notify = true)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (_readIds.Add(id))
        {
            SaveReadIds();
            if (notify)
            {
                try { AnnouncementsUpdated?.Invoke(); } catch { }
            }
        }
    }

    public static async Task FetchConfigAsync()
    {
        using (LogService.StartOperation("Config", "FetchConfigAsync"))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                LogService.Write("Network", "ServerConfig Fetch Start");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EricGameLauncher");
                var content = await client.GetStringAsync("https://raw.githubusercontent.com/EricZhang233/EricGameLauncher/master/ServerCfg.yaml");

                var deserializer = new DeserializerBuilder()
                    .IgnoreUnmatchedProperties()
                    .Build();

                CurrentConfig = deserializer.Deserialize<ServerConfigInfo>(content);
                if (CurrentConfig?.Announcements != null)
                {
                    foreach (var a in CurrentConfig.Announcements)
                    {
                        a.Position = (a.Position ?? "").Trim().ToLowerInvariant();
                        a.TitleCn = NormalizeSingleLine(a.TitleCn);
                        a.TitleZh = NormalizeSingleLine(a.TitleZh);
                        a.TitleEn = NormalizeSingleLine(a.TitleEn);
                        a.BodyCn = NormalizeMultiline(a.BodyCn).Trim();
                        a.BodyZh = NormalizeMultiline(a.BodyZh).Trim();
                        a.BodyEn = NormalizeMultiline(a.BodyEn).Trim();
                    }

                    var first = CurrentConfig.Announcements.FirstOrDefault();
                    if (first != null)
                    {
                        LogService.Write("Announcement", $"Parsed announcements count={CurrentConfig.Announcements.Count} firstId={first.Id} titleCnLen={first.TitleCn.Length} titleZhLen={first.TitleZh.Length} titleEnLen={first.TitleEn.Length} bodyCnLen={first.BodyCn.Length} bodyZhLen={first.BodyZh.Length} bodyEnLen={first.BodyEn.Length}");
                    }
                }
                LogService.Write("Network", $"ServerConfig Fetch End (YAML) Duration={sw.ElapsedMilliseconds}ms Size={content?.Length ?? 0}");
            }
            catch (Exception ex)
            {
                CurrentConfig = null;
                LogService.Write("Network", $"ServerConfig Fetch Failed Duration={sw.ElapsedMilliseconds}ms", ex);
            }
                finally
                {
                    try { AnnouncementsUpdated?.Invoke(); } catch { }
                }
        }
    }

    private static string NormalizeSingleLine(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var normalized = s.Replace("\r\n", "\n").Replace("\r", "\n");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, "\n+", " ");
        return normalized.Trim();
    }

    private static string NormalizeMultiline(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var normalized = s.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd();
        return string.Join("\n", lines);
    }

    public static List<Announcement> GetActiveAnnouncements()
    {
        var now = DateTime.UtcNow;
        var anns = CurrentConfig?.Announcements ?? new List<Announcement>();
        return anns
            .Where(a => a.Visible && (a.GetTimeValue() == null || a.GetTimeValue()!.Value.UtcDateTime <= now))
            .OrderBy(a => a.GetPositionPriority())
            .ThenByDescending(a => a.GetTimeValue() ?? DateTimeOffset.MinValue)
            .ToList();
    }
}

public static class ConfigService
{
    private const string AppFolderName = "EricGameLauncher";
    private const string DataFileName = "config.yaml";
    private const string IconFolderName = "ico";

    public const int CurrentConfigVersion = 1;

    public static bool RequiresMigration { get; private set; } = false;
    private static bool _blockSaving = false;

    private static string SystemBasePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "eric", AppFolderName);
    private static string PortableBasePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

    private static string? _overrideSystemCachePath;
    public static string SystemCachePath => _overrideSystemCachePath ?? Path.Combine(Path.GetTempPath(), "eric", "ericgamelauncher");

    private static bool _debugModeApplied = false;

    public static string ConfigFilePath => Path.Combine(CurrentDataPath, DataFileName);
    public static string FixedCachePath => Path.Combine(CurrentDataPath, IconFolderName);
    public static string CurrentDataPath { get; private set; } = "";
    private static ConfigData? _configData;

    public static string LaunchMode
    {
        get => _configData?.Settings?.LaunchMode ?? "single";
        set { if (_configData?.Settings != null) _configData.Settings.LaunchMode = value; }
    }

    private static readonly SemaphoreSlim _saveSemaphore = new(1, 1);
    public static event Action? DataChanged;

    public static void Initialize()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        LogService.Write("Config", "Initialize Start");
        if (_debugModeApplied)
        {
            try { LogService.Write("Config", "Initialize detected debug mode, skipping normal path selection"); } catch { }
            if (!Directory.Exists(CurrentDataPath)) Directory.CreateDirectory(CurrentDataPath);
            if (!Directory.Exists(FixedCachePath)) Directory.CreateDirectory(FixedCachePath);
            if (!Directory.Exists(SystemCachePath)) Directory.CreateDirectory(SystemCachePath);
            LoadConfigData();
            LogService.Write("Config", $"Initialize End path={CurrentDataPath} mode=Debug duration={sw.ElapsedMilliseconds}ms");
            return;
        }

        if (!Directory.Exists(SystemBasePath)) Directory.CreateDirectory(SystemBasePath);

        string portableYamlPath = Path.Combine(PortableBasePath, DataFileName);
        string systemYamlPath = Path.Combine(SystemBasePath, DataFileName);

        if (File.Exists(portableYamlPath))
            CurrentDataPath = PortableBasePath;
        else if (File.Exists(systemYamlPath))
            CurrentDataPath = SystemBasePath;
        else
            CurrentDataPath = SystemBasePath;

        if (!Directory.Exists(CurrentDataPath)) Directory.CreateDirectory(CurrentDataPath);
        if (!Directory.Exists(FixedCachePath)) Directory.CreateDirectory(FixedCachePath);

        LoadConfigData();
        LogService.Write("Config", $"Initialize End path={CurrentDataPath} mode={(CurrentDataPath == SystemBasePath ? "System" : "Portable")} duration={sw.ElapsedMilliseconds}ms");
    }

    public static void ApplyDebugMode(string baseDir)
    {
        try
        {
            if (string.IsNullOrEmpty(baseDir)) return;
            string dataPath = Path.Combine(baseDir, "Data");
            string cachePath = Path.Combine(baseDir, "Cache");
            CurrentDataPath = dataPath;
            _overrideSystemCachePath = cachePath;
            _debugModeApplied = true;
            if (!Directory.Exists(CurrentDataPath)) Directory.CreateDirectory(CurrentDataPath);
            if (!Directory.Exists(FixedCachePath)) Directory.CreateDirectory(FixedCachePath);
            if (!Directory.Exists(SystemCachePath)) Directory.CreateDirectory(SystemCachePath);
            LogService.Write("Config", $"ApplyDebugMode applied baseDir={baseDir} data={CurrentDataPath} cache={SystemCachePath}");
        }
        catch (Exception ex) { LogService.Write("Config", "ApplyDebugMode failed", ex); }
    }

    public static void SwitchStorageMode(bool useSystemPath)
    {
        try
        {
            string newPath = useSystemPath ? SystemBasePath : PortableBasePath;
            if (CurrentDataPath == newPath) return;

            string oldConfigPath = Path.Combine(CurrentDataPath, DataFileName);
            string newConfigPath = Path.Combine(newPath, DataFileName);
            string oldIconPath = Path.Combine(CurrentDataPath, IconFolderName);
            string newIconPath = Path.Combine(newPath, IconFolderName);

            if (!Directory.Exists(newPath)) Directory.CreateDirectory(newPath);
            if (!Directory.Exists(newIconPath)) Directory.CreateDirectory(newIconPath);

            if (File.Exists(oldConfigPath))
            {
                File.Copy(oldConfigPath, newConfigPath, true);
                File.Delete(oldConfigPath);
            }

            if (Directory.Exists(oldIconPath))
            {
                foreach (var iconFile in Directory.GetFiles(oldIconPath))
                {
                    string fileName = Path.GetFileName(iconFile);
                    string destFile = Path.Combine(newIconPath, fileName);
                    File.Copy(iconFile, destFile, true);
                }
                try { Directory.Delete(oldIconPath, true); } catch (Exception ex) { LogService.Write("Config", "Delete old icon path failed", ex); }
            }

            CurrentDataPath = newPath;
            SteamHelper.ClearCache();
            LoadConfigData();
        }
        catch (Exception ex) { LogService.Write("Config", "SwitchStorageMode failed", ex); }
    }

    public static Task SwitchStorageModeAsync(bool useSystemPath) => Task.Run(() => SwitchStorageMode(useSystemPath));


    public static void SaveItems(List<AppItem> items, List<AppItem> recycleItems, bool triggerEvent = true)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (_configData == null) return;
        LogService.Write("Config", $"SaveItems Start items={items.Count} recycle={recycleItems.Count}");
        lock (_configData)
        {
            foreach (var item in items)
            {
                item.Status = (int)AppItemStatus.Normal;
                item.DeletedAt = null;
            }
            _configData.Items = items.Select(AppItemDto.FromViewModel).ToList();
            _configData.RecycleBinItems = recycleItems.Select(AppItemDto.FromViewModel).ToList();
        }
        SaveConfig();
        if (triggerEvent) DataChanged?.Invoke();
        LogService.Write("Config", $"SaveItems End duration={sw.ElapsedMilliseconds}ms");
    }

    public static List<AppItem> LoadItems()
    {
        var dtos = _configData?.Items ?? [];
        LogService.Write("Config", $"LoadItems count={dtos.Count}");
        var items = dtos.Select(dto => dto.ToViewModel(FixedCachePath)).ToList();
        for (int i = 0; i < items.Count; i++)
        {
            items[i].SortOrder = i;
        }
        return items;
    }

    public static List<AppItem> LoadRecycleBinItems()
    {
        var dtos = _configData?.RecycleBinItems ?? [];
        LogService.Write("Config", $"LoadRecycleBinItems count={dtos.Count}");
        var items = dtos.Select(dto => dto.ToViewModel(FixedCachePath)).ToList();
        for (int i = 0; i < items.Count; i++)
        {
            items[i].SortOrder = i;
        }
        return items;
    }

    private static void LoadConfigData()
    {
        try
        {
            LogService.Write("Config", "LoadConfigData Start");
            if (string.IsNullOrEmpty(CurrentDataPath)) { _configData = new ConfigData(); return; }
            string yamlPath = Path.Combine(CurrentDataPath, DataFileName);
            if (!File.Exists(yamlPath)) { _configData = new ConfigData(); return; }

            string yamlText = File.ReadAllText(yamlPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            _configData = deserializer.Deserialize<ConfigData>(yamlText) ?? new ConfigData();
            if (_configData.Version < CurrentConfigVersion)
            {
                RequiresMigration = true;
                _blockSaving = true;
                _configData = new ConfigData();
                return;
            }

            LogService.Write("Config", $"LoadConfigData deserialized items={_configData.Items?.Count ?? 0} recycle={_configData.RecycleBinItems?.Count ?? 0}");
            _configData.Settings ??= new AppSettings();
            _configData.Items ??= [];
            _configData.RecycleBinItems ??= [];
            if (_configData.RecycleBinItems.Count == 0 && _configData.Items.Any(x => x.Status != (int)AppItemStatus.Normal))
            {
                var normalItems = _configData.Items.Where(x => x.Status == (int)AppItemStatus.Normal).ToList();
                var recycleItems = _configData.Items.Where(x => x.Status != (int)AppItemStatus.Normal).ToList();
                _configData.Items = normalItems;
                _configData.RecycleBinItems = recycleItems;
            }
            bool normalized = false;
            if (_configData.Items.Any(x => x.Status != (int)AppItemStatus.Normal))
            {
                var recycleIds = new HashSet<string>(_configData.RecycleBinItems.Select(x => x.Id));
                var moveItems = _configData.Items.Where(x => x.Status != (int)AppItemStatus.Normal).ToList();
                _configData.Items = _configData.Items.Where(x => x.Status == (int)AppItemStatus.Normal).ToList();
                foreach (var item in moveItems)
                {
                    if (recycleIds.Add(item.Id))
                    {
                        _configData.RecycleBinItems.Add(item);
                    }
                }
                normalized = true;
            }
            foreach (var item in _configData.RecycleBinItems)
            {
                if (item.Status == (int)AppItemStatus.Normal)
                {
                    item.Status = (int)AppItemStatus.Recycled;
                    item.DeletedAt = null;
                    normalized = true;
                }
            }
            if (normalized)
            {
                SaveConfigData();
            }
            LogService.Write("Config", "LoadConfigData End");
        }
        catch (Exception ex)
        {
            LogService.Write("Config", $"LoadConfigData failed", ex);
            if (_configData == null) _configData = new ConfigData();
        }
    }

    private static void SaveConfigData()
    {
        if (_blockSaving) return;

        LogService.Write("Config", "SaveConfigData Start");

        _ = Task.Run(async () =>
        {
            await _saveSemaphore.WaitAsync();
            try
            {
                if (string.IsNullOrEmpty(CurrentDataPath) || _configData == null) return;
                string yamlPath = Path.Combine(CurrentDataPath, DataFileName);

                string yamlString;
                lock (_configData)
                {
                    var serializer = new SerializerBuilder().Build();
                    yamlString = serializer.Serialize(_configData);
                }
                yamlString = YamlQuoteHelper.ToSingleQuoted(yamlString);

                if (!Directory.Exists(CurrentDataPath)) Directory.CreateDirectory(CurrentDataPath);
                await File.WriteAllTextAsync(yamlPath, yamlString);
            }
            catch (Exception ex) { LogService.Write("Config", $"SaveConfigData failed", ex); }
            finally
            {
                _saveSemaphore.Release();
                LogService.Write("Config", "SaveConfigData End");
            }
        });
    }

    public static bool CloseAfterLaunch
    {
        get => _configData?.Settings?.CloseAfterLaunch ?? false;
        set { if (_configData?.Settings != null) { LogService.Write("Config", $"CloseAfterLaunch changed to={value}"); _configData.Settings.CloseAfterLaunch = value; } }
    }

    public static double IconSize
    {
        get => _configData?.Settings?.IconSize ?? 118;
        set { if (_configData?.Settings != null) { LogService.Write("Config", $"IconSize changed to={value}"); _configData.Settings.IconSize = value; } }
    }

    public static string Language
    {
        get
        {
            var lang = _configData?.Settings?.Language;
            if (string.IsNullOrEmpty(lang))
            {
                lang = I18n.DetectSystemLanguage();
                if (_configData?.Settings != null)
                {
                    _configData.Settings.Language = lang;
                    SaveConfig();
                }
            }
            return lang;
        }
        set { if (_configData?.Settings != null) { LogService.Write("Config", $"Language set to={value}"); _configData.Settings.Language = value; } }
    }

    public static string UpdateChannel
    {
        get => _configData?.Settings?.UpdateChannel ?? "stable";
        set { if (_configData?.Settings != null) { LogService.Write("Config", $"UpdateChannel set to={value}"); _configData.Settings.UpdateChannel = value; } }
    }

    public static bool IsSystemMode => CurrentDataPath == SystemBasePath;

    public static async Task<bool> ReconstructMissingConfigAsync()
    {
        using (LogService.StartOperation("Config", "ReconstructMissingConfigAsync"))
        {
            if (_configData == null) return false;

            var items = _configData.Items ?? [];
            var recycleItems = _configData.RecycleBinItems ?? [];
            if (items.Count == 0 && recycleItems.Count == 0) return false;

            bool modified = false;
            LogService.Write("Config", "Reconstruct Start");
            foreach (var item in items.Concat(recycleItems))
            {
                bool itemChanged = false;

                string? exePath = item.Actions?.Main?.Path;
                if (string.IsNullOrEmpty(item.Platform) && !string.IsNullOrEmpty(exePath))
                {
                    try
                    {
                        var platform = await GamePlatformHelper.DetectPlatformAsync(exePath);
                        if (platform != null)
                        {
                            item.Platform = platform.PlatformName;
                            itemChanged = true;
                            LogService.Write("Config", $"Reconstruct Set platform: {item.Platform} {item.Title}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Write("Config", $"Reconstruct Detect platform failed: {item.Title}", ex);
                    }
                }

                if (itemChanged) modified = true;
            }

            if (modified)
            {
                SaveConfigData();
            }
            LogService.Write("Config", "Reconstruct End");
            return modified;
        }
    }

    public static void SaveConfig() => SaveConfigData();

    public static async Task RefreshGlobalAsync()
    {
        using (LogService.StartOperation("Config", "RefreshGlobalAsync"))
        {
            var sw = Stopwatch.StartNew();
            LogService.Write("Config", "RefreshGlobal Start");
            var items = LoadItems();
            var recycleItems = LoadRecycleBinItems();
            var allItems = items.Concat(recycleItems).ToList();
            LogService.Write("Config", $"RefreshGlobal AfterLoadItems {sw.ElapsedMilliseconds}ms");
            var rebuildTasks = new List<Task>();
            int successfulRebuilds = 0;

            foreach (var item in allItems)
            {
                if (string.IsNullOrEmpty(item.IconPath) || !File.Exists(item.IconPath))
                {
                    rebuildTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            string? sourcePath = item.ExePath;
                            string? resolvedPath = null;

                            if (SteamHelper.ExtractAppIdFromUrl(item.ExePath!) is int)
                                resolvedPath = SteamHelper.GetExecutableFromSteamUrl(item.ExePath!);
                            else if (GamePlatformHelper.DetectPlatform(item.ExePath!)?.PlatformName == "Epic Games")
                                resolvedPath = EpicGamesHelper.GetExecutableFromEpicUrl(item.ExePath!);
                            else if (!string.IsNullOrEmpty(item.ExePath) &&
                                        (item.ExePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                            item.ExePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase)))
                            {
                                if (File.Exists(item.ExePath))
                                {
                                    if (item.ExePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var info = ShortcutResolver.GetShortcutInfo(item.ExePath);
                                        if (info != null && !string.IsNullOrEmpty(info.TargetPath))
                                            resolvedPath = info.TargetPath;
                                    }
                                    else if (item.ExePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var info = ShortcutResolver.GetUrlFileInfo(item.ExePath);
                                        if (info != null && !string.IsNullOrEmpty(info.TargetPath))
                                            resolvedPath = info.TargetPath;
                                    }
                                }
                            }
                            else
                            {
                                resolvedPath = item.ExePath;
                            }

                            string? iconPath = null;
                            bool resolvedIsStoreApp = !string.IsNullOrEmpty(resolvedPath) &&
                                                        resolvedPath.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase);
                            bool sourceIsStoreApp = !string.IsNullOrEmpty(sourcePath) &&
                                                    sourcePath.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase);

                            if (!string.IsNullOrEmpty(resolvedPath) &&
                                (resolvedIsStoreApp || File.Exists(resolvedPath)))
                            {
                                iconPath = await IconHelper.GetIconPathAsync(resolvedPath, item.Id);
                                if (string.IsNullOrEmpty(iconPath) &&
                                    !string.IsNullOrEmpty(sourcePath) &&
                                    (sourceIsStoreApp || File.Exists(sourcePath)) &&
                                    sourcePath != resolvedPath)
                                {
                                    iconPath = await IconHelper.GetIconPathAsync(sourcePath, item.Id);
                                }
                            }
                            else if (!string.IsNullOrEmpty(sourcePath) &&
                                        (sourceIsStoreApp || File.Exists(sourcePath)))
                            {
                                iconPath = await IconHelper.GetIconPathAsync(sourcePath, item.Id);
                            }

                            if (!string.IsNullOrEmpty(iconPath))
                            {
                                Interlocked.Increment(ref successfulRebuilds);
                                item.IconPath = iconPath;
                            }
                        }
                        catch (Exception ex) { LogService.Write("Config", "RefreshGlobal rebuild task failed", ex); }
                    }));
                }
            }

            if (rebuildTasks.Any())
            {
                LogService.Write("Config", $"RefreshGlobal BeforeRebuildAwait {sw.ElapsedMilliseconds}ms");
                await Task.WhenAll(rebuildTasks);
                LogService.Write("Config", $"RefreshGlobal AfterRebuildAwait {sw.ElapsedMilliseconds}ms");
                if (successfulRebuilds > 0)
                {
                    SaveItems(items, recycleItems, false);
                }
            }

            DataChanged?.Invoke();
            LogService.Write("Config", $"RefreshGlobal AfterDataChanged {sw.ElapsedMilliseconds}ms");
        }
    }

    public static (int X, int Y, int Width, int Height) GetWindowBounds()
    {
        try { LogService.Write("Config", "GetWindowBounds called"); } catch { }
        var bounds = _configData?.Settings?.Window;
        if (bounds != null)
        {
            try { LogService.Write("Config", $"GetWindowBounds returning x={bounds.X} y={bounds.Y} w={bounds.Width} h={bounds.Height}"); } catch { }
            return (bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
        try { LogService.Write("Config", "GetWindowBounds returning default"); } catch { }
        return (-1, -1, 950, 650);
    }

    public static void SetWindowBounds(int x, int y, int width, int height)
    {
        try { LogService.Write("Config", $"SetWindowBounds called x={x} y={y} w={width} h={height}"); } catch { }
        if (_configData?.Settings != null)
        {
            _configData.Settings.Window = new WindowBoundsInfo { X = x, Y = y, Width = width, Height = height };
            try { LogService.Write("Config", "SetWindowBounds applied"); } catch { }
        }
    }
}

internal static class YamlQuoteHelper
{
    public static string ToSingleQuoted(string yaml)
    {
        return Regex.Replace(yaml, @": ""((?:[^""\\]|\\.)*)""", match =>
        {
            string escaped = match.Groups[1].Value;
            string unescaped = UnescapeDoubleQuotedYaml(escaped);
            return ": '" + unescaped.Replace("'", "''") + "'";
        });
    }

    private static string UnescapeDoubleQuotedYaml(string s)
    {
        return Regex.Replace(s, @"\\x[0-9a-fA-F]{2}|\\u[0-9a-fA-F]{4}|\\U[0-9a-fA-F]{8}|\\(.)", m =>
        {
            if (m.Value.StartsWith("\\x"))
                return ((char)Convert.ToInt32(m.Value.Substring(2), 16)).ToString();
            if (m.Value.StartsWith("\\u"))
                return ((char)Convert.ToInt32(m.Value.Substring(2), 16)).ToString();
            if (m.Value.StartsWith("\\U"))
                return char.ConvertFromUtf32(Convert.ToInt32(m.Value.Substring(2), 16));
            return m.Groups[1].Value switch
            {
                "\\" => "\\",
                "\"" => "\"",
                "0" => "\0",
                "a" => "\a",
                "b" => "\b",
                "t" => "\t",
                "n" => "\n",
                "v" => "\v",
                "f" => "\f",
                "r" => "\r",
                "e" => "\u001b",
                "/" => "/",
                _ => m.Groups[1].Value
            };
        });
    }
}