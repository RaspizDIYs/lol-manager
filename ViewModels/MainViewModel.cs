using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LolManager.Models;
using LolManager.Services;

namespace LolManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAccountsStorage _accountsStorage;
    private readonly IRiotClientService _riotClientService;
    private readonly ILogger _logger;

    public ObservableCollection<AccountRecord> Accounts { get; } = new();

	[ObservableProperty]
	private bool isNavExpanded;

	[ObservableProperty]
	private int selectedTabIndex;

    [ObservableProperty]
    private string newUsername = string.Empty;

    public string NewPassword { get; set; } = string.Empty;

    [ObservableProperty]
    private AccountRecord? selectedAccount;

	public MainViewModel()
		: this(new AccountsStorage(), new RiotClientService(), new FileLogger())
    {
    }

    public MainViewModel(IAccountsStorage accountsStorage, IRiotClientService riotClientService, ILogger logger)
    {
        _accountsStorage = accountsStorage;
        _riotClientService = riotClientService;
        _logger = logger;

        foreach (var acc in _accountsStorage.LoadAll())
            Accounts.Add(acc);

		SelectedTabIndex = 0;
    }

	[RelayCommand]
	private void ToggleNav()
	{
		IsNavExpanded = !IsNavExpanded;
	}

	[RelayCommand]
	private void OpenAccounts()
	{
		SelectedTabIndex = 0;
	}

	[RelayCommand]
	private void OpenSettings()
	{
		SelectedTabIndex = 1;
	}

    [RelayCommand]
    private void AddAccount()
    {
        if (string.IsNullOrWhiteSpace(NewUsername) || string.IsNullOrWhiteSpace(NewPassword)) return;
        var created = new AccountRecord
        {
            Username = NewUsername.Trim(),
            EncryptedPassword = _accountsStorage.Protect(NewPassword),
            CreatedAt = DateTime.Now
        };
        _accountsStorage.Save(created);
        Accounts.Add(created);
        NewUsername = string.Empty;
        NewPassword = string.Empty;
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedAccount is null) return;
        _accountsStorage.Delete(SelectedAccount.Username);
        Accounts.Remove(SelectedAccount);
        SelectedAccount = null;
    }

    [RelayCommand]
    private async Task LoginSelected()
    {
        if (SelectedAccount is null) return;
        var password = _accountsStorage.Unprotect(SelectedAccount.EncryptedPassword);

        try
        {
            _logger.Info($"LCU login flow start for {SelectedAccount.Username}");
            try { await _riotClientService.LogoutAsync(); } catch { }
            try { await _riotClientService.KillLeagueAsync(includeRiotClient: false); } catch { }
            await _riotClientService.LoginAsync(SelectedAccount.Username, password);
        }
        catch (Exception ex)
        {
            _logger.Error($"Login error for {SelectedAccount.Username}: {ex}");
            MessageBox.Show($"Ошибка входа: {ex.Message}\nЛоги: {_logger.LogFilePath}");
        }
    }

    [RelayCommand]
    private void OpenLogs()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _logger.LogFilePath, UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    private async Task RestartWithoutLogin()
    {
        try
        {
            _logger.Info("RestartWithoutLogin requested");
            await _riotClientService.RestartLeagueAsync(includeRiotClient: false);
        }
        catch (Exception ex)
        {
            _logger.Error($"RestartWithoutLogin error: {ex}");
            MessageBox.Show($"Ошибка перезапуска: {ex.Message}\nЛоги: {_logger.LogFilePath}");
        }
    }

    [RelayCommand]
    private async Task ReLogin()
    {
        if (SelectedAccount is null) return;
        try
        {
            var password = _accountsStorage.Unprotect(SelectedAccount.EncryptedPassword);
            _logger.Info($"ReLogin requested for {SelectedAccount.Username}");
            await _riotClientService.KillLeagueAsync(includeRiotClient: true);
            await _riotClientService.StartLeagueAsync();
            _ = Task.Run(async () =>
            {
                try { await _riotClientService.LoginAsync(SelectedAccount.Username, password); }
                catch (Exception ex) { _logger.Error($"ReLogin LCU path failed: {ex.Message}"); }
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"ReLogin error: {ex}");
            MessageBox.Show($"Ошибка перезахода: {ex.Message}\nЛоги: {_logger.LogFilePath}");
        }
    }

    [RelayCommand]
    private async Task Logout()
    {
        try
        {
            _logger.Info("Logout requested");
            await _riotClientService.LogoutAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"Logout error: {ex}");
            MessageBox.Show($"Ошибка выхода: {ex.Message}\nЛоги: {_logger.LogFilePath}");
        }
    }

	[RelayCommand]
	private async Task GenerateLcuDocs()
	{
		try
		{
			_logger.Info("GenerateLcuDocs requested");
			var docsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "LCU-Docs");
			Directory.CreateDirectory(docsDir);
			var md = Path.Combine(docsDir, "lcu_endpoints.md");
			var json = Path.Combine(docsDir, "lcu_openapi.json");
			var count = await _riotClientService.GenerateLcuEndpointsMarkdownAsync(md, json);
			MessageBox.Show($"Готово: {count} эндпоинтов\n{md}");
		}
		catch (Exception ex)
		{
			_logger.Error($"GenerateLcuDocs error: {ex}");
			MessageBox.Show($"Ошибка генерации: {ex.Message}\nЛоги: {_logger.LogFilePath}");
		}
	}
}


