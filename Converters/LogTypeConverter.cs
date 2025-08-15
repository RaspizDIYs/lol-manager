using System;
using System.Globalization;
using System.Windows.Data;

namespace LolManager.Converters
{
	public sealed class LogTypeConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is not string logLine)
				return "INFO";

			// Извлекаем тип лога из строки формата "[HH:mm:ss.fff] TYPE ..."
			var bracketEnd = logLine.IndexOf(']');
			if (bracketEnd == -1) return "INFO";

			var afterBracket = logLine.Substring(bracketEnd + 1).Trim();
			var spaceIndex = afterBracket.IndexOf(' ');
			if (spaceIndex == -1) return "INFO";

			var logType = afterBracket.Substring(0, spaceIndex).Trim();
			
			return logType switch
			{
				"ERROR" => "ERROR",
				"WARN" => "WARN", 
				"HTTP" => "HTTP",
				"LOGIN" => "LOGIN",
				"UI" => "UI",
				"PROC" => "PROC",
				"DEBUG" => "DEBUG",
				"INFO" => "INFO",
				_ => "INFO"
			};
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}
}
