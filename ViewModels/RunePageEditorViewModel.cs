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
        System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] ========== СОЗДАНИЕ VIEWMODEL ==========");
        System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] existingPage передана: {existingPage != null}");
        
        _runeDataService = runeDataService;
        
        // Если передана существующая страница, создаем ее копию
        _runePage = existingPage != null ? existingPage.Clone() : new RunePage();
        _pageName = _runePage.Name;
        
        System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] Создан ViewModel для страницы: {_pageName}");
        System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] Данные страницы: Primary={_runePage.PrimaryPathId}, Secondary={_runePage.SecondaryPathId}");
        System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] Основные руны: Keystone={_runePage.PrimaryKeystoneId}, P1={_runePage.PrimarySlot1Id}, P2={_runePage.PrimarySlot2Id}, P3={_runePage.PrimarySlot3Id}");
        System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] Дополнительные руны: S1={_runePage.SecondarySlot1Id}, S2={_runePage.SecondarySlot2Id}, S3={_runePage.SecondarySlot3Id}");

        System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] Загружаем все пути...");
        AllPaths = new ObservableCollection<RunePath>(_runeDataService.GetAllPaths());
        System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] Загружено путей: {AllPaths.Count}");
        
        System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] Загружаем статы...");
        StatModsRow1 = new ObservableCollection<Rune>(_runeDataService.GetStatModsRow1());
        StatModsRow2 = new ObservableCollection<Rune>(_runeDataService.GetStatModsRow2());
        StatModsRow3 = new ObservableCollection<Rune>(_runeDataService.GetStatModsRow3());
        System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] Загружено статов: {StatModsRow1.Count + StatModsRow2.Count + StatModsRow3.Count}");

        System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] Настраиваем обработчик PropertyChanged...");
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SelectedPrimaryPath))
            {
                System.Diagnostics.Debug.WriteLine($"[PropertyChanged] SelectedPrimaryPath изменен на: {SelectedPrimaryPath?.Name}");
                UpdateAvailableSecondaryPaths();
                UpdateSelectedRunes();
            }
            else if (e.PropertyName == nameof(SelectedSecondaryPath))
            {
                System.Diagnostics.Debug.WriteLine($"[PropertyChanged] SelectedSecondaryPath изменен на: {SelectedSecondaryPath?.Name}");
                ClearSecondarySelection();
            }
        };
        System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] Обработчик PropertyChanged настроен");

        if (existingPage != null)
        {
            System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] Загружаем существующую страницу");
            LoadExistingPage();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] Создаем новую страницу, выбираем первый путь");
            SelectedPrimaryPath = AllPaths.FirstOrDefault();
        }
        
        System.Diagnostics.Debug.WriteLine($"[ViewModel Constructor] ========== VIEWMODEL СОЗДАН ==========");
    }

    private void LoadExistingPage()
    {
        System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] Начало загрузки страницы: {_runePage.Name}");
        Console.WriteLine($"[LoadExistingPage] Начало загрузки страницы: {_runePage.Name}");
        System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] ID путей: Primary={_runePage.PrimaryPathId}, Secondary={_runePage.SecondaryPathId}");
        Console.WriteLine($"[LoadExistingPage] ID путей: Primary={_runePage.PrimaryPathId}, Secondary={_runePage.SecondaryPathId}");
        System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] ID рун: Keystone={_runePage.PrimaryKeystoneId}, P1={_runePage.PrimarySlot1Id}, P2={_runePage.PrimarySlot2Id}, P3={_runePage.PrimarySlot3Id}");
        Console.WriteLine($"[LoadExistingPage] ID рун: Keystone={_runePage.PrimaryKeystoneId}, P1={_runePage.PrimarySlot1Id}, P2={_runePage.PrimarySlot2Id}, P3={_runePage.PrimarySlot3Id}");
        System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] ID вторичных рун: S1={_runePage.SecondarySlot1Id}, S2={_runePage.SecondarySlot2Id}, S3={_runePage.SecondarySlot3Id}");
        Console.WriteLine($"[LoadExistingPage] ID вторичных рун: S1={_runePage.SecondarySlot1Id}, S2={_runePage.SecondarySlot2Id}, S3={_runePage.SecondarySlot3Id}");
        
        // Сначала загружаем пути, чтобы обновились доступные руны
        var primaryPath = _runeDataService.GetPathById(_runePage.PrimaryPathId);
        var secondaryPath = _runeDataService.GetPathById(_runePage.SecondaryPathId);
        
        System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] Загружены пути: Primary={primaryPath?.Name}, Secondary={secondaryPath?.Name}");
        Console.WriteLine($"[LoadExistingPage] Загружены пути: Primary={primaryPath?.Name}, Secondary={secondaryPath?.Name}");
        
        // ВАЖНО: Временно отключаем автоматическое обновление рун
        System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] Отключаем автоматическое обновление рун");
        Console.WriteLine($"[LoadExistingPage] Отключаем автоматическое обновление рун");
        
        // Устанавливаем пути без автоматического сброса рун
        SelectedPrimaryPath = primaryPath;
        SelectedSecondaryPath = secondaryPath;
        
        // Вручную обновляем коллекции рун
        if (primaryPath != null)
        {
            UpdateSelectedRunes();
        }
        if (secondaryPath != null)
        {
            UpdateSecondaryRuneSlots();
        }
        
        System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] Пути установлены напрямую: Primary={SelectedPrimaryPath?.Name}, Secondary={SelectedSecondaryPath?.Name}");
        Console.WriteLine($"[LoadExistingPage] Пути установлены напрямую: Primary={SelectedPrimaryPath?.Name}, Secondary={SelectedSecondaryPath?.Name}");
        
        // Теперь загружаем основные руны
        SelectedKeystone = _runeDataService.GetRuneById(_runePage.PrimaryKeystoneId);
        SelectedPrimarySlot1 = _runeDataService.GetRuneById(_runePage.PrimarySlot1Id);
        SelectedPrimarySlot2 = _runeDataService.GetRuneById(_runePage.PrimarySlot2Id);
        SelectedPrimarySlot3 = _runeDataService.GetRuneById(_runePage.PrimarySlot3Id);
        
        System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] Загружены основные руны: Keystone={SelectedKeystone?.Name}, P1={SelectedPrimarySlot1?.Name}, P2={SelectedPrimarySlot2?.Name}, P3={SelectedPrimarySlot3?.Name}");
        Console.WriteLine($"[LoadExistingPage] Загружены основные руны: Keystone={SelectedKeystone?.Name}, P1={SelectedPrimarySlot1?.Name}, P2={SelectedPrimarySlot2?.Name}, P3={SelectedPrimarySlot3?.Name}");
        
        // Загружаем вторичные руны
        var secondarySlot1 = _runeDataService.GetRuneById(_runePage.SecondarySlot1Id);
        var secondarySlot2 = _runeDataService.GetRuneById(_runePage.SecondarySlot2Id);
        var secondarySlot3 = _runeDataService.GetRuneById(_runePage.SecondarySlot3Id);
        
        System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] Получены вторичные руны: S1={secondarySlot1?.Name}, S2={secondarySlot2?.Name}, S3={secondarySlot3?.Name}");
        Console.WriteLine($"[LoadExistingPage] Получены вторичные руны: S1={secondarySlot1?.Name}, S2={secondarySlot2?.Name}, S3={secondarySlot3?.Name}");
        
        // Обновляем список выбранных слотов
        _secondarySelectionOrder.Clear();
        
        if (secondarySlot1 != null)
        {
            SelectedSecondarySlot1 = secondarySlot1;
            _secondarySelectionOrder.Add(1);
            System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] Добавлен слот 1: {secondarySlot1.Name}");
            Console.WriteLine($"[LoadExistingPage] Добавлен слот 1: {secondarySlot1.Name}");
        }
        
        if (secondarySlot2 != null)
        {
            SelectedSecondarySlot2 = secondarySlot2;
            _secondarySelectionOrder.Add(2);
            System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] Добавлен слот 2: {secondarySlot2.Name}");
            Console.WriteLine($"[LoadExistingPage] Добавлен слот 2: {secondarySlot2.Name}");
        }
        
        if (secondarySlot3 != null)
        {
            SelectedSecondarySlot3 = secondarySlot3;
            _secondarySelectionOrder.Add(3);
            System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] Добавлен слот 3: {secondarySlot3.Name}");
            Console.WriteLine($"[LoadExistingPage] Добавлен слот 3: {secondarySlot3.Name}");
        }
        
        // Загружаем статы
        SelectedStatMod1 = _runeDataService.GetRuneById(_runePage.StatMod1Id);
        SelectedStatMod2 = _runeDataService.GetRuneById(_runePage.StatMod2Id);
        SelectedStatMod3 = _runeDataService.GetRuneById(_runePage.StatMod3Id);
        
        System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] Загружены статы: S1={SelectedStatMod1?.Name}, S2={SelectedStatMod2?.Name}, S3={SelectedStatMod3?.Name}");
        Console.WriteLine($"[LoadExistingPage] Загружены статы: S1={SelectedStatMod1?.Name}, S2={SelectedStatMod2?.Name}, S3={SelectedStatMod3?.Name}");
        
        // Теперь уведомляем UI об изменениях путей
        OnPropertyChanged(nameof(SelectedPrimaryPath));
        OnPropertyChanged(nameof(SelectedSecondaryPath));
        
        System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] Отправлены уведомления об изменении путей в UI");
        Console.WriteLine($"[LoadExistingPage] Отправлены уведомления об изменении путей в UI");
        
        // Логируем итоговый порядок выбора
        System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] Итоговый порядок выбора вторичных рун: [{string.Join(", ", _secondarySelectionOrder)}]");
        Console.WriteLine($"[LoadExistingPage] Итоговый порядок выбора вторичных рун: [{string.Join(", ", _secondarySelectionOrder)}]");
        
        // Проверяем, что все коллекции рун заполнены
        System.Diagnostics.Debug.WriteLine($"[LoadExistingPage] Количество рун в коллекциях: " +
                                          $"Keystones={KeystoneRunes.Count}, " +
                                          $"PrimarySlot1={PrimarySlot1Runes.Count}, " +
                                          $"PrimarySlot2={PrimarySlot2Runes.Count}, " +
                                          $"PrimarySlot3={PrimarySlot3Runes.Count}, " +
                                          $"SecondarySlot1={SecondarySlot1Runes.Count}, " +
                                          $"SecondarySlot2={SecondarySlot2Runes.Count}, " +
                                          $"SecondarySlot3={SecondarySlot3Runes.Count}");
        Console.WriteLine($"[LoadExistingPage] Количество рун в коллекциях: " +
                          $"Keystones={KeystoneRunes.Count}, " +
                          $"PrimarySlot1={PrimarySlot1Runes.Count}, " +
                          $"PrimarySlot2={PrimarySlot2Runes.Count}, " +
                          $"PrimarySlot3={PrimarySlot3Runes.Count}, " +
                          $"SecondarySlot1={SecondarySlot1Runes.Count}, " +
                          $"SecondarySlot2={SecondarySlot2Runes.Count}, " +
                          $"SecondarySlot3={SecondarySlot3Runes.Count}");
    }

    private void UpdateAvailableSecondaryPaths()
    {
        System.Diagnostics.Debug.WriteLine($"[UpdateAvailableSecondaryPaths] Начало обновления доступных вторичных путей");
        System.Diagnostics.Debug.WriteLine($"[UpdateAvailableSecondaryPaths] Текущий основной путь: {SelectedPrimaryPath?.Name}, ID: {SelectedPrimaryPath?.Id}");
        System.Diagnostics.Debug.WriteLine($"[UpdateAvailableSecondaryPaths] Текущий вторичный путь: {SelectedSecondaryPath?.Name}, ID: {SelectedSecondaryPath?.Id}");
        
        AvailableSecondaryPaths.Clear();
        System.Diagnostics.Debug.WriteLine($"[UpdateAvailableSecondaryPaths] Очищены доступные вторичные пути");
        
        foreach (var path in AllPaths)
        {
            if (path.Id != SelectedPrimaryPath?.Id)
            {
                AvailableSecondaryPaths.Add(path);
                System.Diagnostics.Debug.WriteLine($"[UpdateAvailableSecondaryPaths] Добавлен путь: {path.Name}, ID: {path.Id}");
            }
        }
        
        System.Diagnostics.Debug.WriteLine($"[UpdateAvailableSecondaryPaths] Всего доступных вторичных путей: {AvailableSecondaryPaths.Count}");
        
        // Не сбрасываем выбранный путь автоматически, если он все еще доступен
        if (SelectedSecondaryPath != null && SelectedSecondaryPath.Id == SelectedPrimaryPath?.Id)
        {
            var oldPath = SelectedSecondaryPath;
            SelectedSecondaryPath = AvailableSecondaryPaths.FirstOrDefault();
            System.Diagnostics.Debug.WriteLine($"[UpdateAvailableSecondaryPaths] Автоматически выбран новый вторичный путь: {SelectedSecondaryPath?.Name}, ID: {SelectedSecondaryPath?.Id} (был: {oldPath?.Name})");
        }
        else if (SelectedSecondaryPath == null && AvailableSecondaryPaths.Any())
        {
            SelectedSecondaryPath = AvailableSecondaryPaths.FirstOrDefault();
            System.Diagnostics.Debug.WriteLine($"[UpdateAvailableSecondaryPaths] Автоматически выбран первый доступный вторичный путь: {SelectedSecondaryPath?.Name}, ID: {SelectedSecondaryPath?.Id}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateAvailableSecondaryPaths] Сохранен текущий вторичный путь: {SelectedSecondaryPath?.Name}, ID: {SelectedSecondaryPath?.Id}");
        }
    }

    private void UpdateSelectedRunes()
    {
        System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Начало обновления основных рун");
        
        if (SelectedPrimaryPath == null)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Основной путь не выбран, руны не обновлены");
            return;
        }
        
        System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Текущий основной путь: {SelectedPrimaryPath.Name}, ID: {SelectedPrimaryPath.Id}");
        
        KeystoneRunes.Clear();
        PrimarySlot1Runes.Clear();
        PrimarySlot2Runes.Clear();
        PrimarySlot3Runes.Clear();
        
        System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Очищены коллекции основных рун");
        
        // Проверяем, что у пути есть слоты
        if (SelectedPrimaryPath.Slots == null || SelectedPrimaryPath.Slots.Count < 4)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] ОШИБКА: У пути {SelectedPrimaryPath.Name} отсутствуют слоты или их меньше 4");
            return;
        }
        
        System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Количество слотов в основном пути: {SelectedPrimaryPath.Slots.Count}");
        
        // Проверяем каждый слот перед добавлением рун
        if (SelectedPrimaryPath.Slots[0].Runes != null)
        {
            foreach (var rune in SelectedPrimaryPath.Slots[0].Runes)
            {
                KeystoneRunes.Add(rune);
                System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Добавлен keystone: {rune.Name}, ID: {rune.Id}");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] ОШИБКА: Отсутствуют руны в слоте 0 (keystone)");
        }
        
        if (SelectedPrimaryPath.Slots[1].Runes != null)
        {
            foreach (var rune in SelectedPrimaryPath.Slots[1].Runes)
            {
                PrimarySlot1Runes.Add(rune);
                System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Добавлена руна в слот 1: {rune.Name}, ID: {rune.Id}");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] ОШИБКА: Отсутствуют руны в слоте 1");
        }
        
        if (SelectedPrimaryPath.Slots[2].Runes != null)
        {
            foreach (var rune in SelectedPrimaryPath.Slots[2].Runes)
            {
                PrimarySlot2Runes.Add(rune);
                System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Добавлена руна в слот 2: {rune.Name}, ID: {rune.Id}");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] ОШИБКА: Отсутствуют руны в слоте 2");
        }
        
        if (SelectedPrimaryPath.Slots[3].Runes != null)
        {
            foreach (var rune in SelectedPrimaryPath.Slots[3].Runes)
            {
                PrimarySlot3Runes.Add(rune);
                System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Добавлена руна в слот 3: {rune.Name}, ID: {rune.Id}");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] ОШИБКА: Отсутствуют руны в слоте 3");
        }
        
        // Проверяем и устанавливаем выбранные руны
        var oldKeystone = SelectedKeystone;
        if (SelectedKeystone == null || !SelectedPrimaryPath.Slots[0].Runes.Any(r => r.Id == SelectedKeystone.Id))
        {
            SelectedKeystone = SelectedPrimaryPath.Slots[0].Runes.FirstOrDefault();
            System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Автоматически выбран keystone: {SelectedKeystone?.Name} (был: {oldKeystone?.Name})");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Сохранен текущий keystone: {SelectedKeystone.Name}");
        }
        
        var oldSlot1 = SelectedPrimarySlot1;
        if (SelectedPrimarySlot1 == null || !SelectedPrimaryPath.Slots[1].Runes.Any(r => r.Id == SelectedPrimarySlot1.Id))
        {
            SelectedPrimarySlot1 = SelectedPrimaryPath.Slots[1].Runes.FirstOrDefault();
            System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Автоматически выбрана руна слота 1: {SelectedPrimarySlot1?.Name} (была: {oldSlot1?.Name})");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Сохранена текущая руна слота 1: {SelectedPrimarySlot1.Name}");
        }
        
        var oldSlot2 = SelectedPrimarySlot2;
        if (SelectedPrimarySlot2 == null || !SelectedPrimaryPath.Slots[2].Runes.Any(r => r.Id == SelectedPrimarySlot2.Id))
        {
            SelectedPrimarySlot2 = SelectedPrimaryPath.Slots[2].Runes.FirstOrDefault();
            System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Автоматически выбрана руна слота 2: {SelectedPrimarySlot2?.Name} (была: {oldSlot2?.Name})");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Сохранена текущая руна слота 2: {SelectedPrimarySlot2.Name}");
        }
        
        var oldSlot3 = SelectedPrimarySlot3;
        if (SelectedPrimarySlot3 == null || !SelectedPrimaryPath.Slots[3].Runes.Any(r => r.Id == SelectedPrimarySlot3.Id))
        {
            SelectedPrimarySlot3 = SelectedPrimaryPath.Slots[3].Runes.FirstOrDefault();
            System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Автоматически выбрана руна слота 3: {SelectedPrimarySlot3?.Name} (была: {oldSlot3?.Name})");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateSelectedRunes] Сохранена текущая руна слота 3: {SelectedPrimarySlot3.Name}");
        }
        
        // Больше не автозаполняем осколки — требуем явного выбора
        
        UpdateSecondaryRuneSlots();
    }
    
    private void UpdateSecondaryRuneSlots()
    {
        System.Diagnostics.Debug.WriteLine($"[UpdateSecondaryRuneSlots] Начало обновления вторичных рун");
        
        SecondarySlot1Runes.Clear();
        SecondarySlot2Runes.Clear();
        SecondarySlot3Runes.Clear();

        if (SelectedSecondaryPath == null)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateSecondaryRuneSlots] Вторичный путь не выбран");
            return;
        }
        
        if (SelectedSecondaryPath.Slots.Count < 4)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateSecondaryRuneSlots] ОШИБКА: У вторичного пути недостаточно слотов: {SelectedSecondaryPath.Slots.Count}");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[UpdateSecondaryRuneSlots] Обновление для пути: {SelectedSecondaryPath.Name}");

        foreach (var rune in SelectedSecondaryPath.Slots[1].Runes)
        {
            SecondarySlot1Runes.Add(rune);
            System.Diagnostics.Debug.WriteLine($"[UpdateSecondaryRuneSlots] Добавлена руна в слот 1: {rune.Name}");
        }

        foreach (var rune in SelectedSecondaryPath.Slots[2].Runes)
        {
            SecondarySlot2Runes.Add(rune);
            System.Diagnostics.Debug.WriteLine($"[UpdateSecondaryRuneSlots] Добавлена руна в слот 2: {rune.Name}");
        }

        foreach (var rune in SelectedSecondaryPath.Slots[3].Runes)
        {
            SecondarySlot3Runes.Add(rune);
            System.Diagnostics.Debug.WriteLine($"[UpdateSecondaryRuneSlots] Добавлена руна в слот 3: {rune.Name}");
        }
        
        System.Diagnostics.Debug.WriteLine($"[UpdateSecondaryRuneSlots] Итого рун: Slot1={SecondarySlot1Runes.Count}, Slot2={SecondarySlot2Runes.Count}, Slot3={SecondarySlot3Runes.Count}");
    }

    private void ClearSecondarySelection()
    {
        System.Diagnostics.Debug.WriteLine($"[ClearSecondarySelection] Очистка выбора вторичных рун");
        
        SelectedSecondarySlot1 = null;
        SelectedSecondarySlot2 = null;
        SelectedSecondarySlot3 = null;
        _secondarySelectionOrder.Clear();
        
        System.Diagnostics.Debug.WriteLine($"[ClearSecondarySelection] Очищены выбранные вторичные руны и порядок выбора");
        
        UpdateSecondaryRuneSlots();
    }

    public bool TrySelectSecondaryRune(int slotIndex, Rune? rune)
    {
        System.Diagnostics.Debug.WriteLine($"[TrySelectSecondaryRune] Попытка выбрать руну {rune?.Name} в слоте {slotIndex}");
        
        if (rune == null)
        {
            System.Diagnostics.Debug.WriteLine($"[TrySelectSecondaryRune] Руна null, отказ");
            return false;
        }

        // Количество выбранных рун до добавления текущей
        int selectedBeforeCount = 0;
        if (_secondarySelectionOrder != null)
            selectedBeforeCount = _secondarySelectionOrder.Count;
        
        System.Diagnostics.Debug.WriteLine($"[TrySelectSecondaryRune] Текущий порядок выбора: [{(_secondarySelectionOrder != null ? string.Join(", ", _secondarySelectionOrder) : "null")}], количество: {selectedBeforeCount}");
        
        // Инициализируем список, если он null
        if (_secondarySelectionOrder == null)
            _secondarySelectionOrder = new List<int>();
            
        // Если уже выбрано 2 слота и пытаемся выбрать третий - сбрасываем самый старый
        if (selectedBeforeCount >= 2 && !_secondarySelectionOrder.Contains(slotIndex))
        {
            var oldestSlotIndex = _secondarySelectionOrder[0];
            _secondarySelectionOrder.RemoveAt(0);

            System.Diagnostics.Debug.WriteLine($"[TrySelectSecondaryRune] Сбрасываем самый старый слот: {oldestSlotIndex}");

            // Сбрасываем самую старую руну
            switch (oldestSlotIndex)
            {
                case 1:
                    SelectedSecondarySlot1 = null;
                    System.Diagnostics.Debug.WriteLine($"[TrySelectSecondaryRune] Сброшен SelectedSecondarySlot1");
                    break;
                case 2:
                    SelectedSecondarySlot2 = null;
                    System.Diagnostics.Debug.WriteLine($"[TrySelectSecondaryRune] Сброшен SelectedSecondarySlot2");
                    break;
                case 3:
                    SelectedSecondarySlot3 = null;
                    System.Diagnostics.Debug.WriteLine($"[TrySelectSecondaryRune] Сброшен SelectedSecondarySlot3");
                    break;
            }
        }

        // Выбираем руну в слоте
        switch (slotIndex)
        {
            case 1:
                SelectedSecondarySlot1 = rune;
                System.Diagnostics.Debug.WriteLine($"[TrySelectSecondaryRune] Установлен SelectedSecondarySlot1 = {rune.Name}");
                break;
            case 2:
                SelectedSecondarySlot2 = rune;
                System.Diagnostics.Debug.WriteLine($"[TrySelectSecondaryRune] Установлен SelectedSecondarySlot2 = {rune.Name}");
                break;
            case 3:
                SelectedSecondarySlot3 = rune;
                System.Diagnostics.Debug.WriteLine($"[TrySelectSecondaryRune] Установлен SelectedSecondarySlot3 = {rune.Name}");
                break;
        }

        // Добавляем слот в порядок выбора, если его там нет
        if (_secondarySelectionOrder != null && !_secondarySelectionOrder.Contains(slotIndex))
        {
            _secondarySelectionOrder.Add(slotIndex);
            System.Diagnostics.Debug.WriteLine($"[TrySelectSecondaryRune] Добавлен слот {slotIndex} в порядок выбора");
        }
        
        System.Diagnostics.Debug.WriteLine($"[TrySelectSecondaryRune] Итоговый порядок выбора: [{(_secondarySelectionOrder != null ? string.Join(", ", _secondarySelectionOrder) : "null")}]");

        return true;
    }


    public bool CanSave()
    {
        int secondaryRunesSelected = 0;
        if (SelectedSecondarySlot1 != null) secondaryRunesSelected++;
        if (SelectedSecondarySlot2 != null) secondaryRunesSelected++;
        if (SelectedSecondarySlot3 != null) secondaryRunesSelected++;

        return !string.IsNullOrWhiteSpace(PageName) &&
               SelectedPrimaryPath != null &&
               SelectedSecondaryPath != null &&
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
        System.Diagnostics.Debug.WriteLine($"[Save] Сохранение страницы: {PageName}");
        
        _runePage.Name = PageName;
        _runePage.PrimaryPathId = SelectedPrimaryPath!.Id;
        _runePage.SecondaryPathId = SelectedSecondaryPath!.Id;
        
        System.Diagnostics.Debug.WriteLine($"[Save] Сохранены пути: Primary={_runePage.PrimaryPathId}, Secondary={_runePage.SecondaryPathId}");
        
        _runePage.PrimaryKeystoneId = SelectedKeystone!.Id;
        _runePage.PrimarySlot1Id = SelectedPrimarySlot1!.Id;
        _runePage.PrimarySlot2Id = SelectedPrimarySlot2!.Id;
        _runePage.PrimarySlot3Id = SelectedPrimarySlot3!.Id;
        
        System.Diagnostics.Debug.WriteLine($"[Save] Сохранены основные руны: " +
                                          $"Keystone={_runePage.PrimaryKeystoneId}, " +
                                          $"P1={_runePage.PrimarySlot1Id}, " +
                                          $"P2={_runePage.PrimarySlot2Id}, " +
                                          $"P3={_runePage.PrimarySlot3Id}");
        
        _runePage.SecondarySlot1Id = SelectedSecondarySlot1?.Id ?? 0;
        _runePage.SecondarySlot2Id = SelectedSecondarySlot2?.Id ?? 0;
        _runePage.SecondarySlot3Id = SelectedSecondarySlot3?.Id ?? 0;
        
        System.Diagnostics.Debug.WriteLine($"[Save] Сохранены вторичные руны: " +
                                          $"S1={_runePage.SecondarySlot1Id}, " +
                                          $"S2={_runePage.SecondarySlot2Id}, " +
                                          $"S3={_runePage.SecondarySlot3Id}");
        
        _runePage.StatMod1Id = SelectedStatMod1!.Id;
        _runePage.StatMod2Id = SelectedStatMod2!.Id;
        _runePage.StatMod3Id = SelectedStatMod3!.Id;
        
        System.Diagnostics.Debug.WriteLine($"[Save] Сохранены статы: " +
                                          $"Stat1={_runePage.StatMod1Id}, " +
                                          $"Stat2={_runePage.StatMod2Id}, " +
                                          $"Stat3={_runePage.StatMod3Id}");
        
        // Используем метод Clone для создания глубокой копии
        var savedPage = _runePage.Clone();
        
        System.Diagnostics.Debug.WriteLine($"[Save] Создана копия страницы для возврата");
        System.Diagnostics.Debug.WriteLine($"[Save] Проверка копии: Name={savedPage.Name}, Primary={savedPage.PrimaryPathId}, Secondary={savedPage.SecondaryPathId}");
        
        return savedPage;
    }
}

