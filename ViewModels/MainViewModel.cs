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
using LolManager.Views;
using System.Collections.Generic;
using System.Collections;

namespace LolManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAccountsStorage _accountsStorage;
    private readonly IRiotClientService _riotClientService;
    private readonly ILogger _logger;
    private readonly ISettingsService _settingsService;

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

	[ObservableProperty]
	private string logsText = string.Empty;

	public ObservableCollection<string> LogLines { get; } = new();

	[ObservableProperty]
	private string appVersion = "v0.0.1";

	[ObservableProperty]
	private UpdateSettings updateSettings = new();

	[ObservableProperty]
	private List<string> updateChannels = new() { "stable", "beta", "alpha" };

	public MainViewModel()
		: this(new AccountsStorage(), new RiotClientService(), new FileLogger(), new SettingsService())
    {
    }

    	public MainViewModel(IAccountsStorage accountsStorage, IRiotClientService riotClientService, ILogger logger, ISettingsService settingsService)
	{
		_accountsStorage = accountsStorage;
		_riotClientService = riotClientService;
		_logger = logger;
		_settingsService = settingsService;

		foreach (var acc in _accountsStorage.LoadAll())
			Accounts.Add(acc);

		SelectedTabIndex = 0;
		
		// Загружаем настройки обновлений
		UpdateSettings = _settingsService.LoadUpdateSettings();
		
		// Подписываемся на изменения настроек для автоматического сохранения
		PropertyChanged += (s, e) =>
		{
			if (e.PropertyName == nameof(UpdateSettings) && UpdateSettings != null)
			{
				_settingsService.SaveUpdateSettings(UpdateSettings);
			}
		};
		
		// Автоматическая проверка обновлений
		_ = Task.Run(async () => await CheckForUpdatesAsync());
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
	private void OpenLogsPane()
	{
		SelectedTabIndex = 2;
		EnsureLogsTail();
	}



	[RelayCommand]
	private void CopyAllLogs()
	{
		try
		{
			var text = string.Join("\n", LogLines);
			Clipboard.SetText(text);
		}
		catch { }
	}

	[RelayCommand]
	private void CopySelectedLogs(IList? selected)
	{
		try
		{
			if (selected == null || selected.Count == 0)
			{
				CopyAllLogs();
				return;
			}
			var lines = selected.Cast<object>().Select(o => o?.ToString() ?? string.Empty);
			Clipboard.SetText(string.Join("\n", lines));
		}
		catch { }
	}

	[RelayCommand]
	private void OpenLogFile()
	{
		try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _logger.LogFilePath, UseShellExecute = true }); } catch { }
	}

	[RelayCommand]
	private void ShowInfo()
	{
		try
		{
			var updateService = new UpdateService(_logger, _settingsService);
			var infoWindow = new InfoWindow(updateService);
			infoWindow.Owner = Application.Current.MainWindow;
			infoWindow.ShowDialog();
		}
		catch (Exception ex)
		{
			_logger.Error($"Failed to show info: {ex.Message}");
		}
	}

	private async Task CheckForUpdatesAsync()
	{
		try
		{
			var updateService = new UpdateService(_logger, _settingsService);
			var hasUpdates = await updateService.CheckForUpdatesAsync();
			
			if (hasUpdates)
			{
				Application.Current.Dispatcher.Invoke(() =>
				{
					var updateWindow = new UpdateWindow(updateService);
					updateWindow.Owner = Application.Current.MainWindow;
					updateWindow.ShowDialog();
				});
			}
		}
		catch (Exception ex)
		{
			_logger.Error($"Failed to check for updates: {ex.Message}");
		}
	}

	[RelayCommand]
	private async Task CheckUpdatesNow()
	{
		try
		{
			var updateService = new UpdateService(_logger, _settingsService);
			var hasUpdates = await updateService.CheckForUpdatesAsync();
			
			if (hasUpdates)
			{
				var updateWindow = new UpdateWindow(updateService);
				updateWindow.Owner = Application.Current.MainWindow;
				updateWindow.ShowDialog();
			}
			else
			{
				System.Windows.MessageBox.Show("Обновлений не найдено.", "Проверка обновлений", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
			}
		}
		catch (Exception ex)
		{
			_logger.Error($"Failed to check updates: {ex.Message}");
			System.Windows.MessageBox.Show($"Ошибка при проверке обновлений: {ex.Message}", "Ошибка", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
		}
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

	private System.Threading.CancellationTokenSource? _logsCts;
	private long _logsLastLength = 0;
	private string _logsPartial = string.Empty;

	partial void OnSelectedTabIndexChanged(int value)
	{
		if (value == 2) EnsureLogsTail();
	}

	private void EnsureLogsTail()
	{
		if (_logsCts != null) return;
		_logsCts = new System.Threading.CancellationTokenSource();
		_ = Task.Run(() => TailLogsAsync(_logsCts.Token));
	}

	private async Task TailLogsAsync(System.Threading.CancellationToken token)
	{
		const int maxTailBytes = 200_000;
		const int maxLines = 4000;

		while (!token.IsCancellationRequested)
		{
			try
			{
				var path = _logger.LogFilePath;
				var dir = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
				if (!File.Exists(path))
				{
					await File.WriteAllTextAsync(path, string.Empty, Encoding.UTF8);
					_logsLastLength = 0;
					_logsPartial = string.Empty;
				}

				var fi = new FileInfo(path);
				// Файл обнулился — перечитать хвост
				if (fi.Length < _logsLastLength)
				{
					_logsLastLength = 0;
					_logsPartial = string.Empty;
					Application.Current.Dispatcher.Invoke(() => LogLines.Clear());
				}

				using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
				if (_logsLastLength == 0 && fs.Length > 0)
				{
					// первая инициализация — читаем хвост
					long start = Math.Max(0, fs.Length - maxTailBytes);
					fs.Position = start;
					using var srInit = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
					var initContent = await srInit.ReadToEndAsync();
					_logsLastLength = fs.Length;
					var linesInit = SplitLines(initContent);
					// Положим новейшие сверху: добавляем в обратном порядке (с конца файла к началу)
					Application.Current.Dispatcher.Invoke(() =>
					{
						for (int i = linesInit.Count - 1; i >= 0; i--)
						{
							LogLines.Insert(0, linesInit[i]);
							if (LogLines.Count > maxLines) LogLines.RemoveAt(LogLines.Count - 1);
						}
					});
				}
				else if (fs.Length > _logsLastLength)
				{
					// дочитать приращение
					fs.Position = _logsLastLength;
					using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
					var delta = await sr.ReadToEndAsync();
					_logsLastLength = fs.Length;
					var newLines = SplitLines(_logsPartial + delta, out var trailingPartial);
					_logsPartial = trailingPartial; // сохранить незавершённую строку
					if (newLines.Count > 0)
					{
						Application.Current.Dispatcher.Invoke(() =>
						{
							// новые строки в конце файла -> добавляем сверху
							for (int i = newLines.Count - 1; i >= 0; i--)
							{
								LogLines.Insert(0, newLines[i]);
								if (LogLines.Count > maxLines) LogLines.RemoveAt(LogLines.Count - 1);
							}
						});
					}
				}
			}
			catch { }
			await Task.Delay(500, token);
		}
	}

	private static List<string> SplitLines(string text) => SplitLines(text, out _);

	private static List<string> SplitLines(string text, out string partial)
	{
		partial = string.Empty;
		var list = new List<string>();
		if (string.IsNullOrEmpty(text)) return list;
		text = text.Replace("\r\n", "\n");
		int start = 0;
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] == '\n')
			{
				var line = text.Substring(start, i - start);
				list.Add(line);
				start = i + 1;
			}
		}
		if (start < text.Length)
		{
			partial = text.Substring(start);
		}
		return list;
	}

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedAccount is null) return;
        _accountsStorage.Delete(SelectedAccount.Username);
        Accounts.Remove(SelectedAccount);
        SelectedAccount = null;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoginSelected()
    {
        if (SelectedAccount is null) return;
        var password = _accountsStorage.Unprotect(SelectedAccount.EncryptedPassword);

        try
        {
            _logger.Info($"Login requested for {SelectedAccount.Username}");
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


}


