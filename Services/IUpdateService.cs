namespace LolManager.Services;

public interface IUpdateService
{
    string CurrentVersion { get; }
    Task<bool> CheckForUpdatesAsync(bool forceCheck = false);
    Task<bool> UpdateAsync();
    Task<string> GetChangelogAsync();
    void RefreshUpdateSource();
}
