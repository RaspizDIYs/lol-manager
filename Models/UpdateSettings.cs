namespace LolManager.Models;

public class UpdateSettings
{
    public bool AutoUpdateEnabled { get; set; } = true;
    public string UpdateChannel { get; set; } = "stable";
    public int CheckIntervalHours { get; set; } = 24;
    public DateTime LastCheckTime { get; set; } = DateTime.MinValue;
    public bool SkipVersion { get; set; } = false;
    public string SkippedVersion { get; set; } = string.Empty;
}
