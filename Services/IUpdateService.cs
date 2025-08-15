namespace LolManager.Services;

public interface IUpdateService
{
    string CurrentVersion { get; }
    Task<bool> CheckForUpdatesAsync();
    Task<bool> UpdateAsync();
    Task<string> GetChangelogAsync();
}
