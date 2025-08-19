using System.IO;
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

    public string CurrentVersion 
    { 
        get 
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            
            // Обрезаем Git metadata (+commit_hash) для чистой версии
            if (!string.IsNullOrEmpty(version))
            {
                // Убираем всё после '+' (Git metadata)
                var cleanVersion = version.Split('+')[0];
                return cleanVersion;
            }
            
            return assembly.GetName().Version?.ToString(3) ?? "0.0.1";
        }
    }

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
        
        // Velopack GithubSource ожидает URL к репозиторию GitHub
        var repoUrl = $"https://github.com/{repoOwner}/{repoName}";
        
        return channel switch
        {
            "beta" => new GithubSource(repoUrl, null, true), // включает pre-releases
            "stable" => new GithubSource(repoUrl, null, false), // только stable releases
            _ => new GithubSource(repoUrl, null, false)
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

    public async Task<bool> CheckForUpdatesAsync(bool forceCheck = false)
    {
        try
        {
            var checkType = forceCheck ? "manual" : "automatic";
            _logger.Info($"Starting {checkType} update check...");
            
            var updateManager = GetUpdateManager();
            if (updateManager == null)
            {
                _logger.Error("UpdateManager not available");
                return false;
            }

            var settings = _settingsService.LoadUpdateSettings();
            _logger.Info($"Current version: {CurrentVersion}, Channel: {settings.UpdateChannel}");
            
            // Проверяем интервал проверки только для автоматических проверок
            if (!forceCheck)
            {
                var timeSinceLastCheck = DateTime.UtcNow - settings.LastCheckTime;
                _logger.Info($"Time since last check: {timeSinceLastCheck.TotalHours:F1} hours (interval: {settings.CheckIntervalHours} hours)");
                
                if (settings.AutoUpdateEnabled && timeSinceLastCheck.TotalHours < settings.CheckIntervalHours)
                {
                    _logger.Info("Skipping automatic update check - interval not reached. Use manual check to force.");
                    return false;
                }
            }
            else
            {
                _logger.Info("Force check - ignoring interval restrictions");
            }
                
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
            _logger.Debug($"Update check exception details: {ex}");
            return false;
        }
    }

    public async Task<bool> UpdateAsync()
    {
        try
        {
            _logger.Info("Starting update process...");
            
            var updateManager = GetUpdateManager();
            if (updateManager == null)
            {
                _logger.Error("UpdateManager not available");
                return false;
            }

            var updateInfo = await updateManager.CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                _logger.Info("No updates available");
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
            _logger.Info("Getting changelog...");
            
            // Сначала пытаемся получить changelog из GitHub API
            var githubChangelog = await GetGitHubChangelogAsync();
            if (!string.IsNullOrEmpty(githubChangelog))
            {
                _logger.Info($"Got GitHub changelog, length: {githubChangelog.Length}");
                return githubChangelog;
            }
            
            _logger.Warning("GitHub changelog is empty, trying Velopack...");

            // Если не удалось - пытаемся через Velopack
            var updateManager = GetUpdateManager();
            if (updateManager != null)
            {
                var updateInfo = await updateManager.CheckForUpdatesAsync();
                if (updateInfo?.TargetFullRelease != null)
                {
                    var releaseNotes = $"## Обновление до версии {updateInfo.TargetFullRelease.Version}\n\n" +
                                       $"**Пакет:** {updateInfo.TargetFullRelease.PackageId}\n\n" +
                                       "Подробную информацию об изменениях можно найти в GitHub Releases.";
                    _logger.Info("Using Velopack release notes");
                    return releaseNotes;
                }
            }
            
            _logger.Info("Using default changelog");
            return GetDefaultChangelog();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get changelog: {ex.Message}");
            _logger.Debug($"Changelog exception details: {ex}");
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
            
            foreach (var release in releases.RootElement.EnumerateArray().Take(10)) // Берем последние 10 релизов
            {
                var tagName = release.GetProperty("tag_name").GetString() ?? "Unknown";
                var name = release.GetProperty("name").GetString() ?? "";
                var body = release.GetProperty("body").GetString() ?? "Нет описания";
                var isPrerelease = release.GetProperty("prerelease").GetBoolean();
                
                var releaseType = isPrerelease ? " (Beta)" : "";
                
                // Форматируем как "версия - название"
                var title = string.IsNullOrEmpty(name) ? tagName : $"{tagName} - {name}";
                
                changelog.AppendLine($"## {title}{releaseType}");
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
