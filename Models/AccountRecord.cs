using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LolManager.Models;

public class AccountRecord : INotifyPropertyChanged
{
    public string Username { get; set; } = string.Empty;
    public string EncryptedPassword { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    private string _avatarUrl = string.Empty;
    public string AvatarUrl
    {
        get => _avatarUrl;
        set => SetProperty(ref _avatarUrl, value);
    }
    
    private string _summonerName = string.Empty;
    public string SummonerName
    {
        get => _summonerName;
        set => SetProperty(ref _summonerName, value);
    }
    
    private string _rank = string.Empty;
    public string Rank
    {
        get => _rank;
        set => SetProperty(ref _rank, value);
    }
    
    private string _riotId = string.Empty;
    public string RiotId
    {
        get => _riotId;
        set => SetProperty(ref _riotId, value);
    }
    
    private string _rankIconUrl = string.Empty;
    public string RankIconUrl
    {
        get => _rankIconUrl;
        set => SetProperty(ref _rankIconUrl, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
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

public class EncryptedExportData
{
    public int Version { get; set; } = 2;
    public string AppName { get; set; } = "LolManager";
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public string EncryptedAccounts { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public string? IV { get; set; }
}

// Внутренняя структура для шифрования
public class ExportAccountRecord
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string AvatarUrl { get; set; } = string.Empty;
    public string SummonerName { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;
    public string RiotId { get; set; } = string.Empty;
    public string RankIconUrl { get; set; } = string.Empty;
}

public class ExportAccountRecordV3
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string AvatarUrl { get; set; } = string.Empty;
    public string SummonerName { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;
    public string RiotId { get; set; } = string.Empty;
    public string RankIconUrl { get; set; } = string.Empty;
}

// Старый формат для обратной совместимости (v1)
public class LegacyExportAccountRecord
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    // Note отсутствует в старом формате
}


