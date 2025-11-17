namespace LolManager.Models;

public class ClientConnectivityStatus
{
    public bool IsRiotClientRunning { get; set; }
    public bool IsLeagueRunning { get; set; }
    public bool RcLockfileFound { get; set; }
    public bool LcuLockfileFound { get; set; }
    public int? LcuPort { get; set; }
    public bool LcuHttpOk { get; set; }
    public string? LcuLockfilePath { get; set; }
    public string? LeagueInstallPath { get; set; }
}


