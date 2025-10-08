using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LolManager.Models;

public class PlayerInfo : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _rank = "Unranked";
    private string _tier = string.Empty;
    private int _leaguePoints;
    private int _wins;
    private int _losses;
    private string _winRate = "0%";
    private string _champion = string.Empty;
    private string _uggLink = string.Empty;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Rank
    {
        get => _rank;
        set => SetProperty(ref _rank, value);
    }

    public string Tier
    {
        get => _tier;
        set => SetProperty(ref _tier, value);
    }

    public int LeaguePoints
    {
        get => _leaguePoints;
        set => SetProperty(ref _leaguePoints, value);
    }

    public int Wins
    {
        get => _wins;
        set => SetProperty(ref _wins, value);
    }

    public int Losses
    {
        get => _losses;
        set => SetProperty(ref _losses, value);
    }

    public string WinRate
    {
        get => _winRate;
        set => SetProperty(ref _winRate, value);
    }

    public string Champion
    {
        get => _champion;
        set => SetProperty(ref _champion, value);
    }

    public string UggLink
    {
        get => _uggLink;
        set => SetProperty(ref _uggLink, value);
    }

    public string FullRank => string.IsNullOrEmpty(Tier) ? Rank : $"{Tier} {Rank} {LeaguePoints}LP";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(Wins) || propertyName == nameof(Losses))
        {
            OnPropertyChanged(nameof(FullRank));
            UpdateWinRate();
        }
        if (propertyName == nameof(Tier) || propertyName == nameof(Rank) || propertyName == nameof(LeaguePoints))
        {
            OnPropertyChanged(nameof(FullRank));
        }
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void UpdateWinRate()
    {
        var total = Wins + Losses;
        if (total > 0)
        {
            var rate = (double)Wins / total * 100;
            WinRate = $"{rate:F0}%";
        }
        else
        {
            WinRate = "0%";
        }
    }
}
