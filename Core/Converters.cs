using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace EricGameLauncher;

public static class ConverterSummary
{
    private static int _imageTotal, _imageMissing, _nullTotal, _boolTotal;
    private static HashSet<string> _missingPaths = new();

    public static void RecordImage() { _imageTotal++; }
    public static void RecordImageMissing(string path) { _imageMissing++; _missingPaths.Add(path); }
    public static void RecordNull() { _nullTotal++; }
    public static void RecordBool() { _boolTotal++; }

    public static void Flush()
    {
        int total = _imageTotal + _nullTotal + _boolTotal;
        if (total == 0) return;
        if (_imageMissing > 0)
            LogService.Write("UI", $"Converter Summary: {total} calls (img={_imageTotal} null={_nullTotal} bool={_boolTotal}), {_imageMissing} missing icons: [{string.Join(", ", _missingPaths)}]");
        else
            LogService.Write("UI", $"Converter Summary: {total} calls (img={_imageTotal} null={_nullTotal} bool={_boolTotal}), all ok");
        _imageTotal = _imageMissing = _nullTotal = _boolTotal = 0;
        _missingPaths.Clear();
    }
}

public sealed class ImagePathConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, WeakReference<BitmapImage>> _bitmapCache = new();

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        ConverterSummary.RecordImage();
        if (value is not string path || string.IsNullOrEmpty(path))
            return null;

        try
        {
            if (!File.Exists(path))
            {
                ConverterSummary.RecordImageMissing(path);
                return null;
            }

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
        throw new NotImplementedException();
    }
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        ConverterSummary.RecordNull();
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        ConverterSummary.RecordBool();
        return value is bool boolValue && boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
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
