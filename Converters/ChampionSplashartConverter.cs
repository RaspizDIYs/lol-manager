using System;
using System.Globalization;
using System.Windows.Data;
using LolManager.Services;

namespace LolManager.Converters;

public class ChampionSplashartConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string championName && !string.IsNullOrWhiteSpace(championName) && championName != "(Не выбрано)")
        {
            var dataDragonService = ((App)App.Current).GetService<DataDragonService>();
            if (dataDragonService != null)
            {
                return dataDragonService.GetChampionSplashartUrl(championName);
            }
        }
        
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

