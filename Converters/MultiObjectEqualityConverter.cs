using System;
using System.Globalization;
using System.Windows.Data;

namespace LolManager.Converters;

public class MultiObjectEqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        
        var selectedValue = values[0];
        var currentValue = values[1];
        
        return selectedValue != null && selectedValue.Equals(currentValue);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
