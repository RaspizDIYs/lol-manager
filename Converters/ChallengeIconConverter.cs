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
        if (value is ChallengeInfo challenge)
        {
            // Используем IconUrl если он уже установлен (формат Community Dragon)
            if (!string.IsNullOrWhiteSpace(challenge.IconUrl))
            {
                var width = ImageHelper.ResolveWidth(parameter, DefaultWidth);
                return ImageHelper.Load(challenge.IconUrl, width) ?? DependencyProperty.UnsetValue;
            }
            
            // Fallback: формируем URL если IconUrl пустой
            if (challenge.Id > 0 && !string.IsNullOrWhiteSpace(challenge.Tier))
            {
                var url = $"https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/assets/challenges/config/{challenge.Id}/tokens/{challenge.Tier.ToLowerInvariant()}.png";
                var width = ImageHelper.ResolveWidth(parameter, DefaultWidth);
                return ImageHelper.Load(url, width) ?? DependencyProperty.UnsetValue;
            }
        }
        
        return DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

