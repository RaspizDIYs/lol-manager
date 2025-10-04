using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LolManager.Converters;

public class HideLoginConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 2)
            return string.Empty;
        
        // Проверяем что значения не UnsetValue
        if (values[0] == DependencyProperty.UnsetValue || values[1] == DependencyProperty.UnsetValue)
            return string.Empty;
        
        // Безопасное извлечение значений
        var login = values[0] as string;
        if (login == null)
            return string.Empty;
        
        var hideLogin = values[1] is bool b && b;
        
        if (hideLogin && !string.IsNullOrEmpty(login))
        {
            return new string('*', login.Length);
        }
        
        return login ?? string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

