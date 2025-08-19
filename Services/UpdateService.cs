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
            
            _logger.Info("No updates available via Velopack, checking GitHub directly...");
            
            // Fallback: проверяем GitHub напрямую (для отладочной среды)
            var hasGitHubUpdate = await CheckGitHubForUpdatesAsync(settings.UpdateChannel);
            if (hasGitHubUpdate)
            {
                _logger.Info("Update available via GitHub API");
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

    private async Task<bool> CheckGitHubForUpdatesAsync(string channel)
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
            
            foreach (var release in releases.RootElement.EnumerateArray())
            {
                var isPrerelease = release.GetProperty("prerelease").GetBoolean();
                
                // Фильтруем по каналу
                if (channel == "stable" && isPrerelease)
                    continue;
                    
                var tagName = release.GetProperty("tag_name").GetString() ?? "";
                var releaseVersion = tagName.TrimStart('v');
                
                _logger.Info($"Comparing versions: current={CurrentVersion}, latest={releaseVersion}");
                
                // Простое сравнение версий
                if (IsNewerVersion(CurrentVersion, releaseVersion))
                {
                    _logger.Info($"Found newer version: {releaseVersion}");
                    return true;
                }
                
                // Берем только первый подходящий релиз
                break;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to check GitHub for updates: {ex.Message}");
            return false;
        }
    }
    
    private bool IsNewerVersion(string currentVersion, string newVersion)
    {
        try
        {
            // Убираем возможные суффиксы и берем только числовую часть
            var current = ParseVersion(currentVersion);
            var latest = ParseVersion(newVersion);
            
            return latest > current;
        }
        catch
        {
            return false;
        }
    }
    
    private Version ParseVersion(string versionString)
    {
        // Убираем 'v' в начале и берем только основную версию
        var cleaned = versionString.TrimStart('v').Split('-')[0];
        
        // Если версия вида "0.0.4.0", берем только "0.0.4"
        var parts = cleaned.Split('.');
        if (parts.Length >= 3)
        {
            return new Version($"{parts[0]}.{parts[1]}.{parts[2]}");
        }
        
        return Version.Parse(cleaned);
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
