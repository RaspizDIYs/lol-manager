using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LolManager.Services;

namespace LolManager.Converters;

public class ChampionSplashartConverter : IValueConverter
{
    private const int DefaultDecodeWidth = 384;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string championName ||
            string.IsNullOrWhiteSpace(championName) ||
            championName == "(Не выбрано)")
        {
            return DependencyProperty.UnsetValue;
        }

        var dataDragonService = ((App)App.Current).GetService<DataDragonService>();
        if (dataDragonService == null) return DependencyProperty.UnsetValue;

        var url = dataDragonService.GetChampionSplashartUrl(championName);
        if (string.IsNullOrEmpty(url)) return DependencyProperty.UnsetValue;

        var decodeWidth = ImageHelper.ResolveWidth(parameter, DefaultDecodeWidth);
        return ImageHelper.Load(url, decodeWidth) ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static int ResolveDecodeWidth(object? parameter, int defaultWidth) => ImageHelper.ResolveWidth(parameter, defaultWidth);
}

