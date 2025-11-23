using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LolManager.Models;

public class AutomationSettings : INotifyPropertyChanged
{
    private string _championToPick1 = string.Empty;
    private string _championToPick2 = string.Empty;
    private string _championToPick3 = string.Empty;
    private string _championToBan = string.Empty;
    private string _summonerSpell1 = string.Empty;
    private string _summonerSpell2 = string.Empty;
    private bool _isEnabled;
    private string _autoAcceptMethod = "Polling";
    private List<RunePage> _runePages = new();
    private string _selectedRunePageName = string.Empty;
    private bool _isPickDelayEnabled;
    private int _pickDelaySeconds;
    private bool _autoRuneGenerationEnabled;

    public string ChampionToPick
    {
        get => _championToPick1;
        set
        {
            if (string.IsNullOrWhiteSpace(_championToPick1))
            {
                SetProperty(ref _championToPick1, value);
            }
        }
    }

    public string ChampionToPick1
    {
        get => _championToPick1;
        set => SetProperty(ref _championToPick1, value);
    }

    public string ChampionToPick2
    {
        get => _championToPick2;
        set => SetProperty(ref _championToPick2, value);
    }

    public string ChampionToPick3
    {
        get => _championToPick3;
        set => SetProperty(ref _championToPick3, value);
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

    public string AutoAcceptMethod
    {
        get => _autoAcceptMethod;
        set => SetProperty(ref _autoAcceptMethod, value);
    }

    public List<RunePage> RunePages
    {
        get => _runePages;
        set => SetProperty(ref _runePages, value);
    }

    public string SelectedRunePageName
    {
        get => _selectedRunePageName;
        set => SetProperty(ref _selectedRunePageName, value);
    }

    public bool AutoRuneGenerationEnabled
    {
        get => _autoRuneGenerationEnabled;
        set => SetProperty(ref _autoRuneGenerationEnabled, value);
    }

    public bool IsPickDelayEnabled
    {
        get => _isPickDelayEnabled;
        set => SetProperty(ref _isPickDelayEnabled, value);
    }

    public int PickDelaySeconds
    {
        get => _pickDelaySeconds;
        set => SetProperty(ref _pickDelaySeconds, value);
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

