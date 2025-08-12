namespace LolManager.Services;

public interface IUiAutomationService
{
    bool FocusRiotClient();
    bool TryLogin(string username, string password);
}


