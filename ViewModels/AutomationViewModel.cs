using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LolManager.Models;
using LolManager.Services;
using System.Windows;

namespace LolManager.ViewModels;

public partial class AutomationViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly ISettingsService _settingsService;
    private readonly DataDragonService _dataDragonService;
    private readonly AutoAcceptService _autoAcceptService;
    private readonly RuneDataService _runeDataService;
    private readonly RiotClientService _riotClientService;
    private readonly IRunePagesStorage _runePagesStorage;

    [ObservableProperty]
    private bool isAutomationEnabled;

    [ObservableProperty]
    private string selectedChampionToPick1 = "(Не выбрано)";

    [ObservableProperty]
    private string selectedChampionToPick2 = "(Не выбрано)";

    [ObservableProperty]
    private string selectedChampionToPick3 = "(Не выбрано)";

    [ObservableProperty]
    private string selectedChampionToBan = "(Не выбрано)";

    [ObservableProperty]
    private string selectedSummonerSpell1 = "(Не выбрано)";

    [ObservableProperty]
    private string selectedSummonerSpell2 = "(Не выбрано)";
    
    [ObservableProperty]
    private string selectedRunePageName = "(Не выбрано)";
    
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
    public ObservableCollection<RunePage> RunePages { get; } = new();
    public ObservableCollection<string> RunePageNames { get; } = new();
    
    private bool _isUpdatingSettings = false;

    [ObservableProperty]
    private bool isPickDelayEnabled;

    [ObservableProperty]
    private int pickDelaySeconds;

    public AutomationViewModel(ILogger logger, ISettingsService settingsService, DataDragonService dataDragonService, AutoAcceptService autoAcceptService, RuneDataService runeDataService, RiotClientService riotClientService, IRunePagesStorage runePagesStorage)
    {
        _logger = logger;
        _settingsService = settingsService;
        _dataDragonService = dataDragonService;
        _autoAcceptService = autoAcceptService;
        _runeDataService = runeDataService;
        _riotClientService = riotClientService;
        _runePagesStorage = runePagesStorage;
        
        // Добавляем базовые элементы
        Champions.Add("(Не выбрано)");
        SummonerSpells.Add("(Не выбрано)");
        RunePageNames.Add("(Не выбрано)");
        
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
            
            if (e.PropertyName == nameof(SelectedChampionToPick1) ||
                e.PropertyName == nameof(SelectedChampionToPick2) ||
                e.PropertyName == nameof(SelectedChampionToPick3))
            {
                FilterChampions(); // Обновляем список бана (исключаем пики)
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
                e.PropertyName == nameof(IsPickDelayEnabled) ||
                e.PropertyName == nameof(PickDelaySeconds) ||
                e.PropertyName == nameof(SelectedChampionToPick1) ||
                e.PropertyName == nameof(SelectedChampionToPick2) ||
                e.PropertyName == nameof(SelectedChampionToPick3) ||
                e.PropertyName == nameof(SelectedChampionToBan) ||
                e.PropertyName == nameof(SelectedSummonerSpell1) ||
                e.PropertyName == nameof(SelectedSummonerSpell2) ||
                e.PropertyName == nameof(SelectedRunePageName))
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
            
            _logger.Info("Запрашиваю чемпионов...");
            var championsTask = _dataDragonService.GetChampionsAsync();
            _logger.Info("Запрашиваю заклинания...");
            var spellsTask = _dataDragonService.GetSummonerSpellsAsync();
            
            await Task.WhenAll(championsTask, spellsTask).ConfigureAwait(false);
            
            var champions = await championsTask.ConfigureAwait(false);
            var spells = await spellsTask.ConfigureAwait(false);
            
            _logger.Info($"Получено {champions.Count} чемпионов и {spells.Count} заклинаний");
            
            var championNames = champions.Keys.OrderBy(x => x).ToList();
            var popularSpells = new[]
            {
                "Скачок", "Телепорт", "Воспламенение", "Барьер", "Исцеление",
                "Кара", "Очищение", "Изнурение", "Призрак"
            };
            var availableSpells = popularSpells.Where(spells.ContainsKey).ToList();
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var currentPick1Selection = SelectedChampionToPick1;
                var currentPick2Selection = SelectedChampionToPick2;
                var currentPick3Selection = SelectedChampionToPick3;
                var currentBanSelection = SelectedChampionToBan;
                var currentSpell1Selection = SelectedSummonerSpell1;
                var currentSpell2Selection = SelectedSummonerSpell2;
                
                Champions.Clear();
                Champions.Add("(Не выбрано)");
                foreach (var champ in championNames)
                {
                    Champions.Add(champ);
                }
                
                SummonerSpells.Clear();
                SummonerSpells.Add("(Не выбрано)");
                foreach (var spellName in availableSpells)
                {
                    SummonerSpells.Add(spellName);
                }
                
                SelectedChampionToPick1 = Champions.Contains(currentPick1Selection) ? currentPick1Selection : "(Не выбрано)";
                SelectedChampionToPick2 = Champions.Contains(currentPick2Selection) ? currentPick2Selection : "(Не выбрано)";
                SelectedChampionToPick3 = Champions.Contains(currentPick3Selection) ? currentPick3Selection : "(Не выбрано)";
                SelectedChampionToBan = Champions.Contains(currentBanSelection) ? currentBanSelection : "(Не выбрано)";
                SelectedSummonerSpell1 = SummonerSpells.Contains(currentSpell1Selection) ? currentSpell1Selection : "(Не выбрано)";
                SelectedSummonerSpell2 = SummonerSpells.Contains(currentSpell2Selection) ? currentSpell2Selection : "(Не выбрано)";
                
                FilterChampions();
            });
            
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
            await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = false);
        }
    }

    private void LoadSettings()
    {
        _isUpdatingSettings = true;
        try
        {
            var settings = _settingsService.LoadSetting<AutomationSettings>("AutomationSettings", new AutomationSettings());
            
            IsAutomationEnabled = settings.IsEnabled;
            IsPickDelayEnabled = settings.IsPickDelayEnabled;
            PickDelaySeconds = Math.Clamp(settings.PickDelaySeconds, 0, 30);
            
            // Обратная совместимость обрабатывается через JSON десериализацию
            
            // Обратная совместимость: если ChampionToPick1 пустой, но есть старый ChampionToPick, используем его
            if (string.IsNullOrWhiteSpace(settings.ChampionToPick1) && !string.IsNullOrWhiteSpace(settings.ChampionToPick))
            {
                settings.ChampionToPick1 = settings.ChampionToPick;
            }
            
            SelectedChampionToPick1 = string.IsNullOrWhiteSpace(settings.ChampionToPick1) ? "(Не выбрано)" : settings.ChampionToPick1;
            SelectedChampionToPick2 = string.IsNullOrWhiteSpace(settings.ChampionToPick2) ? "(Не выбрано)" : settings.ChampionToPick2;
            SelectedChampionToPick3 = string.IsNullOrWhiteSpace(settings.ChampionToPick3) ? "(Не выбрано)" : settings.ChampionToPick3;
            SelectedChampionToBan = string.IsNullOrWhiteSpace(settings.ChampionToBan) ? "(Не выбрано)" : settings.ChampionToBan;
            SelectedSummonerSpell1 = string.IsNullOrWhiteSpace(settings.SummonerSpell1) ? "(Не выбрано)" : settings.SummonerSpell1;
            SelectedSummonerSpell2 = string.IsNullOrWhiteSpace(settings.SummonerSpell2) ? "(Не выбрано)" : settings.SummonerSpell2;
            SelectedRunePageName = string.IsNullOrWhiteSpace(settings.SelectedRunePageName) ? "(Не выбрано)" : settings.SelectedRunePageName;
            
            // Инициализируем предыдущие значения
            _previousSpell1 = SelectedSummonerSpell1;
            _previousSpell2 = SelectedSummonerSpell2;
            
            RunePages.Clear();
            var savedPages = _runePagesStorage.LoadAll();
            foreach (var page in savedPages)
            {
                RunePages.Add(page);
            }
            UpdateRunePageNames();
            
            _logger.Info($"✅ Загружены настройки: IsEnabled={IsAutomationEnabled}, Pick1={SelectedChampionToPick1}, Pick2={SelectedChampionToPick2}, Pick3={SelectedChampionToPick3}, Ban={SelectedChampionToBan}");
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
        
        var currentPick1 = SelectedChampionToPick1;
        var currentPick2 = SelectedChampionToPick2;
        var currentPick3 = SelectedChampionToPick3;
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
        
        // Список для ПИКА: исключаем выбранных в БАН и других пиках
        var excludedPicks = new HashSet<string> { currentBan, currentPick1, currentPick2, currentPick3 };
        FilteredChampionsForPick.Clear();
        foreach (var champion in baseFilteredList)
        {
            if (champion == "(Не выбрано)" || !excludedPicks.Contains(champion))
            {
                FilteredChampionsForPick.Add(champion);
            }
        }
        
        // КРИТИЧНО: Всегда добавляем текущие пики если их нет
        foreach (var pick in new[] { currentPick1, currentPick2, currentPick3 })
        {
            if (!string.IsNullOrEmpty(pick) && pick != "(Не выбрано)" && !FilteredChampionsForPick.Contains(pick))
            {
                FilteredChampionsForPick.Add(pick);
            }
        }
        
        // Список для БАНА: исключаем выбранных в ПИКАХ
        var excludedBans = new HashSet<string> { currentPick1, currentPick2, currentPick3 };
        FilteredChampionsForBan.Clear();
        foreach (var champion in baseFilteredList)
        {
            if (champion == "(Не выбрано)" || !excludedBans.Contains(champion))
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
        SelectedChampionToPick1 = currentPick1;
        SelectedChampionToPick2 = currentPick2;
        SelectedChampionToPick3 = currentPick3;
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
        if (_isUpdatingSettings) return;
        
        try
        {
            var pick1Value = SelectedChampionToPick1 == "(Не выбрано)" ? string.Empty : SelectedChampionToPick1;
            var pick2Value = SelectedChampionToPick2 == "(Не выбрано)" ? string.Empty : SelectedChampionToPick2;
            var pick3Value = SelectedChampionToPick3 == "(Не выбрано)" ? string.Empty : SelectedChampionToPick3;
            var banValue = SelectedChampionToBan == "(Не выбрано)" ? string.Empty : SelectedChampionToBan;
            
            var picks = new[] { pick1Value, pick2Value, pick3Value };
            if (picks.Any(p => !string.IsNullOrWhiteSpace(p) && p == banValue))
            {
                _logger.Warning($"Одинаковый чемпион в пике и бане ({banValue}) — сбрасываю бан");
                banValue = string.Empty;
            }

            var settings = new AutomationSettings
            {
                IsEnabled = IsAutomationEnabled,
                IsPickDelayEnabled = IsPickDelayEnabled,
                PickDelaySeconds = Math.Clamp(PickDelaySeconds, 0, 30),
                ChampionToPick1 = pick1Value,
                ChampionToPick2 = pick2Value,
                ChampionToPick3 = pick3Value,
                ChampionToBan = banValue,
                SummonerSpell1 = SelectedSummonerSpell1 == "(Не выбрано)" ? string.Empty : SelectedSummonerSpell1,
                SummonerSpell2 = SelectedSummonerSpell2 == "(Не выбрано)" ? string.Empty : SelectedSummonerSpell2,
                SelectedRunePageName = SelectedRunePageName == "(Не выбрано)" ? string.Empty : SelectedRunePageName
            };
            
            _settingsService.SaveSetting("AutomationSettings", settings);
            _autoAcceptService.SetAutomationSettings(settings);
            
            _logger.Info($"💾 Автосохранение: Enabled={settings.IsEnabled}, Pick1=[{settings.ChampionToPick1}], Pick2=[{settings.ChampionToPick2}], Pick3=[{settings.ChampionToPick3}], Ban=[{settings.ChampionToBan}], Spell1=[{settings.SummonerSpell1}], Spell2=[{settings.SummonerSpell2}]");
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка автосохранения: {ex.Message}");
        }
    }

    [RelayCommand]
    private void AddRunePage()
    {
        var window = new Views.RunePageEditorWindow(_runeDataService);
        window.Owner = System.Windows.Application.Current.MainWindow;
        
        if (window.ShowDialog() == true)
        {
            var runePage = window.GetSavedPage();
            _runePagesStorage.Save(runePage);
            RunePages.Add(runePage);
            UpdateRunePageNames();
            SelectedRunePageName = runePage.Name;
        }
    }

    [RelayCommand]
    private void EditRunePage(RunePage page)
    {
        _logger.Info($"[EditRunePage] ========== НАЧАЛО РЕДАКТИРОВАНИЯ ==========");
        _logger.Info($"[EditRunePage] Страница для редактирования: {page.Name}");
        _logger.Info($"[EditRunePage] ID страницы: Primary={page.PrimaryPathId}, Secondary={page.SecondaryPathId}");
        _logger.Info($"[EditRunePage] Основные руны: Keystone={page.PrimaryKeystoneId}, P1={page.PrimarySlot1Id}, P2={page.PrimarySlot2Id}, P3={page.PrimarySlot3Id}");
        _logger.Info($"[EditRunePage] Дополнительные руны: S1={page.SecondarySlot1Id}, S2={page.SecondarySlot2Id}, S3={page.SecondarySlot3Id}");
        _logger.Info($"[EditRunePage] Статы: Stat1={page.StatMod1Id}, Stat2={page.StatMod2Id}, Stat3={page.StatMod3Id}");
        
        _logger.Info($"[EditRunePage] Создаем окно редактирования...");
        var window = new Views.RunePageEditorWindow(_runeDataService, page);
        window.Owner = System.Windows.Application.Current.MainWindow;

        _logger.Info($"[EditRunePage] Открываем диалог...");
        if (window.ShowDialog() == true)
        {
            _logger.Info($"[EditRunePage] Диалог закрыт с результатом TRUE");
            var updatedPage = window.GetSavedPage();
            _logger.Info($"[EditRunePage] Получена обновленная страница: {updatedPage.Name}");
            _logger.Info($"[EditRunePage] Обновленные данные: Primary={updatedPage.PrimaryPathId}, Secondary={updatedPage.SecondaryPathId}");
            
            var index = RunePages.IndexOf(page);
            
            if (index >= 0)
            {
                _runePagesStorage.Save(updatedPage);
                RunePages[index] = updatedPage;
                UpdateRunePageNames();

                if (SelectedRunePageName == page.Name)
                {
                    SelectedRunePageName = updatedPage.Name;
                }
            }
            _logger.Info($"[EditRunePage] Страница рун '{updatedPage.Name}' успешно обновлена");
        }
        else
        {
            _logger.Info($"[EditRunePage] Диалог закрыт с результатом FALSE (отменено)");
        }
        
        _logger.Info($"[EditRunePage] ========== КОНЕЦ РЕДАКТИРОВАНИЯ ==========");
    }

    [RelayCommand]
    private async Task ApplySelectedRunePage()
    {
        if (string.IsNullOrWhiteSpace(SelectedRunePageName) || SelectedRunePageName == "(Не выбрано)")
        {
            System.Windows.MessageBox.Show("Выберите страницу рун для применения.", "Предупреждение", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var selectedPage = RunePages.FirstOrDefault(p => p.Name == SelectedRunePageName);
        if (selectedPage == null)
        {
            System.Windows.MessageBox.Show("Выбранная страница рун не найдена.", "Ошибка", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        try
        {
            IsLoading = true;

            // Проверяем доступность LCU
            var lcuAuth = await _riotClientService.GetLcuAuthAsync();
            if (lcuAuth == null)
            {
                System.Windows.MessageBox.Show("LCU недоступен. Откройте клиент League of Legends (в лобби) и попробуйте снова.", "Ошибка", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            var success = await _riotClientService.ApplyRunePageAsync(selectedPage);

            if (success)
            {
                System.Windows.MessageBox.Show($"Страница рун '{selectedPage.Name}' успешно применена в клиенте LoL!", "Успех", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                _logger.Info($"Rune page '{selectedPage.Name}' applied successfully");
            }
            else
            {
                System.Windows.MessageBox.Show("Не удалось применить страницу рун. Проверьте, что клиент LoL запущен и находится в лобби.", "Ошибка", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                _logger.Error($"Failed to apply rune page '{selectedPage.Name}'");
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Произошла ошибка при применении рун: {ex.Message}", "Ошибка", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            _logger.Error($"Error applying rune page: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void DeleteRunePage(RunePage page)
    {
        _runePagesStorage.Delete(page.Name);
        RunePages.Remove(page);
        UpdateRunePageNames();

        if (SelectedRunePageName == page.Name)
        {
            SelectedRunePageName = "(Не выбрано)";
        }
        
        _logger.Info($"Страница рун '{page.Name}' удалена");
    }
    
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ApplyRunePage(RunePage page, CancellationToken cancellationToken)
    {
        if (page == null) return;
        
        try
        {
            _logger.Info($"Применение страницы рун '{page.Name}'...");
            
            // Используем CancellationTokenSource с таймаутом
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2)); // 2 секунды таймаут
            
            bool success = false;
            
            try
            {
                success = await _riotClientService.ApplyRunePageAsync(page).WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Таймаут (не отмена пользователем)
                _logger.Error($"❌ Таймаут при применении рун '{page.Name}' - клиент League of Legends не отвечает");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    System.Windows.MessageBox.Show(
                        "Не удалось применить страницу рун.\n\nУбедитесь что клиент League of Legends запущен и находится в лобби.",
                        "Таймаут операции",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                });
                return;
            }
            
            if (success)
            {
                _logger.Info($"✅ Страница рун '{page.Name}' успешно применена");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    System.Windows.MessageBox.Show(
                        $"Страница рун '{page.Name}' успешно применена в клиенте!",
                        "Успех",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                });
            }
            else
            {
                _logger.Error($"❌ Не удалось применить страницу рун '{page.Name}'");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    System.Windows.MessageBox.Show(
                        "Не удалось применить страницу рун.\n\nПроверьте:\n• Клиент League of Legends запущен\n• Вы находитесь в лобби\n• У вас есть свободные слоты для страниц рун",
                        "Ошибка",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка применения рун: {ex.Message}");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Произошла ошибка при применении рун:\n\n{ex.Message}",
                    "Ошибка",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            });
        }
    }

    private void UpdateRunePageNames()
    {
        var currentSelection = SelectedRunePageName;
        
        RunePageNames.Clear();
        RunePageNames.Add("(Не выбрано)");
        
        foreach (var page in RunePages)
        {
            RunePageNames.Add(page.Name);
        }
        
        if (RunePageNames.Contains(currentSelection))
        {
            SelectedRunePageName = currentSelection;
        }
        else
        {
            SelectedRunePageName = "(Не выбрано)";
        }
    }
}

