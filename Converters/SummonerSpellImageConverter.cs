using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LolManager.Converters;

public class SummonerSpellImageConverter : IValueConverter
{
    private const string LatestVersion = "15.19.1";
    private const int DefaultWidth = 48;
    
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string spellName && !string.IsNullOrWhiteSpace(spellName) && spellName != "(Не выбрано)")
        {
            var dataDragonService = ((App)App.Current).GetService<Services.DataDragonService>();
            string url = string.Empty;

            if (dataDragonService != null)
            {
                var imageFileName = dataDragonService.GetSummonerSpellImageFileName(spellName);
                if (!string.IsNullOrWhiteSpace(imageFileName))
                {
                    url = $"https://ddragon.leagueoflegends.com/cdn/{LatestVersion}/img/spell/{imageFileName}.png";
                }
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                url = $"https://ddragon.leagueoflegends.com/cdn/{LatestVersion}/img/spell/Summoner{spellName}.png";
            }

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


