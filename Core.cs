using System;
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
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using TinyPinyin;

namespace EricGameLauncher
{
    #region Base Infrastructure

    /// Internationalization support
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
                catch (Exception) { }
            }

            if (_allTranslations != null && _allTranslations.TryGetValue(langCode, out var dict))
            {
                _strings = dict;
            }

            LanguageChanged?.Invoke();
        }

        /// Get a translated string by key.
        public static string T(string key)
        {
            return _strings.TryGetValue(key, out var value) ? value : key;
        }

        /// Get the list of available languages.
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

        /// Detect the system language.
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
                    _    => "EN",
                };
            }
            catch { return "EN"; }
        }

        /// Get the display name for a language code.
        public static string GetDisplayName(string langCode)
        {
            // Native name from the target language itself
            string nativeName = langCode;
            if (_allTranslations != null &&
                _allTranslations.TryGetValue(langCode, out var dict) &&
                dict.TryGetValue("_LangName", out var native) &&
                !string.IsNullOrEmpty(native))
                nativeName = native;

            // Localized name from the current UI language
            string localizedKey = "LangName_" + langCode;
            if (_strings.TryGetValue(localizedKey, out var localized) &&
                !string.IsNullOrEmpty(localized) &&
                localized != nativeName)
                return $"{nativeName} ({localized})";

            return nativeName;
        }
    }


    /// Configuration management service.
    public static class ConfigService
    {
        private const string AppFolderName = "EricGameLauncher";
        private const string DataFileName = "config.json";
        private const string IconFolderName = "ico";

        private static string SystemBasePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "eric", AppFolderName);
        private static string PortableBasePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

        public static string FixedCachePath => Path.Combine(CurrentDataPath, IconFolderName);
        public static string CurrentDataPath { get; private set; } = "";
        private static ConfigData? _configData;

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
                    int iconCount = 0;
                    foreach (var iconFile in Directory.GetFiles(oldIconPath, "*.png"))
                    {
                        string fileName = Path.GetFileName(iconFile);
                        string destFile = Path.Combine(newIconPath, fileName);
                        File.Copy(iconFile, destFile, true);
                        iconCount++;
                    }
                    if (iconCount > 0)
                    {
                        try { Directory.Delete(oldIconPath, true); } catch (Exception) { }
                    }
                }

                CurrentDataPath = newPath;
                LoadConfigData();
            }
            catch (Exception) { }
        }

        public static Task SwitchStorageModeAsync(bool useSystemPath) => Task.Run(() => SwitchStorageMode(useSystemPath));

        public static void SaveItems(List<AppItem> items)
        {
            if (_configData != null)
            {
                _configData.Items = items.Select(item => new AppItem
                {
                    Id = item.Id,
                    Title = item.Title,
                    IconPath = !string.IsNullOrEmpty(item.IconPath) ? Path.GetFileName(item.IconPath) : null,
                    ExePath = item.ExePath,
                    IsAdmin = item.IsAdmin,
                    MgrPath = item.MgrPath,
                    IsMgrAdmin = item.IsMgrAdmin,
                    IsAltAdmin = item.IsAltAdmin,
                    IsAlongsideAdmin = item.IsAlongsideAdmin,
                    UseAlternativeLaunch = item.UseAlternativeLaunch,
                    AlternativeLaunchCommand = item.AlternativeLaunchCommand,
                    RunAlongside = item.RunAlongside,
                    AlongsideCommand = item.AlongsideCommand
                }).ToList();
                SaveConfigData();
            }
        }

        public static List<AppItem> LoadItems()
        {
            var items = _configData?.Items ?? [];
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.IconPath) && !item.IconPath.Contains(Path.DirectorySeparatorChar) && !item.IconPath.Contains(Path.AltDirectorySeparatorChar))
                    item.IconPath = Path.Combine(FixedCachePath, item.IconPath);
            }
            return items;
        }

        private static void LoadConfigData()
        {
            try
            {
                if (string.IsNullOrEmpty(CurrentDataPath)) { _configData = new ConfigData(); return; }
                string jsonPath = Path.Combine(CurrentDataPath, DataFileName);
                if (!File.Exists(jsonPath)) { _configData = new ConfigData(); return; }
                string jsonString = File.ReadAllText(jsonPath);
                _configData = JsonSerializer.Deserialize<ConfigData>(jsonString) ?? new ConfigData();
                _configData.Settings ??= new AppSettings();
                _configData.Items ??= [];
            }
            catch { _configData = new ConfigData(); }
        }

        private static void SaveConfigData()
        {
            try
            {
                if (string.IsNullOrEmpty(CurrentDataPath) || _configData == null) return;
                string jsonPath = Path.Combine(CurrentDataPath, DataFileName);
                if (!Directory.Exists(CurrentDataPath)) Directory.CreateDirectory(CurrentDataPath);
                string jsonString = JsonSerializer.Serialize(_configData, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(jsonPath, jsonString);
            }
            catch (Exception) { }
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

        public static void SaveConfig() => SaveConfigData();

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

    public class AppItem : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string? _title;
        private string? _iconPath;
        private string? _exePath;
        private bool _isAdmin;
        private string? _mgrPath;
        private bool _isMgrAdmin;
        private bool _useAlternativeLaunch;
        private string? _alternativeLaunchCommand;
        private bool _runAlongside;
        private string? _alongsideCommand;
        private bool _isAltAdmin;
        private bool _isAlongsideAdmin;

        public event PropertyChangedEventHandler? PropertyChanged;

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

        public string? ExePath
        {
            get => _exePath;
            set
            {
                if (SetProperty(ref _exePath, value))
                {
                    if (!string.IsNullOrEmpty(value))
                        Id = PathHashHelper.GetPathHash(value);
                }
            }
        }

        public bool IsAdmin
        {
            get => _isAdmin;
            set => SetProperty(ref _isAdmin, value);
        }

        public string? MgrPath
        {
            get => _mgrPath;
            set
            {
                if (SetProperty(ref _mgrPath, value))
                    OnPropertyChanged(nameof(HasManager));
            }
        }

        public bool IsMgrAdmin
        {
            get => _isMgrAdmin;
            set => SetProperty(ref _isMgrAdmin, value);
        }

        public bool UseAlternativeLaunch
        {
            get => _useAlternativeLaunch;
            set => SetProperty(ref _useAlternativeLaunch, value);
        }

        public string? AlternativeLaunchCommand
        {
            get => _alternativeLaunchCommand;
            set => SetProperty(ref _alternativeLaunchCommand, value);
        }

        public bool IsAltAdmin
        {
            get => _isAltAdmin;
            set => SetProperty(ref _isAltAdmin, value);
        }

        public bool RunAlongside
        {
            get => _runAlongside;
            set => SetProperty(ref _runAlongside, value);
        }

        public string? AlongsideCommand
        {
            get => _alongsideCommand;
            set => SetProperty(ref _alongsideCommand, value);
        }

        public bool IsAlongsideAdmin
        {
            get => _isAlongsideAdmin;
            set => SetProperty(ref _isAlongsideAdmin, value);
        }

        [JsonIgnore]
        public bool HasManager => !string.IsNullOrEmpty(MgrPath);

        [JsonIgnore]
        public string? RuntimeManagerPath => GamePlatformHelper.GetRuntimeManagerPath(MgrPath, ExePath);

        [JsonIgnore]
        public bool IsPlatformUrl => !string.IsNullOrEmpty(ExePath) && GamePlatformHelper.IsSupportedPlatformUrl(ExePath);

        [JsonIgnore]
        public string? PlatformName => !string.IsNullOrEmpty(ExePath) ? GamePlatformHelper.GetPlatformDisplayName(ExePath) : null;

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
        [JsonPropertyName("settings")]
        public AppSettings Settings { get; set; } = new();

        [JsonPropertyName("items")]
        public List<AppItem> Items { get; set; } = [];
    }

    public class ShortcutInfo
    {
        public string? TargetPath { get; set; }
        public string? Arguments { get; set; }
        public string? IconPath { get; set; }
        public int IconIndex { get; set; }
        public bool IsUrl { get; set; }
        public string? ActualUrl { get; set; }
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

    #endregion

    #region UI & XAML Support

    public sealed class ImagePathConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not string path || string.IsNullOrEmpty(path))
                return null;

            try
            {
                if (!File.Exists(path))
                    return null;

                var bitmap = new BitmapImage();
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.DecodePixelWidth = 256;
                bitmap.DecodePixelHeight = 256;

                string fileUri = $"file:///{path.Replace("\\", "/")}?t={DateTime.Now.Ticks}";

                try
                {
                    bitmap.UriSource = new Uri(fileUri, UriKind.Absolute);
                    return bitmap;
                }
                catch (Exception)
                {
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                    return bitmap;
                }
            }
            catch (Exception)
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
            if (!exePath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase) && !File.Exists(exePath)) return null;
            
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
                if (!isStoreApp && !File.Exists(sourcePath)) return null;

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
                // First try IShellItemImageFactory (Modern Shell approach)
                Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"); // IShellItemImageFactory
                int hr = SHCreateItemFromParsingName(shellPath, IntPtr.Zero, iid, out IShellItemImageFactory? factory);
                
                if (hr == 0 && factory != null)
                {
                    // Try ICONONLY (0x4) | SCALEUP (0x100)
                    hr = factory.GetImage(new SIZE { cx = 256, cy = 256 }, 0x104, out IntPtr hBitmap);
                    
                    if (hr != 0)
                    {
                        // Fallback: THUMBNAILONLY (0x8) | SCALEUP (0x100)
                        hr = factory.GetImage(new SIZE { cx = 256, cy = 256 }, 0x108, out hBitmap);
                    }

                    if (hr == 0 && hBitmap != IntPtr.Zero)
                    {
                        try
                        {
                            // SHCreateItemFromParsingName GetImage returns an hBitmap (often a DIB section).
                            // Image.FromHbitmap often discards alpha. We manually create a 32bppArgb bitmap.
                            using var bmp = CreateBitmapFromHBitmap(hBitmap);
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

                // Fallback: SHGetFileInfo with PIDL (Classic Shell approach)
                IntPtr pidl = ILCreateFromPath(shellPath);
                if (pidl != IntPtr.Zero)
                {
                    try
                    {
                        SHFILEINFO shfi = new SHFILEINFO();
                        // 0x100 = SHGFI_ICON, 0x0 = SHGFI_LARGEICON, 0x8 = SHGFI_PIDL
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

        private static Bitmap? CreateBitmapFromHBitmap(IntPtr hBitmap)
        {
            if (hBitmap == IntPtr.Zero) return null;
            
            BITMAP bm;
            if (GetObject(hBitmap, Marshal.SizeOf(typeof(BITMAP)), out bm) != 0)
            {
                // If it's a 32-bit bitmap, it likely has an alpha channel.
                // bm.bmBits might be null if it's not a DIB section, but GetImage usually returns a DIB.
                if (bm.bmBitsPixel == 32 && bm.bmBits != IntPtr.Zero)
                {
                    // Create a bitmap that points to the same memory, then clone it to own the data.
                    using (var temp = new Bitmap(bm.bmWidth, bm.bmHeight, bm.bmWidthBytes, PixelFormat.Format32bppArgb, bm.bmBits))
                    {
                        return new Bitmap(temp);
                    }
                }
            }
            // Fallback for non-32bpp or non-DIB bitmaps
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
                try { File.Delete(filePath); return; } catch (Exception) { await Task.Delay(50); }
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
            return info?.TargetPath;
        }

        public static ShortcutInfo? GetShortcutInfo(string lnkPath)
        {
            if (string.IsNullOrEmpty(lnkPath) || !File.Exists(lnkPath))
                return null;

            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return null;

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return null;

                try
                {
                    dynamic link = shell.CreateShortcut(lnkPath);
                    if (link == null) return null;

                    var info = new ShortcutInfo
                    {
                        TargetPath = link.TargetPath as string,
                        Arguments = link.Arguments as string,
                        IconPath = link.IconLocation as string
                    };

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
                    if (Marshal.IsComObject(shell))
                        Marshal.ReleaseComObject(shell);
                }
            }
            catch (Exception)
            {
                // Logger.Log(ex); // Removed
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
                // Logger.Log(ex); // Removed
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

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ILCreateFromPath(string pszPath);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        [ComImport]
        [Guid("000214E6-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellFolder
        {
            void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
            void EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
            void BindToObject(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
            void BindToStorage(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
            void CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
            void CreateViewObject(IntPtr hwnd, [In] ref Guid riid, out IntPtr ppv);
            void GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
            void GetUIObjectOf(IntPtr hwnd, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, [In] ref Guid riid, IntPtr prgfInOut, out IntPtr ppv);
            void GetDisplayNameOf(IntPtr pidl, uint uFlags, out STRRET pName);
            void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
        }

        [StructLayout(LayoutKind.Sequential, Size = 272)]
        private struct STRRET
        {
            public uint uType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 260)]
            public byte[] data;
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrRetToBuf(ref STRRET pstr, IntPtr pidl, StringBuilder pszBuf, uint cchBuf);

        public static List<FileItem> GetStartMenuItems()
        {
            var items = new List<FileItem>();
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return items;

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return items;

                // shell:AppsFolder is a virtual folder containing all apps
                var appsFolder = shell.NameSpace("shell:AppsFolder");
                if (appsFolder != null)
                {
                    foreach (var item in appsFolder.Items())
                    {
                        try
                        {
                            string name = item.Name;
                            string path = item.Path; 

                            // Try to resolve to a physical path if it's a Win32 app
                            string finalPath = path;
                            bool isPhysical = File.Exists(path) || Directory.Exists(path);
                            
                            if (!isPhysical)
                            {
                                try
                                {
                                    // Try extended properties for MSI/other shell links
                                    // System.Link.TargetParsingPath (canonical name: PKEY_Link_TargetParsingPath)
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
                                catch { /* Ignore resolution errors */ }
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
                items.AddRange(ScanDirectory(userDesktop, recursive: true));
            if (Directory.Exists(publicDesktop))
                items.AddRange(ScanDirectory(publicDesktop, recursive: true));

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

        private static List<FileItem> ScanDirectory(string path, bool recursive = true)
        {
            var items = new List<FileItem>();
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

                if (recursive)
                {
                    foreach (var dir in Directory.GetDirectories(path))
                    {
                        string dirName = Path.GetFileName(dir);
                        var children = ScanDirectory(dir, recursive: true);
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
        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetOpenFileNameA(ref OpenFileName ofn);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            [MarshalAs(UnmanagedType.LPStr)] public string lpstrFilter;
            [MarshalAs(UnmanagedType.LPStr)] public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            [MarshalAs(UnmanagedType.LPStr)] public string lpstrFile;
            public int nMaxFile;
            [MarshalAs(UnmanagedType.LPStr)] public string lpstrFileTitle;
            public int nMaxFileTitle;
            [MarshalAs(UnmanagedType.LPStr)] public string lpstrInitialDir;
            [MarshalAs(UnmanagedType.LPStr)] public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            [MarshalAs(UnmanagedType.LPStr)] public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            [MarshalAs(UnmanagedType.LPStr)] public string lpTemplateName;
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
                lpstrFile = new string(new char[260]),
                nMaxFile = 260,
                lpstrFileTitle = new string(new char[64]),
                nMaxFileTitle = 64,
                nFilterIndex = 1,
                Flags = 0x00080000 | 0x00001000
            };

            try
            {
                if (GetOpenFileNameA(ref ofn))
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
            ["steam://"] = new GamePlatformInfo { PlatformName = "Steam", DefaultLauncherPath = "steam://open/main", UrlProtocol = "steam://" },
            ["com.epicgames.launcher://"] = new GamePlatformInfo { PlatformName = "Epic Games", DefaultLauncherPath = "com.epicgames.launcher://", UrlProtocol = "com.epicgames.launcher://" }
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

        public static bool IsSupportedPlatformUrl(string url) => DetectPlatform(url) != null;

        public static string? GetRuntimeManagerPath(string? mgrPath, string? exePath)
        {
            if (!string.IsNullOrEmpty(mgrPath)) return mgrPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                var platform = DetectPlatform(exePath);
                if (platform != null && !string.IsNullOrEmpty(platform.DefaultLauncherPath)) return platform.DefaultLauncherPath;
            }
            return null;
        }

        public static string? GetPlatformDisplayName(string url) => DetectPlatform(url)?.PlatformName;
    }

    public static class SteamHelper
    {
        private static readonly string[] DefaultSteamPaths = ["Program Files (x86)\\Steam", "Program Files\\Steam", "Steam"];
        private static string? _cachedSteamPath;
        private static List<string>? _cachedLibraryFolders;
        private static Dictionary<int, string>? _cachedExecutables;

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
            if (!Directory.Exists(gameDir)) return null;
            string? executable = GetExecutableFromAppInfo(appId);
            string? fullExePath = null;
            if (!string.IsNullOrEmpty(executable))
            {
                fullExePath = Path.Combine(gameDir, executable);
                if (!File.Exists(fullExePath)) fullExePath = FindExecutableInDirectory(gameDir, executable);
            }
            if (string.IsNullOrEmpty(fullExePath) || !File.Exists(fullExePath)) fullExePath = FindMainExecutable(gameDir, gameName);
            return (string.IsNullOrEmpty(fullExePath) || !File.Exists(fullExePath)) ? null : new SteamGameInfo { AppId = appId, Name = gameName, InstallDir = installDir, Executable = executable, FullExePath = fullExePath };
        }

        public static string? GetExecutableFromSteamUrl(string steamUrl)
        {
            var appId = ExtractAppIdFromUrl(steamUrl);
            return appId == null ? null : GetGameInfo(appId.Value)?.FullExePath;
        }

        private static string? GetExecutableFromAppInfo(int appId)
        {
            _cachedExecutables ??= ParseAppInfoExecutables();
            return _cachedExecutables.TryGetValue(appId, out string? exe) ? exe : null;
        }

        private static Dictionary<int, string> ParseAppInfoExecutables()
        {
            var result = new Dictionary<int, string>();
            string? steamPath = DetectSteamPath();
            if (string.IsNullOrEmpty(steamPath)) return result;
            string appInfoPath = Path.Combine(steamPath, "appcache", "appinfo.vdf");
            if (!File.Exists(appInfoPath)) return result;
            try
            {
                using var stream = new FileStream(appInfoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new BinaryReader(stream);
                reader.ReadUInt32(); reader.ReadUInt32();
                while (stream.Position < stream.Length - 4)
                {
                    uint id = reader.ReadUInt32();
                    if (id == 0) break;
                    try { reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt64(); reader.ReadBytes(20); reader.ReadUInt32(); string? exe = ParseVdfForExecutable(reader); if (!string.IsNullOrEmpty(exe)) result[(int)id] = exe; } catch (Exception) { break; }
                }
            }
            catch (Exception) { }
            return result;
        }

        private static string ReadCString(BinaryReader reader)
        {
            var bytes = new List<byte>();
            byte b;
            while ((b = reader.ReadByte()) != 0) bytes.Add(b);
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        private static string? ParseVdfForExecutable(BinaryReader reader)
        {
            string? executable = null;
            int depth = 0;
            bool inConfig = false, inLaunch = false, inLaunch0 = false;
            try
            {
                while (true)
                {
                    byte type = reader.ReadByte();
                    switch (type)
                    {
                        case 0x00: string name = ReadCString(reader); depth++; if (depth == 2 && name == "config") inConfig = true; if (inConfig && depth == 3 && name == "launch") inLaunch = true; if (inLaunch && depth == 4 && name == "0") inLaunch0 = true; break;
                        case 0x01: string key = ReadCString(reader); string value = ReadCString(reader); if (inLaunch0 && key == "executable" && string.IsNullOrEmpty(executable)) executable = value; break;
                        case 0x02: ReadCString(reader); reader.ReadInt32(); break;
                        case 0x07: ReadCString(reader); reader.ReadInt64(); break;
                        case 0x08: depth--; if (depth == 3) inLaunch0 = false; if (depth == 2) inLaunch = false; if (depth == 1) inConfig = false; if (depth < 0) return executable; break;
                        default: return executable;
                    }
                }
            }
            catch (Exception) { return executable; }
        }


        private static string? FindExecutableInDirectory(string directory, string exeName)
        {
            try
            {
                string directPath = Path.Combine(directory, exeName);
                if (File.Exists(directPath)) return directPath;
                foreach (var file in Directory.EnumerateFiles(directory, exeName, SearchOption.AllDirectories)) return file;
            }
            catch (Exception) { }
            return null;
        }

        private static string? FindMainExecutable(string gameDir, string? gameName)
        {
            try
            {
                var exeFiles = new List<string>();
                foreach (var file in Directory.EnumerateFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly)) exeFiles.Add(file);
                foreach (var subDir in Directory.EnumerateDirectories(gameDir))
                {
                    try { foreach (var file in Directory.EnumerateFiles(subDir, "*.exe", SearchOption.TopDirectoryOnly)) exeFiles.Add(file); } catch { /* Ignore access denied */ }
                }
                if (exeFiles.Count == 0) return null;
                var excludePatterns = new[] { "unins", "uninst", "setup", "install", "crash", "report", "update", "launcher", "redist", "vcredist", "dxsetup", "ue4prereq", "dotnet", "directx" };
                var candidates = exeFiles.Where(f => { string fileName = Path.GetFileNameWithoutExtension(f).ToLower(); return !excludePatterns.Any(p => fileName.Contains(p)); }).ToList();
                if (candidates.Count == 0) candidates = exeFiles;
                if (!string.IsNullOrEmpty(gameName))
                {
                    string normalizedName = Regex.Replace(gameName.ToLower(), @"[^a-z0-9]", "");
                    var bestMatch = candidates.OrderByDescending(f => { string fileName = Regex.Replace(Path.GetFileNameWithoutExtension(f).ToLower(), @"[^a-z0-9]", ""); if (fileName == normalizedName) return 100; if (fileName.Contains(normalizedName) || normalizedName.Contains(fileName)) return 50; return 0; }).ThenByDescending(f => new FileInfo(f).Length).FirstOrDefault();
                    if (bestMatch != null) return bestMatch;
                }
                return candidates.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
            }
            catch (Exception) { return null; }
        }

        public static void ClearCache() { _cachedSteamPath = null; _cachedLibraryFolders = null; _cachedExecutables = null; }
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
            if (string.IsNullOrEmpty(url) || !url.StartsWith("com.epicgames.launcher://apps/", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                int prefixLen = "com.epicgames.launcher://apps/".Length;
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
                    string installDir = installLocMatch.Groups[1].Value.Replace("\\\\", "\\");
                    string launchExe = launchExeMatch.Groups[1].Value.Replace("\\\\", "\\");
                    
                    string fullPath = Path.Combine(installDir, launchExe);
                    if (File.Exists(fullPath)) return fullPath;
                }
            }
            catch (Exception) { }
            return null;
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
}
