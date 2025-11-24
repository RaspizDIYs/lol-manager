using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LolManager.Models;

namespace LolManager.Converters;

public class ChallengeIconConverter : IValueConverter
{
    private const int DefaultWidth = 64;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ChallengeInfo challenge && !string.IsNullOrWhiteSpace(challenge.IconUrl))
        {
            var url = challenge.IconUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? challenge.IconUrl
                : $"https://ddragon.leagueoflegends.com/cdn/latest/img/challenge/{challenge.IconUrl}";

            var width = ImageHelper.ResolveWidth(parameter, DefaultWidth);
            return ImageHelper.Load(url, width) ?? DependencyProperty.UnsetValue;
        }
        
        return DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

