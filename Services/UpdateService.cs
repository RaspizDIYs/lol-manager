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
    public string? LatestAvailableVersion { get; private set; }

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
            LatestAvailableVersion = null;
            var checkType = forceCheck ? "manual" : "automatic";
            _logger.Info($"Starting {checkType} update check...");
            
            // Не используем Velopack/GitHub API для детекта

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
            
            // Проверка без API: через прямые assets/atom
            // Режим Velopack: используем менеджер, если выбран
            if (settings.UpdateMode == "Velopack")
            {
                var um = GetUpdateManager();
                if (um != null)
                {
                    try
                    {
                        var info = await um.CheckForUpdatesAsync();
                        if (info != null)
                        {
                            LatestAvailableVersion = info.TargetFullRelease?.Version?.ToString();
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Velopack check failed, fallback to direct: {ex.Message}");
                    }
                }
            }

            if (settings.UpdateChannel == "stable")
            {
                var stableTag = await TryGetLatestStableFromAtomAsync();
                if (!string.IsNullOrEmpty(stableTag) && IsStableTagNewerThanCurrentBase(stableTag!))
                {
                    LatestAvailableVersion = stableTag;
                    _logger.Info($"Found newer (stable via atom): {stableTag}");
                    return true;
                }
            }
            else // beta
            {
                var latestBetaTag = await TryGetLatestBetaFromAtomAsync();
                if (!string.IsNullOrEmpty(latestBetaTag))
                {
                    if (IsTagNewerThanCurrentForBeta(latestBetaTag!))
                    {
                        LatestAvailableVersion = latestBetaTag;
                        _logger.Info($"Found newer (beta via atom): {latestBetaTag}");
                        return true;
                    }
                    _logger.Info($"Beta (via atom) not newer than current: {latestBetaTag} vs {CurrentVersion}");
                }
            }
            
            // Диагностический метод выключен по умолчанию
            // await CheckGitHubReleasesDirectlyAsync(settings.UpdateChannel);
            
            _logger.Info("No updates available via direct atom");
            
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
            
            var settings = _settingsService.LoadUpdateSettings();

            // Если выбран Velopack — попытка обновиться через него
            if (settings.UpdateMode == "Velopack")
            {
                var um = GetUpdateManager();
                if (um != null)
                {
                    try
                    {
                        var info = await um.CheckForUpdatesAsync();
                        if (info != null)
                        {
                            await um.DownloadUpdatesAsync(info);
                            RegisterDataValidationAfterUpdate();
                            um.ApplyUpdatesAndRestart(info.TargetFullRelease);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Velopack update failed, fallback to direct: {ex.Message}");
                    }
                }
            }

            // Полный отказ от Velopack для скачивания: качаем установщик напрямую
            var latestStableTag = await TryGetLatestStableFromAtomAsync();
            if (string.IsNullOrEmpty(latestStableTag))
            {
                if (settings.UpdateChannel == "beta")
                {
                    var betaTag = await TryGetLatestBetaFromAtomAsync();
                    if (IsTagNewerThanCurrentForBeta(betaTag))
                    {
                        // для beta открываем страницу релизов
                        _ = OpenReleasesPage();
                        return true;
                    }
                }
                _logger.Info("No update available via assets/atom");
                return false;
            }
            if (IsStableTagNewerThanCurrentBase(latestStableTag!))
            {
                // Скачиваем инсталлятор напрямую и запускаем
                var ok = await DownloadAndRunStableInstallerAsync();
                if (ok)
                {
                    RegisterDataValidationAfterUpdate();
                    return true;
                }
            }
            else if (_settingsService.LoadUpdateSettings().UpdateChannel == "beta")
            {
                var betaTag = await TryGetLatestBetaFromAtomAsync();
                if (IsTagNewerThanCurrentForBeta(betaTag))
                {
                    var ok = await DownloadAndRunBetaInstallerAsync(betaTag!);
                    if (ok)
                    {
                        RegisterDataValidationAfterUpdate();
                        return true;
                    }
                    else
                    {
                        _ = OpenReleasesPage();
                        return true;
                    }
                }
            }
            return false;
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
            var latestSetupUrl = "https://github.com/RaspizDIYs/lol-manager/releases/latest/download/LolManager-stable-Setup.exe";
            var tempPath = Path.Combine(Path.GetTempPath(), "LolManager-stable-Setup.exe");
            _logger.Info($"Downloading installer: {latestSetupUrl}");
            var data = await HttpGetBytesWithRetry(latestSetupUrl, TimeSpan.FromMinutes(2));
            await File.WriteAllBytesAsync(tempPath, data);
            _logger.Info($"Installer saved: {tempPath}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to download/run stable installer: {ex.Message}");
        }
        return false;
    }

    private async Task<bool> DownloadAndRunBetaInstallerAsync(string tag)
    {
        try
        {
            var normalizedTag = tag.StartsWith("v") ? tag : $"v{tag}";
            var latestSetupUrl = $"https://github.com/RaspizDIYs/lol-manager/releases/download/{normalizedTag}/LolManager-beta-Setup.exe";
            var tempPath = Path.Combine(Path.GetTempPath(), "LolManager-beta-Setup.exe");
            _logger.Info($"Downloading beta installer: {latestSetupUrl}");
            var data = await HttpGetBytesWithRetry(latestSetupUrl, TimeSpan.FromMinutes(2));
            await File.WriteAllBytesAsync(tempPath, data);
            _logger.Info($"Installer saved: {tempPath}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to download/run beta installer: {ex.Message}");
            return false;
        }
    }



    public async Task<string> GetChangelogAsync()
    {
        try
        {
            _logger.Info("Getting changelog...");
            
            // Получаем текущий канал пользователя
            var settings = _settingsService.LoadUpdateSettings();
            
            // Сначала пытаемся получить changelog из GitHub API с фильтрацией по каналу
            var atomChangelog = await GetChangelogFromAtomAsync(settings.UpdateChannel);
            if (!string.IsNullOrEmpty(atomChangelog))
            {
                _logger.Info($"Got Atom changelog, length: {atomChangelog.Length}");
                return atomChangelog;
            }
            
            _logger.Warning("GitHub changelog is empty, trying Velopack...");

            // No-API changelog через Atom
            var md = await GetChangelogFromAtomAsync(settings.UpdateChannel);
            if (!string.IsNullOrWhiteSpace(md)) return md;
            return GetDefaultChangelog();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get changelog: {ex.Message}");
            _logger.Debug($"Changelog exception details: {ex}");
            return GetDefaultChangelog();
        }
    }

    // API changelog removed; using Atom instead (see GetChangelogFromAtomAsync)

    private async Task CheckGitHubReleasesDirectlyAsync(string channel)
    {
        // Диагностика через API удалена в no-API режиме
        await Task.CompletedTask;
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
            try
            {
                var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
                if (string.IsNullOrWhiteSpace(token))
                {
                    var st = _settingsService.LoadUpdateSettings();
                    if (!string.IsNullOrWhiteSpace(st.GithubToken)) token = st.GithubToken;
                }
                if (!string.IsNullOrWhiteSpace(token))
                {
                    httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", token);
                }
            }
            catch { }
            
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
                            LatestAvailableVersion = tagName; // показываем как на GitHub
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

    private static Version? ParseVersionFromReleasesLine(string line)
    {
        // Формат строки RELEASES: SHA1 SIZE FILENAME.nupkg
        // Имя файла содержит версию, например LolManager-0.2.4-full.nupkg
        try
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return null;
            var fileName = parts[2];
            var name = Path.GetFileNameWithoutExtension(fileName);
            // ищем подстроку с версией после последнего '-'
            var lastDash = name.LastIndexOf('-');
            if (lastDash < 0) return null;
            var after = name.Substring(lastDash + 1); // может быть 0.2.4-full
            var versionPart = after.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(versionPart)) return null;
            if (Version.TryParse(versionPart, out var v)) return v;
        }
        catch { }
        return null;
    }

    // Стабильная версия из Atom (так как RELEASES не публикуется)
    private async Task<string?> TryGetLatestStableFromAtomAsync()
    {
        try
        {
            var url = "https://github.com/RaspizDIYs/lol-manager/releases.atom";
            var xml = await HttpGetStringWithRetry(url, TimeSpan.FromSeconds(15));
            
            // Парсим <id> теги из atom feed: <id>tag:github.com,2008:Repository/xxx/v0.2.9</id>
            var ids = System.Text.RegularExpressions.Regex.Matches(xml, @"<id>tag:github\.com,\d+:Repository/\d+/(v[\d\.\-\w]+)</id>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Select(m => m.Groups[1].Value)
                .Where(t => !t.Contains("-beta.", StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            var result = ids.FirstOrDefault()?.Trim();
            _logger.Info($"Latest stable from atom: {result ?? "null"}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to parse stable from atom: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> TryGetLatestBetaFromAtomAsync()
    {
        try
        {
            var url = "https://github.com/RaspizDIYs/lol-manager/releases.atom";
            var xml = await HttpGetStringWithRetry(url, TimeSpan.FromSeconds(15));
            
            // Парсим <id> теги из atom feed: <id>tag:github.com,2008:Repository/xxx/v0.2.9-beta.1</id>
            var ids = System.Text.RegularExpressions.Regex.Matches(xml, @"<id>tag:github\.com,\d+:Repository/\d+/(v[\d\.\-\w]+)</id>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Select(m => m.Groups[1].Value)
                .Where(t => t.Contains("-beta.", StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            var result = ids.FirstOrDefault()?.Trim();
            _logger.Info($"Latest beta from atom: {result ?? "null"}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to parse releases.atom: {ex.Message}");
            return null;
        }
    }

    private bool IsStableTagNewerThanCurrentBase(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            _logger.Info("IsStableTagNewerThanCurrentBase: tag is null or empty");
            return false;
        }
        
        var versionStr = tag.StartsWith("v") ? tag.Substring(1) : tag;
        var currentBaseStr = CurrentVersion.Contains("-beta.") ? CurrentVersion.Split("-beta.")[0] : CurrentVersion;
        
        _logger.Info($"Comparing versions: remote '{versionStr}' vs current '{currentBaseStr}'");
        
        if (Version.TryParse(versionStr, out var rel) && Version.TryParse(currentBaseStr, out var cur))
        {
            var isNewer = rel > cur;
            _logger.Info($"Version comparison result: {rel} > {cur} = {isNewer}");
            return isNewer;
        }
        
        _logger.Warning($"Failed to parse versions: remote '{versionStr}' or current '{currentBaseStr}'");
        return false;
    }

    private bool IsTagNewerThanCurrentForBeta(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var versionString = tag.StartsWith("v") ? tag.Substring(1) : tag;
        var isPrerelease = versionString.Contains("-beta.");
        var current = CurrentVersion;
        var currentBase = current.Contains("-beta.") ? current.Split("-beta.")[0] : current;
        if (isPrerelease && versionString.Contains("-beta."))
        {
            var parts = versionString.Split("-beta.");
            if (parts.Length >= 2 && int.TryParse(parts[1], out var betaNum))
            {
                if (parts[0] == currentBase)
                {
                    // сравнение номера беты
                    var curBeta = 0;
                    if (current.Contains("-beta.") && int.TryParse(current.Split("-beta.")[1], out var cb)) curBeta = cb;
                    return betaNum > curBeta;
                }
                // более новая базовая версия в бете
                if (Version.TryParse(parts[0], out var baseVer) && Version.TryParse(currentBase, out var curBase))
                    return baseVer > curBase;
            }
        }
        else
        {
            // стаб тэг новее — тоже считаем апдейтом для beta канала
            if (Version.TryParse(versionString, out var stableVer) && Version.TryParse(currentBase, out var curBase))
                return stableVer > curBase;
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

    public void CleanupInstallerCache()
    {
        try
        {
            var temp = Path.GetTempPath();
            foreach (var name in new[] { "LolManager-stable-Setup.exe", "LolManager-beta-Setup.exe" })
            {
                var path = Path.Combine(temp, name);
                if (File.Exists(path))
                {
                    try { File.Delete(path); _logger.Info($"Deleted cached installer: {path}"); } catch { }
                }
            }
        }
        catch { }
    }

    private async Task OpenReleasesPage()
    {
        try
        {
            var tag = LatestAvailableVersion?.Trim();
            var url = !string.IsNullOrEmpty(tag)
                ? $"https://github.com/RaspizDIYs/lol-manager/releases/tag/{tag}"
                : "https://github.com/RaspizDIYs/lol-manager/releases";
            _logger.Info($"Opening releases page: {url}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to open releases page: {ex.Message}");
        }
    }

    private async Task<string?> GetChangelogFromAtomAsync(string channel)
    {
        try
        {
            var url = "https://github.com/RaspizDIYs/lol-manager/releases.atom";
            var xml = await HttpGetStringWithRetry(url, TimeSpan.FromSeconds(15));

            // Простой парсинг: ищем <title>vX.Y.Z-beta.N</title>
            var titles = System.Text.RegularExpressions.Regex.Matches(xml, @"<title>(.*?)</title>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value))
                .Where(t => t.Contains("-beta.", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Первый заголовок после общего заголовка канала — обычно последний релиз
            foreach (var t in titles)
            {
                // Отбрасываем общий title канала Releases · owner/repo
                if (t.Contains("Releases ·", StringComparison.OrdinalIgnoreCase)) continue;
                // Возвращаем первый beta-тег
                return t.Trim();
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to parse releases.atom: {ex.Message}");
            return null;
        }
    }

    private string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", string.Empty);
    }

    private async Task<string> HttpGetStringWithRetry(string url, TimeSpan timeout, int maxAttempts = 3)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "LolManager-UpdateCheck");
                var resp = await http.GetAsync(url, cts.Token);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync(cts.Token);
            }
            catch (Exception ex)
            {
                last = ex;
                var delay = TimeSpan.FromSeconds(Math.Min(10, Math.Pow(2, attempt)));
                _logger.Warning($"GET failed ({attempt}/{maxAttempts}) for {url}: {ex.Message}. Retry in {delay.TotalSeconds}s");
                await Task.Delay(delay);
            }
        }
        throw new HttpRequestException($"Failed to GET {url} after {maxAttempts} attempts", last);
    }

    private async Task<byte[]> HttpGetBytesWithRetry(string url, TimeSpan timeout, int maxAttempts = 3)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "LolManager-UpdateCheck");
                var resp = await http.GetAsync(url, cts.Token);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync(cts.Token);
            }
            catch (Exception ex)
            {
                last = ex;
                var delay = TimeSpan.FromSeconds(Math.Min(20, Math.Pow(2, attempt)));
                _logger.Warning($"GET bytes failed ({attempt}/{maxAttempts}) for {url}: {ex.Message}. Retry in {delay.TotalSeconds}s");
                await Task.Delay(delay);
            }
        }
        throw new HttpRequestException($"Failed to GET bytes {url} after {maxAttempts} attempts", last);
    }
}


