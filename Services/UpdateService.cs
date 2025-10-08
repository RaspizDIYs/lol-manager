using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Velopack;
using Velopack.Sources;
using LolManager.Models;
using LolManager.Services;

namespace LolManager.Services;

public class UpdateService : IUpdateService
{
    private readonly ILogger _logger;
    private readonly ISettingsService _settingsService;
    private readonly NotificationService _notificationService;
    private UpdateManager? _updateManager;

    public string CurrentVersion 
    { 
        get 
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            
            // Обрезаем Git metadata (+commit_hash) но оставляем beta суффикс
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
        _notificationService = new NotificationService();
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
            
            // Используем стандартный GithubSource - файлы releases.{channel}.json будут проверяться отдельно
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
            
            // Интервал проверки отключен - проверяем при каждом запуске приложения
            // Оставляем логику только для принудительной проверки vs автоматической
            if (forceCheck)
            {
                _logger.Info("Force check - manual update check requested");
            }
            else
            {
                _logger.Info("Automatic check - checking for updates on startup");
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
            // Создаем резервные копии пользовательских данных перед обновлением
            BackupUserData();
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
            
            // Создаем аргументы для ApplyUpdatesAndRestart для сохранения пользовательских данных
            var extraArgs = new List<string>();
            
            // Защита от удаления пользовательских данных в AppData
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var roamingLolManagerDir = Path.Combine(appDataPath, "LolManager");
            extraArgs.Add($"--keepalive={roamingLolManagerDir}");
            _logger.Info($"Added protection for user data directory: {roamingLolManagerDir}");
            
            // Защита от удаления пользовательских данных в LocalAppData
            var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var localLolManagerDir = Path.Combine(localAppDataPath, "LolManager");
            extraArgs.Add($"--keepalive={localLolManagerDir}");
            _logger.Info($"Added protection for settings directory: {localLolManagerDir}");
            
            // Применяем обновление. При необходимости даунгрейда (когда версия ниже) Velopack применит полный пакет.
            // Запускаем проверку пользовательских данных после перезапуска приложения
            RegisterDataValidationAfterUpdate();
            
            // Применяем обновление и перезапускаем приложение
            updateManager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to update: {ex.Message}");
            return false;
        }
    }

    public async Task<Version?> GetLatestStableVersionAsync()
    {
        try
        {
            const string repoOwner = "RaspizDIYs";
            const string repoName = "lol-manager";
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "LolManager-UpdateCheck");
            var url = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases";
            var response = await httpClient.GetStringAsync(url);
            var releases = JsonDocument.Parse(response);
            foreach (var release in releases.RootElement.EnumerateArray())
            {
                var isPrerelease = release.GetProperty("prerelease").GetBoolean();
                var isDraft = release.GetProperty("draft").GetBoolean();
                if (isDraft || isPrerelease) continue;
                var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(tag)) continue;
                var versionStr = tag.StartsWith("v") ? tag.Substring(1) : tag;
                if (Version.TryParse(versionStr, out var v))
                    return v;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get latest stable version: {ex.Message}");
        }
        return null;
    }

    public async Task<bool> ForceDowngradeToStableAsync()
    {
        try
        {
            var latestStable = await GetLatestStableVersionAsync();
            if (latestStable == null)
            {
                _logger.Error("Cannot find latest stable version for downgrade");
                return false;
            }

            // Текущая версия приложения (с возможным -beta.N)
            var fullCurrent = CurrentVersion;
            string currentBaseStr = fullCurrent.Contains("-beta.")
                ? fullCurrent.Split("-beta.")[0]
                : fullCurrent;

            if (!Version.TryParse(currentBaseStr, out var currentBase))
            {
                _logger.Error($"Failed to parse current base version: '{currentBaseStr}'");
                return false;
            }

            _logger.Info($"Force downgrade check: currentBase={currentBase}, latestStable={latestStable}");

            if (latestStable <= currentBase)
            {
                // На той же или более новой базовой версии — инсталлятор покажет 'та же версия'. Не запускаем.
                _logger.Info("Stable channel selected but latest stable is not greater than current base version. Skipping installer to avoid 'same version' message.");
                return false;
            }

            var settings = _settingsService.LoadUpdateSettings();
            settings.UpdateChannel = "stable";
            _settingsService.SaveUpdateSettings(settings);
            _updateManager = null; // перезагрузим источник на stable

            var updateManager = GetUpdateManager();
            if (updateManager == null)
            {
                _logger.Error("UpdateManager not available for downgrade");
                return false;
            }

            _logger.Info($"Attempting move to newer stable {latestStable} from base {currentBase}");

            var updateInfo = await updateManager.CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                _logger.Warning("No updateInfo available on stable. Using installer fallback to upgrade to newer stable.");
                return await DownloadAndRunStableInstallerAsync();
            }

            try
            {
                await updateManager.DownloadUpdatesAsync(updateInfo);
                
                // Создаем аргументы для защиты пользовательских данных при даунгрейде
                var extraArgs = new List<string>();
                
                // Защита от удаления пользовательских данных в AppData
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var roamingLolManagerDir = Path.Combine(appDataPath, "LolManager");
                extraArgs.Add($"--keepalive={roamingLolManagerDir}");
                _logger.Info($"Added protection for user data directory during downgrade: {roamingLolManagerDir}");
                
                // Защита от удаления пользовательских данных в LocalAppData
                var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var localLolManagerDir = Path.Combine(localAppDataPath, "LolManager");
                extraArgs.Add($"--keepalive={localLolManagerDir}");
                _logger.Info($"Added protection for settings directory during downgrade: {localLolManagerDir}");
                
                // Запускаем проверку пользовательских данных после перезапуска приложения
                RegisterDataValidationAfterUpdate();
                
                // Применяем обновление и перезапускаем приложение
                updateManager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
                return true;
            }
            catch (Exception)
            {
                _logger.Warning("Velopack apply failed during move to stable, using installer fallback");
                return await DownloadAndRunStableInstallerAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Force downgrade failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> DownloadAndRunStableInstallerAsync()
    {
        try
        {
            const string repoOwner = "RaspizDIYs";
            const string repoName = "lol-manager";
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "LolManager-Installer");
            var url = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases";
            var response = await http.GetStringAsync(url);
            var releases = JsonDocument.Parse(response);
            foreach (var release in releases.RootElement.EnumerateArray())
            {
                var isPrerelease = release.GetProperty("prerelease").GetBoolean();
                var isDraft = release.GetProperty("draft").GetBoolean();
                if (isDraft || isPrerelease) continue;
                foreach (var asset in release.GetProperty("assets").EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (!name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    var downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    if (string.IsNullOrEmpty(downloadUrl)) continue;

                    var tempPath = Path.Combine(Path.GetTempPath(), name);
                    _logger.Info($"Downloading stable installer: {name}");
                    var data = await http.GetByteArrayAsync(downloadUrl);
                    await File.WriteAllBytesAsync(tempPath, data);
                    _logger.Info($"Installer saved: {tempPath}");

                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = tempPath,
                        UseShellExecute = true
                    });
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to download/run stable installer: {ex.Message}");
        }
        return false;
    }



    public async Task<string> GetChangelogAsync()
    {
        try
        {
            _logger.Info("Getting changelog...");
            
            // Получаем текущий канал пользователя
            var settings = _settingsService.LoadUpdateSettings();
            
            // Сначала пытаемся получить changelog из GitHub API с фильтрацией по каналу
            var githubChangelog = await GetGitHubChangelogAsync(settings.UpdateChannel);
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

    private async Task<string> GetGitHubChangelogAsync(string channel)
    {
        try
        {
            const string repoOwner = "RaspizDIYs";
            const string repoName = "lol-manager";
            
            _logger.Info($"Fetching changelog for channel: {channel}");
            
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "LolManager");
            
            var url = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases";
            var response = await httpClient.GetStringAsync(url);
            
            var releases = System.Text.Json.JsonDocument.Parse(response);
            var changelog = new StringBuilder();
            var includedReleases = 0;
            
            foreach (var release in releases.RootElement.EnumerateArray())
            {
                if (includedReleases >= 10) break; // Берем максимум 10 релизов
                
                var tagName = release.GetProperty("tag_name").GetString() ?? "Unknown";
                var name = release.GetProperty("name").GetString() ?? "";
                var body = release.GetProperty("body").GetString() ?? "Нет описания";
                var isPrerelease = release.GetProperty("prerelease").GetBoolean();
                var isDraft = release.GetProperty("draft").GetBoolean();
                
                // Пропускаем черновики
                if (isDraft) continue;
                
                // Фильтруем по каналу
                if (channel == "stable" && isPrerelease) continue; // Stable канал - только стабильные релизы
                // Beta канал - показываем все релизы (и stable, и prerelease)
                
                var releaseType = isPrerelease ? " (Beta)" : "";
                
                // Форматируем как "версия - название"
                var title = string.IsNullOrEmpty(name) ? tagName : $"{tagName} - {name}";
                
                changelog.AppendLine($"## {title}{releaseType}");
                changelog.AppendLine(body);
                changelog.AppendLine();
                
                includedReleases++;
            }
            
            _logger.Info($"Generated changelog with {includedReleases} releases for channel '{channel}'");
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
            
            // Диагностика наличия Velopack артефактов (RELEASES, full/delta nupkg) и канал-специфичных JSON
            await CheckVelopackArtifactsAsync(httpClient, repoOwner, repoName, releases, channel);
            
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

    private async Task CheckVelopackArtifactsAsync(HttpClient httpClient, string repoOwner, string repoName, JsonDocument releases, string channel)
    {
        try
        {
            var latestRelease = releases.RootElement.EnumerateArray().FirstOrDefault();
            if (latestRelease.ValueKind == JsonValueKind.Undefined)
            {
                _logger.Warning("No releases found to check Velopack artifacts");
                return;
            }

            var tagName = latestRelease.GetProperty("tag_name").GetString() ?? "Unknown";
            var assets = latestRelease.GetProperty("assets");
            
            bool hasReleasesTxt = false;
            bool hasFullNupkg = false;
            bool hasAnyDelta = false;
            string? releasesWinJsonUrl = null;
            string? channelJsonUrl = null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.Equals("RELEASES", StringComparison.OrdinalIgnoreCase)) hasReleasesTxt = true;
                if (name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && name.Contains("full", StringComparison.OrdinalIgnoreCase)) hasFullNupkg = true;
                if (name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && name.Contains("delta", StringComparison.OrdinalIgnoreCase)) hasAnyDelta = true;
                if (name.Equals("releases.win.json", StringComparison.OrdinalIgnoreCase))
                {
                    releasesWinJsonUrl = asset.GetProperty("browser_download_url").GetString();
                }
                // Поддерживаем ваши артефакты каналов: release[s].{channel}.json
                var expected1 = $"release.{channel}.json";
                var expected2 = $"releases.{channel}.json";
                if (name.Equals(expected1, StringComparison.OrdinalIgnoreCase) || name.Equals(expected2, StringComparison.OrdinalIgnoreCase))
                {
                    channelJsonUrl = asset.GetProperty("browser_download_url").GetString();
                }
            }

            _logger.Info($"Velopack artifacts in latest release {tagName}: RELEASES={hasReleasesTxt}, full.nupkg={hasFullNupkg}, delta.nupkg={hasAnyDelta}");

            if (!string.IsNullOrEmpty(releasesWinJsonUrl))
            {
                try
                {
                    var jsonContent = await httpClient.GetStringAsync(releasesWinJsonUrl);
                    _logger.Info($"releases.win.json length: {jsonContent.Length} chars");
                }
                catch (Exception jsonEx)
                {
                    _logger.Warning($"Failed to fetch releases.win.json: {jsonEx.Message}");
                }
            }

            if (!string.IsNullOrEmpty(channelJsonUrl))
            {
                try
                {
                    var jsonContent = await httpClient.GetStringAsync(channelJsonUrl);
                    _logger.Info($"{Path.GetFileName(channelJsonUrl)} length: {jsonContent.Length} chars");
                }
                catch (Exception jsonEx)
                {
                    _logger.Warning($"Failed to fetch {channel} channel json: {jsonEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error checking Velopack artifacts: {ex.Message}");
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
            
            var fullCurrentVersion = CurrentVersion;
            _logger.Info($"Comparing with current version: {fullCurrentVersion}");
            
            // Дополнительная диагностика версии
            var assembly = Assembly.GetExecutingAssembly();
            var rawVersion = assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            _logger.Info($"Raw assembly version: '{rawVersion}', Cleaned: '{fullCurrentVersion}'");
            
            // Определяем текущую базовую версию и beta номер
            string currentBaseVersion;
            int currentBetaNumber = 0;
            bool isCurrentBeta = false;
            
            if (fullCurrentVersion.Contains("-beta."))
            {
                var parts = fullCurrentVersion.Split("-beta.");
                currentBaseVersion = parts[0];
                isCurrentBeta = true;
                if (parts.Length > 1 && int.TryParse(parts[1], out currentBetaNumber))
                {
                    _logger.Info($"Current beta version: {currentBaseVersion} beta #{currentBetaNumber}");
                }
            }
            else
            {
                currentBaseVersion = fullCurrentVersion;
                _logger.Info($"Current stable version: {currentBaseVersion}");
            }
            
            Version currentVersion;
            try
            {
                currentVersion = Version.Parse(currentBaseVersion);
            }
            catch (Exception versionEx)
            {
                _logger.Error($"Failed to parse current base version '{currentBaseVersion}': {versionEx.Message}");
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
                
                // Для beta версий убираем суффикс -beta.XX только для парсинга Version
                string parseableVersion = versionString;
                if (versionString.Contains("-beta."))
                {
                    var betaParts = versionString.Split("-beta.");
                    if (betaParts.Length >= 2)
                    {
                        parseableVersion = betaParts[0]; // Берем только основную версию для парсинга
                    }
                }
                
                if (Version.TryParse(parseableVersion, out var releaseVersion))
                {
                    try
                    {
                        // Новая логика сравнения версий
                        bool isNewerVersion = false;
                        
                        if (channel == "stable")
                        {
                            // Для stable канала: только Major.Minor.Build больше текущей
                            // Защищаемся от ArgumentOutOfRangeException если Version имеет меньше компонентов
                            if (!isPrerelease)
                            {
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
                        }
                        else
                        {
                            // Для beta канала: ищем обновления среди beta версий той же базовой версии
                            if (isPrerelease && versionString.Contains("-beta."))
                            {
                                // Извлекаем базовую версию релиза
                                var releaseParts = versionString.Split("-beta.");
                                if (releaseParts.Length >= 2)
                                {
                                    var releaseBaseVersion = releaseParts[0];
                                    
                                    // Сравниваем beta только в рамках той же базовой версии
                                    if (releaseBaseVersion == currentBaseVersion && int.TryParse(releaseParts[1], out var releaseBetaNum))
                                    {
                                        if (isCurrentBeta)
                                        {
                                            // Текущая тоже beta - сравниваем номера
                                            if (releaseBetaNum > currentBetaNumber)
                                            {
                                                isNewerVersion = true;
                                                _logger.Info($"Found newer beta: {tagName}(#{releaseBetaNum}) > current #{currentBetaNumber}");
                                            }
                                        }
                                        else
                                        {
                                            // Текущая стабильная, beta всегда новее
                                            isNewerVersion = true;
                                            _logger.Info($"Found beta version {tagName}(#{releaseBetaNum}) for stable base {currentBaseVersion}");
                                        }
                                    }
                                    else if (Version.TryParse(releaseBaseVersion, out var releaseBase) && releaseBase > currentVersion)
                                    {
                                        // Beta версия с более новой базовой версией
                                        isNewerVersion = true;
                                        _logger.Info($"Found beta with newer base version: {releaseBaseVersion} > {currentBaseVersion}");
                                    }
                                }
                            }
                            else if (!isPrerelease && releaseVersion > currentVersion)
                            {
                                // Стабильный релиз новее текущей версии
                                isNewerVersion = true;
                                _logger.Info($"Found newer stable version: {releaseVersion} > {currentVersion}");
                            }
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
                    // Логируем только если это может быть релевантная версия
                    if (versionString.Contains("-beta.") || !versionString.StartsWith("0.1.25"))
                    {
                        _logger.Warning($"Could not parse version from tag: {tagName}");
                    }
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

    private bool TryParseBetaNumber(string versionTag, out int betaNumber)
    {
        betaNumber = 0;
        
        if (string.IsNullOrEmpty(versionTag)) return false;
        
        // Ищем паттерн -beta.XX
        var betaIndex = versionTag.IndexOf("-beta.", StringComparison.OrdinalIgnoreCase);
        if (betaIndex < 0) return false;
        
        var betaStart = betaIndex + "-beta.".Length;
        if (betaStart >= versionTag.Length) return false;
        
        var betaString = versionTag.Substring(betaStart);
        
        // Может содержать дополнительные символы после числа, берем только число
        var match = System.Text.RegularExpressions.Regex.Match(betaString, @"^(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out betaNumber))
        {
            return true;
        }
        
        return false;
    }

    // Создает резервные копии пользовательских файлов перед обновлением
    private void BackupUserData()
    {
        try
        {
            _logger.Info("Creating backup of user data before update...");
            
            // Резервное копирование аккаунтов из AppData
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var roamingLolManagerDir = Path.Combine(appDataPath, "LolManager");
            var accountsFilePath = Path.Combine(roamingLolManagerDir, "accounts.json");
            
            if (File.Exists(accountsFilePath))
            {
                var backupFileName = $"accounts.json.backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                var backupPath = Path.Combine(roamingLolManagerDir, backupFileName);
                File.Copy(accountsFilePath, backupPath);
                _logger.Info($"Created accounts backup: {backupPath}");
            }
            
            // Резервное копирование настроек из LocalAppData
            var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var localLolManagerDir = Path.Combine(localAppDataPath, "LolManager");
            var settingsFilePath = Path.Combine(localLolManagerDir, "settings.json");
            var updateSettingsFilePath = Path.Combine(localLolManagerDir, "update-settings.json");
            
            if (File.Exists(settingsFilePath))
            {
                var backupFileName = $"settings.json.backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                var backupPath = Path.Combine(localLolManagerDir, backupFileName);
                File.Copy(settingsFilePath, backupPath);
                _logger.Info($"Created settings backup: {backupPath}");
            }
            
            if (File.Exists(updateSettingsFilePath))
            {
                var backupFileName = $"update-settings.json.backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                var backupPath = Path.Combine(localLolManagerDir, backupFileName);
                File.Copy(updateSettingsFilePath, backupPath);
                _logger.Info($"Created update settings backup: {backupPath}");
            }
            
            _logger.Info("User data backup completed successfully");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to create user data backup: {ex.Message}");
            // Продолжаем обновление, даже если создание резервной копии не удалось
        }
    }
    
    // Регистрирует задачу проверки целостности пользовательских данных после обновления
    private void RegisterDataValidationAfterUpdate()
    {
        try
        {
            _logger.Info("Registering post-update data validation...");
            
            // Создаем флаг для проверки данных после перезапуска
            var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var localLolManagerDir = Path.Combine(localAppDataPath, "LolManager");
            var validationFlagPath = Path.Combine(localLolManagerDir, "validate_after_update");
            
            // Записываем текущую версию для проверки после обновления
            File.WriteAllText(validationFlagPath, CurrentVersion);
            
            _logger.Info($"Validation flag created: {validationFlagPath}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to register data validation: {ex.Message}");
        }
    }
    
    // Проверяет наличие и восстанавливает пользовательские данные, если необходимо
    public void ValidateUserDataAfterUpdate()
    {
        try
        {
            var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var localLolManagerDir = Path.Combine(localAppDataPath, "LolManager");
            var validationFlagPath = Path.Combine(localLolManagerDir, "validate_after_update");
            
            // Проверяем, нужно ли выполнять проверку данных
            if (!File.Exists(validationFlagPath))
            {
                return; // Нет флага, проверка не требуется
            }
            
            _logger.Info("Performing post-update data validation...");
            
            // Удаляем флаг проверки, чтобы не проверять повторно
            string previousVersion = File.ReadAllText(validationFlagPath);
            File.Delete(validationFlagPath);
            
            _logger.Info($"Update detected: {previousVersion} -> {CurrentVersion}");
            
            // Проверяем наличие файлов данных
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var roamingLolManagerDir = Path.Combine(appDataPath, "LolManager");
            var accountsFilePath = Path.Combine(roamingLolManagerDir, "accounts.json");
            
            var updateSettingsFilePath = Path.Combine(localLolManagerDir, "update-settings.json");
            
            if (!Directory.Exists(roamingLolManagerDir))
            {
                _logger.Warning("User data directory not found, creating...");
                Directory.CreateDirectory(roamingLolManagerDir);
            }
            
            if (!Directory.Exists(localLolManagerDir))
            {
                _logger.Warning("Settings directory not found, creating...");
                Directory.CreateDirectory(localLolManagerDir);
            }
            
            // Проверяем наличие файла аккаунтов
            if (!File.Exists(accountsFilePath))
            {
                _logger.Warning("Accounts file not found, looking for backup...");
                
                // Ищем последнюю резервную копию
                var backupFiles = Directory.GetFiles(roamingLolManagerDir, "accounts.json.backup_*");
                if (backupFiles.Length > 0)
                {
                    // Сортируем по дате (от новых к старым)
                    Array.Sort(backupFiles);
                    Array.Reverse(backupFiles);
                    
                    // Восстанавливаем из самой свежей копии
                    File.Copy(backupFiles[0], accountsFilePath);
                    _logger.Info($"Restored accounts from backup: {backupFiles[0]}");
                }
            }
            
            // Проверяем наличие файла настроек обновления
            if (!File.Exists(updateSettingsFilePath))
            {
                _logger.Warning("Update settings file not found, looking for backup...");
                
                // Ищем последнюю резервную копию
                var backupFiles = Directory.GetFiles(localLolManagerDir, "update-settings.json.backup_*");
                if (backupFiles.Length > 0)
                {
                    // Сортируем по дате (от новых к старым)
                    Array.Sort(backupFiles);
                    Array.Reverse(backupFiles);
                    
                    // Восстанавливаем из самой свежей копии
                    File.Copy(backupFiles[0], updateSettingsFilePath);
                    _logger.Info($"Restored update settings from backup: {backupFiles[0]}");
                }
                else
                {
                    // Создаем настройки по умолчанию
                    _settingsService.SaveUpdateSettings(new Models.UpdateSettings());
                    _logger.Info("Created default update settings");
                }
            }
            
            _logger.Info("Post-update data validation completed");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to validate user data after update: {ex.Message}");
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

    public async Task ShowUpdateNotificationAsync(string version)
    {
        try
        {
            await _notificationService.ShowUpdateNotificationAsync(version, 
                downloadAction: async () => await UpdateAsync(),
                dismissAction: () => _logger.Info("Update notification dismissed"));
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to show update notification: {ex.Message}");
        }
    }
}


