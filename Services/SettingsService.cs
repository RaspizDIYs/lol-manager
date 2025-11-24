using System.Text.Json;
using System.Diagnostics;
using LolManager.Models;
using System.IO;

namespace LolManager.Services;

public class SettingsService : ISettingsService
{
    private static readonly object SettingsLock = new();
    private readonly string _configPath;
    private readonly Dictionary<string, object> _settings;

    public SettingsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LolManager");
        
        if (!Directory.Exists(appDataPath))
            Directory.CreateDirectory(appDataPath);
            
        _configPath = Path.Combine(appDataPath, "settings.json");
        _settings = LoadSettings();
    }

    public UpdateSettings LoadUpdateSettings()
    {
        try
        {
            var updateSettingsPath = Path.Combine(
                Path.GetDirectoryName(_configPath)!, 
                "update-settings.json");
                
            if (File.Exists(updateSettingsPath))
            {
                var json = File.ReadAllText(updateSettingsPath);
                var settings = JsonSerializer.Deserialize<UpdateSettings>(json);
                return settings ?? new UpdateSettings();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SettingsService: failed to load update settings: {ex}");
        }
        
        return new UpdateSettings();
    }

    public void SaveUpdateSettings(UpdateSettings settings)
    {
        try
        {
            var updateSettingsPath = Path.Combine(
                Path.GetDirectoryName(_configPath)!, 
                "update-settings.json");
                
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(updateSettingsPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SettingsService: failed to save update settings: {ex}");
        }
    }

    public T LoadSetting<T>(string key, T defaultValue)
    {
        if (_settings.TryGetValue(key, out var value))
        {
            try
            {
                // Если value - это JsonElement, десериализуем его
                if (value is JsonElement jsonElement)
                {
                    return JsonSerializer.Deserialize<T>(jsonElement.GetRawText()) ?? defaultValue;
                }
                
                // Для простых типов используем Convert
                if (typeof(T).IsPrimitive || typeof(T) == typeof(string))
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                
                // Для сложных объектов пробуем сериализовать обратно
                var json = JsonSerializer.Serialize(value);
                return JsonSerializer.Deserialize<T>(json) ?? defaultValue;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsService: failed to deserialize setting '{key}': {ex}");
            }
        }
        return defaultValue;
    }

    public void SaveSetting<T>(string key, T value)
    {
        _settings[key] = value!;
        SaveSettings();
    }

    private Dictionary<string, object> LoadSettings()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                string json;
                lock (SettingsLock)
                {
                    json = File.ReadAllText(_configPath);
                }
                var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                return settings ?? new Dictionary<string, object>();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SettingsService: failed to load settings: {ex}");
        }
        
        return new Dictionary<string, object>();
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            lock (SettingsLock)
            {
                File.WriteAllText(_configPath, json);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SettingsService: failed to save settings: {ex}");
        }
    }
}
