using System;
using System.Globalization;
using System.Windows.Data;
using LolManager.Models;

namespace LolManager.Converters;

public class ChallengeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ChallengeInfo challenge && !string.IsNullOrWhiteSpace(challenge.IconUrl))
        {
            if (challenge.IconUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return challenge.IconUrl;
            }
            
            return $"https://ddragon.leagueoflegends.com/cdn/latest/img/challenge/{challenge.IconUrl}";
        }
        
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

