using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LolManager.Models;

public class AutomationSettings : INotifyPropertyChanged
{
    private string _championToPick = string.Empty;
    private string _championToBan = string.Empty;
    private string _summonerSpell1 = string.Empty;
    private string _summonerSpell2 = string.Empty;
    private bool _isEnabled;

    public string ChampionToPick
    {
        get => _championToPick;
        set => SetProperty(ref _championToPick, value);
    }

    public string ChampionToBan
    {
        get => _championToBan;
        set => SetProperty(ref _championToBan, value);
    }

    public string SummonerSpell1
    {
        get => _summonerSpell1;
        set => SetProperty(ref _summonerSpell1, value);
    }

    public string SummonerSpell2
    {
        get => _summonerSpell2;
        set => SetProperty(ref _summonerSpell2, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
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

