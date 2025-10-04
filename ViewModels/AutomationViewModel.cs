using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LolManager.Models;
using LolManager.Services;

namespace LolManager.ViewModels;

public partial class AutomationViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly ISettingsService _settingsService;
    private readonly DataDragonService _dataDragonService;
    private readonly AutoAcceptService _autoAcceptService;

    [ObservableProperty]
    private bool isAutomationEnabled;

    [ObservableProperty]
    private string selectedChampionToPick = "(Не выбрано)";

    [ObservableProperty]
    private string selectedChampionToBan = "(Не выбрано)";

    [ObservableProperty]
    private string selectedSummonerSpell1 = "(Не выбрано)";

    [ObservableProperty]
    private string selectedSummonerSpell2 = "(Не выбрано)";
    
    private string _previousSpell1 = "(Не выбрано)";
    private string _previousSpell2 = "(Не выбрано)";
    
    [ObservableProperty]
    private string championSearchText = string.Empty;
    
    [ObservableProperty]
    private bool isLoading = false;

    public ObservableCollection<string> Champions { get; } = new();
    public ObservableCollection<string> FilteredChampionsForPick { get; } = new();
    public ObservableCollection<string> FilteredChampionsForBan { get; } = new();
    public ObservableCollection<string> SummonerSpells { get; } = new();
    
    private bool _isUpdatingSettings = false;

    public AutomationViewModel(ILogger logger, ISettingsService settingsService, DataDragonService dataDragonService, AutoAcceptService autoAcceptService)
    {
        _logger = logger;
        _settingsService = settingsService;
        _dataDragonService = dataDragonService;
        _autoAcceptService = autoAcceptService;
        
        // Добавляем базовые элементы
        Champions.Add("(Не выбрано)");
        SummonerSpells.Add("(Не выбрано)");
        
        // Загружаем сохраненные настройки
        LoadSettings();
        
        // Загружаем данные асинхронно
        _ = LoadDataAsync();
        
        // Автосохранение и валидация при изменении настроек
        PropertyChanged += (s, e) =>
        {
            if (_isUpdatingSettings) return;
            
            if (e.PropertyName == nameof(ChampionSearchText))
            {
                FilterChampions();
                return;
            }
            
            if (e.PropertyName == nameof(SelectedChampionToPick))
            {
                FilterChampions(); // Обновляем список бана (исключаем пик)
            }
            else if (e.PropertyName == nameof(SelectedChampionToBan))
            {
                FilterChampions(); // Обновляем список пика (исключаем бан)
            }
            else if (e.PropertyName == nameof(SelectedSummonerSpell1))
            {
                ValidateSummonerSpellSelection(isSpell1Changed: true);
                _previousSpell1 = SelectedSummonerSpell1;
            }
            else if (e.PropertyName == nameof(SelectedSummonerSpell2))
            {
                ValidateSummonerSpellSelection(isSpell1Changed: false);
                _previousSpell2 = SelectedSummonerSpell2;
            }
            
            if (e.PropertyName == nameof(IsAutomationEnabled) ||
                e.PropertyName == nameof(SelectedChampionToPick) ||
                e.PropertyName == nameof(SelectedChampionToBan) ||
                e.PropertyName == nameof(SelectedSummonerSpell1) ||
                e.PropertyName == nameof(SelectedSummonerSpell2))
            {
                SaveSettingsInternal();
            }
        };
    }

    private async Task LoadDataAsync()
    {
        if (IsLoading) return;
        
        try
        {
            IsLoading = true;
            _logger.Info("Начинаю загрузку данных автоматизации...");
            
            // Загружаем чемпионов и заклинания
            _logger.Info("Запрашиваю чемпионов...");
            var championsTask = _dataDragonService.GetChampionsAsync();
            _logger.Info("Запрашиваю заклинания...");
            var spellsTask = _dataDragonService.GetSummonerSpellsAsync();
            
            await Task.WhenAll(championsTask, spellsTask);
            
            var champions = await championsTask;
            var spells = await spellsTask;
            
            _logger.Info($"Получено {champions.Count} чемпионов и {spells.Count} заклинаний");
            
            // Обновляем списки
            var currentPickSelection = SelectedChampionToPick;
            var currentBanSelection = SelectedChampionToBan;
            var currentSpell1Selection = SelectedSummonerSpell1;
            var currentSpell2Selection = SelectedSummonerSpell2;
            
            Champions.Clear();
            Champions.Add("(Не выбрано)");
            foreach (var champ in champions.Keys.OrderBy(x => x))
            {
                Champions.Add(champ);
            }
            
            // Фильтруем и сортируем саммонерки в порядке популярности
            var popularSpells = new[] {
                "Скачок", "Телепорт", "Воспламенение", "Барьер", "Исцеление", 
                "Кара", "Очищение", "Изнурение", "Призрак"
            };
            
            SummonerSpells.Clear();
            SummonerSpells.Add("(Не выбрано)");
            foreach (var spellName in popularSpells)
            {
                if (spells.ContainsKey(spellName))
                {
                    SummonerSpells.Add(spellName);
                }
            }
            
            // Восстанавливаем выбор
            SelectedChampionToPick = Champions.Contains(currentPickSelection) ? currentPickSelection : "(Не выбрано)";
            SelectedChampionToBan = Champions.Contains(currentBanSelection) ? currentBanSelection : "(Не выбрано)";
            SelectedSummonerSpell1 = SummonerSpells.Contains(currentSpell1Selection) ? currentSpell1Selection : "(Не выбрано)";
            SelectedSummonerSpell2 = SummonerSpells.Contains(currentSpell2Selection) ? currentSpell2Selection : "(Не выбрано)";
            
            // Инициализируем отфильтрованный список
            FilterChampions();
            
            _logger.Info($"✅ Данные автоматизации загружены: {Champions.Count} чемпионов, {SummonerSpells.Count} заклинаний");
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка загрузки данных автоматизации: {ex.Message}\n{ex.StackTrace}");
            
            // В случае ошибки добавляем заглушку
            if (Champions.Count == 1) Champions.Add("(Ошибка загрузки)");
            if (SummonerSpells.Count == 1) SummonerSpells.Add("(Ошибка загрузки)");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void LoadSettings()
    {
        _isUpdatingSettings = true;
        try
        {
            var settings = _settingsService.LoadSetting<AutomationSettings>("AutomationSettings", new AutomationSettings());
            
            IsAutomationEnabled = settings.IsEnabled;
            SelectedChampionToPick = string.IsNullOrWhiteSpace(settings.ChampionToPick) ? "(Не выбрано)" : settings.ChampionToPick;
            SelectedChampionToBan = string.IsNullOrWhiteSpace(settings.ChampionToBan) ? "(Не выбрано)" : settings.ChampionToBan;
            SelectedSummonerSpell1 = string.IsNullOrWhiteSpace(settings.SummonerSpell1) ? "(Не выбрано)" : settings.SummonerSpell1;
            SelectedSummonerSpell2 = string.IsNullOrWhiteSpace(settings.SummonerSpell2) ? "(Не выбрано)" : settings.SummonerSpell2;
            
            // Инициализируем предыдущие значения
            _previousSpell1 = SelectedSummonerSpell1;
            _previousSpell2 = SelectedSummonerSpell2;
            
            _logger.Info($"✅ Загружены настройки: IsEnabled={IsAutomationEnabled}, Pick={SelectedChampionToPick}, Ban={SelectedChampionToBan}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка загрузки настроек автоматизации: {ex.Message}");
        }
        finally
        {
            _isUpdatingSettings = false;
        }
    }

    [RelayCommand]
    private void SwapSummonerSpells()
    {
        _isValidatingSpells = true;
        
        var temp = SelectedSummonerSpell1;
        SelectedSummonerSpell1 = SelectedSummonerSpell2;
        SelectedSummonerSpell2 = temp;
        
        _previousSpell1 = SelectedSummonerSpell1;
        _previousSpell2 = SelectedSummonerSpell2;
        
        _isValidatingSpells = false;
    }
    
    private void FilterChampions()
    {
        _isUpdatingSettings = true;
        
        var currentPick = SelectedChampionToPick;
        var currentBan = SelectedChampionToBan;
        
        var searchLower = ChampionSearchText?.ToLowerInvariant().Trim() ?? string.Empty;
        var hasSearch = !string.IsNullOrWhiteSpace(searchLower);
        
        // Строим базовый список чемпионов с учетом поиска
        var baseFilteredList = new List<string> { "(Не выбрано)" };
        
        foreach (var championName in Champions)
        {
            if (championName == "(Не выбрано)") continue;
            
            var info = _dataDragonService.GetChampionInfo(championName);
            if (info == null) continue;
            
            // Проверка поиска
            if (hasSearch)
            {
                bool matches = info.DisplayName.ToLowerInvariant().Contains(searchLower) ||
                              info.EnglishName.ToLowerInvariant().Contains(searchLower) ||
                              info.Aliases.Any(alias => alias.Contains(searchLower));
                
                if (!matches) continue;
            }
            
            baseFilteredList.Add(championName);
        }
        
        // Список для ПИКА: исключаем выбранного в БАН
        FilteredChampionsForPick.Clear();
        foreach (var champion in baseFilteredList)
        {
            if (champion == "(Не выбрано)" || champion != currentBan)
            {
                FilteredChampionsForPick.Add(champion);
            }
        }
        
        // КРИТИЧНО: Всегда добавляем текущий пик если его нет
        if (!string.IsNullOrEmpty(currentPick) && currentPick != "(Не выбрано)" && !FilteredChampionsForPick.Contains(currentPick))
        {
            FilteredChampionsForPick.Add(currentPick);
        }
        
        // Список для БАНА: исключаем выбранного в ПИК
        FilteredChampionsForBan.Clear();
        foreach (var champion in baseFilteredList)
        {
            if (champion == "(Не выбрано)" || champion != currentPick)
            {
                FilteredChampionsForBan.Add(champion);
            }
        }
        
        // КРИТИЧНО: Всегда добавляем текущий бан если его нет
        if (!string.IsNullOrEmpty(currentBan) && currentBan != "(Не выбрано)" && !FilteredChampionsForBan.Contains(currentBan))
        {
            FilteredChampionsForBan.Add(currentBan);
        }
        
        // Восстанавливаем выбор
        SelectedChampionToPick = currentPick;
        SelectedChampionToBan = currentBan;
        
        _isUpdatingSettings = false;
    }
    
    private bool _isValidatingSpells = false;
    
    private void ValidateSummonerSpellSelection(bool isSpell1Changed)
    {
        if (_isValidatingSpells) return;
        
        // Проверяем что саммонерки не совпадают
        if (SelectedSummonerSpell1 != "(Не выбрано)" && 
            SelectedSummonerSpell2 != "(Не выбрано)" && 
            SelectedSummonerSpell1 == SelectedSummonerSpell2)
        {
            _isValidatingSpells = true;
            
            if (isSpell1Changed)
            {
                // Пользователь изменил spell1 на то что было в spell2
                // Меняем spell2 на предыдущее значение spell1
                _logger.Info($"🔄 Автосмена: {SelectedSummonerSpell1} ⇄ {_previousSpell1}");
                SelectedSummonerSpell2 = _previousSpell1;
                _previousSpell2 = SelectedSummonerSpell2;
            }
            else
            {
                // Пользователь изменил spell2 на то что было в spell1
                // Меняем spell1 на предыдущее значение spell2
                _logger.Info($"🔄 Автосмена: {_previousSpell2} ⇄ {SelectedSummonerSpell2}");
                SelectedSummonerSpell1 = _previousSpell2;
                _previousSpell1 = SelectedSummonerSpell1;
            }
            
            _isValidatingSpells = false;
        }
    }

    private void SaveSettingsInternal()
    {
        if (_isUpdatingSettings) return; // Предотвращаем рекурсивное сохранение
        
        try
        {
            var settings = new AutomationSettings
            {
                IsEnabled = IsAutomationEnabled,
                ChampionToPick = SelectedChampionToPick == "(Не выбрано)" ? string.Empty : SelectedChampionToPick,
                ChampionToBan = SelectedChampionToBan == "(Не выбрано)" ? string.Empty : SelectedChampionToBan,
                SummonerSpell1 = SelectedSummonerSpell1 == "(Не выбрано)" ? string.Empty : SelectedSummonerSpell1,
                SummonerSpell2 = SelectedSummonerSpell2 == "(Не выбрано)" ? string.Empty : SelectedSummonerSpell2
            };
            
            _settingsService.SaveSetting("AutomationSettings", settings);
            _autoAcceptService.SetAutomationSettings(settings);
            
            _logger.Info($"💾 Автосохранение: Enabled={settings.IsEnabled}, Pick=[{settings.ChampionToPick}], Ban=[{settings.ChampionToBan}], Spell1=[{settings.SummonerSpell1}], Spell2=[{settings.SummonerSpell2}]");
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка автосохранения: {ex.Message}");
        }
    }
}

