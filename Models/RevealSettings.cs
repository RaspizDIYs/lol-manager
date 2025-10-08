using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LolManager.Models;

public class RevealSettings : INotifyPropertyChanged
{
    private bool _isEnabled;
    private string _riotApiKey = string.Empty;
    private string _selectedRegion = "euw1";

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public string RiotApiKey
    {
        get => _riotApiKey;
        set => SetProperty(ref _riotApiKey, value);
    }

    public string SelectedRegion
    {
        get => _selectedRegion;
        set => SetProperty(ref _selectedRegion, value);
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
