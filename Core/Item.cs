using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
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
    public string? Title { get; set; }
    public string? Command { get; set; }
    public bool IsAdmin { get; set; }
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
    private string? _customMenu;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(Status));
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
                OnPropertyChanged(nameof(DeletedAt));
                OnPropertyChanged(nameof(TimeRemainingText));
                OnPropertyChanged(nameof(TimeBadgeVisibility));
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

    public virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class AppItemDto
{
    public string Id { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Status { get; set; } = 0;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? DeletedAt { get; set; }

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
        Status = vm.Status,
        DeletedAt = vm.DeletedAt,
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
        Id = string.IsNullOrEmpty(Id) ? PathHashHelper.GetPathHash(MainAction?.Path ?? "") : Id,
        Status = Status,
        DeletedAt = DeletedAt,
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
        if (string.IsNullOrEmpty(exePath)) return null;
        string iconPath = Path.Combine(CachePath, $"{itemId}.png");
        if (forceExtract && File.Exists(iconPath))
        {
            try { File.Delete(iconPath); } catch { }
        }
        if (!forceExtract && File.Exists(iconPath) && new FileInfo(iconPath).Length > 0) return iconPath;
        return await ExtractAndSaveIconAsync(exePath, itemId);
    }

    public static async Task<string?> ExtractAndSaveIconAsync(string sourcePath, string itemId, bool extractFromLnk = false)
    {
        try
        {
            if (string.IsNullOrEmpty(sourcePath)) return null;
            bool isStoreApp = sourcePath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase);

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
                        if (File.Exists(savePath) && new FileInfo(savePath).Length > 0) return savePath;
                    }
                    catch { }
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