using System;
using System.Globalization;
using System.Windows.Data;

namespace LolManager.Converters;

public class SummonerSpellImageConverter : IValueConverter
{
    private const string LatestVersion = "15.19.1";
    
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string spellName && !string.IsNullOrWhiteSpace(spellName) && spellName != "(Не выбрано)")
        {
            // Получаем DataDragonService для маппинга имён файлов
            var dataDragonService = ((App)App.Current).GetService<Services.DataDragonService>();
            if (dataDragonService != null)
            {
                var imageFileName = dataDragonService.GetSummonerSpellImageFileName(spellName);
                if (!string.IsNullOrWhiteSpace(imageFileName))
                {
                    return $"https://ddragon.leagueoflegends.com/cdn/{LatestVersion}/img/spell/{imageFileName}.png";
                }
            }
            
            // Фоллбек: пробуем добавить "Summoner" префикс
            return $"https://ddragon.leagueoflegends.com/cdn/{LatestVersion}/img/spell/Summoner{spellName}.png";
        }
        
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

