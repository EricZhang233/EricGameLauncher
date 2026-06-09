using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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

    [YamlMember(Alias = "updateChannel")]
    public string UpdateChannel { get; set; } = "stable";

    [YamlMember(Alias = "window")]
    public WindowBoundsInfo Window { get; set; } = new();

    [YamlMember(Alias = "githubToken")]
    public string GitHubToken { get; set; } = "";
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

public class ItemsData
{
    [YamlMember(Alias = "version")]
    public int Version { get; set; } = ConfigService.CurrentConfigVersion;

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
    private const string SettingsFileName = "settings.yaml";
    private const string ItemsFileName = "items.yaml";
    private const string IconFolderName = "ico";

    public const int CurrentConfigVersion = 1;

    public static bool RequiresMigration { get; private set; } = false;
    private static bool _blockSaving = false;

    private static string SystemBasePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "eric", AppFolderName);
    private static string PortableBasePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

    private static string? _overrideSystemCachePath;
    public static string SystemCachePath => _overrideSystemCachePath ?? Path.Combine(Path.GetTempPath(), "eric", "ericgamelauncher");

    private static bool _debugModeApplied = false;

    public static string SettingsFilePath => Path.Combine(CurrentDataPath, SettingsFileName);
    public static string ItemsFilePath => Path.Combine(CurrentDataPath, ItemsFileName);
    public static string FixedCachePath => Path.Combine(CurrentDataPath, IconFolderName);
    public static string CurrentDataPath { get; private set; } = "";
    private static AppSettings? _settings;
    private static ItemsData? _itemsData;

    public static string LaunchMode
    {
        get => _settings?.LaunchMode ?? "single";
        set { if (_settings != null) _settings.LaunchMode = value; }
    }

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

        string portableSettingsPath = Path.Combine(PortableBasePath, SettingsFileName);
        string portableItemsPath = Path.Combine(PortableBasePath, ItemsFileName);
        string systemSettingsPath = Path.Combine(SystemBasePath, SettingsFileName);
        string systemItemsPath = Path.Combine(SystemBasePath, ItemsFileName);

        if (File.Exists(portableSettingsPath) || File.Exists(portableItemsPath))
            CurrentDataPath = PortableBasePath;
        else if (File.Exists(systemSettingsPath) || File.Exists(systemItemsPath))
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

            SaveAll();

            string[] fileNames = { SettingsFileName, ItemsFileName };
            foreach (var fn in fileNames)
            {
                string oldPath = Path.Combine(CurrentDataPath, fn);
                string newFilePath = Path.Combine(newPath, fn);
                if (File.Exists(oldPath))
                {
                    if (!Directory.Exists(newPath)) Directory.CreateDirectory(newPath);
                    File.Copy(oldPath, newFilePath, true);
                    File.Delete(oldPath);
                }
            }

            string oldIconPath = Path.Combine(CurrentDataPath, IconFolderName);
            string newIconPath = Path.Combine(newPath, IconFolderName);
            if (Directory.Exists(oldIconPath))
            {
                if (!Directory.Exists(newIconPath)) Directory.CreateDirectory(newIconPath);
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
        if (_itemsData == null) return;
        LogService.Write("Config", $"SaveItems Start items={items.Count} recycle={recycleItems.Count}");
        lock (_itemsData)
        {
            foreach (var item in items)
            {
                item.Status = (int)AppItemStatus.Normal;
                item.DeletedAt = null;
            }
            _itemsData.Items = items.Select(AppItemDto.FromViewModel).ToList();
            _itemsData.RecycleBinItems = recycleItems.Select(AppItemDto.FromViewModel).ToList();
        }
        if (triggerEvent) DataChanged?.Invoke();
        LogService.Write("Config", $"SaveItems End duration={sw.ElapsedMilliseconds}ms");
    }

    public static List<AppItem> LoadItems()
    {
        var dtos = _itemsData?.Items ?? [];
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
        var dtos = _itemsData?.RecycleBinItems ?? [];
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
            if (string.IsNullOrEmpty(CurrentDataPath)) { _settings = new AppSettings(); _itemsData = new ItemsData(); return; }

            string settingsPath = Path.Combine(CurrentDataPath, SettingsFileName);
            string itemsPath = Path.Combine(CurrentDataPath, ItemsFileName);

            if (File.Exists(settingsPath))
            {
                try
                {
                    string settingsYaml = File.ReadAllText(settingsPath);
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .IgnoreUnmatchedProperties()
                        .Build();
                    _settings = deserializer.Deserialize<AppSettings>(settingsYaml) ?? new AppSettings();
                }
                catch (Exception ex) { LogService.Write("Config", "LoadConfigData settings load failed", ex); _settings = new AppSettings(); }
            }
            else
            {
                _settings = new AppSettings();
            }

            if (File.Exists(itemsPath))
            {
                try
                {
                    string itemsYaml = File.ReadAllText(itemsPath);
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .IgnoreUnmatchedProperties()
                        .Build();
                    _itemsData = deserializer.Deserialize<ItemsData>(itemsYaml) ?? new ItemsData();
                }
                catch (Exception ex) { LogService.Write("Config", "LoadConfigData items load failed", ex); _itemsData = new ItemsData(); }
            }
            else
            {
                _itemsData = new ItemsData();
            }

            if (_itemsData.Version < CurrentConfigVersion)
            {
                RequiresMigration = true;
                _blockSaving = true;
                _itemsData = new ItemsData();
                LogService.Write("Config", "LoadConfigData version mismatch, migration required");
                return;
            }

            _settings ??= new AppSettings();
            _itemsData ??= new ItemsData();
            _itemsData.Items ??= [];
            _itemsData.RecycleBinItems ??= [];

            if (_itemsData.RecycleBinItems.Count == 0 && _itemsData.Items.Any(x => x.Status != (int)AppItemStatus.Normal))
            {
                var normalItems = _itemsData.Items.Where(x => x.Status == (int)AppItemStatus.Normal).ToList();
                var recycleItems = _itemsData.Items.Where(x => x.Status != (int)AppItemStatus.Normal).ToList();
                _itemsData.Items = normalItems;
                _itemsData.RecycleBinItems = recycleItems;
            }
            bool normalized = false;
            if (_itemsData.Items.Any(x => x.Status != (int)AppItemStatus.Normal))
            {
                var recycleIds = new HashSet<string>(_itemsData.RecycleBinItems.Select(x => x.Id));
                var moveItems = _itemsData.Items.Where(x => x.Status != (int)AppItemStatus.Normal).ToList();
                _itemsData.Items = _itemsData.Items.Where(x => x.Status == (int)AppItemStatus.Normal).ToList();
                foreach (var item in moveItems)
                {
                    if (recycleIds.Add(item.Id))
                    {
                        _itemsData.RecycleBinItems.Add(item);
                    }
                }
                normalized = true;
            }
            foreach (var item in _itemsData.RecycleBinItems)
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
                SaveItemsData();
            }
            LogService.Write("Config", "LoadConfigData End");
        }
        catch (Exception ex)
        {
            LogService.Write("Config", $"LoadConfigData failed", ex);
            _settings ??= new AppSettings();
            _itemsData ??= new ItemsData();
        }
    }

    public static void SaveAll()
    {
        if (_blockSaving)
        {
            LogService.Write("Config", "SaveAll blocked (migration pending)");
            return;
        }
        LogService.Write("Config", "SaveAll Start");
        SaveSettingsData();
        SaveItemsData();
        LogService.Write("Config", "SaveAll End");
    }

    private static void SaveSettingsData()
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentDataPath) || _settings == null) return;
            string path = Path.Combine(CurrentDataPath, SettingsFileName);
            var serializer = new SerializerBuilder().Build();
            string yaml = serializer.Serialize(_settings);
            if (!Directory.Exists(CurrentDataPath)) Directory.CreateDirectory(CurrentDataPath);
            File.WriteAllText(path, yaml);
            LogService.Write("Config", "SaveSettingsData written");
        }
        catch (Exception ex) { LogService.Write("Config", "SaveSettingsData failed", ex); }
    }

    private static void SaveItemsData()
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentDataPath) || _itemsData == null) return;
            string path = Path.Combine(CurrentDataPath, ItemsFileName);
            var serializer = new SerializerBuilder().Build();
            string yaml = serializer.Serialize(_itemsData);
            if (!Directory.Exists(CurrentDataPath)) Directory.CreateDirectory(CurrentDataPath);
            File.WriteAllText(path, yaml);
            LogService.Write("Config", "SaveItemsData written");
        }
        catch (Exception ex) { LogService.Write("Config", "SaveItemsData failed", ex); }
    }

    public static bool CloseAfterLaunch
    {
        get => _settings?.CloseAfterLaunch ?? false;
        set { if (_settings != null) { LogService.Write("Config", $"CloseAfterLaunch changed to={value}"); _settings.CloseAfterLaunch = value; } }
    }

    public static double IconSize
    {
        get => _settings?.IconSize ?? 118;
        set { if (_settings != null) { LogService.Write("Config", $"IconSize changed to={value}"); _settings.IconSize = value; } }
    }

    public static string Language => I18n.DetectSystemLanguage();

    public static string UpdateChannel
    {
        get => _settings?.UpdateChannel ?? "stable";
        set { if (_settings != null) { LogService.Write("Config", $"UpdateChannel set to={value}"); _settings.UpdateChannel = value; } }
    }

    public static string GitHubToken
    {
        get => _settings?.GitHubToken ?? "";
        set { if (_settings != null) { LogService.Write("Config", $"GitHubToken changed"); _settings.GitHubToken = value ?? ""; } }
    }

    public static bool IsSystemMode => CurrentDataPath == SystemBasePath;

    public static async Task<bool> ReconstructMissingConfigAsync()
    {
        using (LogService.StartOperation("Config", "ReconstructMissingConfigAsync"))
        {
            if (_itemsData == null) return false;

            var items = _itemsData.Items ?? [];
            var recycleItems = _itemsData.RecycleBinItems ?? [];
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
                SaveItemsData();
            }
            LogService.Write("Config", "Reconstruct End");
            return modified;
        }
    }

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
        var bounds = _settings?.Window;
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
        if (_settings != null)
        {
            _settings.Window = new WindowBoundsInfo { X = x, Y = y, Width = width, Height = height };
            try { LogService.Write("Config", "SetWindowBounds applied"); } catch { }
        }
    }
}