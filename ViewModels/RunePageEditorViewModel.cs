using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LolManager.Models;
using LolManager.Services;

namespace LolManager.ViewModels;

public partial class RunePageEditorViewModel : ObservableObject
{
    private readonly RuneDataService _runeDataService;
    private readonly RunePage _runePage;

    [ObservableProperty]
    private string _pageName;

    [ObservableProperty]
    private RunePath? _selectedPrimaryPath;

    [ObservableProperty]
    private RunePath? _selectedSecondaryPath;

    public ObservableCollection<RunePath> AllPaths { get; }
    public ObservableCollection<RunePath> AvailableSecondaryPaths { get; } = new();

    [ObservableProperty]
    private Rune? _selectedKeystone;

    [ObservableProperty]
    private Rune? _selectedPrimarySlot1;

    [ObservableProperty]
    private Rune? _selectedPrimarySlot2;

    [ObservableProperty]
    private Rune? _selectedPrimarySlot3;

    [ObservableProperty]
    private Rune? _selectedSecondarySlot1;

    [ObservableProperty]
    private Rune? _selectedSecondarySlot2;

    [ObservableProperty]
    private Rune? _selectedSecondarySlot3;

    private List<int> _secondarySelectionOrder = new();

    [ObservableProperty]
    private Rune? _selectedStatMod1;

    [ObservableProperty]
    private Rune? _selectedStatMod2;

    [ObservableProperty]
    private Rune? _selectedStatMod3;

    public ObservableCollection<Rune> StatModsRow1 { get; }
    public ObservableCollection<Rune> StatModsRow2 { get; }
    public ObservableCollection<Rune> StatModsRow3 { get; }
    
    public ObservableCollection<Rune> KeystoneRunes { get; } = new();
    public ObservableCollection<Rune> PrimarySlot1Runes { get; } = new();
    public ObservableCollection<Rune> PrimarySlot2Runes { get; } = new();
    public ObservableCollection<Rune> PrimarySlot3Runes { get; } = new();
    public ObservableCollection<Rune> SecondarySlot1Runes { get; } = new();
    public ObservableCollection<Rune> SecondarySlot2Runes { get; } = new();
    public ObservableCollection<Rune> SecondarySlot3Runes { get; } = new();

    public bool DialogResult { get; set; }

    public RunePageEditorViewModel(RuneDataService runeDataService, RunePage? existingPage = null)
    {
        _runeDataService = runeDataService;
        
        _runePage = existingPage != null ? existingPage.Clone() : new RunePage();
        _pageName = _runePage.Name;

        AllPaths = new ObservableCollection<RunePath>(_runeDataService.GetAllPaths());
        
        StatModsRow1 = new ObservableCollection<Rune>(_runeDataService.GetStatModsRow1());
        StatModsRow2 = new ObservableCollection<Rune>(_runeDataService.GetStatModsRow2());
        StatModsRow3 = new ObservableCollection<Rune>(_runeDataService.GetStatModsRow3());

        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SelectedPrimaryPath))
            {
                UpdateAvailableSecondaryPaths();
                UpdateSelectedRunes();
            }
            else if (e.PropertyName == nameof(SelectedSecondaryPath))
            {
                ClearSecondarySelection();
            }
        };

        if (existingPage != null)
        {
            LoadExistingPage();
        }
        else
        {
            SelectedPrimaryPath = AllPaths.FirstOrDefault();
        }
    }

    private bool _isLoading = false; // Флаг для предотвращения очистки во время загрузки
    
    private void LoadExistingPage()
    {
        _isLoading = true; // Устанавливаем флаг загрузки
        
        try
        {
            var primaryPath = _runeDataService.GetPathById(_runePage.PrimaryPathId);
            var secondaryPath = _runeDataService.GetPathById(_runePage.SecondaryPathId);
            
            // Устанавливаем основной путь
            SelectedPrimaryPath = primaryPath;
            
            // Обновляем коллекции рун для основного пути
            if (primaryPath != null)
            {
                UpdateSelectedRunes();
            }
            
            // КРИТИЧНО: находим руны ИЗ КОЛЛЕКЦИЙ для правильного биндинга
            SelectedKeystone = KeystoneRunes.FirstOrDefault(r => r.Id == _runePage.PrimaryKeystoneId);
            SelectedPrimarySlot1 = PrimarySlot1Runes.FirstOrDefault(r => r.Id == _runePage.PrimarySlot1Id);
            SelectedPrimarySlot2 = PrimarySlot2Runes.FirstOrDefault(r => r.Id == _runePage.PrimarySlot2Id);
            SelectedPrimarySlot3 = PrimarySlot3Runes.FirstOrDefault(r => r.Id == _runePage.PrimarySlot3Id);
            
            // Обновляем список доступных дополнительных путей
            UpdateAvailableSecondaryPaths();
            
            // Устанавливаем дополнительный путь (ClearSecondarySelection НЕ вызовется из-за флага _isLoading)
            SelectedSecondaryPath = secondaryPath;
            
            // Обновляем коллекции дополнительных рун
            if (secondaryPath != null)
            {
                UpdateSecondaryRuneSlots();
            }
            
            // КРИТИЧНО: находим руны ИЗ КОЛЛЕКЦИЙ, а не через GetRuneById
            // Это важно для правильного срабатывания MultiObjectEqualityConverter в XAML
            _secondarySelectionOrder.Clear();
        
        if (_runePage.SecondarySlot1Id != 0)
        {
            var rune = SecondarySlot1Runes.FirstOrDefault(r => r.Id == _runePage.SecondarySlot1Id);
            if (rune != null)
            {
                SelectedSecondarySlot1 = rune;
                _secondarySelectionOrder.Add(1);
            }
        }
        
        if (_runePage.SecondarySlot2Id != 0)
        {
            var rune = SecondarySlot2Runes.FirstOrDefault(r => r.Id == _runePage.SecondarySlot2Id);
            if (rune != null)
            {
                SelectedSecondarySlot2 = rune;
                _secondarySelectionOrder.Add(2);
            }
        }
        
        if (_runePage.SecondarySlot3Id != 0)
        {
            var rune = SecondarySlot3Runes.FirstOrDefault(r => r.Id == _runePage.SecondarySlot3Id);
            if (rune != null)
            {
                SelectedSecondarySlot3 = rune;
                _secondarySelectionOrder.Add(3);
            }
        }
            
            // КРИТИЧНО: находим стат моды ИЗ КОЛЛЕКЦИЙ для правильного биндинга
            SelectedStatMod1 = StatModsRow1.FirstOrDefault(r => r.Id == _runePage.StatMod1Id);
            SelectedStatMod2 = StatModsRow2.FirstOrDefault(r => r.Id == _runePage.StatMod2Id);
            SelectedStatMod3 = StatModsRow3.FirstOrDefault(r => r.Id == _runePage.StatMod3Id);
            
            // ТЕПЕРЬ уведомляем UI об изменениях (после установки всех значений)
            OnPropertyChanged(nameof(SelectedPrimaryPath));
            OnPropertyChanged(nameof(SelectedSecondaryPath));
        }
        finally
        {
            _isLoading = false; // Снимаем флаг загрузки
        }
    }

    private void UpdateAvailableSecondaryPaths()
    {
        AvailableSecondaryPaths.Clear();
        
        foreach (var path in AllPaths)
        {
            if (path.Id != SelectedPrimaryPath?.Id)
            {
                AvailableSecondaryPaths.Add(path);
            }
        }
        
        if (SelectedSecondaryPath != null && SelectedSecondaryPath.Id == SelectedPrimaryPath?.Id)
        {
            SelectedSecondaryPath = AvailableSecondaryPaths.FirstOrDefault();
        }
        else if (SelectedSecondaryPath == null && AvailableSecondaryPaths.Any())
        {
            SelectedSecondaryPath = AvailableSecondaryPaths.FirstOrDefault();
        }
    }

    private void UpdateSelectedRunes()
    {
        if (SelectedPrimaryPath == null) return;
        
        KeystoneRunes.Clear();
        PrimarySlot1Runes.Clear();
        PrimarySlot2Runes.Clear();
        PrimarySlot3Runes.Clear();
        
        if (SelectedPrimaryPath.Slots == null || SelectedPrimaryPath.Slots.Count < 4) return;
        
        foreach (var rune in SelectedPrimaryPath.Slots[0].Runes)
            KeystoneRunes.Add(rune);
        
        foreach (var rune in SelectedPrimaryPath.Slots[1].Runes)
            PrimarySlot1Runes.Add(rune);
        
        foreach (var rune in SelectedPrimaryPath.Slots[2].Runes)
            PrimarySlot2Runes.Add(rune);
        
        foreach (var rune in SelectedPrimaryPath.Slots[3].Runes)
            PrimarySlot3Runes.Add(rune);
        
        if (SelectedKeystone == null || !SelectedPrimaryPath.Slots[0].Runes.Any(r => r.Id == SelectedKeystone.Id))
            SelectedKeystone = SelectedPrimaryPath.Slots[0].Runes.FirstOrDefault();
        
        if (SelectedPrimarySlot1 == null || !SelectedPrimaryPath.Slots[1].Runes.Any(r => r.Id == SelectedPrimarySlot1.Id))
            SelectedPrimarySlot1 = SelectedPrimaryPath.Slots[1].Runes.FirstOrDefault();
        
        if (SelectedPrimarySlot2 == null || !SelectedPrimaryPath.Slots[2].Runes.Any(r => r.Id == SelectedPrimarySlot2.Id))
            SelectedPrimarySlot2 = SelectedPrimaryPath.Slots[2].Runes.FirstOrDefault();
        
        if (SelectedPrimarySlot3 == null || !SelectedPrimaryPath.Slots[3].Runes.Any(r => r.Id == SelectedPrimarySlot3.Id))
            SelectedPrimarySlot3 = SelectedPrimaryPath.Slots[3].Runes.FirstOrDefault();
        
        UpdateSecondaryRuneSlots();
    }
    
    private void UpdateSecondaryRuneSlots()
    {
        SecondarySlot1Runes.Clear();
        SecondarySlot2Runes.Clear();
        SecondarySlot3Runes.Clear();

        if (SelectedSecondaryPath == null || SelectedSecondaryPath.Slots.Count < 4) return;

        foreach (var rune in SelectedSecondaryPath.Slots[1].Runes)
            SecondarySlot1Runes.Add(rune);

        foreach (var rune in SelectedSecondaryPath.Slots[2].Runes)
            SecondarySlot2Runes.Add(rune);

        foreach (var rune in SelectedSecondaryPath.Slots[3].Runes)
            SecondarySlot3Runes.Add(rune);
    }

    private void ClearSecondarySelection()
    {
        // Не очищаем во время загрузки существующей страницы
        if (_isLoading) return;
        
        SelectedSecondarySlot1 = null;
        SelectedSecondarySlot2 = null;
        SelectedSecondarySlot3 = null;
        _secondarySelectionOrder.Clear();
        
        UpdateSecondaryRuneSlots();
    }

    public bool TrySelectSecondaryRune(int slotIndex, Rune? rune)
    {
        if (rune == null) return false;

        _secondarySelectionOrder ??= new List<int>();
        
        if (_secondarySelectionOrder.Contains(slotIndex))
        {
            SetSecondarySlot(slotIndex, rune);
            return true;
        }
        
        if (_secondarySelectionOrder.Count >= 2)
        {
            var oldestSlotIndex = _secondarySelectionOrder[0];
            _secondarySelectionOrder.RemoveAt(0);
            SetSecondarySlot(oldestSlotIndex, null);
        }

        SetSecondarySlot(slotIndex, rune);
        _secondarySelectionOrder.Add(slotIndex);

        return true;
    }
    
    private void SetSecondarySlot(int slotIndex, Rune? rune)
    {
        switch (slotIndex)
        {
            case 1: SelectedSecondarySlot1 = rune; break;
            case 2: SelectedSecondarySlot2 = rune; break;
            case 3: SelectedSecondarySlot3 = rune; break;
        }
    }


    public bool CanSave()
    {
        if (string.IsNullOrWhiteSpace(PageName) || PageName.Length > 50)
            return false;

        int secondaryRunesSelected = 0;
        if (SelectedSecondarySlot1 != null) secondaryRunesSelected++;
        if (SelectedSecondarySlot2 != null) secondaryRunesSelected++;
        if (SelectedSecondarySlot3 != null) secondaryRunesSelected++;

        return SelectedPrimaryPath != null &&
               SelectedSecondaryPath != null &&
               SelectedPrimaryPath.Id != SelectedSecondaryPath.Id &&
               SelectedKeystone != null &&
               SelectedPrimarySlot1 != null &&
               SelectedPrimarySlot2 != null &&
               SelectedPrimarySlot3 != null &&
               secondaryRunesSelected == 2 &&
               SelectedStatMod1 != null &&
               SelectedStatMod2 != null &&
               SelectedStatMod3 != null;
    }

    public RunePage Save()
    {
        _runePage.Name = PageName.Trim();
        _runePage.PrimaryPathId = SelectedPrimaryPath!.Id;
        _runePage.SecondaryPathId = SelectedSecondaryPath!.Id;
        
        _runePage.PrimaryKeystoneId = SelectedKeystone!.Id;
        _runePage.PrimarySlot1Id = SelectedPrimarySlot1!.Id;
        _runePage.PrimarySlot2Id = SelectedPrimarySlot2!.Id;
        _runePage.PrimarySlot3Id = SelectedPrimarySlot3!.Id;
        
        _runePage.SecondarySlot1Id = SelectedSecondarySlot1?.Id ?? 0;
        _runePage.SecondarySlot2Id = SelectedSecondarySlot2?.Id ?? 0;
        _runePage.SecondarySlot3Id = SelectedSecondarySlot3?.Id ?? 0;
        
        _runePage.StatMod1Id = SelectedStatMod1!.Id;
        _runePage.StatMod2Id = SelectedStatMod2!.Id;
        _runePage.StatMod3Id = SelectedStatMod3!.Id;
        
        return _runePage.Clone();
    }
}

