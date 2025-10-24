namespace LolManager.Services;

public interface IUpdateService
{
    string CurrentVersion { get; }
    string? LatestAvailableVersion { get; }
    Task<bool> CheckForUpdatesAsync(bool forceCheck = false);
    Task<bool> UpdateAsync();
    Task<string> GetChangelogAsync();
    void RefreshUpdateSource();
    Task ShowUpdateNotificationAsync(string version);
    void ValidateUserDataAfterUpdate();
    void CleanupInstallerCache();
}
