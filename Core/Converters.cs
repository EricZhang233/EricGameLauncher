using System;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace EricGameLauncher;

public sealed class ImagePathConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, WeakReference<BitmapImage>> _bitmapCache = new();

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        try { LogService.Write("Shortcut", $"ImagePathConverter.Convert called valueType={(value==null?"null":value.GetType().Name)} parameter={parameter}"); } catch { }
        if (value is not string path || string.IsNullOrEmpty(path))
            return null;

        try
        {
            if (!File.Exists(path))
                return null;

            long cacheKey = new FileInfo(path).LastWriteTime.Ticks;
            string cacheEntry = $"{path}@{cacheKey}";

            if (_bitmapCache.TryGetValue(cacheEntry, out var weakRef) && weakRef.TryGetTarget(out var cached))
                return cached;

            var bitmap = new BitmapImage();
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
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
    {
        try { LogService.Write("Shortcut", "ImagePathConverter.ConvertBack called"); } catch { }
        throw new NotImplementedException();
    }
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        try { LogService.Write("Shortcut", $"NullToVisibilityConverter.Convert called value={(value==null?"null":value.ToString())}"); } catch { }
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        try { LogService.Write("Shortcut", "NullToVisibilityConverter.ConvertBack called"); } catch { }
        throw new NotImplementedException();
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        try { LogService.Write("Shortcut", $"BoolToVisibilityConverter.Convert called value={(value==null?"null":value.ToString())}"); } catch { }
        return value is bool boolValue && boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        try { LogService.Write("Shortcut", $"BoolToVisibilityConverter.ConvertBack called value={(value==null?"null":value.ToString())}"); } catch { }
        return value is Visibility visibility && visibility == Visibility.Visible;
    }
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
    {
        try { LogService.Write("Shortcut", "SizeToCornerRadiusConverter.ConvertBack called"); } catch { }
        if (value is CornerRadius cr && cr.TopLeft > 0 && Ratio > 0)
            return cr.TopLeft / Ratio + MarginOffset;
        return 0.0;
    }
}
