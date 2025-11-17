using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LolManager.Models;

public class LeagueSettings : INotifyPropertyChanged
{
    private bool _preferManualPath;
    private string? _installDirectory;
    private string? _lastDetectedInstallDirectory;
    private DateTime _lastDetectedAtUtc;

    public bool PreferManualPath
    {
        get => _preferManualPath;
        set => SetProperty(ref _preferManualPath, value);
    }

    public string? InstallDirectory
    {
        get => _installDirectory;
        set => SetProperty(ref _installDirectory, value);
    }

    public string? LastDetectedInstallDirectory
    {
        get => _lastDetectedInstallDirectory;
        set => SetProperty(ref _lastDetectedInstallDirectory, value);
    }

    public DateTime LastDetectedAtUtc
    {
        get => _lastDetectedAtUtc;
        set => SetProperty(ref _lastDetectedAtUtc, value);
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


