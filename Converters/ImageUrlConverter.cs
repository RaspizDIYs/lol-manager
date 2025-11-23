using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace LolManager.Converters;

public class ImageUrlConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string urlString;
        
        if (value is string url && !string.IsNullOrWhiteSpace(url))
        {
            urlString = url;
        }
        else
        {
            // Возвращаем null для пустых значений, чтобы Image не пытался загрузить дефолтное изображение
            return null;
        }
        
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(urlString, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnDemand;
            bitmap.DecodePixelWidth = 400;
            bitmap.DownloadFailed += (s, e) => 
            {
                // Логируем ошибку загрузки, но не падаем
                System.Diagnostics.Debug.WriteLine($"Failed to load image: {urlString}");
            };
            bitmap.EndInit();
            
            // Если изображение уже загружено в кеше, оно будет показано сразу
            // Если нет - WPF загрузит его асинхронно
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creating BitmapImage for {urlString}: {ex.Message}");
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

