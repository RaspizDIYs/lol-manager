using LolManager.Models;

namespace LolManager.Services;

public interface ISettingsService
{
    UpdateSettings LoadUpdateSettings();
    void SaveUpdateSettings(UpdateSettings settings);
    T LoadSetting<T>(string key, T defaultValue);
    void SaveSetting<T>(string key, T value);
}
