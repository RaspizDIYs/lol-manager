namespace LolManager.Models;

public class LeagueClientInfo
{
    public string? InstallDirectory { get; set; }
    public string? LockfilePath { get; set; }
    public int? Port { get; set; }
    public string? Password { get; set; }
    public string Protocol { get; set; } = "https";
    public int? LeagueClientUxPid { get; set; }
    public string? CommandLine { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}




