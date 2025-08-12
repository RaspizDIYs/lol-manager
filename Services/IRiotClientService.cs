using System.Threading.Tasks;

namespace LolManager.Services;

public interface IRiotClientService
{
    Task LoginAsync(string username, string password);
    Task KillLeagueAsync(bool includeRiotClient);
    Task StartLeagueAsync();
    Task RestartLeagueAsync(bool includeRiotClient);
    Task LogoutAsync();
    Task StartRiotClientAsync();
    Task RestartRiotClientAsync();
}


