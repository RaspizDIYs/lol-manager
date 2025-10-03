using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
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
using System.ComponentModel;

namespace LolManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAccountsStorage _accountsStorage;
    private readonly IRiotClientService _riotClientService;
    private readonly ILogger _logger;
    private readonly ISettingsService _settingsService;
    private readonly Lazy<UpdateService> _updateService;
    private readonly AutoAcceptService _autoAcceptService;

    public ObservableCollection<AccountRecord> Accounts { get; } = new();

	[ObservableProperty]
	private bool isNavExpanded;

	[ObservableProperty]
	private int selectedTabIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddAccount))]
    private string newUsername = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddAccount))]
    private string newPassword = string.Empty;

    [ObservableProperty]
    private string newNote = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    [NotifyPropertyChangedFor(nameof(SubmitButtonText))]
    private bool isEditMode;

    [ObservableProperty]
    private AccountRecord? editingAccount;

    public bool CanAddAccount => !string.IsNullOrWhiteSpace(NewUsername) && !string.IsNullOrWhiteSpace(NewPassword);

    public string FormTitle => IsEditMode ? "Редактировать аккаунт" : "Добавить аккаунт";

    public string SubmitButtonText => IsEditMode ? "Сохранить" : "Добавить";

    [ObservableProperty]
    private AccountRecord? selectedAccount;

	[ObservableProperty]
	private string logsText = string.Empty;

	public ObservableCollection<string> LogLines { get; } = new();
	public ObservableCollection<string> FilteredLogLines { get; } = new();

	[ObservableProperty]
	private LogFilters logFilters = new();

	[ObservableProperty]
	private string appVersion = GetAppVersion();
	
	private static string GetAppVersion()
	{
		var assembly = Assembly.GetExecutingAssembly();
		var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		
		// Обрезаем Git metadata (+commit_hash) для отображения чистой версии
		if (!string.IsNullOrEmpty(version))
		{
			var cleanVersion = version.Split('+')[0];
			return $"v{cleanVersion}";
		}
		
		return $"v{assembly.GetName().Version?.ToString(3) ?? "0.0.1"}";
	}

	[ObservableProperty]
	private UpdateSettings updateSettings = new();

	[ObservableProperty]
	private List<string> updateChannels = new() { "stable", "beta" };

	[ObservableProperty]
	private SystemInfo systemInfo = new();

	private bool _isAutoAcceptEnabled;
	public bool IsAutoAcceptEnabled
	{
		get => _isAutoAcceptEnabled;
		set
		{
			if (SetProperty(ref _isAutoAcceptEnabled, value))
			{
				_autoAcceptService.SetEnabled(value);
				_logger.Info($"AutoAccept {(value ? "включен" : "выключен")}");
			}
		}
	}

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(LoginButtonText))]
	private bool isLoggingIn = false;

	[ObservableProperty]
	private string loginStatus = string.Empty;

	public string LoginButtonText => IsLoggingIn ? "Вход..." : "Войти";

	[ObservableProperty]
	private bool isChangelogVisible = false;

	[ObservableProperty]
	private string changelogText = string.Empty;

	[ObservableProperty]
	private bool isExportSelectionMode = false;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ExportButtonText))]
	private bool hasSelectedAccounts = false;

	public string ExportButtonText => IsExportSelectionMode 
		? (HasSelectedAccounts ? "Далее" : "Пропустить")
		: "Экспорт";

	public MainViewModel()
		: this(new AccountsStorage(new FileLogger()), new RiotClientService(), new FileLogger(), new SettingsService())
    {
    }

    	public MainViewModel(IAccountsStorage accountsStorage, IRiotClientService riotClientService, ILogger logger, ISettingsService settingsService)
	{
		_accountsStorage = accountsStorage;
		_riotClientService = riotClientService;
		_logger = logger;
		_settingsService = settingsService;
		_updateService = new Lazy<UpdateService>(() => new UpdateService(_logger, _settingsService));
		_autoAcceptService = new AutoAcceptService(_logger, _riotClientService);

		_logger.Info("MainViewModel initialized");

		foreach (var acc in _accountsStorage.LoadAll())
		{
			acc.PropertyChanged += OnAccountSelectionChanged;
			Accounts.Add(acc);
		}

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
		
		// Подписываемся на изменения фильтров логов
		LogFilters.PropertyChanged += (s, e) => RefreshFilteredLogs();
		
		// Начальная фильтрация логов
		RefreshFilteredLogs();
		
		// Подписываемся на изменения канала обновлений
		if (UpdateSettings != null)
		{
			UpdateSettings.PropertyChanged += (s, e) =>
			{
				if (e.PropertyName == nameof(UpdateSettings.UpdateChannel))
				{
									try
				{
					_updateService.Value.RefreshUpdateSource();
					_logger.Info($"Update channel changed to: {UpdateSettings.UpdateChannel}");
					_settingsService.SaveUpdateSettings(UpdateSettings);
					_logger.Info("Update channel saved to update-settings.json");

					// Предупреждение при переключении с beta на stable
					if (UpdateSettings.UpdateChannel == "stable")
					{
						try
						{
							var res = System.Windows.MessageBox.Show(
								"Вы переключились на стабильный канал. Установить последнюю стабильную версию сейчас? (иначе дождитесь следующего стабильного обновления)",
								"Канал обновлений",
								System.Windows.MessageBoxButton.YesNo,
								System.Windows.MessageBoxImage.Question);
							if (res == System.Windows.MessageBoxResult.Yes)
							{
								_ = Task.Run(async () =>
								{
									try
									{
										await _updateService.Value.ForceDowngradeToStableAsync();
									}
									catch (Exception exDowngrade)
									{
										_logger.Error($"Force downgrade failed: {exDowngrade.Message}");
									}
								});
							}
						}
						catch { }
					}
				}
					catch (Exception ex)
					{
						_logger.Error($"Failed to refresh update source: {ex.Message}");
					}
				}
			};
		}
		
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
	private void OpenInfo()
	{
		SelectedTabIndex = 3;
		IsChangelogVisible = false;
	}

	[RelayCommand]
	private void OpenAddAccount()
	{
		IsEditMode = false;
		EditingAccount = null;
		ClearForm();
		SelectedTabIndex = 4;
	}

	[RelayCommand]
	private void EditSelected()
	{
		if (SelectedAccount == null) return;
		IsEditMode = true;
		EditingAccount = SelectedAccount;
		NewUsername = SelectedAccount.Username;
		NewNote = SelectedAccount.Note;
		NewPassword = _accountsStorage.Unprotect(SelectedAccount.EncryptedPassword);
		SelectedTabIndex = 4;
	}

	[RelayCommand]
	private void GoBack()
	{
		SelectedTabIndex = 0;
	}

	[RelayCommand]
	private void ClearForm()
	{
		NewUsername = string.Empty;
		NewPassword = string.Empty;
		NewNote = string.Empty;
		IsEditMode = false;
		EditingAccount = null;
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
	private async Task ToggleChangelog()
	{
		try
		{
			IsChangelogVisible = true;
			
			if (string.IsNullOrEmpty(ChangelogText))
			{
				ChangelogText = await _updateService.Value.GetChangelogAsync();
			}
		}
		catch (Exception ex)
		{
			_logger.Error($"Failed to load changelog: {ex.Message}");
			ChangelogText = $"Ошибка загрузки истории изменений:\n{ex.Message}";
		}
	}

	[RelayCommand]
	private void CloseChangelog()
	{
		IsChangelogVisible = false;
	}

	private async Task CheckForUpdatesAsync()
	{
		try
		{
			var hasUpdates = await _updateService.Value.CheckForUpdatesAsync();
			
			if (hasUpdates)
			{
				Application.Current.Dispatcher.Invoke(() =>
				{
					var updateWindow = new UpdateWindow(_updateService.Value);
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
					var hasUpdates = await _updateService.Value.CheckForUpdatesAsync(forceCheck: true);

		// Перезагружаем настройки после проверки обновлений для обновления UI
		UpdateSettings = _settingsService.LoadUpdateSettings();
		
		if (hasUpdates)
			{
				var updateWindow = new UpdateWindow(_updateService.Value);
				updateWindow.Owner = Application.Current.MainWindow;
				updateWindow.ShowDialog();
			}
			else
			{
				MessageWindow.Show("Обновлений не найдено.", "Проверка обновлений", MessageWindow.MessageType.Information);
			}
		}
		catch (Exception ex)
		{
			_logger.Error($"Failed to check updates: {ex.Message}");
			MessageWindow.Show($"Ошибка при проверке обновлений: {ex.Message}", "Ошибка", MessageWindow.MessageType.Error);
		}
	}

	[RelayCommand]
	private void Submit()
	{
		if (IsEditMode)
		{
			UpdateAccount();
		}
		else
		{
			AddAccount();
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
            Note = NewNote.Trim(),
            CreatedAt = DateTime.Now
        };
        _accountsStorage.Save(created);
        created.PropertyChanged += OnAccountSelectionChanged;
        Accounts.Add(created);
        ClearForm();
        SelectedTabIndex = 0;
    }

	private void UpdateAccount()
	{
		if (EditingAccount == null || string.IsNullOrWhiteSpace(NewUsername) || string.IsNullOrWhiteSpace(NewPassword)) return;
		
		// Удаляем старый аккаунт если имя изменилось
		if (!string.Equals(EditingAccount.Username, NewUsername.Trim(), StringComparison.OrdinalIgnoreCase))
		{
			_accountsStorage.Delete(EditingAccount.Username);
			Accounts.Remove(EditingAccount);
		}
		else
		{
			Accounts.Remove(EditingAccount);
		}
		
		var updated = new AccountRecord
		{
			Username = NewUsername.Trim(),
			EncryptedPassword = _accountsStorage.Protect(NewPassword),
			Note = NewNote.Trim(),
			CreatedAt = EditingAccount.CreatedAt
		};
		
		_accountsStorage.Save(updated);
		updated.PropertyChanged += OnAccountSelectionChanged;
		Accounts.Add(updated);
		ClearForm();
		SelectedTabIndex = 0;
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
					// Добавляем строки в обратном порядке: новейшие (из конца файла) сверху
					Application.Current.Dispatcher.Invoke(() =>
					{
						// Читаем строки из файла (старые сверху, новые снизу)
						// Добавляем в обратном порядке, чтобы новые оказались сверху в UI
						for (int i = linesInit.Count - 1; i >= 0; i--)
						{
							LogLines.Add(linesInit[i]); // Добавляем в конец, но в обратном порядке
						}
						
						// Ограничиваем размер
						while (LogLines.Count > maxLines)
							LogLines.RemoveAt(LogLines.Count - 1);
							
						RefreshFilteredLogs();
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
							// новые строки в конце файла -> добавляем в начало списка (новые сверху)
							for (int i = 0; i < newLines.Count; i++)
							{
								LogLines.Insert(0, newLines[i]);
								if (LogLines.Count > maxLines) LogLines.RemoveAt(LogLines.Count - 1);
							}
							RefreshFilteredLogs();
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

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoginSelected()
    {
        if (SelectedAccount is null) return;
        
        IsLoggingIn = true;
        LoginStatus = "Подготовка к входу...";
        
        try
        {
            var password = _accountsStorage.Unprotect(SelectedAccount.EncryptedPassword);
            _logger.Info($"Login requested for {SelectedAccount.Username}");
            
            LoginStatus = "Выход из текущего аккаунта...";
            try { await _riotClientService.LogoutAsync(); } catch { }
            
            LoginStatus = "Закрытие клиента League of Legends...";
            try { await _riotClientService.KillLeagueAsync(includeRiotClient: false); } catch { }
            
            await Task.Delay(500);
            
            LoginStatus = $"Вход в {SelectedAccount.Username}...";
            await _riotClientService.LoginAsync(SelectedAccount.Username, password);
            
            LoginStatus = "Готово! Вход выполнен успешно";
            await Task.Delay(1500);
            LoginStatus = string.Empty;
        }
        catch (Exception ex)
        {
            LoginStatus = $"Ошибка: {ex.Message}";
            _logger.Error($"Login error for {SelectedAccount.Username}: {ex}");
            MessageWindow.Show($"Ошибка входа: {ex.Message}\nЛоги: {_logger.LogFilePath}", "Ошибка входа", MessageWindow.MessageType.Error);
            await Task.Delay(3000);
            LoginStatus = string.Empty;
        }
        finally
        {
            IsLoggingIn = false;
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
            MessageWindow.Show($"Ошибка перезапуска: {ex.Message}\nЛоги: {_logger.LogFilePath}", "Ошибка перезапуска", MessageWindow.MessageType.Error);
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
            MessageWindow.Show($"Ошибка перезахода: {ex.Message}\nЛоги: {_logger.LogFilePath}", "Ошибка перезахода", MessageWindow.MessageType.Error);
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
            MessageWindow.Show($"Ошибка выхода: {ex.Message}\nЛоги: {_logger.LogFilePath}", "Ошибка выхода", MessageWindow.MessageType.Error);
        }
    }

    [RelayCommand]
    private void SelectAllFilters()
    {
        LogFilters.ShowLogin = true;
        LogFilters.ShowHttp = true;
        LogFilters.ShowUi = true;
        LogFilters.ShowProcess = true;
        LogFilters.ShowInfo = true;
        LogFilters.ShowWarning = true;
        LogFilters.ShowError = true;
        LogFilters.ShowDebug = true;
    }

    [RelayCommand]
    private void ClearAllFilters()
    {
        LogFilters.ShowLogin = false;
        LogFilters.ShowHttp = false;
        LogFilters.ShowUi = false;
        LogFilters.ShowProcess = false;
        LogFilters.ShowInfo = false;
        LogFilters.ShowWarning = false;
        LogFilters.ShowError = false;
        LogFilters.ShowDebug = false;
    }

    [RelayCommand]
    private void CopyFilteredLogs()
    {
        try
        {
            var text = string.Join("\n", FilteredLogLines);
            Clipboard.SetText(text);
        }
        catch { }
    }

    [RelayCommand]
    private void CopySelectedLogsWithParam(object? parameter)
    {
        try
        {
            IList? selectedItems = parameter as IList;
            if (selectedItems == null || selectedItems.Count == 0)
                return;

            var selectedTexts = new List<string>();
            foreach (var item in selectedItems)
            {
                if (item is string logLine)
                {
                    selectedTexts.Add(logLine);
                }
            }

            if (selectedTexts.Count > 0)
            {
                var text = string.Join("\n", selectedTexts);
                Clipboard.SetText(text);
            }
        }
        catch { }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        try
        {
            LogLines.Clear();
            FilteredLogLines.Clear();
            
            // Очистить файл логов
            File.WriteAllText(_logger.LogFilePath, string.Empty);
            _logsLastLength = 0;
            _logsPartial = string.Empty;
        }
        catch (Exception ex)
        {
            MessageWindow.Show($"Ошибка очистки логов: {ex.Message}", "Ошибка очистки", MessageWindow.MessageType.Error);
        }
    }

    [RelayCommand]
    private void RefreshLogs()
    {
        RefreshFilteredLogs();
    }

    private void RefreshFilteredLogs()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            FilteredLogLines.Clear();
            
            // LogLines уже в правильном порядке (новые сверху), просто фильтруем
            foreach (var logLine in LogLines)
            {
                if (ShouldShowLogLine(logLine))
                {
                    FilteredLogLines.Add(logLine);
                }
            }
        });
    }

    private bool ShouldShowLogLine(string logLine)
    {
        if (string.IsNullOrWhiteSpace(logLine))
            return false;

        // Извлекаем тип лога из строки формата "[HH:mm:ss.fff] TYPE ..."
        var bracketEnd = logLine.IndexOf(']');
        if (bracketEnd == -1) return LogFilters.ShowInfo; // Неизвестный формат - показываем как INFO

        var afterBracket = logLine.Substring(bracketEnd + 1).Trim();
        var spaceIndex = afterBracket.IndexOf(' ');
        if (spaceIndex == -1) return LogFilters.ShowInfo;

        var logType = afterBracket.Substring(0, spaceIndex).Trim();
        
        return logType switch
        {
            "LOGIN" => LogFilters.ShowLogin,
            "HTTP" => LogFilters.ShowHttp,
            "UI" => LogFilters.ShowUi,
            "PROC" => LogFilters.ShowProcess,
            "INFO" => LogFilters.ShowInfo,
            "WARN" => LogFilters.ShowWarning,
            "ERROR" => LogFilters.ShowError,
            "DEBUG" => LogFilters.ShowDebug,
            _ => LogFilters.ShowInfo // По умолчанию показываем как INFO
        };
    }

    [RelayCommand]
    private void ExportAccounts()
    {
        if (IsExportSelectionMode)
        {
            if (HasSelectedAccounts)
            {
                // Экспорт выбранных аккаунтов
                PerformExport();
            }
            else
            {
                // Пропустить - отмена режима выбора
                CancelExportSelection();
            }
        }
        else
        {
            // Входим в режим выбора аккаунтов
            EnterExportSelectionMode();
        }
    }

    private void EnterExportSelectionMode()
    {
        IsExportSelectionMode = true;
        
        // Очищаем предыдущие выборы
        foreach (var account in Accounts)
        {
            account.IsSelected = false;
        }
        
        UpdateSelectedAccountsCount();
    }

    private void CancelExportSelection()
    {
        IsExportSelectionMode = false;
        
        // Очищаем выборы
        foreach (var account in Accounts)
        {
            account.IsSelected = false;
        }
        
        UpdateSelectedAccountsCount();
    }

    private void PerformExport()
    {
        try
        {
            var selectedAccounts = Accounts.Where(a => a.IsSelected).ToList();
            if (!selectedAccounts.Any())
            {
                MessageWindow.Show("Выберите хотя бы один аккаунт для экспорта.", "Экспорт", MessageWindow.MessageType.Warning);
                return;
            }

            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Зашифрованный файл LolManager (*.lolm)|*.lolm",
                DefaultExt = ".lolm",
                FileName = $"accounts_export_{DateTime.Now:yyyy-MM-dd}_{selectedAccounts.Count}_accounts"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                _logger.Info($"Начат экспорт {selectedAccounts.Count} аккаунтов в файл: {Path.GetFileName(saveFileDialog.FileName)}");
                _accountsStorage.ExportAccounts(saveFileDialog.FileName, selectedAccounts);
                
                var message = $"Успешно экспортировано {selectedAccounts.Count} аккаунт(ов)!\n\nФайл: {Path.GetFileName(saveFileDialog.FileName)}";
                MessageWindow.Show(message, "Экспорт завершен", MessageWindow.MessageType.Information);
                _logger.Info($"Экспорт завершен успешно: {selectedAccounts.Count} аккаунтов");
                
                // Выходим из режима выбора
                CancelExportSelection();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка экспорта: {ex.Message}");
            MessageWindow.Show($"Ошибка экспорта: {ex.Message}", "Ошибка", MessageWindow.MessageType.Error);
        }
    }

    [RelayCommand]
    private void SelectAllAccounts()
    {
        foreach (var account in Accounts)
        {
            account.IsSelected = true;
        }
        UpdateSelectedAccountsCount();
    }

    [RelayCommand]
    private void DeselectAllAccounts()
    {
        foreach (var account in Accounts)
        {
            account.IsSelected = false;
        }
        UpdateSelectedAccountsCount();
    }

    private void OnAccountSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountRecord.IsSelected))
        {
            UpdateSelectedAccountsCount();
        }
    }


    private void UpdateSelectedAccountsCount()
    {
        HasSelectedAccounts = Accounts.Any(a => a.IsSelected);
    }

    [RelayCommand]
    private void ImportAccounts()
    {
        try
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Все поддерживаемые файлы (*.lolm;*.json)|*.lolm;*.json|Зашифрованные файлы LolManager (*.lolm)|*.lolm|JSON файлы (*.json)|*.json",
                DefaultExt = ".lolm"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var selectedFile = openFileDialog.FileName;
                _logger.Info($"Начат импорт из файла: {Path.GetFileName(selectedFile)}");
                
                var result = MessageWindow.Show(
                    "Импорт добавит новые аккаунты и обновит существующие с теми же именами.\n\nПродолжить импорт?", 
                    "Подтверждение импорта", 
                    MessageWindow.MessageType.Question, 
                    MessageWindow.MessageButtons.YesNo);
                
                if (result == true)
                {
                    var accountsCountBefore = Accounts.Count;
                    _accountsStorage.ImportAccounts(selectedFile);
                    
                    // Перезагружаем список аккаунтов
                    Accounts.Clear();
                    foreach (var acc in _accountsStorage.LoadAll())
                    {
                        acc.PropertyChanged += OnAccountSelectionChanged;
                        Accounts.Add(acc);
                    }
                    
                    var fileExt = Path.GetExtension(selectedFile).ToLower();
                    var formatInfo = fileExt switch
                    {
                        ".lolm" => "зашифрованного файла",
                        ".json" => "JSON файла",
                        _ => "файла"
                    };
                    
                    _logger.Info($"Импорт завершен успешно. Аккаунтов было: {accountsCountBefore}, стало: {Accounts.Count}");
                    MessageWindow.Show($"Аккаунты успешно импортированы из {formatInfo}!", "Импорт завершен", MessageWindow.MessageType.Information);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка импорта: {ex.Message}");
            
            var errorMsg = ex.Message.Contains("расшифровать") 
                ? "Не удалось расшифровать файл. Убедитесь, что файл не поврежден и был создан на этом компьютере."
                : $"Ошибка импорта: {ex.Message}";
            
            MessageWindow.Show(errorMsg, "Ошибка импорта", MessageWindow.MessageType.Error);
        }
    }


}


