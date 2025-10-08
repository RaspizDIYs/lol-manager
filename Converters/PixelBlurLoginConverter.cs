using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LolManager.Converters;

public class PixelBlurLoginConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 2)
            return string.Empty;
        
        if (values[0] == DependencyProperty.UnsetValue || values[1] == DependencyProperty.UnsetValue)
            return string.Empty;
        
        var login = values[0] as string;
        if (login == null)
            return string.Empty;
        
        var hideLogin = values[1] is bool b && b;
        
        if (hideLogin && !string.IsNullOrEmpty(login))
        {
            // Имитация пикселизированного блюра через символы разного размера
            return GeneratePixelBlurText(login);
        }
        
        return login ?? string.Empty;
    }

    private string GeneratePixelBlurText(string originalText)
    {
        // Символы для имитации пикселей разных оттенков серого (от темного к светлому)
        var pixelChars = new char[] { 
            '█', '▓', '▒', '░', '■', '□', '▪', '▫'
        };
        
        var random = new Random(originalText.GetHashCode()); 
        var result = string.Empty;
        
        // Создаем блоки пикселей в одну строку, пропорциональные длине логина
        var pixelCount = originalText.Length * 2; // в 2 раза больше пикселей чем букв
        
        for (int i = 0; i < pixelCount; i++)
        {
            // Группируем пиксели по 2-3 одинаковых подряд для более реалистичного эффекта
            if (i > 0 && i % 3 == 0)
            {
                // Новая группа пикселей
                var pixelChar = pixelChars[random.Next(pixelChars.Length)];
                result += pixelChar;
            }
            else if (result.Length > 0)
            {
                // Повторяем последний символ для группировки
                result += result[result.Length - 1];
            }
            else
            {
                // Первый символ
                var pixelChar = pixelChars[random.Next(pixelChars.Length)];
                result += pixelChar;
            }
        }
        
        return result;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
