using System;
using System.Globalization;
using System.Windows.Data;

namespace LolManager.Converters;

public class RuneImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Если строка пустая или null, возвращаем null - тогда Image просто не отобразится
        if (value is string urlString && !string.IsNullOrWhiteSpace(urlString))
        {
            return urlString; // Возвращаем непустой URL, WPF сам конвертирует в ImageSource
        }
        
        return null; // Для пустых строк возвращаем null
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
