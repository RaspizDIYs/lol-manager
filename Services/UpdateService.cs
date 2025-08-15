using System.Reflection;
using Velopack;

namespace LolManager.Services;

public class UpdateService : IUpdateService
{
    private readonly ILogger _logger;
    private readonly UpdateManager _updateManager;

    public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.1";

    public UpdateService(ILogger logger)
    {
        _logger = logger;
        _updateManager = new UpdateManager();
    }

    public async Task<bool> CheckForUpdatesAsync()
    {
        try
        {
            var updateInfo = await _updateManager.CheckForUpdatesAsync();
            return updateInfo != null;
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
            await _updateManager.UpdateAsync();
            return true;
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
