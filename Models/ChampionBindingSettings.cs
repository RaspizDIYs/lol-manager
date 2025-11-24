using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LolManager.Models;

public class ChampionBindingSettings : INotifyPropertyChanged
{
    private Dictionary<int, string> _championBindings = new();

    public Dictionary<int, string> ChampionBindings
    {
        get => _championBindings;
        set => SetProperty(ref _championBindings, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class BindingGroup
{
    public string Name { get; set; } = "default";
    public Dictionary<string, string> Settings { get; set; } = new();
}

