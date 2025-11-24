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

public partial class BindingEditorViewModel : ObservableObject
{
    private readonly BindingService _bindingService;
    private readonly ILogger _logger;
    private readonly int _championId;

    [ObservableProperty]
    private string _championName;

    public ObservableCollection<BindingItem> Bindings { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private BindingItem? selectedBinding;

    public bool DialogResult { get; set; }

    public BindingEditorViewModel(BindingService bindingService, ILogger logger, int championId, string championName, Dictionary<string, string>? existingBindings = null)
    {
        _bindingService = bindingService;
        _logger = logger;
        _championId = championId;
        ChampionName = championName;

        if (existingBindings != null)
        {
            LoadBindings(existingBindings);
        }
        else
        {
            _ = LoadBindingsAsync();
        }
    }

    private async Task LoadBindingsAsync()
    {
        IsLoading = true;
        try
        {
            var settings = await _bindingService.GetInputSettingsAsync();
            if (settings != null)
            {
                LoadBindings(settings);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка загрузки биндингов: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void LoadBindings(Dictionary<string, string> settings)
    {
        Bindings.Clear();
        foreach (var kvp in settings.OrderBy(k => k.Key))
        {
            Bindings.Add(new BindingItem
            {
                Key = kvp.Key,
                Value = kvp.Value
            });
        }
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            var settings = Bindings.ToDictionary(b => b.Key, b => b.Value);
            var group = new BindingGroup
            {
                Name = $"champion_{_championId}",
                Settings = settings
            };

            _bindingService.SetChampionBinding(_championId, group);
            DialogResult = true;
            _logger.Info($"Биндинги сохранены для {ChampionName} (ID: {_championId})");
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка сохранения биндингов: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }

    public Dictionary<string, string> GetBindings()
    {
        return Bindings.ToDictionary(b => b.Key, b => b.Value);
    }
}

