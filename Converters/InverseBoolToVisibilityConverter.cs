using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LolManager.Converters
{
	public sealed class InverseBoolToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is not bool boolValue)
				return DependencyProperty.UnsetValue;

			// Инвертируем логику: true -> Collapsed, false -> Visible
			return boolValue ? Visibility.Collapsed : Visibility.Visible;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is not Visibility visibility)
				return DependencyProperty.UnsetValue;

			return visibility == Visibility.Collapsed;
		}
	}
}
