//using System;
using System.Threading;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Threading.Tasks;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using TinyPinyin;
using Windows.Management.Deployment;
using Windows.ApplicationModel;

namespace EricGameLauncher
{
    internal static class LauncherConstants
    {
        public const string UwpAppsFolderPrefix = "shell:AppsFolder\\";
        public const string SteamProtocol = "steam://";
        public const string EpicProtocol = "com.epicgames.launcher://";
        public const string EpicAppsProtocol = "com.epicgames.launcher://apps/";
        public const string XboxProtocol = "xbox://";
    }

    #region Base Infrastructure

    public static class I18n
    {
        private static Dictionary<string, string> _strings = new();
        private static Dictionary<string, Dictionary<string, string>>? _allTranslations = null;
        private static string _currentLanguage = "Zh-CN";

        public static string CurrentLanguage => _currentLanguage;

        public static event Action? LanguageChanged;

        public static void Load(string langCode)
        {
            _currentLanguage = langCode;

            if (_allTranslations == null)
            {
                try
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    using var stream = assembly.GetManifestResourceStream("EricGameLauncher.i18n.json");
                    if (stream == null) return;

                    using var reader = new StreamReader(stream);
                    string json = reader.ReadToEnd();
                    _allTranslations = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                }
                catch (Exception ex) { Debug.WriteLine($"[I18n] Failed to load translations: {ex.Message}"); }
            }

            if (_allTranslations != null && _allTranslations.TryGetValue(langCode, out var dict))
            {
                _strings = dict;
            }

            LanguageChanged?.Invoke();
        }

        public static string T(string key)
        {
            return _strings.TryGetValue(key, out var value) ? value : key;
        }

        public static List<string> GetAvailableLanguages()
        {
            if (_allTranslations == null)
            {
                Load(_currentLanguage);
            }

            if (_allTranslations != null)
            {
                return new List<string>(_allTranslations.Keys);
            }

            return new List<string> { "Zh-CN", "EN" };
        }

        public static string DetectSystemLanguage()
        {
            try
            {
                var culture = System.Globalization.CultureInfo.CurrentUICulture;
                string name = culture.Name.ToLowerInvariant();
                string lang = culture.TwoLetterISOLanguageName.ToLowerInvariant();

                if (lang == "zh")
                {
                    bool isTraditional = name.Contains("tw") || name.Contains("hk") ||
                                         name.Contains("mo") || name.Contains("hant");
                    return isTraditional ? "Zh-TW" : "Zh-CN";
                }

                return lang switch
                {
                    "ja" => "JA",
                    "ko" => "KO",
                    "fr" => "FR",
                    "de" => "DE",
                    "es" => "ES",
                    _ => "EN",
                };
            }
            catch { return "EN"; }
        }

        public static string GetDisplayName(string langCode)
        {
            string nativeName = langCode;
            if (_allTranslations != null &&
                _allTranslations.TryGetValue(langCode, out var dict) &&
                dict.TryGetValue("_LangName", out var native) &&
                !string.IsNullOrEmpty(native))
                nativeName = native;

            string localizedKey = "LangName_" + langCode;
            if (_strings.TryGetValue(localizedKey, out var localized) &&
                !string.IsNullOrEmpty(localized) &&
                localized != nativeName)
                return $"{nativeName} ({localized})";

            return nativeName;
        }
    }

    public static class ConfigService
    {
        private const string AppFolderName = "EricGameLauncher";
        private const string DataFileName = "config.json";
        private const string IconFolderName = "ico";

        public const int CurrentConfigVersion = 2;

        public static bool RequiresMigration { get; private set; } = false;
        private static bool _blockSaving = false;

        private static string SystemBasePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "eric", AppFolderName);
        private static string PortableBasePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

        public static string FixedCachePath => Path.Combine(CurrentDataPath, IconFolderName);
        public static string CurrentDataPath { get; private set; } = "";
        private static ConfigData? _configData;
        private static readonly SemaphoreSlim _saveSemaphore = new(1, 1);
        public static event Action? DataChanged;

        public static void Initialize()
        {
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
                    try { Directory.Delete(oldIconPath, true); } catch (Exception) { }
                }

                CurrentDataPath = newPath;
                SteamHelper.ClearCache();
                LoadConfigData();
            }
            catch (Exception) { }
        }

        public static Task SwitchStorageModeAsync(bool useSystemPath) => Task.Run(() => SwitchStorageMode(useSystemPath));

        public static void SaveItems(List<AppItem> items, bool triggerEvent = true)
        {
            if (_configData == null) return;
            lock (_configData)
            {
                _configData.Items = items.Select(AppItemDto.FromViewModel).ToList();
            }
            SaveConfig();
            if (triggerEvent) DataChanged?.Invoke();
        }

        public static List<AppItem> LoadItems()
        {
            var dtos = _configData?.Items ?? [];
            return dtos.Select(dto => dto.ToViewModel(FixedCachePath)).ToList();
        }

        private static void LoadConfigData()
        {
            try
            {
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
                _configData.Settings ??= new AppSettings();
                _configData.Items ??= [];
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LoadConfigData] Handled exception parsing JSON. Fallback to empty context. {ex.Message}");
                if (_configData == null) _configData = new ConfigData();
            }
        }

        private static void SaveConfigData()
        {
            if (_blockSaving) return;

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
                catch (Exception ex) { Debug.WriteLine($"[SaveConfigData] Async write failed: {ex.Message}"); }
                finally
                {
                    _saveSemaphore.Release();
                }
            });
        }

        public static bool CloseAfterLaunch
        {
            get => _configData?.Settings?.CloseAfterLaunch ?? false;
            set { if (_configData?.Settings != null) _configData.Settings.CloseAfterLaunch = value; }
        }

        public static double IconSize
        {
            get => _configData?.Settings?.IconSize ?? 118;
            set { if (_configData?.Settings != null) _configData.Settings.IconSize = value; }
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
            set { if (_configData?.Settings != null) _configData.Settings.Language = value; }
        }

        public static bool IsSystemMode => CurrentDataPath == SystemBasePath;

        public static async Task<bool> ReconstructMissingConfigAsync()
        {
            if (_configData == null || _configData.Items == null || _configData.Items.Count == 0) return false;

            bool modified = false;
            foreach (var item in _configData.Items)
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
                            Debug.WriteLine($"[ConfigReconstruction] Restored platform '{item.Platform}' for {item.Title}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ConfigReconstruction] Error identifying platform for {item.Title}: {ex.Message}");
                    }
                }

                if (itemChanged) modified = true;
            }

            if (modified)
            {
                SaveConfigData();
            }
            return modified;
        }

        public static void SaveConfig() => SaveConfigData();

        public static async Task RefreshGlobalAsync()
        {
            // 1. Icon Reconstruction
            var items = LoadItems();
            var rebuildTasks = new List<Task>();
            int successfulRebuilds = 0;

            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.IconPath) || !File.Exists(item.IconPath))
                {
                    rebuildTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            string? sourcePath = item.ExePath;
                            string? resolvedPath = null;

                            // Resolve protocols
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
                            // When dealing with UWP/apps folder paths the file system checks
                            // above will always return false (they're not real files), which
                            // previously meant icons could never be rebuilt after a batch import
                            // scan.  We now treat any path prefixed with the UWP constant as
                            // valid for icon extraction.
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
                        catch { }
                    }));
                }
            }

            if (rebuildTasks.Any())
            {
                await Task.WhenAll(rebuildTasks);
                if (successfulRebuilds > 0)
                {
                    // Pass false to avoid double event trigger
                    SaveItems(items, false);
                }
            }

            // 2. Platform Reconstruction
            await ReconstructMissingConfigAsync();

            // 3. Notify UI (One stop shop)
            DataChanged?.Invoke();
        }

        public static (int X, int Y, int Width, int Height) GetWindowBounds()
        {
            var bounds = _configData?.Settings?.WindowBounds;
            if (bounds != null && bounds.Length == 4) return (bounds[0], bounds[1], bounds[2], bounds[3]);
            return (-1, -1, 950, 650);
        }

        public static void SetWindowBounds(int x, int y, int width, int height)
        {
            if (_configData?.Settings != null)
                _configData.Settings.WindowBounds = new int[] { x, y, width, height };
        }
    }

    #endregion

    #region Data Models & DTOs

    public class AppItemDto
    {
        public string Id { get; set; } = string.Empty;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Title { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? IconPath { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CustomMenu { get; set; }

        [JsonPropertyName("platform")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Platform { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ActionDto? MainAction { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ActionDto? ManagerAction { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AltActionDto? AltAction { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AlongActionDto? AlongsideAction { get; set; }

        public static AppItemDto FromViewModel(AppItem vm) => new AppItemDto
        {
            Id = vm.Id,
            Title = vm.Title,
            IconPath = !string.IsNullOrEmpty(vm.IconPath) ? Path.GetFileName(vm.IconPath) : null,
            CustomMenu = vm.CustomMenu,
            Platform = vm.Platform,
            MainAction = new ActionDto { Path = vm.ExePath, IsAdmin = vm.IsAdmin },
            ManagerAction = string.IsNullOrEmpty(vm.MgrPath) ? null
                : new ActionDto { Path = vm.MgrPath, IsAdmin = vm.IsMgrAdmin },
            AltAction = (vm.UseAlternativeLaunch || !string.IsNullOrEmpty(vm.AlternativeLaunchCommand))
                ? new AltActionDto { Enabled = vm.UseAlternativeLaunch, Path = vm.AlternativeLaunchCommand, IsAdmin = vm.IsAltAdmin }
                : null,
            AlongsideAction = (vm.RunAlongside || !string.IsNullOrEmpty(vm.AlongsideCommand))
                ? new AlongActionDto { Enabled = vm.RunAlongside, Path = vm.AlongsideCommand, IsAdmin = vm.IsAlongsideAdmin }
                : null,
        };

        public AppItem ToViewModel(string iconCachePath) => new AppItem
        {
            ExePath = MainAction?.Path,
            IsAdmin = MainAction?.IsAdmin ?? false,
            Id = Id,
            Title = Title,
            IconPath = (string.IsNullOrEmpty(IconPath) || Path.IsPathRooted(IconPath))
                         ? IconPath
                         : (IconPath.StartsWith("ico\\", StringComparison.OrdinalIgnoreCase) || IconPath.StartsWith("ico/", StringComparison.OrdinalIgnoreCase))
                            ? Path.Combine(iconCachePath, Path.GetFileName(IconPath))
                            : (!IconPath.Contains(Path.DirectorySeparatorChar) && !IconPath.Contains(Path.AltDirectorySeparatorChar))
                               ? Path.Combine(iconCachePath, IconPath)
                               : IconPath,
            CustomMenu = CustomMenu,
            Platform = Platform,
            MgrPath = ManagerAction?.Path,
            IsMgrAdmin = ManagerAction?.IsAdmin ?? false,
            UseAlternativeLaunch = AltAction?.Enabled ?? false,
            AlternativeLaunchCommand = AltAction?.Path,
            IsAltAdmin = AltAction?.IsAdmin ?? false,
            RunAlongside = AlongsideAction?.Enabled ?? false,
            AlongsideCommand = AlongsideAction?.Path,
            IsAlongsideAdmin = AlongsideAction?.IsAdmin ?? false,
        };

        public class ActionDto
        {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Path { get; set; }
            public bool IsAdmin { get; set; }
        }

        public class AltActionDto
        {
            public bool Enabled { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Path { get; set; }
            public bool IsAdmin { get; set; }
        }

        public class AlongActionDto
        {
            public bool Enabled { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Path { get; set; }
            public bool IsAdmin { get; set; }
        }
    }

    public class ScannedGame
    {
        public string Title { get; set; } = string.Empty;
        public string ExePath { get; set; } = string.Empty;
        public string PlatformBadge { get; set; } = string.Empty;
    }

    public class AppItem : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string? _title;
        private string? _iconPath;
        public event PropertyChangedEventHandler? PropertyChanged;

        private RunActionBase? _mainAction;
        private RunActionBase? _managerAction;
        private AlternativeRunAction? _altAction;
        private AlongsideRunAction? _alongsideAction;

        private string? _platform;

        public RunActionBase? MainAction
        {
            get => _mainAction;
            set
            {
                if (SetProperty(ref _mainAction, value))
                {
                    OnPropertyChanged(nameof(ExePath));
                    OnPropertyChanged(nameof(IsAdmin));
                }
            }
        }

        public RunActionBase? ManagerAction
        {
            get => _managerAction;
            set
            {
                if (SetProperty(ref _managerAction, value))
                {
                    OnPropertyChanged(nameof(MgrPath));
                    OnPropertyChanged(nameof(IsMgrAdmin));
                }
            }
        }

        private int _sortOrder = 0;
        public int SortOrder
        {
            get => _sortOrder;
            set
            {
                if (SetProperty(ref _sortOrder, value))
                {
                    OnPropertyChanged(nameof(SortOrder));
                }
            }
        }

        public AlternativeRunAction? AltAction
        {
            get => _altAction;
            set
            {
                if (SetProperty(ref _altAction, value))
                {
                    OnPropertyChanged(nameof(UseAlternativeLaunch));
                    OnPropertyChanged(nameof(AlternativeLaunchCommand));
                    OnPropertyChanged(nameof(IsAltAdmin));
                }
            }
        }

        public AlongsideRunAction? AlongsideAction
        {
            get => _alongsideAction;
            set
            {
                if (SetProperty(ref _alongsideAction, value))
                {
                    OnPropertyChanged(nameof(RunAlongside));
                    OnPropertyChanged(nameof(AlongsideCommand));
                    OnPropertyChanged(nameof(IsAlongsideAdmin));
                }
            }
        }

        [JsonIgnore]
        public string? ExePath
        {
            get => _mainAction?.Path;
            set
            {
                if (_mainAction == null) _mainAction = new RunActionBase();
                if (_mainAction.Path != value)
                {
                    _mainAction.Path = value;
                    if (!string.IsNullOrEmpty(value))
                        Id = PathHashHelper.GetPathHash(value);
                    OnPropertyChanged(nameof(ExePath));
                    OnPropertyChanged(nameof(MainAction));
                }
            }
        }

        [JsonIgnore]
        public bool IsAdmin
        {
            get => _mainAction?.IsAdmin ?? false;
            set
            {
                if (_mainAction == null) _mainAction = new RunActionBase();
                if (_mainAction.IsAdmin != value)
                {
                    _mainAction.IsAdmin = value;
                    OnPropertyChanged(nameof(IsAdmin));
                    OnPropertyChanged(nameof(MainAction));
                }
            }
        }

        [JsonIgnore]
        public string? MgrPath
        {
            get => _managerAction?.Path;
            set
            {
                if (_managerAction == null) _managerAction = new RunActionBase();
                if (_managerAction.Path != value)
                {
                    _managerAction.Path = value;
                    OnPropertyChanged(nameof(MgrPath));
                }
            }
        }

        [JsonIgnore]
        public bool IsMgrAdmin
        {
            get => _managerAction?.IsAdmin ?? false;
            set
            {
                if (_managerAction == null) _managerAction = new RunActionBase();
                if (_managerAction.IsAdmin != value)
                {
                    _managerAction.IsAdmin = value;
                    OnPropertyChanged(nameof(IsMgrAdmin));
                    OnPropertyChanged(nameof(ManagerAction));
                }
            }
        }

        [JsonIgnore]
        public bool UseAlternativeLaunch
        {
            get => _altAction?.Enabled ?? false;
            set
            {
                if (_altAction == null) _altAction = new AlternativeRunAction();
                if (_altAction.Enabled != value)
                {
                    _altAction.Enabled = value;
                    OnPropertyChanged(nameof(UseAlternativeLaunch));
                }
            }
        }

        [JsonIgnore]
        public string? AlternativeLaunchCommand
        {
            get => _altAction?.Path;
            set
            {
                if (_altAction == null) _altAction = new AlternativeRunAction();
                if (_altAction.Path != value)
                {
                    _altAction.Path = value;
                    OnPropertyChanged(nameof(AlternativeLaunchCommand));
                }
            }
        }

        [JsonIgnore]
        public bool IsAltAdmin
        {
            get => _altAction?.IsAdmin ?? false;
            set
            {
                if (_altAction == null) _altAction = new AlternativeRunAction();
                if (_altAction.IsAdmin != value)
                {
                    _altAction.IsAdmin = value;
                    OnPropertyChanged(nameof(IsAltAdmin));
                    OnPropertyChanged(nameof(AltAction));
                }
            }
        }

        [JsonIgnore]
        public bool RunAlongside
        {
            get => _alongsideAction?.Enabled ?? false;
            set
            {
                if (_alongsideAction == null) _alongsideAction = new AlongsideRunAction();
                if (_alongsideAction.Enabled != value)
                {
                    _alongsideAction.Enabled = value;
                    OnPropertyChanged(nameof(RunAlongside));
                }
            }
        }

        [JsonIgnore]
        public string? AlongsideCommand
        {
            get => _alongsideAction?.Path;
            set
            {
                if (_alongsideAction == null) _alongsideAction = new AlongsideRunAction();
                if (_alongsideAction.Path != value)
                {
                    _alongsideAction.Path = value;
                    OnPropertyChanged(nameof(AlongsideCommand));
                }
            }
        }

        [JsonIgnore]
        public bool IsAlongsideAdmin
        {
            get => _alongsideAction?.IsAdmin ?? false;
            set
            {
                if (_alongsideAction == null) _alongsideAction = new AlongsideRunAction();
                if (_alongsideAction.IsAdmin != value)
                {
                    _alongsideAction.IsAdmin = value;
                    OnPropertyChanged(nameof(IsAlongsideAdmin));
                    OnPropertyChanged(nameof(AlongsideAction));
                }
            }
        }
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string? Title
        {
            get => _title;
            set
            {
                if (SetProperty(ref _title, value))
                {
                    _titlePinyin = null;
                    _titlePinyinInitial = null;
                    _titleEnglishInitial = null;
                }
            }
        }

        private string? _titlePinyin;
        private string? _titlePinyinInitial;
        private string? _titleEnglishInitial;

        [JsonIgnore]
        public string TitlePinyin
        {
            get
            {
                if (_titlePinyin == null && !string.IsNullOrEmpty(_title))
                {
                    _titlePinyin = TinyPinyin.PinyinHelper.GetPinyin(_title, "").ToLower();
                }
                return _titlePinyin ?? "";
            }
        }

        [JsonIgnore]
        public string TitlePinyinInitial
        {
            get
            {
                if (_titlePinyinInitial == null && !string.IsNullOrEmpty(_title))
                {
                    var chars = _title.ToCharArray();
                    var initials = new StringBuilder();
                    foreach (var c in chars)
                    {
                        if (TinyPinyin.PinyinHelper.IsChinese(c))
                        {
                            string pinyin = TinyPinyin.PinyinHelper.GetPinyin(c);
                            if (!string.IsNullOrEmpty(pinyin))
                                initials.Append(pinyin[0]);
                        }
                        else if (char.IsLetterOrDigit(c))
                        {
                            initials.Append(c);
                        }
                    }
                    _titlePinyinInitial = initials.ToString().ToLower();
                }
                return _titlePinyinInitial ?? "";
            }
        }

        [JsonIgnore]
        public string TitleEnglishInitial
        {
            get
            {
                if (_titleEnglishInitial == null && !string.IsNullOrEmpty(_title))
                {
                    var initials = new StringBuilder();
                    bool lastWasSpace = true;
                    bool lastWasLower = false;

                    foreach (var c in _title)
                    {
                        if (char.IsWhiteSpace(c) || c == '-' || c == '_')
                        {
                            lastWasSpace = true;
                            lastWasLower = false;
                        }
                        else if (char.IsLetter(c))
                        {
                            if (lastWasSpace)
                            {
                                initials.Append(char.ToLower(c));
                                lastWasSpace = false;
                                lastWasLower = char.IsLower(c);
                            }
                            else if (char.IsUpper(c) && lastWasLower)
                            {
                                initials.Append(char.ToLower(c));
                                lastWasLower = false;
                            }
                            else
                            {
                                lastWasLower = char.IsLower(c);
                            }
                        }
                    }

                    _titleEnglishInitial = initials.ToString();
                }
                return _titleEnglishInitial ?? "";
            }
        }

        public string? IconPath
        {
            get => _iconPath;
            set => SetProperty(ref _iconPath, value);
        }



        public string? CustomMenu
        {
            get => _customMenu;
            set => SetProperty(ref _customMenu, value);
        }

        private string? _customMenu;

        public List<CustomMenuItem> GetCustomMenuItems()
        {
            var result = new List<CustomMenuItem>();
            if (string.IsNullOrEmpty(CustomMenu)) return result;

            try
            {
                var pairs = CustomMenu.Split(':', StringSplitOptions.RemoveEmptyEntries);
                foreach (var pair in pairs)
                {
                    var parts = pair.Split('|');
                    if (parts.Length >= 2)
                    {
                        result.Add(new CustomMenuItem
                        {
                            Title = Uri.UnescapeDataString(parts[0]),
                            Command = Uri.UnescapeDataString(parts[1]),
                            IsAdmin = parts.Length > 2 && bool.TryParse(parts[2], out bool isAdmin) && isAdmin
                        });
                    }
                }
            }
            catch { }
            return result;
        }

        public void SetCustomMenuItems(List<CustomMenuItem> items)
        {
            if (items == null || items.Count == 0)
            {
                CustomMenu = null;
                return;
            }

            var builder = new StringBuilder();
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.Command)) continue;
                if (builder.Length > 0) builder.Append(':');
                builder.Append(Uri.EscapeDataString(item.Title ?? ""));
                builder.Append('|');
                string encodedCmd = Uri.EscapeDataString(item.Command ?? "");
                if (encodedCmd.Contains(':')) encodedCmd = encodedCmd.Replace(":", "%3A");
                builder.Append(encodedCmd);
                builder.Append('|');
                builder.Append(item.IsAdmin.ToString());
            }
            CustomMenu = builder.Length > 0 ? builder.ToString() : null;
        }
        [JsonIgnore]
        public bool HasManager => !string.IsNullOrEmpty(MgrPath);

        [JsonIgnore]
        public string? RuntimeManagerPath => GamePlatformHelper.GetRuntimeManagerPath(MgrPath, ExePath);

        [JsonIgnore]
        public bool IsPlatformUrl => !string.IsNullOrEmpty(ExePath) && GamePlatformHelper.IsSupportedPlatformUrl(ExePath);

        public string? Platform
        {
            get => _platform;
            set => SetProperty(ref _platform, value);
        }

        [JsonIgnore]
        public string? PlatformName => !string.IsNullOrEmpty(Platform) ? Platform : (!string.IsNullOrEmpty(ExePath) ? GamePlatformHelper.GetPlatformDisplayName(ExePath) : null);

        [JsonIgnore]
        public bool HasManagerOrDefault => !string.IsNullOrEmpty(RuntimeManagerPath);

        [JsonIgnore]
        public BitmapImage? DisplayIcon { get; set; }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class AppSettings
    {
        [JsonPropertyName("closeAfterLaunch")]
        public bool CloseAfterLaunch { get; set; } = false;

        [JsonPropertyName("iconSize")]
        public double IconSize { get; set; } = 118;

        [JsonPropertyName("language")]
        public string Language { get; set; } = "";

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
    }

    public class ShortcutInfo
    {
        public string? TargetPath { get; set; }
        public string? Arguments { get; set; }
        public string? IconPath { get; set; }
        public int IconIndex { get; set; }
        public bool IsUrl { get; set; }
        public string? ActualUrl { get; set; }
        public string? AUMID { get; set; }
        public GamePlatformInfo? Platform { get; set; }
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

    public class CustomMenuItem
    {
        public string? Title { get; set; }
        public string? Command { get; set; }
        public bool IsAdmin { get; set; }
    }

    #endregion

    #region UI & XAML Support

    public sealed class ImagePathConverter : IValueConverter
    {
        // In-memory soft-reference cache: key = "path@ticks", value = WeakReference<BitmapImage>
        // WeakReference lets GC reclaim images under memory pressure without needing explicit eviction.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, WeakReference<BitmapImage>> _bitmapCache = new();

        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not string path || string.IsNullOrEmpty(path))
                return null;

            try
            {
                if (!File.Exists(path))
                    return null;

                long cacheKey = new FileInfo(path).LastWriteTime.Ticks;
                string cacheEntry = $"{path}@{cacheKey}";

                // Try to return the already-decoded BitmapImage from the in-memory cache
                if (_bitmapCache.TryGetValue(cacheEntry, out var weakRef) && weakRef.TryGetTarget(out var cached))
                    return cached;

                // Cache miss or GC-collected: create a new BitmapImage
                var bitmap = new BitmapImage();
                bitmap.DecodePixelWidth = 256;
                bitmap.DecodePixelHeight = 256;

                string fileUri = $"file:///{path.Replace("\\", "/")}?t={cacheKey}";
                try
                {
                    bitmap.UriSource = new Uri(fileUri, UriKind.Absolute);
                }
                catch
                {
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                }

                _bitmapCache[cacheEntry] = new WeakReference<BitmapImage>(bitmap);
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value == null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => !string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is bool boolValue && boolValue ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => value is Visibility visibility && visibility == Visibility.Visible;
    }

    public sealed class SizeToCornerRadiusConverter : IValueConverter
    {
        public double Ratio { get; set; } = 0.2;
        public double MarginOffset { get; set; } = 0;

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double size && size > 0)
            {
                double actualSize = size - MarginOffset;
                if (actualSize <= 0) actualSize = size;
                return new CornerRadius(actualSize * Ratio);
            }
            return new CornerRadius(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class IntArrayJsonConverter : JsonConverter<int[]>
    {
        public override int[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                return null;

            var list = new List<int>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.Number)
                    list.Add(reader.GetInt32());
            }
            return list.ToArray();
        }

        public override void Write(Utf8JsonWriter writer, int[] value, JsonSerializerOptions options)
            => writer.WriteRawValue($"[{string.Join(", ", value)}]");
    }

    #endregion

    #region Asset Management

    public static class IconHelper
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern uint PrivateExtractIcons(string lpszFile, int nIconIndex, int cxIcon, int cyIcon, IntPtr[]? phicon, uint[]? piconid, uint nIcons, uint flags);

        private static string CachePath => ConfigService.FixedCachePath;
        private static readonly int[] IconSizes = [512, 256, 192, 128, 96, 72, 64, 48, 32, 24, 16];

        public static async Task<string?> GetIconPathAsync(string exePath, string itemId)
        {
            if (string.IsNullOrEmpty(exePath)) return null;
            // removed strict File.Exists guard: callers (配置/新建/导入) typically supply a valid path,
            // and we prefer to attempt extraction later instead of rejecting up front.
            // UWP shell paths are still allowed unconditionally.

            string iconPath = Path.Combine(CachePath, $"{itemId}.png");
            if (File.Exists(iconPath) && new FileInfo(iconPath).Length > 0) return iconPath;
            return await ExtractAndSaveIconAsync(exePath, itemId);
        }

        public static async Task<string?> ExtractAndSaveIconAsync(string sourcePath, string itemId, bool extractFromLnk = false)
        {
            try
            {
                if (string.IsNullOrEmpty(sourcePath)) return null;
                bool isStoreApp = sourcePath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase);
                // skip existence check here; callers generally expect extraction to proceed
                // and failure will be handled by returning null downstream.

                string targetPath = sourcePath;
                int iconIndex = 0;

                if (!isStoreApp)
                {
                    if (sourcePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!extractFromLnk)
                        {
                            string? resolvedPath = ShortcutResolver.GetLnkTarget(sourcePath);
                            if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath)) targetPath = resolvedPath;
                        }
                        else
                        {
                            var shortcutInfo = ShortcutResolver.GetShortcutInfo(sourcePath);
                            if (shortcutInfo != null && !string.IsNullOrEmpty(shortcutInfo.IconPath) && File.Exists(shortcutInfo.IconPath)) { targetPath = shortcutInfo.IconPath; iconIndex = shortcutInfo.IconIndex; }
                        }
                    }
                    else if (sourcePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                    {
                        var urlInfo = ShortcutResolver.GetUrlFileInfo(sourcePath);
                        if (urlInfo != null && !string.IsNullOrEmpty(urlInfo.IconPath) && File.Exists(urlInfo.IconPath)) { targetPath = urlInfo.IconPath; iconIndex = urlInfo.IconIndex; }
                    }
                }

                if (!Directory.Exists(CachePath)) Directory.CreateDirectory(CachePath);
                string savePath = Path.Combine(CachePath, $"{itemId}.png");
                await ForceDeleteFileAsync(savePath);

                Icon? icon = null;
                if (isStoreApp)
                {
                    if (ExtractStoreAppIcon(sourcePath, savePath)) return savePath;
                }
                else
                {
                    icon = ExtractLargestIcon(targetPath, iconIndex) ?? Icon.ExtractAssociatedIcon(targetPath);
                }

                if (icon != null)
                {
                    try { using var bmp = icon.ToBitmap(); bmp.Save(savePath, ImageFormat.Png); } finally { icon.Dispose(); }
                    await Task.Delay(50);
                    if (File.Exists(savePath) && new FileInfo(savePath).Length > 0) return savePath;
                }
                return null;
            }
            catch { return null; }
        }

        private static bool ExtractStoreAppIcon(string shellPath, string savePath)
        {
            try
            {
                Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
                int hr = SHCreateItemFromParsingName(shellPath, IntPtr.Zero, iid, out IShellItemImageFactory? factory);

                if (hr == 0 && factory != null)
                {
                    hr = factory.GetImage(new SIZE { cx = 256, cy = 256 }, 0x104, out IntPtr hBitmap);

                    if (hr != 0)
                    {
                        hr = factory.GetImage(new SIZE { cx = 256, cy = 256 }, 0x108, out hBitmap);
                    }

                    if (hr == 0 && hBitmap != IntPtr.Zero)
                    {
                        try
                        {
                            using var bmp = CreateBitmapFromHBitmap(hBitmap, true);
                            if (bmp != null)
                            {
                                bmp.Save(savePath, ImageFormat.Png);
                                return true;
                            }
                        }
                        catch (Exception)
                        {
                        }
                        finally { DeleteObject(hBitmap); }
                    }
                }

                IntPtr pidl = ILCreateFromPath(shellPath);
                if (pidl != IntPtr.Zero)
                {
                    try
                    {
                        SHFILEINFO shfi = new SHFILEINFO();
                        const uint SHGFI_PIDL = 0x000000008;
                        IntPtr res = SHGetFileInfo(pidl, 0, ref shfi, (uint)Marshal.SizeOf(shfi),
                            SHGFI_ICON | SHGFI_LARGEICON | SHGFI_PIDL);

                        if (shfi.hIcon != IntPtr.Zero)
                        {
                            using (var icon = Icon.FromHandle(shfi.hIcon))
                            using (var bmp = icon.ToBitmap())
                            {
                                bmp.Save(savePath, ImageFormat.Png);
                                return true;
                            }
                        }
                    }
                    finally { ILFree(pidl); }
                }
            }
            catch (Exception) { }
            return false;
        }

        private static Bitmap? CreateBitmapFromHBitmap(IntPtr hBitmap, bool flipVertical = false)
        {
            if (hBitmap == IntPtr.Zero) return null;

            BITMAP bm;
            if (GetObject(hBitmap, Marshal.SizeOf(typeof(BITMAP)), out bm) != 0)
            {
                if (bm.bmBitsPixel == 32 && bm.bmBits != IntPtr.Zero)
                {
                    using (var temp = new Bitmap(bm.bmWidth, bm.bmHeight, bm.bmWidthBytes, PixelFormat.Format32bppArgb, bm.bmBits))
                    {
                        var result = new Bitmap(temp);
                        if (flipVertical) result.RotateFlip(RotateFlipType.RotateNoneFlipY);
                        return result;
                    }
                }
            }
            return Bitmap.FromHbitmap(hBitmap);
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage([In, MarshalAs(UnmanagedType.Struct)] SIZE size, [In] int flags, [Out] out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ILCreateFromPath(string pszPath);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            [In] IntPtr pbc,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(IntPtr pidl, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;

        private static Icon? ExtractLargestIcon(string filePath, int iconIndex = 0)
        {
            try
            {
                foreach (int size in IconSizes)
                {
                    var hIcons = new IntPtr[1];
                    uint count = PrivateExtractIcons(filePath, iconIndex, size, size, hIcons, null, 1, 0);
                    if (count > 0 && hIcons[0] != IntPtr.Zero)
                    {
                        try { return (Icon)Icon.FromHandle(hIcons[0]).Clone(); } finally { DestroyIcon(hIcons[0]); }
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        private static async Task ForceDeleteFileAsync(string filePath)
        {
            if (!File.Exists(filePath)) return;
            for (int i = 0; i < 3; i++)
            {
                try { await Task.Run(() => File.Delete(filePath)); return; } catch (Exception) { await Task.Delay(50); }
            }
        }
    }

    #endregion

    #region System & Shell Integration

    public static class ShortcutResolver
    {
        public static string? GetLnkTarget(string lnkPath)
        {
            if (string.IsNullOrEmpty(lnkPath) || !File.Exists(lnkPath))
                return null;

            var info = GetShortcutInfo(lnkPath);
            if (info != null && !string.IsNullOrEmpty(info.AUMID))
                return $"{LauncherConstants.UwpAppsFolderPrefix}{info.AUMID}";

            return info?.TargetPath;
        }

        public static ShortcutInfo? GetShortcutInfo(string lnkPath)
        {
            if (string.IsNullOrEmpty(lnkPath) || !File.Exists(lnkPath))
                return null;

            try
            {
                Type? wshType = Type.GetTypeFromProgID("WScript.Shell");
                if (wshType == null) return null;

                dynamic? wsh = Activator.CreateInstance(wshType);
                if (wsh == null) return null;

                try
                {
                    dynamic link = wsh.CreateShortcut(lnkPath);
                    if (link == null) return null;

                    var info = new ShortcutInfo
                    {
                        TargetPath = link.TargetPath as string,
                        Arguments = link.Arguments as string,
                        IconPath = link.IconLocation as string
                    };

                    try
                    {
                        Type? shellAppType = Type.GetTypeFromProgID("Shell.Application");
                        if (shellAppType != null)
                        {
                            dynamic? shellApp = Activator.CreateInstance(shellAppType);
                            if (shellApp != null)
                            {
                                string? dir = Path.GetDirectoryName(lnkPath);
                                string file = Path.GetFileName(lnkPath);
                                if (!string.IsNullOrEmpty(dir))
                                {
                                    var folder = shellApp.NameSpace(dir);
                                    var folderItem = folder?.ParseName(file);
                                    if (folderItem != null)
                                    {
                                        var aumidProp = folderItem.ExtendedProperty("System.AppUserModel.ID");
                                        if (aumidProp != null) info.AUMID = aumidProp.ToString();

                                        if (string.IsNullOrEmpty(info.TargetPath))
                                        {
                                            var parsingPath = folderItem.ExtendedProperty("System.Link.TargetParsingPath");
                                            if (parsingPath != null) info.TargetPath = parsingPath.ToString();
                                        }

                                        if (string.IsNullOrEmpty(info.AUMID) && !string.IsNullOrEmpty(info.TargetPath))
                                        {
                                            string tPath = info.TargetPath;
                                            if (tPath.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
                                                tPath = tPath.Substring(LauncherConstants.UwpAppsFolderPrefix.Length);

                                            if (tPath.Contains("!") || (!tPath.Contains("\\") && !tPath.Contains("/")))
                                            {
                                                info.AUMID = tPath;
                                            }
                                        }
                                    }
                                }
                                if (Marshal.IsComObject(shellApp)) Marshal.ReleaseComObject(shellApp);
                            }
                        }
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(info.IconPath))
                    {
                        var parts = info.IconPath.Split(',');
                        if (parts.Length > 1 && int.TryParse(parts[1], out int index))
                        {
                            info.IconPath = parts[0];
                            info.IconIndex = index;
                        }
                    }

                    return info;
                }
                finally
                {
                    if (Marshal.IsComObject(wsh))
                        Marshal.ReleaseComObject(wsh);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ShortcutInfo? GetUrlFileInfo(string urlPath)
        {
            if (string.IsNullOrEmpty(urlPath) || !File.Exists(urlPath))
                return null;

            try
            {
                string[] lines = File.ReadAllLines(urlPath);
                string? targetUrl = null;
                string? iconFile = null;
                int iconIndex = 0;

                foreach (string line in lines)
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                        targetUrl = line.Substring(4).Trim();
                    else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                        iconFile = line.Substring(9).Trim();
                    else if (line.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line.Substring(10).Trim(), out iconIndex);
                }

                if (string.IsNullOrEmpty(targetUrl)) return null;

                return new ShortcutInfo
                {
                    TargetPath = targetUrl,
                    IsUrl = true,
                    ActualUrl = targetUrl,
                    IconPath = iconFile,
                    IconIndex = iconIndex
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void CreateShortcut(string targetPath, string shortcutPath, string description = "")
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return;

                try
                {
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetPath;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                    shortcut.Description = description;
                    shortcut.Save();
                }
                finally
                {
                    if (Marshal.IsComObject(shell))
                        Marshal.ReleaseComObject(shell);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    public static class ShortcutScanner
    {
        public class FileItem
        {
            public string Name { get; set; } = "";
            public string FullPath { get; set; } = "";
            public bool IsFolder { get; set; }
            public List<FileItem> Children { get; set; } = [];
        }

        private static readonly string[] SupportedExtensions = [".lnk", ".url", ".exe"];

        public static List<FileItem> GetStartMenuItems()
        {
            var items = new List<FileItem>();
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return items;

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return items;

                var appsFolder = shell.NameSpace("shell:AppsFolder");
                if (appsFolder != null)
                {
                    foreach (var item in appsFolder.Items())
                    {
                        try
                        {
                            string name = item.Name;
                            string path = item.Path;

                            string finalPath = path;
                            bool isPhysical = File.Exists(path) || Directory.Exists(path);

                            if (!isPhysical)
                            {
                                try
                                {
                                    var targetProperty = item.ExtendedProperty("System.Link.TargetParsingPath");
                                    if (targetProperty != null)
                                    {
                                        string tPath = targetProperty.ToString() ?? "";
                                        if (!string.IsNullOrEmpty(tPath) && (File.Exists(tPath) || Directory.Exists(tPath)))
                                        {
                                            finalPath = tPath;
                                            isPhysical = true;
                                        }
                                    }

                                    if (!isPhysical && item.IsLink)
                                    {
                                        var link = item.GetLink;
                                        string? lPath = link?.Path;
                                        if (!string.IsNullOrEmpty(lPath) && (File.Exists(lPath) || Directory.Exists(lPath)))
                                        {
                                            finalPath = lPath;
                                            isPhysical = true;
                                        }
                                    }
                                }
                                catch { }
                            }

                            if (!string.IsNullOrEmpty(name))
                            {
                                items.Add(new FileItem
                                {
                                    Name = name,
                                    FullPath = (!isPhysical && (path.Contains("!") || !path.Contains("\\"))) ? $"shell:AppsFolder\\{path}" : finalPath,
                                    IsFolder = false
                                });
                            }
                        }
                        catch (Exception) { }
                    }
                }

                if (Marshal.IsComObject(shell))
                    Marshal.ReleaseComObject(shell);
            }
            catch (Exception) { }

            return items.OrderBy(x => x.Name).ToList();
        }

        public static List<FileItem> GetDesktopItems()
        {
            var items = new List<FileItem>();
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

            if (Directory.Exists(userDesktop))
                items.AddRange(ScanDirectory(userDesktop, recursive: true, maxDepth: 3));
            if (Directory.Exists(publicDesktop))
                items.AddRange(ScanDirectory(publicDesktop, recursive: true, maxDepth: 3));

            return MergeItems(items);
        }

        private static List<FileItem> MergeItems(List<FileItem> items)
        {
            var merged = new List<FileItem>();
            var folderMap = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
            var fileSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                if (item.IsFolder)
                {
                    if (folderMap.TryGetValue(item.Name, out var existing))
                    {
                        existing.Children.AddRange(item.Children);
                        existing.Children = MergeItems(existing.Children);
                    }
                    else
                    {
                        item.Children = MergeItems(item.Children);
                        folderMap[item.Name] = item;
                        merged.Add(item);
                    }
                }
                else
                {
                    if (fileSet.Add(item.Name))
                        merged.Add(item);
                }
            }

            return merged.OrderByDescending(x => x.IsFolder).ThenBy(x => x.Name).ToList();
        }

        private static List<FileItem> ScanDirectory(string path, bool recursive = true, int maxDepth = 99)
        {
            var items = new List<FileItem>();
            if (maxDepth <= 0) return items;

            try
            {
                foreach (var file in Directory.GetFiles(path))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (SupportedExtensions.Contains(ext))
                    {
                        items.Add(new FileItem { Name = Path.GetFileNameWithoutExtension(file), FullPath = file, IsFolder = false });
                    }
                }

                if (recursive && maxDepth > 1)
                {
                    foreach (var dir in Directory.GetDirectories(path))
                    {
                        string dirName = Path.GetFileName(dir);
                        var children = ScanDirectory(dir, recursive: true, maxDepth: maxDepth - 1);
                        if (children.Count > 0)
                        {
                            items.Add(new FileItem
                            {
                                Name = dirName,
                                FullPath = dir,
                                IsFolder = true,
                                Children = children.OrderByDescending(x => x.IsFolder).ThenBy(x => x.Name).ToList()
                            });
                        }
                    }
                }
            }
            catch (Exception) { }
            return items;
        }
    }

    public static class Win32FileDialog
    {
        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpstrFilter;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpstrFile;
            public int nMaxFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpstrFileTitle;
            public int nMaxFileTitle;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpstrInitialDir;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        public static string? ShowOpenFileDialog(IntPtr hwnd, string title = "Select File",
            string filter = "Executable Files (*.exe;*.lnk;*.url)\0*.exe;*.lnk;*.url\0All Files (*.*)\0*.*\0\0")
        {
            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf(typeof(OpenFileName)),
                hwndOwner = hwnd,
                lpstrTitle = title,
                lpstrFilter = filter,
                lpstrFile = new string('\0', 520),
                nMaxFile = 520,
                lpstrFileTitle = new string('\0', 128),
                nMaxFileTitle = 64,
                nFilterIndex = 1,
                Flags = 0x00080000 | 0x00001000
            };

            try
            {
                if (GetOpenFileNameW(ref ofn))
                    return ofn.lpstrFile.TrimEnd('\0');
            }
            catch (Exception) { }
            return null;
        }
    }

    #endregion

    #region Platform Specifics

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
            if (string.IsNullOrEmpty(url)) return null;
            foreach (var kvp in PlatformRegistry)
            {
                if (url.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase)) return kvp.Value;
            }
            return null;
        }

        public static async Task<GamePlatformInfo?> DetectPlatformAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;


            if (url.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (await StoreHelper.IsGameAsync(url))
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
            return platform;
        }

        public static bool IsSupportedPlatformUrl(string url) => DetectPlatform(url) != null;

        public static string? GetRuntimeManagerPath(string? mgrPath, string? exePath)
        {
            if (!string.IsNullOrEmpty(mgrPath)) return mgrPath;
            if (string.IsNullOrEmpty(exePath)) return null;

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

        public static string? GetPlatformDisplayName(string url) => DetectPlatform(url)?.PlatformName;
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
                var pathRegex = new Regex(@"""path""\s*""([^""]+)""");
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
                        var installDirMatch = Regex.Match(content, @"""installdir""\s*""([^""]+)""");
                        if (installDirMatch.Success) { installDir = installDirMatch.Groups[1].Value; libraryPath = libFolder; }
                        var nameMatch = Regex.Match(content, @"""name""\s*""([^""]+)""");
                        if (nameMatch.Success) gameName = nameMatch.Groups[1].Value;
                        if (!string.IsNullOrEmpty(installDir)) break;
                    }
                    catch (Exception) { }
                }
            }
            if (string.IsNullOrEmpty(installDir) || string.IsNullOrEmpty(libraryPath)) return null;
            string gameDir = Path.Combine(libraryPath, "steamapps", "common", installDir);

            // 优化：不再使用不稳定的可执行文件探测逻辑，直接进行启发式扫描
            string? fullExePath = FindMainExecutable(gameDir, gameName);

            // 增加兜底方案：如果找不到 EXE（或 DLL），则使用 Steam 本地缓存的图标
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
                // 获取两层深度的所有 EXE
                foreach (var file in Directory.EnumerateFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly)) exeFiles.Add(file);
                foreach (var subDir in Directory.EnumerateDirectories(gameDir))
                {
                    try
                    {
                        string dirName = Path.GetFileName(subDir).ToLower();
                        // 忽略某些明显的非主程序目录以提速
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

                    // 1. 黑名单过滤（致命打击 -1000）
                    if (excludePatterns.Any(p => fileName.Contains(p))) score -= 1000;

                    // 2. 名称匹配评分 (+50)
                    if (normalizedFileName == normalizedGameName || normalizedFileName == normalizedDirName) score += 50;
                    else if (normalizedFileName.Contains(normalizedGameName) || normalizedGameName.Contains(normalizedFileName)) score += 30;

                    // 3. 文件体积评分 (+30)
                    long length = new FileInfo(f).Length;
                    if (length > 10 * 1024 * 1024) score += 30; // 超过 10MB
                    else if (length > 1 * 1024 * 1024) score += 15; // 超过 1MB

                    // 4. 目录深度评分 (+20)
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

    #endregion

    #region Utilities

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
                        var match = Regex.Match(content, @"""AppName""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
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
                var installLocMatch = Regex.Match(jsonContent, @"""InstallLocation""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
                var launchExeMatch = Regex.Match(jsonContent, @"""LaunchExecutable""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);

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
                        var nameMatch = Regex.Match(content, @"""DisplayName""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
                        var appNameMatch = Regex.Match(content, @"""AppName""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
                        var catalogNsMatch = Regex.Match(content, @"""CatalogNamespace""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
                        var catalogItemMatch = Regex.Match(content, @"""CatalogItemId""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);

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

    public class RunActionBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void NotifyPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private string? _path;
        public string? Path
        {
            get => _path;
            set
            {
                if (_path != value)
                {
                    _path = value;
                    NotifyPropertyChanged(nameof(Path));
                }
            }
        }

        private bool _isAdmin;
        public bool IsAdmin
        {
            get => _isAdmin;
            set
            {
                if (_isAdmin != value)
                {
                    _isAdmin = value;
                    NotifyPropertyChanged(nameof(IsAdmin));
                }
            }
        }
    }

    public class AlternativeRunAction : RunActionBase
    {
        private bool _enabled;
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled != value) { _enabled = value; NotifyPropertyChanged(nameof(Enabled)); }
            }
        }
    }

    public class AlongsideRunAction : RunActionBase
    {
        private bool _enabled;
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled != value) { _enabled = value; NotifyPropertyChanged(nameof(Enabled)); }
            }
        }
    }

    public static class PathHashHelper
    {
        public static string GetPathHash(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Guid.NewGuid().ToString("N")[..16];

            string normalizedPath = path.ToLowerInvariant().Replace('/', '\\');
            byte[] hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(normalizedPath));

            StringBuilder sb = new();
            for (int i = 0; i < 8; i++)
                sb.Append(hashBytes[i].ToString("x2"));

            return sb.ToString();
        }

        public static bool VerifyPathHash(string path, string hash) => GetPathHash(path) == hash;
    }

    #endregion

    #region Update System

    public class UpdateService
    {
        private static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private const string GitHubApiUrl = "https://api.github.com/repos/EricZhang233/EricGameLauncher/releases/latest";
        private const string MirrorPrefix = "https://ghproxy.com/";

        public class ReleaseInfo
        {
            public string tag_name { get; set; } = "";
            public string name { get; set; } = "";
            public string html_url { get; set; } = "";
            public string body { get; set; } = "";
            public List<Asset> assets { get; set; } = new List<Asset>();
        }

        public class Asset
        {
            public string name { get; set; } = "";
            public string browser_download_url { get; set; } = "";
            public long size { get; set; }
        }

        public static async Task<ReleaseInfo?> CheckForUpdateAsync()
        {
            try
            {
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    client.DefaultRequestHeaders.Add("User-Agent", "EricGameLauncher-Updater");

                var release = await client.GetFromJsonAsync<ReleaseInfo>(GitHubApiUrl);
                if (release == null || string.IsNullOrEmpty(release.tag_name)) return null;

                var match = System.Text.RegularExpressions.Regex.Match(release.tag_name, @"(\d+\.\d+\.\d+(\.\d+)?)");
                if (!match.Success) return null;

                Version latestVersion = new Version(match.Value);
                Version currentVersion = new Version(AppVersion.Version);

                return latestVersion > currentVersion ? release : null;
            }
            catch { return null; }
        }

        public static void StartUpdater(string downloadUrl)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "EricGameLauncher");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

                string mainUpdaterPath = Path.Combine(tempDir, "updater.main.exe");

                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                string[] resources = { "updater.main.exe", "updater.main.dll", "updater.main.runtimeconfig.json" };
                foreach (var res in resources)
                {
                    string resName = $"EricGameLauncher.{res}";
                    string outputPath = Path.Combine(tempDir, res);
                    using (var stream = assembly.GetManifestResourceStream(resName))
                    {
                        if (stream == null) continue;
                        using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                        {
                            stream.CopyTo(fileStream);
                        }
                    }
                }

                string installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                string args = string.Join(" ", new[] { installDir, downloadUrl }
                    .Select(a => "\"" + a.Replace("\"", "\\\"") + "\""));

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = mainUpdaterPath,
                    Arguments = args,
                    UseShellExecute = true
                };

                Process.Start(psi);

                Application.Current.Exit();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to start updater: " + ex.Message);
            }
        }
    }

    #endregion
}
