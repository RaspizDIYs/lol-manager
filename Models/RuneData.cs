using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LolManager.Models;

public class Rune
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ShortDesc { get; set; } = string.Empty;
    public string LongDesc { get; set; } = string.Empty;
}

public class RuneSlot
{
    public List<Rune> Runes { get; set; } = new();
}

public class RunePath
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public List<RuneSlot> Slots { get; set; } = new();
    
    public string ColorHex => Key switch
    {
        "Precision" => "#C8AA6E",
        "Domination" => "#C83C51",
        "Sorcery" => "#6C8CD5",
        "Resolve" => "#A1D586",
        "Inspiration" => "#48C9B0",
        _ => "#6C8CD5"
    };
}

public class RunePage : INotifyPropertyChanged
{
    private string _name = "Новая страница рун";
    private int _primaryPathId;
    private int _secondaryPathId;
    private int _primaryKeystoneId;
    private int _primarySlot1Id;
    private int _primarySlot2Id;
    private int _primarySlot3Id;
    private int _secondarySlot1Id;
    private int _secondarySlot2Id;
    private int _secondarySlot3Id;
    private int _statMod1Id;
    private int _statMod2Id;
    private int _statMod3Id;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public int PrimaryPathId
    {
        get => _primaryPathId;
        set => SetProperty(ref _primaryPathId, value);
    }

    public int SecondaryPathId
    {
        get => _secondaryPathId;
        set => SetProperty(ref _secondaryPathId, value);
    }

    public int PrimaryKeystoneId
    {
        get => _primaryKeystoneId;
        set => SetProperty(ref _primaryKeystoneId, value);
    }

    public int PrimarySlot1Id
    {
        get => _primarySlot1Id;
        set => SetProperty(ref _primarySlot1Id, value);
    }

    public int PrimarySlot2Id
    {
        get => _primarySlot2Id;
        set => SetProperty(ref _primarySlot2Id, value);
    }

    public int PrimarySlot3Id
    {
        get => _primarySlot3Id;
        set => SetProperty(ref _primarySlot3Id, value);
    }

    public int SecondarySlot1Id
    {
        get => _secondarySlot1Id;
        set => SetProperty(ref _secondarySlot1Id, value);
    }

    public int SecondarySlot2Id
    {
        get => _secondarySlot2Id;
        set => SetProperty(ref _secondarySlot2Id, value);
    }

    public int SecondarySlot3Id
    {
        get => _secondarySlot3Id;
        set => SetProperty(ref _secondarySlot3Id, value);
    }

    public int StatMod1Id
    {
        get => _statMod1Id;
        set => SetProperty(ref _statMod1Id, value);
    }

    public int StatMod2Id
    {
        get => _statMod2Id;
        set => SetProperty(ref _statMod2Id, value);
    }

    public int StatMod3Id
    {
        get => _statMod3Id;
        set => SetProperty(ref _statMod3Id, value);
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

    public RunePage Clone()
    {
        return new RunePage
        {
            Name = this.Name,
            PrimaryPathId = this.PrimaryPathId,
            SecondaryPathId = this.SecondaryPathId,
            PrimaryKeystoneId = this.PrimaryKeystoneId,
            PrimarySlot1Id = this.PrimarySlot1Id,
            PrimarySlot2Id = this.PrimarySlot2Id,
            PrimarySlot3Id = this.PrimarySlot3Id,
            SecondarySlot1Id = this.SecondarySlot1Id,
            SecondarySlot2Id = this.SecondarySlot2Id,
            SecondarySlot3Id = this.SecondarySlot3Id,
            StatMod1Id = this.StatMod1Id,
            StatMod2Id = this.StatMod2Id,
            StatMod3Id = this.StatMod3Id
        };
    }
}

