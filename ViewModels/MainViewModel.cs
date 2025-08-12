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
    private readonly IUiAutomationService _ui;

    public ObservableCollection<AccountRecord> Accounts { get; } = new();

    [ObservableProperty]
    private string newUsername = string.Empty;

    public string NewPassword { get; set; } = string.Empty;

    [ObservableProperty]
    private AccountRecord? selectedAccount;

    public MainViewModel()
        : this(new AccountsStorage(), new RiotClientService(), new FileLogger(), new UiAutomationService())
    {
    }

    public MainViewModel(IAccountsStorage accountsStorage, IRiotClientService riotClientService, ILogger logger, IUiAutomationService ui)
    {
        _accountsStorage = accountsStorage;
        _riotClientService = riotClientService;
        _logger = logger;
        _ui = ui;

        foreach (var acc in _accountsStorage.LoadAll())
            Accounts.Add(acc);
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
            _logger.Info($"Login start for {SelectedAccount.Username}");
            // 1) Перезапуск Riot Client
            try { await _riotClientService.RestartRiotClientAsync(); } catch { }

            // 2) Ждём окно RC до 15с без спама
            var deadline = DateTime.UtcNow.AddSeconds(15);
            bool focused = false;
            while (DateTime.UtcNow < deadline)
            {
                if (_ui.FocusRiotClient()) { focused = true; break; }
                await Task.Delay(300);
            }
            if (!focused)
            {
                _logger.Error("RC window not found within 15s");
                MessageBox.Show("Не удалось обнаружить окно Riot Client в течение 15с. Откройте его вручную и повторите.");
                return;
            }

            // 3) Однократный точный ввод
            await Task.Delay(400);
            var ok = _ui.TryLogin(SelectedAccount.Username, password);
            _logger.Info($"RC UI single attempt executed, result={ok}");
            if (!ok)
            {
                MessageBox.Show("Не удалось ввести логин/пароль автоматически. Попробуйте ещё раз или введите вручную.");
            }
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
}


