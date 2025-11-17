using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LolManager.Converters
{
	public sealed class IndexToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is not int currentIndex)
				return DependencyProperty.UnsetValue;

			int targetIndex;
			if (parameter is null)
				targetIndex = 0;
			else if (!int.TryParse(parameter.ToString(), out targetIndex))
				return DependencyProperty.UnsetValue;

			return currentIndex == targetIndex ? Visibility.Visible : Visibility.Collapsed;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}
}


