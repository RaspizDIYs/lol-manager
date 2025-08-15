using System.Reflection;
using Velopack;

namespace LolManager.Services;

public class UpdateService : IUpdateService
{
    private readonly ILogger _logger;
    private readonly ISettingsService _settingsService;

    public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.1";

    public UpdateService(ILogger logger, ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
    }

    public async Task<bool> CheckForUpdatesAsync()
    {
        try
        {
            var settings = _settingsService.LoadUpdateSettings();
            
            // Проверяем, включены ли автообновления
            if (!settings.AutoUpdateEnabled)
                return false;
                
            // Проверяем интервал проверки
            var timeSinceLastCheck = DateTime.UtcNow - settings.LastCheckTime;
            if (timeSinceLastCheck.TotalHours < settings.CheckIntervalHours)
                return false;
                
            // Обновляем время последней проверки
            settings.LastCheckTime = DateTime.UtcNow;
            _settingsService.SaveUpdateSettings(settings);
            
            // В реальном приложении здесь будет API для проверки обновлений
            // Пока возвращаем false (нет обновлений)
            return await Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to check for updates: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UpdateAsync()
    {
        try
        {
            // В реальном приложении здесь будет API для обновления
            // Пока возвращаем false (обновление не выполнено)
            return await Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to update: {ex.Message}");
            return false;
        }
    }

    public async Task<string> GetChangelogAsync()
    {
        try
        {
            // В реальном приложении здесь будет API для получения changelog
            // Пока возвращаем заглушку
            return await Task.FromResult(GetDefaultChangelog());
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get changelog: {ex.Message}");
            return "Ошибка загрузки changelog";
        }
    }

    private string GetDefaultChangelog()
    {
        return @"# Changelog

## [0.0.1] - 2025-08-15

### Added
- Базовая функциональность для управления League of Legends
- Автоматический вход в Riot Client через UI Automation
- Система логирования и мониторинга процессов
- Интерфейс для управления аккаунтами

### Changed
- Переход с P/Invoke на UI Automation (FlaUI) для надёжности
- Оптимизация времени ожидания готовности CEF-контента

### Fixed
- Проблемы с холодным стартом Riot Client
- Задержки при обнаружении элементов UI
- Надёжность автоматического входа";
    }
}
