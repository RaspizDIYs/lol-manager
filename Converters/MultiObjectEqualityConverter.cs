using System;
using System.Globalization;
using System.Windows.Data;
using LolManager.Models;

namespace LolManager.Converters;

public class MultiObjectEqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        
        var selectedValue = values[0];
        var currentValue = values[1];

        if (selectedValue == null || currentValue == null) return false;

        // Сравнение по Id для моделей рун/путей
        if (selectedValue is Rune sr && currentValue is Rune cr)
        {
            return sr.Id == cr.Id && sr.Id != 0;
        }
        if (selectedValue is RunePath sp && currentValue is RunePath cp)
        {
            return sp.Id == cp.Id && sp.Id != 0;
        }

        return selectedValue.Equals(currentValue);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
