using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace EricGameLauncher;

public class AppSettings
{
    [JsonPropertyName("launchMode")]
    public string LaunchMode { get; set; } = "single";

    [JsonPropertyName("closeAfterLaunch")]
    public bool CloseAfterLaunch { get; set; } = false;

    [JsonPropertyName("iconSize")]
    public double IconSize { get; set; } = 118;

    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    [JsonPropertyName("updateChannel")]
    public string UpdateChannel { get; set; } = "stable";

    [JsonPropertyName("windowBounds")]
    [JsonConverter(typeof(IntArrayJsonConverter))]
    public int[] WindowBounds { get; set; } = [-1, -1, 950, 650];
}

public class ConfigData
{
    [JsonPropertyName("Version")]
    public int Version { get; set; } = ConfigService.CurrentConfigVersion;

    [JsonPropertyName("settings")]
    public AppSettings Settings { get; set; } = new();

    [JsonPropertyName("items")]
    public List<AppItemDto> Items { get; set; } = [];

    [JsonPropertyName("recycleBinItems")]
    public List<AppItemDto> RecycleBinItems { get; set; } = [];
}

public class ServerConfigInfo
{
    [JsonPropertyName("forceUpdate")]
    public ForceUpdateInfo? ForceUpdate { get; set; }
}

public class ForceUpdateInfo
{
    [JsonPropertyName("minVersion")]
    public string MinVersion { get; set; } = "";
}

public static class ServerConfigManager
{
    private static readonly HttpClient client = new HttpClient();
    public static ServerConfigInfo? CurrentConfig { get; private set; }

    public static async Task FetchConfigAsync()
    {
        using (LogService.StartOperation("Config", "FetchConfigAsync"))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                LogService.Write("Network", "ServerConfig Fetch Start");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EricGameLauncher");
                var json = await client.GetStringAsync("https://raw.githubusercontent.com/EricZhang233/EricGameLauncher/master/ServerCfg.json");
                CurrentConfig = JsonSerializer.Deserialize<ServerConfigInfo>(json);
                LogService.Write("Network", $"ServerConfig Fetch End Duration={sw.ElapsedMilliseconds}ms Size={json?.Length ?? 0}");
            }
            catch (Exception ex)
            {
                CurrentConfig = null;
                LogService.Write("Network", $"ServerConfig Fetch Failed Duration={sw.ElapsedMilliseconds}ms", ex);
            }
        }
    }
}

public static class ConfigService
{
    private const string AppFolderName = "EricGameLauncher";
    private const string DataFileName = "config.json";
    private const string IconFolderName = "ico";

    public const int CurrentConfigVersion = 3;

    public static bool RequiresMigration { get; private set; } = false;
    private static bool _blockSaving = false;

    private static string SystemBasePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "eric", AppFolderName);
    private static string PortableBasePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

    public static string SystemCachePath => Path.Combine(Path.GetTempPath(), "eric", "ericgamelauncher");

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
        if (!Directory.Exists(SystemBasePath)) Directory.CreateDirectory(SystemBasePath);

        string portableConfigPath = Path.Combine(PortableBasePath, DataFileName);
        string systemConfigPath = Path.Combine(SystemBasePath, DataFileName);

        if (File.Exists(portableConfigPath))
            CurrentDataPath = PortableBasePath;
        else if (File.Exists(systemConfigPath))
            CurrentDataPath = SystemBasePath;
        else
            CurrentDataPath = SystemBasePath;

        if (!Directory.Exists(CurrentDataPath)) Directory.CreateDirectory(CurrentDataPath);
        if (!Directory.Exists(FixedCachePath)) Directory.CreateDirectory(FixedCachePath);

        LoadConfigData();
        LogService.Write("Config", $"Initialize End path={CurrentDataPath} mode={(CurrentDataPath == SystemBasePath ? "System" : "Portable")} duration={sw.ElapsedMilliseconds}ms");
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
        return dtos.Select(dto => dto.ToViewModel(FixedCachePath)).ToList();
    }

    public static List<AppItem> LoadRecycleBinItems()
    {
        var dtos = _configData?.RecycleBinItems ?? [];
        LogService.Write("Config", $"LoadRecycleBinItems count={dtos.Count}");
        return dtos.Select(dto => dto.ToViewModel(FixedCachePath)).ToList();
    }

    private static void LoadConfigData()
    {
        try
        {
            LogService.Write("Config", "LoadConfigData Start");
            if (string.IsNullOrEmpty(CurrentDataPath)) { _configData = new ConfigData(); return; }
            string jsonPath = Path.Combine(CurrentDataPath, DataFileName);
            if (!File.Exists(jsonPath)) { _configData = new ConfigData(); return; }

            string jsonString = File.ReadAllText(jsonPath);

            using (var doc = JsonDocument.Parse(jsonString))
            {
                int version = 1;
                if (doc.RootElement.TryGetProperty("Version", out var versionElement))
                {
                    version = versionElement.TryGetInt32(out int v) ? v : 1;
                }

                if (version < CurrentConfigVersion)
                {
                    RequiresMigration = true;
                    _blockSaving = true;
                    _configData = new ConfigData();
                    return;
                }
            }

            _configData = JsonSerializer.Deserialize<ConfigData>(jsonString) ?? new ConfigData();
            LogService.Write("Config", $"LoadConfigData deserialized jsonSize={jsonString.Length} items={_configData.Items?.Count ?? 0} recycle={_configData.RecycleBinItems?.Count ?? 0}");
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
                string jsonPath = Path.Combine(CurrentDataPath, DataFileName);

                string jsonString;
                lock (_configData)
                {
                    jsonString = JsonSerializer.Serialize(_configData, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    });
                }

                if (!Directory.Exists(CurrentDataPath)) Directory.CreateDirectory(CurrentDataPath);
                await File.WriteAllTextAsync(jsonPath, jsonString);
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

                string? exePath = item.MainAction?.Path;
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
        var bounds = _configData?.Settings?.WindowBounds;
        if (bounds != null && bounds.Length == 4)
        {
            try { LogService.Write("Config", $"GetWindowBounds returning x={bounds[0]} y={bounds[1]} w={bounds[2]} h={bounds[3]}"); } catch { }
            return (bounds[0], bounds[1], bounds[2], bounds[3]);
        }
        try { LogService.Write("Config", "GetWindowBounds returning default"); } catch { }
        return (-1, -1, 950, 650);
    }

    public static void SetWindowBounds(int x, int y, int width, int height)
    {
        try { LogService.Write("Config", $"SetWindowBounds called x={x} y={y} w={width} h={height}"); } catch { }
        if (_configData?.Settings != null)
        {
            _configData.Settings.WindowBounds = new int[] { x, y, width, height };
            try { LogService.Write("Config", "SetWindowBounds applied"); } catch { }
        }
    }
}