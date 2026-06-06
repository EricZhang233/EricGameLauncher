using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using YamlDotNet.Serialization;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using TinyPinyin;

namespace EricGameLauncher;

public enum AppItemStatus
{
    Normal = 0,
    Recycled = 1,
    PendingDeletion = 2
}

public class CustomMenuItem
{
    [YamlMember(Alias = "title")]
    public string? Title { get; set; }
    [YamlMember(Alias = "command")]
    public string? Command { get; set; }
    [YamlMember(Alias = "admin")]
    public bool IsAdmin { get; set; }
}

public static class PathHashHelper
{
    public static string GetPathHash(string path)
    {
        try { LogService.Write("Item", $"GetPathHash called path={(path==null?"null":path)}"); } catch { }
        if (string.IsNullOrEmpty(path))
            return Guid.NewGuid().ToString("N")[..16];

        string normalizedPath = path.ToLowerInvariant().Replace('/', '\\');
        byte[] hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(normalizedPath));

        StringBuilder sb = new();
        for (int i = 0; i < 8; i++)
            sb.Append(hashBytes[i].ToString("x2"));
        var res = sb.ToString();
        try { LogService.Write("Item", $"GetPathHash result={res}"); } catch { }
        return res;
    }

    public static bool VerifyPathHash(string path, string hash) => GetPathHash(path) == hash;
}

public class AppItem : INotifyPropertyChanged
{
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }
    }

    private double _loadingOpacity = 0.0;
    public double LoadingOpacity
    {
        get => _loadingOpacity;
        set
        {
            if (_loadingOpacity != value)
            {
                _loadingOpacity = value;
                OnPropertyChanged(nameof(LoadingOpacity));
            }
        }
    }

    private string _id = string.Empty;
    private int _status = (int)AppItemStatus.Normal;
    private DateTimeOffset? _deletedAt;
    private string? _title;
    private string? _iconPath;
    private RunActionBase? _mainAction;
    private RunActionBase? _managerAction;
    private AlternativeRunAction? _altAction;
    private AlongsideRunAction? _alongsideAction;
    private string? _platform;
    private int _sortOrder = 0;
    private string? _titlePinyin;
    private string? _titlePinyinInitial;
    private string? _titleEnglishInitial;
    private List<CustomMenuItem>? _customMenuItems;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(TitleTextDecorations));
                OnPropertyChanged(nameof(TimeRemainingText));
                OnPropertyChanged(nameof(TimeBadgeVisibility));
            }
        }
    }

    public DateTimeOffset? DeletedAt
    {
        get => _deletedAt;
        set
        {
            if (SetProperty(ref _deletedAt, value))
            {
                OnPropertyChanged(nameof(TimeRemainingText));
            }
        }
    }

    public Windows.UI.Text.TextDecorations TitleTextDecorations => Status == (int)AppItemStatus.PendingDeletion ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;
    public Visibility TimeBadgeVisibility => Status == (int)AppItemStatus.PendingDeletion ? Visibility.Visible : Visibility.Collapsed;

    public string TimeRemainingText
    {
        get
        {
            if (Status == (int)AppItemStatus.PendingDeletion && DeletedAt.HasValue)
            {
                var remaining = DeletedAt.Value.AddHours(72) - DateTimeOffset.UtcNow;
                return $"{(int)Math.Max(0, remaining.TotalHours)}h";
            }
            return "";
        }
    }

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

    public int SortOrder
    {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
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

    [YamlIgnore]
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

    [YamlIgnore]
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

    [YamlIgnore]
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

    [YamlIgnore]
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

    [YamlIgnore]
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

    [YamlIgnore]
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

    [YamlIgnore]
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

    [YamlIgnore]
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

    [YamlIgnore]
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

    [YamlIgnore]
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

    [YamlIgnore]
    public string TitlePinyin
    {
        get
        {
            if (_titlePinyin == null && !string.IsNullOrEmpty(_title))
            {
                try { LogService.Write("Item", $"Computing TitlePinyin for title={_title}"); } catch { }
                _titlePinyin = TinyPinyin.PinyinHelper.GetPinyin(_title, "").ToLower();
                try { LogService.Write("Item", $"Computed TitlePinyin={_titlePinyin}"); } catch { }
            }
            return _titlePinyin ?? "";
        }
    }

    [YamlIgnore]
    public string TitlePinyinInitial
    {
        get
        {
            if (_titlePinyinInitial == null && !string.IsNullOrEmpty(_title))
            {
                try { LogService.Write("Item", $"Computing TitlePinyinInitial for title={_title}"); } catch { }
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
                try { LogService.Write("Item", $"Computed TitlePinyinInitial={_titlePinyinInitial}"); } catch { }
            }
            return _titlePinyinInitial ?? "";
        }
    }

    [YamlIgnore]
    public string TitleEnglishInitial
    {
        get
        {
            if (_titleEnglishInitial == null && !string.IsNullOrEmpty(_title))
            {
                try { LogService.Write("Item", $"Computing TitleEnglishInitial for title={_title}"); } catch { }
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
                try { LogService.Write("Item", $"Computed TitleEnglishInitial={_titleEnglishInitial}"); } catch { }
            }
            return _titleEnglishInitial ?? "";
        }
    }

    public string? IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }

    public List<CustomMenuItem> CustomMenuItems
    {
        get => _customMenuItems ??= [];
        set => SetProperty(ref _customMenuItems, value ?? []);
    }

    public List<CustomMenuItem> GetCustomMenuItems()
    {
        return CustomMenuItems;
    }

    public void SetCustomMenuItems(List<CustomMenuItem> items)
    {
        CustomMenuItems = items ?? [];
    }

    [YamlIgnore]
    public bool HasManager => !string.IsNullOrEmpty(MgrPath);

    [YamlIgnore]
    public string? RuntimeManagerPath => GamePlatformHelper.GetRuntimeManagerPath(MgrPath, ExePath);

    [YamlIgnore]
    public bool IsPlatformUrl => !string.IsNullOrEmpty(ExePath) && GamePlatformHelper.IsSupportedPlatformUrl(ExePath);

    public string? Platform
    {
        get => _platform;
        set => SetProperty(ref _platform, value);
    }

    [YamlIgnore]
    public string? PlatformName => !string.IsNullOrEmpty(Platform) ? Platform : (!string.IsNullOrEmpty(ExePath) ? GamePlatformHelper.GetPlatformDisplayName(ExePath) : null);

    [YamlIgnore]
    public bool HasManagerOrDefault => !string.IsNullOrEmpty(RuntimeManagerPath);

    [YamlIgnore]
    public BitmapImage? DisplayIcon { get; set; }

    public virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        try
        {
            string oldVal = field == null ? "null" : field.ToString() ?? "";
            string newVal = value == null ? "null" : value.ToString() ?? "";
            LogService.Write("App", $"SetProperty {propertyName} from={oldVal} to={newVal}");
        }
        catch { }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class AppItemDto
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "status")]
    public int Status { get; set; } = 0;

    [YamlMember(Alias = "deletedAt")]
    public DateTimeOffset? DeletedAt { get; set; }

    [YamlMember(Alias = "title")]
    public string? Title { get; set; }

    [YamlMember(Alias = "icon")]
    public string? Icon { get; set; }

    [YamlMember(Alias = "customMenu")]
    public List<CustomMenuItem>? CustomMenu { get; set; }

    [YamlMember(Alias = "platform")]
    public string? Platform { get; set; }

    [YamlMember(Alias = "actions")]
    public ActionsDto? Actions { get; set; }

    public static AppItemDto FromViewModel(AppItem vm)
    {
        if (vm == null) throw new ArgumentNullException(nameof(vm));
        try { LogService.Write("App", $"AppItemDto.FromViewModel called id={vm.Id} title={vm.Title}"); } catch { }
        return new AppItemDto
        {
            Id = vm.Id,
            Status = vm.Status,
            DeletedAt = vm.DeletedAt,
            Title = vm.Title,
            Icon = !string.IsNullOrEmpty(vm.IconPath) ? Path.GetFileName(vm.IconPath!) : null,
            CustomMenu = vm.CustomMenuItems.Count > 0 ? vm.CustomMenuItems.ToList() : null,
            Platform = vm.Platform,
            Actions = new ActionsDto
            {
                Main = new ActionDto { Path = vm.ExePath, IsAdmin = vm.IsAdmin },
                Manager = string.IsNullOrEmpty(vm.MgrPath) ? null : new ActionDto { Path = vm.MgrPath, IsAdmin = vm.IsMgrAdmin },
                Alt = (vm.UseAlternativeLaunch || !string.IsNullOrEmpty(vm.AlternativeLaunchCommand))
                    ? new AltActionDto { Enabled = vm.UseAlternativeLaunch, Command = vm.AlternativeLaunchCommand, IsAdmin = vm.IsAltAdmin }
                    : null,
                Alongside = (vm.RunAlongside || !string.IsNullOrEmpty(vm.AlongsideCommand))
                    ? new AlongActionDto { Enabled = vm.RunAlongside, Command = vm.AlongsideCommand, IsAdmin = vm.IsAlongsideAdmin }
                    : null
            }
        };
    }

    public AppItem ToViewModel(string iconCachePath)
    {
        try { LogService.Write("App", $"AppItem.ToViewModel called id={Id} title={Title}"); } catch { }
        return new AppItem
        {
            ExePath = Actions?.Main?.Path,
            IsAdmin = Actions?.Main?.IsAdmin ?? false,
            Id = string.IsNullOrEmpty(Id) ? PathHashHelper.GetPathHash(Actions?.Main?.Path ?? "") : Id,
            Status = Status,
            DeletedAt = DeletedAt,
            Title = Title,
            IconPath = (string.IsNullOrEmpty(Icon) || Path.IsPathRooted(Icon))
                            ? Icon
                            : (Icon.StartsWith("ico\\", StringComparison.OrdinalIgnoreCase) || Icon.StartsWith("ico/", StringComparison.OrdinalIgnoreCase))
                               ? Path.Combine(iconCachePath, Path.GetFileName(Icon))
                               : (!Icon.Contains(Path.DirectorySeparatorChar) && !Icon.Contains(Path.AltDirectorySeparatorChar))
                                  ? Path.Combine(iconCachePath, Icon)
                                  : Icon,
            CustomMenuItems = CustomMenu ?? [],
            Platform = Platform,
            MgrPath = Actions?.Manager?.Path,
            IsMgrAdmin = Actions?.Manager?.IsAdmin ?? false,
            UseAlternativeLaunch = Actions?.Alt?.Enabled ?? false,
            AlternativeLaunchCommand = Actions?.Alt?.Command,
            IsAltAdmin = Actions?.Alt?.IsAdmin ?? false,
            RunAlongside = Actions?.Alongside?.Enabled ?? false,
            AlongsideCommand = Actions?.Alongside?.Command,
            IsAlongsideAdmin = Actions?.Alongside?.IsAdmin ?? false,
        };
    }

    public class ActionsDto
    {
        [YamlMember(Alias = "main")]
        public ActionDto? Main { get; set; }
        [YamlMember(Alias = "manager")]
        public ActionDto? Manager { get; set; }
        [YamlMember(Alias = "alt")]
        public AltActionDto? Alt { get; set; }
        [YamlMember(Alias = "alongside")]
        public AlongActionDto? Alongside { get; set; }
    }

    public class ActionDto
    {
        [YamlMember(Alias = "path")]
        public string? Path { get; set; }
        [YamlMember(Alias = "admin")]
        public bool IsAdmin { get; set; }
    }

    public class AltActionDto
    {
        [YamlMember(Alias = "enabled")]
        public bool Enabled { get; set; }
        [YamlMember(Alias = "command")]
        public string? Command { get; set; }
        [YamlMember(Alias = "admin")]
        public bool IsAdmin { get; set; }
    }

    public class AlongActionDto
    {
        [YamlMember(Alias = "enabled")]
        public bool Enabled { get; set; }
        [YamlMember(Alias = "command")]
        public string? Command { get; set; }
        [YamlMember(Alias = "admin")]
        public bool IsAdmin { get; set; }
    }
}

public static class IconHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern uint PrivateExtractIcons(string lpszFile, int nIconIndex, int cxIcon, int cyIcon, IntPtr[]? phicon, uint[]? piconid, uint nIcons, uint flags);

    private static string CachePath => ConfigService.FixedCachePath;
    private static readonly int[] IconSizes = [512, 256, 192, 128, 96, 72, 64, 48, 32, 24, 16];

    public static async Task<string?> GetIconPathAsync(string exePath, string itemId, bool forceExtract = false)
    {
        using (LogService.StartOperation("Item", "GetIconPathAsync"))
        {
            if (string.IsNullOrEmpty(exePath)) return null;
            if (!exePath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase) && !File.Exists(exePath)) return null;
            string iconPath = Path.Combine(CachePath, $"{itemId}.png");
            if (forceExtract && File.Exists(iconPath))
            {
                try { File.Delete(iconPath); } catch (Exception ex) { LogService.Write("App", "Delete icon cache failed", ex); }
            }
            if (!forceExtract && File.Exists(iconPath) && new FileInfo(iconPath).Length > 0) return iconPath;
            return await ExtractAndSaveIconAsync(exePath, itemId);
        }
    }

    public static async Task<string?> ExtractAndSaveIconAsync(string sourcePath, string itemId, bool extractFromLnk = false)
    {
        using (LogService.StartOperation("Item", "ExtractAndSaveIconAsync"))
        {
            try
            {
                LogService.Write("Item", $"ExtractAndSaveIconAsync Start source={sourcePath} itemId={itemId} extractFromLnk={extractFromLnk}");
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
                            LogService.Write("Item", $"ExtractAndSaveIconAsync resolved lnk target={resolvedPath}");
                            if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath)) targetPath = resolvedPath;
                        }
                        else
                        {
                            var shortcutInfo = ShortcutResolver.GetShortcutInfo(sourcePath);
                            if (shortcutInfo != null && !string.IsNullOrEmpty(shortcutInfo.IconPath) && File.Exists(shortcutInfo.IconPath)) { targetPath = shortcutInfo.IconPath; iconIndex = shortcutInfo.IconIndex; }
                            LogService.Write("Item", $"ExtractAndSaveIconAsync shortcutInfo iconPath={shortcutInfo?.IconPath} index={shortcutInfo?.IconIndex}");
                        }
                    }
                    else if (sourcePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                    {
                        var urlInfo = ShortcutResolver.GetUrlFileInfo(sourcePath);
                        LogService.Write("Item", $"ExtractAndSaveIconAsync urlInfo iconPath={urlInfo?.IconPath} index={urlInfo?.IconIndex}");
                        if (urlInfo != null && !string.IsNullOrEmpty(urlInfo.IconPath) && File.Exists(urlInfo.IconPath)) { targetPath = urlInfo.IconPath; iconIndex = urlInfo.IconIndex; }
                    }
                }

                if (!Directory.Exists(CachePath)) Directory.CreateDirectory(CachePath);

                string savePath = Path.Combine(CachePath, $"{itemId}.png");
                await ForceDeleteFileAsync(savePath);

                Icon? icon = null;
                if (isStoreApp)
                {
                    LogService.Write("Item", $"ExtractAndSaveIconAsync attempting store app extraction for {sourcePath}");
                    if (ExtractStoreAppIcon(sourcePath, savePath))
                    {
                        LogService.Write("Item", $"ExtractAndSaveIconAsync store app icon saved={savePath}");
                        return savePath;
                    }
                }
                else
                {
                    string ext = Path.GetExtension(targetPath)?.ToLowerInvariant() ?? "";
                    var imageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

                    if (imageExts.Contains(ext))
                    {
                        try
                        {
                            using (var img = System.Drawing.Image.FromFile(targetPath))
                            {
                                img.Save(savePath, ImageFormat.Png);
                            }
                            await Task.Delay(50);
                            if (File.Exists(savePath) && new FileInfo(savePath).Length > 0)
                            {
                                LogService.Write("Item", $"ExtractAndSaveIconAsync saved image icon={savePath}");
                                return savePath;
                            }
                        }
                        catch (Exception ex) { LogService.Write("Item", "Save image icon failed", ex); }
                    }
                    else if (ext == ".ico")
                    {
                        icon = ExtractLargestIcon(targetPath, iconIndex) ?? Icon.ExtractAssociatedIcon(targetPath);
                    }
                    else
                    {
                        icon = ExtractLargestIcon(targetPath, iconIndex) ?? Icon.ExtractAssociatedIcon(targetPath);
                    }
                }

                if (icon != null)
                {
                    try { using var bmp = icon.ToBitmap(); bmp.Save(savePath, ImageFormat.Png); } finally { icon.Dispose(); }
                    await Task.Delay(50);
                    if (File.Exists(savePath) && new FileInfo(savePath).Length > 0)
                    {
                        LogService.Write("Item", $"ExtractAndSaveIconAsync saved extracted icon={savePath}");
                        return savePath;
                    }
                }
                LogService.Write("Item", "ExtractAndSaveIconAsync failed to produce icon");
                return null;
            }
            catch (Exception ex) { LogService.Write("Item", "ExtractAndSaveIcon failed", ex); return null; }
        }
    }

    private static readonly int[] StoreIconSizes = [512, 256, 150, 128, 96, 72, 64, 48, 44, 32];

    private static bool ExtractStoreAppIcon(string shellPath, string savePath)
    {
        try
        {
            Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            int hr = SHCreateItemFromParsingName(shellPath, IntPtr.Zero, iid, out IShellItemImageFactory? factory);

            if (hr == 0 && factory != null)
            {
                foreach (int sz in StoreIconSizes)
                {
                    hr = factory.GetImage(new SIZE { cx = sz, cy = sz }, 0x4, out IntPtr hBitmap);
                    if (hr != 0)
                        hr = factory.GetImage(new SIZE { cx = sz, cy = sz }, 0x8, out hBitmap);

                    if (hr == 0 && hBitmap != IntPtr.Zero)
                    {
                        try
                        {
                            using var bmp = CreateBitmapFromHBitmap(hBitmap, true);
                            if (bmp != null)
                            {
                                bmp.Save(savePath, ImageFormat.Png);
                                LogService.Write("Item", $"ExtractStoreAppIcon IShellItemImageFactory success size={sz}");
                                return true;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Write("Item", "ExtractStoreAppIcon: save bitmap failed", ex);
                        }
                        finally { DeleteObject(hBitmap); }
                    }
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
        catch (Exception ex) { LogService.Write("App", "ExtractStoreAppIcon failed", ex); }
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
        catch (Exception ex) { LogService.Write("App", "ExtractLargestIcon failed", ex); }
        return null;
    }

    private static async Task ForceDeleteFileAsync(string filePath)
    {
        using (LogService.StartOperation("Item", "ForceDeleteFileAsync"))
        {
            if (!File.Exists(filePath)) return;
            for (int i = 0; i < 3; i++)
            {
                try { await Task.Run(() => File.Delete(filePath)); return; } catch (Exception ex) { LogService.Write("App", "ForceDeleteFile attempt failed", ex); await Task.Delay(50); }
            }
        }
    }
}