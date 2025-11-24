using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LolManager.Extensions;

public static class ObservableCollectionExtensions
{
    public static void UpdateRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        if (collection == null || items == null)
            return;
            
        var itemsList = items.ToList();
        if (itemsList.Count == 0)
        {
            collection.Clear();
            return;
        }
        
        var existingItems = collection.ToList();
        var itemsToAdd = itemsList.Except(existingItems).ToList();
        var itemsToRemove = existingItems.Except(itemsList).ToList();
        
        foreach (var item in itemsToRemove)
        {
            collection.Remove(item);
        }
        
        foreach (var item in itemsToAdd)
        {
            collection.Add(item);
        }
    }
    
    public static void ReplaceAll<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        if (collection == null || items == null)
            return;
            
        var itemsList = items.ToList();
        
        collection.Clear();
        
        foreach (var item in itemsList)
        {
            collection.Add(item);
        }
    }
}

