using System;
using System.Globalization;
using System.Windows.Data;

namespace LolManager.Converters;

public class ChampionImageConverter : IValueConverter
{
    private const string LatestVersion = "15.19.1";
    
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string championName && !string.IsNullOrWhiteSpace(championName) && championName != "(Не выбрано)")
        {
            // Получаем DataDragonService для маппинга имён файлов
            var dataDragonService = ((App)App.Current).GetService<Services.DataDragonService>();
            if (dataDragonService != null)
            {
                var imageFileName = dataDragonService.GetChampionImageFileName(championName);
                if (!string.IsNullOrWhiteSpace(imageFileName))
                {
                    return $"https://ddragon.leagueoflegends.com/cdn/{LatestVersion}/img/champion/{imageFileName}.png";
                }
            }
            
            // Фоллбек: пробуем использовать имя напрямую
            return $"https://ddragon.leagueoflegends.com/cdn/{LatestVersion}/img/champion/{championName}.png";
        }
        
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

