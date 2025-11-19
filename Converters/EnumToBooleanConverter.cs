using System;
using System.Globalization;
using System.Windows.Data;

#nullable disable

namespace LolManager.Converters
{
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is null || parameter is null)
                return false;

            string enumValue = value.ToString();
            string targetValue = parameter.ToString();

            if (string.IsNullOrEmpty(enumValue) || string.IsNullOrEmpty(targetValue))
                return false;

            return string.Equals(enumValue!, targetValue!, StringComparison.InvariantCultureIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is false)
                return System.Windows.DependencyProperty.UnsetValue;

            if (parameter is null)
                return System.Windows.DependencyProperty.UnsetValue;

            string targetValue = parameter.ToString();
            if (string.IsNullOrEmpty(targetValue))
                return System.Windows.DependencyProperty.UnsetValue;

            return Enum.Parse(targetType, targetValue!);
        }
    }
}
