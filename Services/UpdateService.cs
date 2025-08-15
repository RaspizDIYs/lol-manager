using System.Net.Http;
using System.Reflection;
using System.Text;
using Velopack;
using Velopack.Sources;
using LolManager.Models;

namespace LolManager.Services;

public class UpdateService : IUpdateService
{
    private readonly ILogger _logger;
    private readonly ISettingsService _settingsService;
    private UpdateManager? _updateManager;

    public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.1";

    public UpdateService(ILogger logger, ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
    }

    private UpdateManager? GetUpdateManager()
    {
        try
        {
            if (_updateManager == null)
            {
                var settings = _settingsService.LoadUpdateSettings();
                var updateSource = GetUpdateSource(settings.UpdateChannel);
                _updateManager = new UpdateManager(updateSource);
            }
            return _updateManager;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to create UpdateManager: {ex.Message}");
            return null;
        }
    }

    private IUpdateSource GetUpdateSource(string channel)
    {
        const string repoOwner = "RaspizDIYs"; 
        const string repoName = "lol-manager";
        
        // Velopack GithubSource ожидает полный URL к API GitHub
        var apiUrl = $"https://api.github.com/repos/{repoOwner}/{repoName}";
        
        return channel switch
        {
            "beta" => new GithubSource(apiUrl, null, true), // включает pre-releases
            "stable" => new GithubSource(apiUrl, null, false), // только stable releases
            _ => new GithubSource(apiUrl, null, false)
        };
    }

    public void RefreshUpdateSource()
    {
        try
        {
            // Сбрасываем кэшированный UpdateManager для пересоздания с новым каналом
            _updateManager = null;
            
            var settings = _settingsService.LoadUpdateSettings();
            _logger.Info($"Update source refreshed for channel: {settings.UpdateChannel}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to refresh update source: {ex.Message}");
        }
    }

    public async Task<bool> CheckForUpdatesAsync()
    {
        try
        {
            var updateManager = GetUpdateManager();
            if (updateManager == null)
            {
                _logger.Error("UpdateManager not available");
                return false;
            }

            var settings = _settingsService.LoadUpdateSettings();
            
            // Проверяем интервал проверки (но не для ручной проверки)
            var timeSinceLastCheck = DateTime.UtcNow - settings.LastCheckTime;
            if (settings.AutoUpdateEnabled && timeSinceLastCheck.TotalHours < settings.CheckIntervalHours)
                return false;
                
            // Обновляем время последней проверки
            settings.LastCheckTime = DateTime.UtcNow;
            _settingsService.SaveUpdateSettings(settings);
            
            _logger.Info($"Checking for updates on {settings.UpdateChannel} channel...");
            
            var updateInfo = await updateManager.CheckForUpdatesAsync();
            if (updateInfo != null)
            {
                _logger.Info($"Update available: {updateInfo.TargetFullRelease.Version} ({settings.UpdateChannel})");
                return true;
            }
            
            _logger.Info("No updates available");
            return false;
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
            var updateManager = GetUpdateManager();
            if (updateManager == null)
            {
                _logger.Error("UpdateManager not available");
                return false;
            }

            _logger.Info("Starting update process...");
            
            var updateInfo = await updateManager.CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                _logger.Info("No updates available for download");
                return false;
            }

            _logger.Info($"Downloading update: {updateInfo.TargetFullRelease.Version}");
            await updateManager.DownloadUpdatesAsync(updateInfo);
            
            _logger.Info("Update downloaded, applying and restarting...");
            updateManager.ApplyUpdatesAndRestart(updateInfo);
            
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
            // Сначала пытаемся получить changelog из GitHub API
            var githubChangelog = await GetGitHubChangelogAsync();
            if (!string.IsNullOrEmpty(githubChangelog))
            {
                return githubChangelog;
            }

            // Если не удалось - пытаемся через Velopack
            var updateManager = GetUpdateManager();
            if (updateManager != null)
            {
                var updateInfo = await updateManager.CheckForUpdatesAsync();
                if (updateInfo?.TargetFullRelease != null)
                {
                    var releaseNotes = $"Обновление до версии {updateInfo.TargetFullRelease.Version}\n" +
                                       $"Пакет: {updateInfo.TargetFullRelease.PackageId}\n" +
                                       "Подробности в GitHub Releases";
                    return releaseNotes;
                }
            }
            
            return GetDefaultChangelog();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get changelog: {ex.Message}");
            return GetDefaultChangelog();
        }
    }

    private async Task<string> GetGitHubChangelogAsync()
    {
        try
        {
            const string repoOwner = "RaspizDIYs";
            const string repoName = "lol-manager";
            
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "LolManager");
            
            var url = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases";
            var response = await httpClient.GetStringAsync(url);
            
            var releases = System.Text.Json.JsonDocument.Parse(response);
            var changelog = new StringBuilder();
            changelog.AppendLine("# История изменений\n");
            
            foreach (var release in releases.RootElement.EnumerateArray().Take(10)) // Берем последние 10 релизов
            {
                var tagName = release.GetProperty("tag_name").GetString() ?? "Unknown";
                var name = release.GetProperty("name").GetString() ?? tagName;
                var publishedAt = release.GetProperty("published_at").GetString();
                var body = release.GetProperty("body").GetString() ?? "Нет описания";
                var isPrerelease = release.GetProperty("prerelease").GetBoolean();
                
                var releaseType = isPrerelease ? " (Beta)" : "";
                
                changelog.AppendLine($"## {name}{releaseType}");
                if (!string.IsNullOrEmpty(publishedAt) && DateTime.TryParse(publishedAt, out var date))
                {
                    changelog.AppendLine($"*Опубликовано: {date:dd.MM.yyyy}*\n");
                }
                
                changelog.AppendLine(body);
                changelog.AppendLine();
            }
            
            return changelog.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to fetch GitHub changelog: {ex.Message}");
            return string.Empty;
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
