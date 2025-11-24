using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LolManager.Converters;

public class RuneImageConverter : IValueConverter
{
    private const int DefaultWidth = 48;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string urlString && !string.IsNullOrWhiteSpace(urlString))
        {
            var width = ImageHelper.ResolveWidth(parameter, DefaultWidth);
            return ImageHelper.Load(urlString, width) ?? DependencyProperty.UnsetValue;
        }
        
        return DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
