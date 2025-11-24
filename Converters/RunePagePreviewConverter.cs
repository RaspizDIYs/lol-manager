using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;
using LolManager.Models;

namespace LolManager.Converters;

public class RunePagePreviewConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is not RunePage runePage)
            return Array.Empty<string>();

        var runeIcons = new List<ImageSource>();
        
        var runeDataService = ((App)System.Windows.Application.Current).GetService<Services.RuneDataService>();
        
        // Вспомогательная функция для безопасного добавления иконки
        void TryAddRuneIcon(int runeId)
        {
            if (runeId != 0)
            {
                var rune = runeDataService.GetRuneById(runeId);
                // Проверяем что rune не null, Icon не пустой и начинается с http
                if (rune != null && 
                    !string.IsNullOrWhiteSpace(rune.Icon) && 
                    rune.Icon.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var icon = ImageHelper.Load(rune.Icon, 32);
                    if (icon != null)
                    {
                        runeIcons.Add(icon);
                    }
                }
            }
        }
        
        TryAddRuneIcon(runePage.PrimaryKeystoneId);
        TryAddRuneIcon(runePage.PrimarySlot1Id);
        TryAddRuneIcon(runePage.PrimarySlot2Id);
        TryAddRuneIcon(runePage.PrimarySlot3Id);
        TryAddRuneIcon(runePage.SecondarySlot1Id);
        TryAddRuneIcon(runePage.SecondarySlot2Id);
        TryAddRuneIcon(runePage.SecondarySlot3Id); // Был пропущен!
        TryAddRuneIcon(runePage.StatMod1Id);
        TryAddRuneIcon(runePage.StatMod2Id);
        TryAddRuneIcon(runePage.StatMod3Id);

        return runeIcons.ToArray();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
