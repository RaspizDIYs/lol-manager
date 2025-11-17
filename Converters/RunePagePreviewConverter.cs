using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using LolManager.Models;

namespace LolManager.Converters;

public class RunePagePreviewConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is not RunePage runePage)
            return Array.Empty<string>();

        var runeIcons = new List<string>();
        
        var runeDataService = ((App)System.Windows.Application.Current).GetService<Services.RuneDataService>();
        
        if (runePage.PrimaryKeystoneId != 0)
        {
            var rune = runeDataService.GetRuneById(runePage.PrimaryKeystoneId);
            if (rune != null) runeIcons.Add(rune.Icon);
        }
        
        if (runePage.PrimarySlot1Id != 0)
        {
            var rune = runeDataService.GetRuneById(runePage.PrimarySlot1Id);
            if (rune != null) runeIcons.Add(rune.Icon);
        }
        
        if (runePage.PrimarySlot2Id != 0)
        {
            var rune = runeDataService.GetRuneById(runePage.PrimarySlot2Id);
            if (rune != null) runeIcons.Add(rune.Icon);
        }
        
        if (runePage.PrimarySlot3Id != 0)
        {
            var rune = runeDataService.GetRuneById(runePage.PrimarySlot3Id);
            if (rune != null) runeIcons.Add(rune.Icon);
        }
        
        if (runePage.SecondarySlot1Id != 0)
        {
            var rune = runeDataService.GetRuneById(runePage.SecondarySlot1Id);
            if (rune != null) runeIcons.Add(rune.Icon);
        }
        
        if (runePage.SecondarySlot2Id != 0)
        {
            var rune = runeDataService.GetRuneById(runePage.SecondarySlot2Id);
            if (rune != null) runeIcons.Add(rune.Icon);
        }
        
        // Добавляем шардовые иконки (3 шт.)
        if (runePage.StatMod1Id != 0)
        {
            var shard = runeDataService.GetRuneById(runePage.StatMod1Id);
            if (shard != null) runeIcons.Add(shard.Icon);
        }
        if (runePage.StatMod2Id != 0)
        {
            var shard = runeDataService.GetRuneById(runePage.StatMod2Id);
            if (shard != null) runeIcons.Add(shard.Icon);
        }
        if (runePage.StatMod3Id != 0)
        {
            var shard = runeDataService.GetRuneById(runePage.StatMod3Id);
            if (shard != null) runeIcons.Add(shard.Icon);
        }

        return runeIcons.Take(9).ToArray();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
