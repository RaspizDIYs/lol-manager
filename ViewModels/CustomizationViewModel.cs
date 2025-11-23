using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LolManager.Models;
using LolManager.Services;

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
    public ObservableCollection<Models.SkinInfo> AllSkins { get; } = new();
    public ObservableCollection<Models.SkinInfo> FilteredSkins { get; } = new();
    public ObservableCollection<ChallengeInfo> Challenges { get; } = new();
    public ObservableCollection<ChallengeInfo> FilteredChallenges { get; } = new();
    
    [ObservableProperty]
    private ChallengeInfo? selectedChallenge1;
    
    [ObservableProperty]
    private ChallengeInfo? selectedChallenge2;
    
    [ObservableProperty]
    private ChallengeInfo? selectedChallenge3;

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
                FilterBackgrounds();
            }
            else if (e.PropertyName == nameof(ChallengeSearchText))
            {
                FilterChallenges();
            }
            else if (e.PropertyName == nameof(SelectedChampionForBackground))
            {
                UpdateAvailableSkins();
            }
        };
        
        _ = LoadChampionsAsync();
        _ = LoadChallengesAsync();
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
            var champions = await _dataDragonService.GetChampionsAsync();
            
            Champions.Clear();
            Champions.Add("(Не выбрано)");
            AllSkins.Clear();
            
            foreach (var champ in champions.Keys.OrderBy(x => x))
            {
                Champions.Add(champ);
                
                var info = _dataDragonService.GetChampionInfo(champ);
                if (info != null && info.Skins.Count > 0)
                {
                    foreach (var skin in info.Skins.OrderBy(s => s.SkinNumber))
                    {
                        AllSkins.Add(skin);
                    }
                }
            }
            
            FilterBackgrounds();
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

    private void FilterBackgrounds()
    {
        var searchLower = BackgroundSearchText?.ToLowerInvariant().Trim() ?? string.Empty;
        var hasSearch = !string.IsNullOrWhiteSpace(searchLower);
        
        FilteredChampions.Clear();
        FilteredChampions.Add("(Не выбрано)");
        
        FilteredSkins.Clear();
        
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
                FilteredChampions.Add(championName);
            }
            
            // Добавляем все скины этого чемпиона в фильтрованный список
            foreach (var skin in info.Skins.OrderBy(s => s.SkinNumber))
            {
                bool skinMatches = !hasSearch || 
                    championMatches ||
                    skin.Name.ToLowerInvariant().Contains(searchLower) ||
                    info.DisplayName.ToLowerInvariant().Contains(searchLower);
                
                if (skinMatches)
                {
                    FilteredSkins.Add(skin);
                }
            }
        }
        
        UpdateAvailableSkins();
    }

    private void FilterChallenges()
    {
        var searchLower = ChallengeSearchText?.ToLowerInvariant().Trim() ?? string.Empty;
        var hasSearch = !string.IsNullOrWhiteSpace(searchLower);
        
        FilteredChallenges.Clear();
        
        foreach (var challenge in Challenges)
        {
            if (hasSearch)
            {
                bool matches = challenge.Name.ToLowerInvariant().Contains(searchLower) ||
                              challenge.Description.ToLowerInvariant().Contains(searchLower) ||
                              challenge.Category.ToLowerInvariant().Contains(searchLower);
                
                if (!matches) continue;
            }
            
            FilteredChallenges.Add(challenge);
        }
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
            var success = await _customizationService.SetProfileStatusAsync(CustomStatus);
            
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

            var success = await _customizationService.SetProfileBackgroundAsync(SelectedSkinForBackground.BackgroundSkinId);
            
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
                var success = await _customizationService.SetChallengeTokensAsync(new List<long>(), -1);
                
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
            
            var success = await _customizationService.SetChallengeTokensAsync(finalChallengeIds, -1);
            
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

