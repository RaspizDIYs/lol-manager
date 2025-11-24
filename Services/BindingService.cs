using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LolManager.Models;

namespace LolManager.Services;

public class BindingService
{
    private readonly ILogger _logger;
    private readonly IRiotClientService _riotClientService;
    private readonly ISettingsService _settingsService;
    private readonly string _bindingsStoragePath;
    private Dictionary<int, BindingGroup>? _championBindings;
    private Dictionary<string, string>? _backupSettings;
    private string? _currentChampionGroup;
    private readonly object _lockObject = new();

    public BindingService(ILogger logger, IRiotClientService riotClientService, ISettingsService settingsService)
    {
        _logger = logger;
        _riotClientService = riotClientService;
        _settingsService = settingsService;
        
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LolManager");
        
        if (!Directory.Exists(appDataPath))
            Directory.CreateDirectory(appDataPath);
        
        _bindingsStoragePath = Path.Combine(appDataPath, "champion-bindings.json");
        
        LoadBindings();
    }

    public Dictionary<int, BindingGroup> GetChampionBindings()
    {
        lock (_lockObject)
        {
            return _championBindings ?? new Dictionary<int, BindingGroup>();
        }
    }

    public void SaveBindings()
    {
        lock (_lockObject)
        {
            try
            {
                var json = JsonSerializer.Serialize(_championBindings, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_bindingsStoragePath, json);
            }
            catch (Exception ex)
            {
                _logger.Error($"Не удалось сохранить биндинги: {ex.Message}");
            }
        }
    }

    private void LoadBindings()
    {
        lock (_lockObject)
        {
            try
            {
                if (File.Exists(_bindingsStoragePath))
                {
                    var json = File.ReadAllText(_bindingsStoragePath);
                    var options = new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    };
                    
                    var temp = JsonSerializer.Deserialize<Dictionary<string, BindingGroup>>(json, options);
                    if (temp != null)
                    {
                        _championBindings = new Dictionary<int, BindingGroup>();
                        foreach (var kvp in temp)
                        {
                            if (int.TryParse(kvp.Key, out var championId))
                            {
                                _championBindings[championId] = kvp.Value;
                            }
                        }
                    }
                }
                
                if (_championBindings == null)
                {
                    _championBindings = new Dictionary<int, BindingGroup>();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Не удалось загрузить биндинги: {ex.Message}");
                _championBindings = new Dictionary<int, BindingGroup>();
            }
        }
    }

    public void SetChampionBinding(int championId, BindingGroup group)
    {
        lock (_lockObject)
        {
            if (_championBindings == null)
                _championBindings = new Dictionary<int, BindingGroup>();
            
            _championBindings[championId] = group;
            SaveBindings();
        }
    }

    public BindingGroup? GetChampionBinding(int championId)
    {
        lock (_lockObject)
        {
            return _championBindings?.TryGetValue(championId, out var group) == true ? group : null;
        }
    }

    public void RemoveChampionBinding(int championId)
    {
        lock (_lockObject)
        {
            _championBindings?.Remove(championId);
            SaveBindings();
        }
    }

    public async Task<Dictionary<string, string>?> GetInputSettingsAsync()
    {
        try
        {
            var lcuAuth = await _riotClientService.GetLcuAuthAsync();
            if (lcuAuth == null)
            {
                _logger.Error("LCU недоступен для получения настроек биндингов");
                return null;
            }

            var (port, password) = lcuAuth.Value;
            using var client = CreateHttpClient(port, password);
            
            var response = await client.GetAsync("/lol-game-settings/v1/input-settings");
            if (!response.IsSuccessStatusCode)
            {
                _logger.Error($"Не удалось получить input-settings: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json, options);
            
            return settings;
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка при получении input-settings: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> PatchInputSettingsAsync(Dictionary<string, string> settings)
    {
        try
        {
            var lcuAuth = await _riotClientService.GetLcuAuthAsync();
            if (lcuAuth == null)
            {
                _logger.Error("LCU недоступен для применения настроек биндингов");
                return false;
            }

            var (port, password) = lcuAuth.Value;
            using var client = CreateHttpClient(port, password);
            
            var json = JsonSerializer.Serialize(settings);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await client.PatchAsync("/lol-game-settings/v1/input-settings", content);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.Info("Биндинги успешно применены");
                return true;
            }
            
            _logger.Error($"Не удалось применить биндинги: {response.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка при применении биндингов: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ApplyChampionBindingsAsync(int championId)
    {
        try
        {
            var group = GetChampionBinding(championId);
            if (group == null)
            {
                _logger.Info($"Биндинги для чемпиона {championId} не найдены");
                return false;
            }

            if (_backupSettings == null)
            {
                _backupSettings = await GetInputSettingsAsync();
                if (_backupSettings == null)
                {
                    _logger.Error("Не удалось создать резервную копию настроек");
                    return false;
                }
            }

            _currentChampionGroup = group.Name;
            _logger.Info($"Применяю биндинги группы '{group.Name}' для чемпиона {championId}");
            
            return await PatchInputSettingsAsync(group.Settings);
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка при применении биндингов чемпиона: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RestoreBindingsAsync()
    {
        try
        {
            if (_backupSettings == null || _currentChampionGroup == null)
            {
                return false;
            }

            var currentSettings = await GetInputSettingsAsync();
            if (currentSettings == null)
            {
                _logger.Error("Не удалось получить текущие настройки для синхронизации");
                return false;
            }

            var group = _championBindings?.Values.FirstOrDefault(g => g.Name == _currentChampionGroup);
            if (group != null)
            {
                foreach (var kvp in currentSettings)
                {
                    if (!group.Settings.ContainsKey(kvp.Key))
                    {
                        _backupSettings[kvp.Key] = kvp.Value;
                    }
                }
            }

            _logger.Info("Восстанавливаю оригинальные биндинги");
            var result = await PatchInputSettingsAsync(_backupSettings);
            
            _backupSettings = null;
            _currentChampionGroup = null;
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка при восстановлении биндингов: {ex.Message}");
            return false;
        }
    }

    private HttpClient CreateHttpClient(int port, string password)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{port}")
        };
        
        var authBytes = Encoding.UTF8.GetBytes($"riot:{password}");
        var authHeader = Convert.ToBase64String(authBytes);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
        
        return client;
    }
}

