using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
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
        
        var repoUrl = $"https://github.com/{repoOwner}/{repoName}";
        
        _logger.Info($"Creating GithubSource for {repoUrl} (channel: {channel})");
        
        try
        {
            var includePrerelease = channel == "beta";
            var channelName = channel == "beta" ? "beta" : "stable";
            
            _logger.Info($"Channel: {channelName}, Include prerelease: {includePrerelease}");
            
            var source = new GithubSource(repoUrl, null, includePrerelease);
            _logger.Info($"GithubSource created successfully for channel '{channelName}'");
            
            return source;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to create GithubSource: {ex.GetType().Name} - {ex.Message}");
            _logger.Debug($"GithubSource exception: {ex}");
            throw;
        }
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
            _logger.Info($"Update source: https://github.com/RaspizDIYs/lol-manager (channel: {settings.UpdateChannel})");
            
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
            
            // Сначала проверим GitHub релизы напрямую
            await CheckGitHubReleasesDirectlyAsync(settings.UpdateChannel);
            
            try
            {
                _logger.Info("Calling Velopack CheckForUpdatesAsync...");
                
                var updateInfo = await updateManager.CheckForUpdatesAsync();
                
                if (updateInfo != null)
                {
                    _logger.Info($"Velopack found update: {updateInfo.TargetFullRelease.Version} ({settings.UpdateChannel})");
                    _logger.Info($"Target package: {updateInfo.TargetFullRelease.PackageId}");
                    return true;
                }
                else
                {
                    _logger.Warning("Velopack CheckForUpdatesAsync returned null - no updates found or error accessing RELEASES file");
                    _logger.Warning("This usually means RELEASES file is missing, corrupted, or doesn't contain newer version info");
                }
            }
            catch (Exception veloEx)
            {
                _logger.Error($"Velopack CheckForUpdatesAsync failed: {veloEx.GetType().Name} - {veloEx.Message}");
                
                // Проверяем специфичные типы ошибок Velopack
                if (veloEx.Message.Contains("404") || veloEx.Message.Contains("Not Found"))
                {
                    _logger.Error("HTTP 404 - RELEASES file not found in GitHub release assets");
                }
                else if (veloEx.Message.Contains("403") || veloEx.Message.Contains("Forbidden"))
                {
                    _logger.Error("HTTP 403 - Access denied to GitHub repository or rate limited");
                }
                else if (veloEx.Message.Contains("ssl") || veloEx.Message.Contains("certificate"))
                {
                    _logger.Error("SSL/Certificate error accessing GitHub");
                }
                
                _logger.Debug($"Velopack exception stack trace: {veloEx.StackTrace}");
            }
            
            _logger.Info("No updates available via Velopack, trying direct GitHub check...");
            
            // Если Velopack не нашел обновления, проверим напрямую через GitHub API
            var hasDirectUpdate = await CheckForUpdatesViaGitHubAPIAsync(settings.UpdateChannel);
            if (hasDirectUpdate)
            {
                _logger.Info("Updates available via direct GitHub API check");
                return true;
            }
            
            _logger.Info("No updates available via any method");
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

            _logger.Info("Calling Velopack CheckForUpdatesAsync for update process...");
            
            var updateInfo = await updateManager.CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                _logger.Info("Velopack CheckForUpdatesAsync returned null in UpdateAsync - no updates available");
                
                // Если Velopack не нашел обновления, но мы знаем что они есть через GitHub API
                var settings = _settingsService.LoadUpdateSettings();
                var hasDirectUpdate = await CheckForUpdatesViaGitHubAPIAsync(settings.UpdateChannel);
                if (hasDirectUpdate)
                {
                    _logger.Info("Updates available via GitHub, but Velopack update files missing. Opening GitHub releases page...");
                    
                    try
                    {
                        var githubUrl = "https://github.com/RaspizDIYs/lol-manager/releases";
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = githubUrl,
                            UseShellExecute = true
                        });
                        _logger.Info($"Opened GitHub releases page: {githubUrl}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to open GitHub releases page: {ex.Message}");
                    }
                }
                
                return false;
            }

            _logger.Info($"Velopack found update in UpdateAsync: {updateInfo.TargetFullRelease.Version}");
            _logger.Info($"Update package info - ID: {updateInfo.TargetFullRelease.PackageId}");
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

    private async Task CheckGitHubReleasesDirectlyAsync(string channel)
    {
        try
        {
            _logger.Info("Checking GitHub releases directly for debugging...");
            
            const string repoOwner = "RaspizDIYs";
            const string repoName = "lol-manager";
            
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "LolManager-UpdateCheck");
            
            var url = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases";
            _logger.Info($"Checking GitHub API: {url}");
            
            var response = await httpClient.GetStringAsync(url);
            var releases = System.Text.Json.JsonDocument.Parse(response);
            
            _logger.Info($"Found {releases.RootElement.GetArrayLength()} releases in GitHub");
            
            // Проверяем содержимое releases.win.json файла из latest release
            await CheckReleasesWinJsonContent(httpClient, repoOwner, repoName, releases);
            
            int count = 0;
            foreach (var release in releases.RootElement.EnumerateArray().Take(5))
            {
                var tagName = release.GetProperty("tag_name").GetString() ?? "Unknown";
                var name = release.GetProperty("name").GetString() ?? "";
                var isPrerelease = release.GetProperty("prerelease").GetBoolean();
                var isDraft = release.GetProperty("draft").GetBoolean();
                
                var assets = release.GetProperty("assets");
                var assetCount = assets.GetArrayLength();
                var assetNames = new List<string>();
                
                foreach (var asset in assets.EnumerateArray())
                {
                    var assetName = asset.GetProperty("name").GetString() ?? "";
                    assetNames.Add(assetName);
                }
                
                var releaseType = isPrerelease ? "prerelease" : "stable";
                var status = isDraft ? "DRAFT" : "PUBLIC";
                
                _logger.Info($"Release #{++count}: {tagName} ({releaseType}, {status}) - {assetCount} assets");
                _logger.Info($"  Assets: {string.Join(", ", assetNames)}");
                
                // Проверяем есть ли Velopack файлы
                var releaseFile = assetNames.FirstOrDefault(name => name.Equals("RELEASES", StringComparison.OrdinalIgnoreCase));
                var nupkgFiles = assetNames.Where(name => name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)).ToList();
                
                // Ищем full .nupkg файл который соответствует версии релиза
                var versionString = tagName.StartsWith("v") ? tagName.Substring(1) : tagName;
                var fullNupkg = assetNames.FirstOrDefault(name => 
                    name.Contains("full") && 
                    name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains(versionString));
                
                // Если не найден точный, берем любой full
                if (fullNupkg == null)
                {
                    fullNupkg = assetNames.FirstOrDefault(name => name.Contains("full") && name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
                }
                
                var deltaFiles = assetNames.Where(name => name.Contains("delta") && name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)).ToList();
                
                _logger.Info($"  RELEASES file: {releaseFile ?? "NOT FOUND"}");
                _logger.Info($"  Full .nupkg: {fullNupkg ?? "NOT FOUND"}");
                _logger.Info($"  Delta files: {deltaFiles.Count} found");
                _logger.Info($"  Total .nupkg files: {nupkgFiles.Count}");
                
                var hasVelopackFiles = releaseFile != null && fullNupkg != null;
                _logger.Info($"  Velopack compatible: {hasVelopackFiles}");
                
                // Проверяем подходит ли под текущий канал
                var matchesChannel = channel == "beta" || !isPrerelease;
                _logger.Info($"  Matches channel '{channel}': {matchesChannel}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to check GitHub releases directly: {ex.Message}");
        }
    }

    private async Task CheckReleasesWinJsonContent(HttpClient httpClient, string repoOwner, string repoName, JsonDocument releases)
    {
        try
        {
            var latestRelease = releases.RootElement.EnumerateArray().FirstOrDefault();
            if (latestRelease.ValueKind == JsonValueKind.Undefined)
            {
                _logger.Warning("No releases found to check releases.win.json");
                return;
            }

            var tagName = latestRelease.GetProperty("tag_name").GetString() ?? "Unknown";
            var assets = latestRelease.GetProperty("assets");
            
            string? releasesJsonUrl = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.Equals("releases.win.json", StringComparison.OrdinalIgnoreCase))
                {
                    releasesJsonUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (releasesJsonUrl != null)
            {
                _logger.Info($"Found releases.win.json in {tagName}: {releasesJsonUrl}");
                
                try
                {
                    var jsonContent = await httpClient.GetStringAsync(releasesJsonUrl);
                    _logger.Info($"releases.win.json content length: {jsonContent.Length} chars");
                    
                    // Парсим и анализируем содержимое
                    var releasesDoc = JsonDocument.Parse(jsonContent);
                    if (releasesDoc.RootElement.TryGetProperty("releases", out var releasesArray))
                    {
                        var releasesCount = releasesArray.GetArrayLength();
                        _logger.Info($"releases.win.json contains {releasesCount} release entries");
                        
                        var currentVersion = Version.Parse(CurrentVersion);
                        foreach (var releaseEntry in releasesArray.EnumerateArray())
                        {
                            if (releaseEntry.TryGetProperty("version", out var versionProp))
                            {
                                var versionString = versionProp.GetString() ?? "";
                                if (Version.TryParse(versionString, out var releaseVersion))
                                {
                                    var isNewer = releaseVersion > currentVersion;
                                    _logger.Info($"  Release entry: v{versionString} (newer: {isNewer})");
                                }
                            }
                        }
                    }
                    else
                    {
                        _logger.Warning("releases.win.json does not contain 'releases' array property");
                        _logger.Debug($"releases.win.json root properties: {string.Join(", ", releasesDoc.RootElement.EnumerateObject().Select(p => p.Name))}");
                    }
                }
                catch (Exception jsonEx)
                {
                    _logger.Error($"Failed to read/parse releases.win.json: {jsonEx.Message}");
                }
            }
            else
            {
                _logger.Warning($"releases.win.json NOT FOUND in {tagName} - this is why Velopack fails!");
                _logger.Warning("Velopack requires releases.win.json file, not just RELEASES");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error checking releases.win.json content: {ex.Message}");
        }
    }

    private async Task<bool> CheckForUpdatesViaGitHubAPIAsync(string channel)
    {
        try
        {
            const string repoOwner = "RaspizDIYs";
            const string repoName = "lol-manager";
            
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30); // Таймаут 30 секунд
            httpClient.DefaultRequestHeaders.Add("User-Agent", "LolManager-UpdateCheck");
            
            var url = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases";
            _logger.Info($"Making request to: {url}");
            
            string response;
            try
            {
                response = await httpClient.GetStringAsync(url);
                if (string.IsNullOrEmpty(response))
                {
                    _logger.Warning("GitHub API returned empty response");
                    return false;
                }
            }
            catch (HttpRequestException httpEx)
            {
                _logger.Error($"HTTP error checking GitHub API: {httpEx.Message}");
                return false;
            }
            catch (TaskCanceledException timeoutEx)
            {
                _logger.Error($"Timeout checking GitHub API: {timeoutEx.Message}");
                return false;
            }
            JsonDocument releases;
            try
            {
                releases = System.Text.Json.JsonDocument.Parse(response);
            }
            catch (JsonException jsonEx)
            {
                _logger.Error($"Failed to parse GitHub API JSON response: {jsonEx.Message}");
                return false;
            }
            
            Version currentVersion;
            try
            {
                currentVersion = Version.Parse(CurrentVersion);
                _logger.Info($"Comparing with current version: {currentVersion}");
            }
            catch (Exception versionEx)
            {
                _logger.Error($"Failed to parse current version '{CurrentVersion}': {versionEx.Message}");
                return false;
            }
            
            foreach (var release in releases.RootElement.EnumerateArray())
            {
                try
                {
                    // Безопасное извлечение свойств из JSON
                    var tagName = release.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
                    var isPrerelease = release.TryGetProperty("prerelease", out var preProp) && preProp.GetBoolean();
                    var isDraft = release.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean();
                    
                    if (string.IsNullOrEmpty(tagName))
                    {
                        _logger.Warning("Release has empty or missing tag_name, skipping");
                        continue;
                    }
                
                // Пропускаем драфты
                if (isDraft) continue;
                
                // Проверяем канал
                if (channel == "stable" && isPrerelease) continue;
                
                // Парсим версию из тега (убираем 'v' если есть)
                var versionString = tagName.StartsWith("v") ? tagName.Substring(1) : tagName;
                
                if (Version.TryParse(versionString, out var releaseVersion))
                {
                    try
                    {
                        // Специальная логика сравнения версий для разных каналов
                        bool isNewerVersion = false;
                        
                        if (channel == "stable")
                        {
                            // Для stable канала: только Major.Minor.Build больше текущей
                            // Защищаемся от ArgumentOutOfRangeException если Version имеет меньше компонентов
                            var releaseComponents = new int[] {
                                releaseVersion.Major,
                                Math.Max(0, releaseVersion.Minor),
                                Math.Max(0, releaseVersion.Build >= 0 ? releaseVersion.Build : 0)
                            };
                            
                            var currentComponents = new int[] {
                                currentVersion.Major,
                                Math.Max(0, currentVersion.Minor),
                                Math.Max(0, currentVersion.Build >= 0 ? currentVersion.Build : 0)
                            };
                            
                            var stableReleaseVersion = new Version(releaseComponents[0], releaseComponents[1], releaseComponents[2]);
                            var stableCurrentVersion = new Version(currentComponents[0], currentComponents[1], currentComponents[2]);
                            isNewerVersion = stableReleaseVersion > stableCurrentVersion;
                        }
                        else
                        {
                            // Для beta канала: любая версия больше текущей
                            isNewerVersion = releaseVersion > currentVersion;
                        }
                        
                        if (isNewerVersion)
                        {
                            _logger.Info($"Found newer version: {releaseVersion} > {currentVersion} (channel: {channel})");
                            return true;
                        }
                    }
                    catch (Exception versionEx)
                    {
                        _logger.Warning($"Error comparing versions {releaseVersion} vs {currentVersion}: {versionEx.Message}");
                        continue; // Переходим к следующей версии
                    }
                }
                else
                {
                    _logger.Warning($"Could not parse version from tag: {tagName}");
                }
                }
                catch (Exception releaseEx)
                {
                    _logger.Warning($"Error processing release: {releaseEx.Message}");
                    continue; // Переходим к следующему релизу
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to check updates via GitHub API: {ex.Message}");
            return false;
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
