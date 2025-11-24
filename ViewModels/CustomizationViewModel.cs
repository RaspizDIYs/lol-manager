using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LolManager.Extensions;
using LolManager.Models;
using LolManager.Services;
using System.Windows;

namespace LolManager.ViewModels;

public partial class CustomizationViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly CustomizationService _customizationService;
    private readonly DataDragonService _dataDragonService;
    private readonly IRiotClientService _riotClientService;

    [ObservableProperty]
    private string customStatus = string.Empty;

    [ObservableProperty]
    private string selectedChampionForBackground = "(Не выбрано)";
    
    [ObservableProperty]
    private Models.SkinInfo? selectedSkinForBackground;

    [ObservableProperty]
    private string backgroundSearchText = string.Empty;
    
    public ObservableCollection<Models.SkinInfo> AvailableSkins { get; } = new();

    [ObservableProperty]
    private string challengeSearchText = string.Empty;

    [ObservableProperty]
    private bool isLoading = false;

    public ObservableCollection<string> Champions { get; } = new();
    public ObservableCollection<string> FilteredChampions { get; } = new();
    public ObservableCollection<Models.SkinInfo> FilteredSkins { get; } = new();
    public ObservableCollection<ChallengeInfo> Challenges { get; } = new();
    public ObservableCollection<ChallengeInfo> FilteredChallenges { get; } = new();
    public ObservableCollection<ChallengeInfo> SelectedChallenges { get; } = new();
    
    private readonly List<Models.SkinInfo> _allSkinsBuffer = new();
    private readonly List<Models.SkinInfo> _filteredSkinsBuffer = new();
    private int _loadedSkinsCount;
    private bool _isChampionDataReady;
    private const int SkinsPageSize = 60;
    private const int MaxSkinsToDisplay = 200;
    private Timer? _filterChallengesDebounceTimer;
    private Timer? _filterBackgroundsDebounceTimer;
    private bool _challengesLoaded = false;
    private bool _championsLoaded = false;
    
    [ObservableProperty]
    private bool hasMoreSkins;
    
    [ObservableProperty]
    private ChallengeInfo? selectedChallenge1;
    
    [ObservableProperty]
    private ChallengeInfo? selectedChallenge2;
    
    [ObservableProperty]
    private ChallengeInfo? selectedChallenge3;
    
    public bool HasSelectedChallenges => SelectedChallenge1 != null || SelectedChallenge2 != null || SelectedChallenge3 != null;

    public CustomizationViewModel(ILogger logger, CustomizationService customizationService, DataDragonService dataDragonService, IRiotClientService riotClientService)
    {
        _logger = logger;
        _customizationService = customizationService;
        _dataDragonService = dataDragonService;
        _riotClientService = riotClientService;
        
        Champions.Add("(Не выбрано)");
        FilteredChampions.Add("(Не выбрано)");
        
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(BackgroundSearchText))
            {
                FilterBackgroundsDebounced();
            }
            else if (e.PropertyName == nameof(ChallengeSearchText))
            {
                FilterChallengesDebounced();
            }
            else if (e.PropertyName == nameof(SelectedChampionForBackground))
            {
                UpdateAvailableSkins();
            }
            else if (e.PropertyName == nameof(SelectedChallenge1) || 
                     e.PropertyName == nameof(SelectedChallenge2) || 
                     e.PropertyName == nameof(SelectedChallenge3))
            {
                UpdateSelectedChallenges();
                OnPropertyChanged(nameof(HasSelectedChallenges));
            }
        };
    }
    
    public async Task EnsureChallengesLoadedAsync()
    {
        if (!_challengesLoaded)
        {
            _challengesLoaded = true;
            await LoadChallengesAsync();
        }
    }
    
    public async Task EnsureChampionsLoadedAsync()
    {
        if (!_championsLoaded)
        {
            _championsLoaded = true;
            await LoadChampionsAsync();
        }
    }
    
    private void UpdateAvailableSkins()
    {
        AvailableSkins.Clear();
        
        if (SelectedChampionForBackground == "(Не выбрано)" || string.IsNullOrEmpty(SelectedChampionForBackground))
        {
            return;
        }
        
        var info = _dataDragonService.GetChampionInfo(SelectedChampionForBackground);
        if (info == null) return;
        
        foreach (var skin in info.Skins.OrderBy(s => s.SkinNumber))
        {
            AvailableSkins.Add(skin);
        }
        
        if (AvailableSkins.Count > 0)
        {
            SelectedSkinForBackground = AvailableSkins[0];
        }
    }

    private async Task LoadChampionsAsync()
    {
        try
        {
            IsLoading = true;
            var champions = await _dataDragonService.GetChampionsAsync().ConfigureAwait(false);
            var championNames = champions.Keys.OrderBy(x => x).ToList();
            var allSkins = new List<Models.SkinInfo>();
            
            foreach (var champ in championNames)
            {
                var info = _dataDragonService.GetChampionInfo(champ);
                if (info == null || info.Skins.Count == 0) continue;
                allSkins.AddRange(info.Skins.OrderBy(s => s.SkinNumber));
            }
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Champions.Clear();
                Champions.Add("(Не выбрано)");
                FilteredChampions.Clear();
                FilteredChampions.Add("(Не выбрано)");
                
                foreach (var champ in championNames)
                {
                    Champions.Add(champ);
                }
                
                _allSkinsBuffer.Clear();
                _allSkinsBuffer.AddRange(allSkins);
                _isChampionDataReady = true;
                
                FilterBackgrounds();
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка загрузки чемпионов: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadChallengesAsync()
    {
        try
        {
            IsLoading = true;
            var challenges = await _customizationService.GetChallengesAsync();
            
            Challenges.Clear();
            foreach (var challenge in challenges.OrderBy(x => x.Name))
            {
                Challenges.Add(challenge);
            }
            
            FilterChallenges();
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка загрузки челенджей: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void FilterBackgroundsDebounced()
    {
        _filterBackgroundsDebounceTimer?.Dispose();
        _filterBackgroundsDebounceTimer = new Timer(_ => 
        {
            Application.Current.Dispatcher.InvokeAsync(() => FilterBackgroundsInternal());
        }, null, 150, Timeout.Infinite);
    }
    
    private void FilterBackgrounds()
    {
        _filterBackgroundsDebounceTimer?.Dispose();
        FilterBackgroundsInternal();
    }
    
    private void FilterBackgroundsInternal()
    {
        if (!_isChampionDataReady)
        {
            return;
        }
        
        var searchLower = BackgroundSearchText?.ToLowerInvariant().Trim() ?? string.Empty;
        var hasSearch = !string.IsNullOrWhiteSpace(searchLower);
        
        var filteredChampionsList = new List<string> { "(Не выбрано)" };
        
        var prioritizedSkins = new List<Models.SkinInfo>();
        var otherSkins = new List<Models.SkinInfo>();
        
        foreach (var championName in Champions)
        {
            if (championName == "(Не выбрано)") continue;
            
            var info = _dataDragonService.GetChampionInfo(championName);
            if (info == null) continue;
            
            bool championMatches = !hasSearch ||
                                   info.DisplayName.ToLowerInvariant().Contains(searchLower) ||
                                   info.EnglishName.ToLowerInvariant().Contains(searchLower) ||
                                   info.Aliases.Any(alias => alias.Contains(searchLower));
            
            if (championMatches)
            {
                filteredChampionsList.Add(championName);
            }
        }
        
        FilteredChampions.ReplaceAll(filteredChampionsList);
        
        _filteredSkinsBuffer.Clear();
        foreach (var skin in _allSkinsBuffer)
        {
            var info = _dataDragonService.GetChampionInfo(skin.ChampionName);
            if (info == null) continue;
            
            bool championMatches = !hasSearch ||
                                   info.DisplayName.ToLowerInvariant().Contains(searchLower) ||
                                   info.EnglishName.ToLowerInvariant().Contains(searchLower) ||
                                   info.Aliases.Any(alias => alias.Contains(searchLower));
            
            bool skinMatches = !hasSearch ||
                               championMatches ||
                               skin.Name.ToLowerInvariant().Contains(searchLower);
            
            if (skinMatches)
            {
                if (championMatches)
                {
                    prioritizedSkins.Add(skin);
                }
                else
                {
                    otherSkins.Add(skin);
                }
            }
        }
        
        _filteredSkinsBuffer.Clear();
        _filteredSkinsBuffer.AddRange(prioritizedSkins);
        _filteredSkinsBuffer.AddRange(otherSkins);
        
        ResetSkinsPagination();
        UpdateAvailableSkins();
    }
    
    private void ResetSkinsPagination()
    {
        _loadedSkinsCount = 0;
        FilteredSkins.Clear();
        LoadNextSkinsChunk();
    }
    
    private void LoadNextSkinsChunk()
    {
        if (_filteredSkinsBuffer.Count == 0)
        {
            HasMoreSkins = false;
            return;
        }
        
        var maxToLoad = Math.Min(MaxSkinsToDisplay, _filteredSkinsBuffer.Count);
        var remainingToLoad = maxToLoad - _loadedSkinsCount;
        
        if (remainingToLoad <= 0)
        {
            HasMoreSkins = false;
            return;
        }
        
        var chunk = _filteredSkinsBuffer
            .Skip(_loadedSkinsCount)
            .Take(Math.Min(SkinsPageSize, remainingToLoad))
            .ToList();
        
        foreach (var skin in chunk)
        {
            FilteredSkins.Add(skin);
        }
        
        _loadedSkinsCount += chunk.Count;
        HasMoreSkins = _loadedSkinsCount < maxToLoad && _loadedSkinsCount < _filteredSkinsBuffer.Count;
    }
    
    [RelayCommand]
    private void LoadMoreSkins()
    {
        LoadNextSkinsChunk();
    }

    private void FilterChallengesDebounced()
    {
        _filterChallengesDebounceTimer?.Dispose();
        _filterChallengesDebounceTimer = new Timer(_ => 
        {
            Application.Current.Dispatcher.InvokeAsync(() => FilterChallengesInternal());
        }, null, 150, Timeout.Infinite);
    }
    
    private void FilterChallenges()
    {
        _filterChallengesDebounceTimer?.Dispose();
        FilterChallengesInternal();
    }
    
    private void FilterChallengesInternal()
    {
        var searchLower = ChallengeSearchText?.ToLowerInvariant().Trim() ?? string.Empty;
        var hasSearch = !string.IsNullOrWhiteSpace(searchLower);
        
        var filteredList = Challenges
            .Where(challenge => !hasSearch ||
                               challenge.Name.ToLowerInvariant().Contains(searchLower) ||
                               challenge.Description.ToLowerInvariant().Contains(searchLower) ||
                               challenge.Category.ToLowerInvariant().Contains(searchLower))
            .ToList();
        
        FilteredChallenges.ReplaceAll(filteredList);
    }

    [RelayCommand]
    private async Task SetStatusAsync()
    {
        if (string.IsNullOrWhiteSpace(CustomStatus))
        {
            System.Windows.MessageBox.Show("Введите статус", "Предупреждение", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsLoading = true;
            var success = await _customizationService.SetProfileStatusAsync(CustomStatus).ConfigureAwait(false);
            
            if (success)
            {
                System.Windows.MessageBox.Show("Статус успешно установлен!", "Успех", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show("Не удалось установить статус. Убедитесь, что клиент LoL запущен.", "Ошибка", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка установки статуса: {ex.Message}");
            System.Windows.MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SetBackgroundAsync()
    {
        if (SelectedChampionForBackground == "(Не выбрано)" || string.IsNullOrWhiteSpace(SelectedChampionForBackground))
        {
            System.Windows.MessageBox.Show("Выберите чемпиона для фона", "Предупреждение", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }
        
        if (SelectedSkinForBackground == null)
        {
            System.Windows.MessageBox.Show("Выберите скин для фона", "Предупреждение", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsLoading = true;

            var success = await _customizationService.SetProfileBackgroundAsync(SelectedSkinForBackground.BackgroundSkinId).ConfigureAwait(false);
            
            if (success)
            {
                var skinName = SelectedSkinForBackground.SkinNumber == 0 
                    ? SelectedChampionForBackground 
                    : $"{SelectedChampionForBackground} - {SelectedSkinForBackground.Name}";
                System.Windows.MessageBox.Show($"Фон профиля установлен: {skinName}", "Успех", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            else
            {
                var lcuAuth = await _riotClientService.GetLcuAuthAsync();
                if (lcuAuth == null)
                {
                    System.Windows.MessageBox.Show("Не удалось установить фон, поскольку не найден клиент лиги легенд.\n\nУбедитесь, что:\n1. Клиент League of Legends запущен\n2. Вы вошли в аккаунт\n3. Клиент полностью загрузился", "Ошибка", 
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                else
                {
                    var logPath = _logger.LogFilePath;
                    System.Windows.MessageBox.Show($"Не удалось установить фон. Возможно, клиент еще не готов или произошла ошибка при установке.\n\nПроверьте логи для деталей:\n{logPath}", "Ошибка", 
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка установки фона: {ex.Message}");
            System.Windows.MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    public void SelectChallenge(ChallengeInfo challenge)
    {
        if (SelectedChallenge1 == null)
        {
            SelectedChallenge1 = challenge;
        }
        else if (SelectedChallenge2 == null)
        {
            SelectedChallenge2 = challenge;
        }
        else if (SelectedChallenge3 == null)
        {
            SelectedChallenge3 = challenge;
        }
        else
        {
            SelectedChallenge1 = challenge;
        }
    }
    
    public void RemoveChallenge(ChallengeInfo challenge)
    {
        if (SelectedChallenge1 == challenge)
        {
            SelectedChallenge1 = SelectedChallenge2;
            SelectedChallenge2 = SelectedChallenge3;
            SelectedChallenge3 = null;
        }
        else if (SelectedChallenge2 == challenge)
        {
            SelectedChallenge2 = SelectedChallenge3;
            SelectedChallenge3 = null;
        }
        else if (SelectedChallenge3 == challenge)
        {
            SelectedChallenge3 = null;
        }
    }
    
    private void UpdateSelectedChallenges()
    {
        SelectedChallenges.Clear();
        if (SelectedChallenge1 != null) SelectedChallenges.Add(SelectedChallenge1);
        if (SelectedChallenge2 != null) SelectedChallenges.Add(SelectedChallenge2);
        if (SelectedChallenge3 != null) SelectedChallenges.Add(SelectedChallenge3);
    }

    [RelayCommand]
    private async Task SetChallengesAsync()
    {
        var challengeIds = new List<long>();
        
        if (SelectedChallenge1 != null)
            challengeIds.Add(SelectedChallenge1.Id);
        if (SelectedChallenge2 != null)
            challengeIds.Add(SelectedChallenge2.Id);
        if (SelectedChallenge3 != null)
            challengeIds.Add(SelectedChallenge3.Id);

        if (challengeIds.Count == 0)
        {
            try
            {
                IsLoading = true;
                var success = await _customizationService.SetChallengeTokensAsync(new List<long>(), -1).ConfigureAwait(false);
                
                if (success)
                {
                    System.Windows.MessageBox.Show("Челенджи успешно очищены", "Успех", 
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show("Не удалось очистить челенджи. Убедитесь, что клиент LoL запущен.", "Ошибка", 
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка очистки челенджей: {ex.Message}");
                System.Windows.MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
            return;
        }

        try
        {
            IsLoading = true;
            
            var finalChallengeIds = new List<long>();
            foreach (var id in challengeIds)
            {
                finalChallengeIds.Add(id);
                finalChallengeIds.Add(id);
                finalChallengeIds.Add(id);
            }
            
            while (finalChallengeIds.Count > 3)
            {
                finalChallengeIds.RemoveAt(finalChallengeIds.Count - 1);
            }
            
            var success = await _customizationService.SetChallengeTokensAsync(finalChallengeIds, -1).ConfigureAwait(false);
            
            if (success)
            {
                var names = challengeIds.Select(id => 
                    Challenges.FirstOrDefault(c => c.Id == id)?.Name ?? id.ToString()
                ).ToList();
                System.Windows.MessageBox.Show($"Челенджи успешно установлены: {string.Join(", ", names)}", "Успех", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show("Не удалось установить челенджи. Убедитесь, что клиент LoL запущен.", "Ошибка", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка установки челенджей: {ex.Message}");
            System.Windows.MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }
}

