using System;
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace LolManager.Converters;

public class RoleButtonConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string selectedRole && parameter is string role)
        {
            return selectedRole == role ? ControlAppearance.Primary : ControlAppearance.Secondary;
        }
        return ControlAppearance.Secondary;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

