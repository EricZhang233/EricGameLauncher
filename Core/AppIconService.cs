using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media.Imaging;

namespace EricGameLauncher;

public static class AppIconService
{
    private const string IconFileName = "ico.ico";
    private const string CacheIconName = "EricGameLauncher_TempIcon.ico";
    private const string ResourceName = "EricGameLauncher.ico.ico";

    private static bool _fallbackTried;
    private static string? _cachedPath;
    private static string? _cachedPngPath;

    public static string GetIconPath()
    {
        if (_cachedPath != null && File.Exists(_cachedPath))
            return _cachedPath;

        string exeDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, IconFileName);
        if (File.Exists(exeDirPath))
        {
            _cachedPath = exeDirPath;
            return exeDirPath;
        }

        string cachePath = Path.Combine(ConfigService.SystemCachePath, CacheIconName);
        if (File.Exists(cachePath))
        {
            _cachedPath = cachePath;
            return cachePath;
        }

        if (!_fallbackTried)
        {
            _fallbackTried = true;
            try
            {
                if (!Directory.Exists(ConfigService.SystemCachePath))
                    Directory.CreateDirectory(ConfigService.SystemCachePath);

                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(ResourceName);
                if (stream != null)
                {
                    using var fileStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write);
                    stream.CopyTo(fileStream);
                    LogService.Write("App", "AppIconService fallback extracted from embedded resource");
                }
            }
            catch (Exception ex) { LogService.Write("App", "AppIconService fallback extract failed", ex); }

            if (File.Exists(cachePath))
            {
                _cachedPath = cachePath;
                return cachePath;
            }
        }

        LogService.Write("App", "AppIconService no icon available");
        return string.Empty;
    }

    public static IntPtr GetCustomIconHandle()
    {
        string customPath = ConfigService.AppIconPath;
        if (string.IsNullOrWhiteSpace(customPath))
            return IntPtr.Zero;

        try
        {
            customPath = Environment.ExpandEnvironmentVariables(customPath);

            int iconIndex = 0;
            string basePath = customPath;

            if (!File.Exists(customPath))
            {
                int commaIdx = customPath.LastIndexOf(',');
                if (commaIdx > 0 && int.TryParse(customPath[(commaIdx + 1)..], out iconIndex))
                {
                    basePath = customPath[..commaIdx];
                    if (!File.Exists(basePath))
                        return IntPtr.Zero;
                }
                else
                {
                    return IntPtr.Zero;
                }
            }

            var hIconsLarge = new IntPtr[1];
            var hIconsSmall = new IntPtr[1];
            uint count = ExtractIconEx(basePath, iconIndex, hIconsLarge, hIconsSmall, 1);
            if (count > 0 && hIconsLarge[0] != IntPtr.Zero)
            {
                if (hIconsSmall[0] != IntPtr.Zero)
                    DestroyIcon(hIconsSmall[0]);
                LogService.Write("App", $"AppIconService got custom HICON from {basePath}[{iconIndex}]");
                return hIconsLarge[0];
            }

            if (hIconsSmall[0] != IntPtr.Zero)
                DestroyIcon(hIconsSmall[0]);
        }
        catch (Exception ex) { LogService.Write("App", "AppIconService GetCustomIconHandle failed", ex); }

        return IntPtr.Zero;
    }

    public static BitmapImage? GetBitmapImageFromHicon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero)
            return null;

        try
        {
            string pngPath = Path.Combine(ConfigService.SystemCachePath, "CustomIcon_TitleBar.png");
            if (!File.Exists(pngPath) || new FileInfo(pngPath).Length == 0)
            {
                if (!Directory.Exists(ConfigService.SystemCachePath))
                    Directory.CreateDirectory(ConfigService.SystemCachePath);

                using var icon = Icon.FromHandle(hIcon);
                using var bmp = icon.ToBitmap();
                bmp.Save(pngPath, ImageFormat.Png);
                LogService.Write("App", "AppIconService created titlebar png from HICON");
            }
            return new BitmapImage(new Uri(pngPath));
        }
        catch (Exception ex) { LogService.Write("App", "AppIconService GetBitmapImageFromHicon failed", ex); }
        return null;
    }

    public static BitmapImage? GetBitmapImage()
    {
        if (_cachedPngPath != null && File.Exists(_cachedPngPath))
            return new BitmapImage(new Uri(_cachedPngPath));

        string path = GetIconPath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        string pngPath = Path.ChangeExtension(path, ".png");
        if (!File.Exists(pngPath) || new FileInfo(pngPath).Length == 0)
        {
            try
            {
                using var img = Image.FromFile(path);
                img.Save(pngPath, ImageFormat.Png);
                LogService.Write("App", $"AppIconService converted ico to png: {pngPath}");
            }
            catch (Exception ex)
            {
                LogService.Write("App", "AppIconService convert to png failed", ex);
                return new BitmapImage(new Uri(path));
            }
        }

        _cachedPngPath = pngPath;
        return new BitmapImage(new Uri(pngPath));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
