using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media.Imaging;

namespace EricGameLauncher;

public static class AppIconService
{
    private const string ResourceName = "EricGameLauncher.ico.ico";
    private static readonly Lazy<byte[]> DefaultPng = new(CreateDefaultPng);

    internal static Stream OpenDefaultIconStream()
        => Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName) ?? throw new MissingManifestResourceException(ResourceName);

    public static IntPtr GetDefaultIconHandle()
    {
        try
        {
            using var stream = OpenDefaultIconStream();
            using var icon = new Icon(stream, 256, 256);
            IntPtr handle = CopyIcon(icon.Handle);
            if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            LogService.Write("App", "AppIconService loaded default HICON from embedded resource");
            return handle;
        }
        catch (Exception ex) { LogService.Write("App", "AppIconService GetDefaultIconHandle failed", ex); return IntPtr.Zero; }
    }

    public static IntPtr GetCustomIconHandle()
    {
        string customPath = ConfigService.AppIconPath;
        if (string.IsNullOrWhiteSpace(customPath)) return IntPtr.Zero;
        try
        {
            customPath = Environment.ExpandEnvironmentVariables(customPath);
            int iconIndex = 0;
            string basePath = customPath;
            if (!File.Exists(customPath))
            {
                int commaIdx = customPath.LastIndexOf(',');
                if (commaIdx <= 0 || !int.TryParse(customPath[(commaIdx + 1)..], out iconIndex)) return IntPtr.Zero;
                basePath = customPath[..commaIdx];
                if (!File.Exists(basePath)) return IntPtr.Zero;
            }
            var large = new IntPtr[1];
            var small = new IntPtr[1];
            uint count = ExtractIconEx(basePath, iconIndex, large, small, 1);
            if (small[0] != IntPtr.Zero) DestroyIcon(small[0]);
            if (count > 0 && large[0] != IntPtr.Zero)
            {
                LogService.Write("App", $"AppIconService got custom HICON from {basePath}[{iconIndex}]");
                return large[0];
            }
            if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
        }
        catch (Exception ex) { LogService.Write("App", "AppIconService GetCustomIconHandle failed", ex); }
        return IntPtr.Zero;
    }

    private static byte[] CreatePng(Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private static byte[] CreateDefaultPng()
    {
        using var stream = OpenDefaultIconStream();
        using var icon = new Icon(stream, 256, 256);
        LogService.Write("App", "AppIconService decoded embedded default icon in memory");
        return CreatePng(icon);
    }

    private static BitmapImage CreateBitmapImage(byte[] png)
    {
        using var stream = new MemoryStream(png, false);
        var image = new BitmapImage();
        image.SetSource(stream.AsRandomAccessStream());
        return image;
    }

    public static BitmapImage? GetBitmapImageFromHicon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        try
        {
            using var icon = Icon.FromHandle(hIcon);
            return CreateBitmapImage(CreatePng(icon));
        }
        catch (Exception ex) { LogService.Write("App", "AppIconService GetBitmapImageFromHicon failed", ex); return null; }
    }

    public static BitmapImage? GetBitmapImage()
    {
        try { return CreateBitmapImage(DefaultPng.Value); }
        catch (Exception ex) { LogService.Write("App", "AppIconService GetBitmapImage failed", ex); return null; }
    }

    public static async Task<BitmapImage?> LoadBitmapImageAsync()
    {
        try
        {
            using var stream = new MemoryStream(DefaultPng.Value, false);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream.AsRandomAccessStream());
            return image;
        }
        catch (Exception ex) { LogService.Write("App", "AppIconService LoadBitmapImageAsync failed", ex); return null; }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CopyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
