using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace LolManager.Converters;

public class ImageUrlConverter : IValueConverter
{
    private const int DefaultDecodeWidth = 400;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string urlString || string.IsNullOrWhiteSpace(urlString))
        {
            return null;
        }

        var width = ImageHelper.ResolveWidth(parameter, DefaultDecodeWidth);
        return ImageHelper.Load(urlString, width) ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

