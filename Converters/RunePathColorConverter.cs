using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LolManager.Models;

namespace LolManager.Converters;

public class RunePathColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not RunePath path)
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6C8CD5"));

        var color = (Color)ColorConverter.ConvertFromString(path.ColorHex);
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

