using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LolManager.Models;

public class UpdateSettings : INotifyPropertyChanged
{
    private bool _autoUpdateEnabled = true;
    private string _updateChannel = "stable";
    private int _checkIntervalHours = 24;
    private DateTime _lastCheckTime = DateTime.MinValue;
    private bool _skipVersion = false;
    private string _skippedVersion = string.Empty;
    private string _githubToken = string.Empty;
    private string _updateMode = "Direct"; // Direct | Velopack

    public bool AutoUpdateEnabled
    {
        get => _autoUpdateEnabled;
        set => SetProperty(ref _autoUpdateEnabled, value);
    }

    public string UpdateChannel
    {
        get => _updateChannel;
        set => SetProperty(ref _updateChannel, value);
    }

    public int CheckIntervalHours
    {
        get => _checkIntervalHours;
        set => SetProperty(ref _checkIntervalHours, value);
    }

    public DateTime LastCheckTime
    {
        get => _lastCheckTime;
        set => SetProperty(ref _lastCheckTime, value);
    }

    public bool SkipVersion
    {
        get => _skipVersion;
        set => SetProperty(ref _skipVersion, value);
    }

    public string SkippedVersion
    {
        get => _skippedVersion;
        set => SetProperty(ref _skippedVersion, value);
    }

    public string GithubToken
    {
        get => _githubToken;
        set => SetProperty(ref _githubToken, value ?? string.Empty);
    }

    public string UpdateMode
    {
        get => _updateMode;
        set => SetProperty(ref _updateMode, value ?? "Direct");
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
