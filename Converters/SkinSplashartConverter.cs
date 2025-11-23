using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using LolManager.Models;

namespace LolManager.Converters;

public class SkinSplashartConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SkinInfo skin)
        {
            var dataDragonService = ((App)App.Current).GetService<Services.DataDragonService>();
            if (dataDragonService != null)
            {
                var url = dataDragonService.GetChampionSplashartUrl(skin.ChampionName, skin.SkinNumber);
                if (!string.IsNullOrEmpty(url))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(url, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        bitmap.EndInit();
                        return bitmap;
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
        }
        
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
