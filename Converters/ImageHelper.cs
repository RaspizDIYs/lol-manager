using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LolManager.Converters;

internal static class ImageHelper
{
    public static ImageSource? Load(string? url, int decodePixelWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(url, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodePixelWidth > 0)
            {
                bitmap.DecodePixelWidth = decodePixelWidth;
            }
            bitmap.EndInit();
            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }
            return bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImageHelper: failed to load '{url}': {ex.Message}");
            return null;
        }
    }

    public static int ResolveWidth(object? parameter, int fallback)
    {
        if (parameter is int numeric && numeric > 0)
            return numeric;

        if (parameter is string str && int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            return parsed;

        return fallback;
    }
}

