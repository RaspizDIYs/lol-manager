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
using Newtonsoft.Json.Linq;

namespace LolManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAccountsStorage _accountsStorage;
    private readonly IRiotClientService _riotClientService;
    private readonly ILogger _logger;
    private readonly ISettingsService _settingsService;
    private readonly Lazy<UpdateService> _updateService;
    private readonly AutoAcceptService _autoAcceptService;
    private readonly RevealService _revealService;

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
	private List<string> updateModes = new() { "Velopack", "Direct" };

	[ObservableProperty]
	private bool hasConnectionIssue = false;

	[ObservableProperty]
	private string connectionIssueText = string.Empty;

	private string _lastConnectionIssueToast = string.Empty;
	private DateTime _lastConnectionIssueAt = DateTime.MinValue;

	[ObservableProperty]
	private SystemInfo systemInfo = new();

	[ObservableProperty]
	private RevealSettings revealSettings = new();

	[ObservableProperty]
	private LeagueSettings leagueSettings = new();

	[ObservableProperty]
	private string revealApiStatus = "Не подключен";

	[ObservableProperty]
	private string revealApiStatusColor = "Gray";

	[ObservableProperty]
	private string revealTeamStatus = "Ожидание";

	[ObservableProperty]
	private string revealTeamStatusColor = "Gray";

	[ObservableProperty]
	private bool hasTeamInfo = false;

	public ObservableCollection<PlayerInfo> TeamPlayers { get; } = new();

	[ObservableProperty]
	private List<RegionInfo> availableRegions = new()
	{
		new("euw1", "Europe West"),
		new("eune1", "Europe Nordic & East"),
		new("na1", "North America"),
		new("br1", "Brazil"),
		new("la1", "Latin America North"),
		new("la2", "Latin America South"),
		new("kr", "Korea"),
		new("jp1", "Japan"),
		new("oc1", "Oceania"),
		new("tr1", "Turkey"),
		new("ru", "Russia")
	};

    public IReadOnlyList<KeyValuePair<string, CloseBehavior>> CloseBehaviorOptions { get; } =
        new List<KeyValuePair<string, CloseBehavior>>
        {
            new("Спрашивать каждый раз", CloseBehavior.AskEveryTime),
            new("Свернуть в трей", CloseBehavior.MinimizeToTray),
            new("Закрыть приложение", CloseBehavior.ExitApp)
        };

	private AutoAcceptMethod _autoAcceptMethod = AutoAcceptMethod.Polling;
	
	public AutoAcceptMethod AutoAcceptMethod
	{
		get => _autoAcceptMethod;
		set
		{
			if (SetProperty(ref _autoAcceptMethod, value))
			{
				var settings = _settingsService.LoadSetting<AutomationSettings>("AutomationSettings", new AutomationSettings());
				settings.AutoAcceptMethod = value.ToString();
				_settingsService.SaveSetting("AutomationSettings", settings);
				_autoAcceptService.SetAutomationSettings(settings);
				_logger.Info($"Метод автопринятия изменен на: {value}");
			}
		}
	}
	
	private bool _hideLogin;
	public bool HideLogin
	{
		get => _hideLogin;
		set
		{
			if (SetProperty(ref _hideLogin, value))
			{
				_settingsService.SaveSetting("HideLogin", value);
			}
		}
	}

    private CloseBehavior _closeBehavior;
    public CloseBehavior CloseBehavior
    {
        get => _closeBehavior;
        set
        {
            if (SetProperty(ref _closeBehavior, value))
            {
                _settingsService.SaveSetting("CloseBehavior", value.ToString());
            }
        }
    }

	private bool _isAutoAcceptEnabled;
	public bool IsAutoAcceptEnabled
	{
		get => _isAutoAcceptEnabled;
		set
		{
			if (SetProperty(ref _isAutoAcceptEnabled, value))
			{
				_autoAcceptService.SetEnabled(value);
				_settingsService.SaveSetting("IsAutoAcceptEnabled", value);
				_logger.Info($"AutoAccept {(value ? "включен" : "выключен")}");
			}
		}
	}

	private bool _isAutomationEnabled;
	public bool IsAutomationEnabled
	{
		get => _isAutomationEnabled;
		set
		{
			if (SetProperty(ref _isAutomationEnabled, value))
			{
				var settings = _settingsService.LoadSetting<AutomationSettings>("AutomationSettings", new AutomationSettings());
				settings.IsEnabled = value;
				_settingsService.SaveSetting("AutomationSettings", settings);
				_autoAcceptService.SetAutomationSettings(settings);
				_logger.Info($"Automation {(value ? "включена" : "выключена")}");
			}
		}
	}

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(LoginButtonText))]
	private bool isLoggingIn = false;

    private System.Threading.CancellationTokenSource? _loginCts;

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
		: this(new AccountsStorage(new FileLogger()), new RiotClientService(new FileLogger()), new FileLogger(), new SettingsService())
    {
    }

    	public MainViewModel(IAccountsStorage accountsStorage, IRiotClientService riotClientService, ILogger logger, ISettingsService settingsService)
	{
		_accountsStorage = accountsStorage;
		_riotClientService = riotClientService;
		_logger = logger;
		_settingsService = settingsService;
		_updateService = new Lazy<UpdateService>(() => new UpdateService(_logger, _settingsService));
		
		// Получаем DataDragonService через сервис-локатор (с проверкой на null для дизайнера)
		Services.DataDragonService? dataDragonService = null;
		if (App.Current != null)
		{
			try
			{
				dataDragonService = ((App)App.Current).GetService<Services.DataDragonService>();
			}
			catch
			{
				// Сервисы ещё не зарегистрированы (дизайн-тайм)
				dataDragonService = new Services.DataDragonService(_logger);
			}
		}
		else
		{
			// В режиме дизайнера создаем временный экземпляр
			dataDragonService = new Services.DataDragonService(_logger);
		}
        if (App.Current is App app)
        {
            try { _autoAcceptService = app.GetService<AutoAcceptService>(); }
            catch { _autoAcceptService = new AutoAcceptService(_logger, _riotClientService, dataDragonService!, _settingsService); }
        }
        else
        {
            _autoAcceptService = new AutoAcceptService(_logger, _riotClientService, dataDragonService!, _settingsService);
        }
		_revealService = new RevealService(_riotClientService, _logger);

		_logger.Info("MainViewModel initialized");

		foreach (var acc in _accountsStorage.LoadAll())
		{
			acc.PropertyChanged += OnAccountSelectionChanged;
			Accounts.Add(acc);
		}

		SelectedTabIndex = 0;
		
		// Загружаем настройки обновлений
		UpdateSettings = _settingsService.LoadUpdateSettings();
		
		// Загружаем настройки Reveal
		RevealSettings = _settingsService.LoadSetting<RevealSettings>("RevealSettings", new RevealSettings());

		// Загружаем настройки League
		LeagueSettings = _settingsService.LoadSetting<LeagueSettings>("LeagueSettings", new LeagueSettings());
		
		// Устанавливаем API ключ и регион в сервисе
		_revealService.SetApiConfiguration(RevealSettings.RiotApiKey, RevealSettings.SelectedRegion);
		
		// Загружаем настройку скрытия логина
		_hideLogin = _settingsService.LoadSetting("HideLogin", false);

        var closeBehaviorValue = _settingsService.LoadSetting("CloseBehavior", CloseBehavior.AskEveryTime.ToString());
        if (!Enum.TryParse(closeBehaviorValue, out _closeBehavior))
        {
            _closeBehavior = CloseBehavior.AskEveryTime;
        }
		
		// Загружаем состояние автоматизации
		var automationSettings = _settingsService.LoadSetting<AutomationSettings>("AutomationSettings", new AutomationSettings());
		_isAutomationEnabled = automationSettings.IsEnabled;
		
		// Загружаем метод автопринятия
		_autoAcceptMethod = AutoAcceptMethodExtensions.Parse(automationSettings.AutoAcceptMethod);
		_logger.Info($"Загружен метод автопринятия: {_autoAcceptMethod}");
		
		// Загружаем состояние автопринятия
		_isAutoAcceptEnabled = _settingsService.LoadSetting("IsAutoAcceptEnabled", false);
		_autoAcceptService.SetEnabled(_isAutoAcceptEnabled);
		
		// Подписываемся на изменения настроек для автоматического сохранения
		PropertyChanged += (s, e) =>
		{
			if (e.PropertyName == nameof(UpdateSettings) && UpdateSettings != null)
			{
				_settingsService.SaveUpdateSettings(UpdateSettings);
			}
			if (e.PropertyName == nameof(RevealSettings) && RevealSettings != null)
			{
				_settingsService.SaveSetting("RevealSettings", RevealSettings);
			}
			if (e.PropertyName == nameof(LeagueSettings) && LeagueSettings != null)
			{
				_settingsService.SaveSetting("LeagueSettings", LeagueSettings);
			}
		};
		
		// Подписываемся на изменения настроек Reveal для автоматического сохранения
		if (RevealSettings != null)
		{
			RevealSettings.PropertyChanged += (s, e) =>
			{
				_settingsService.SaveSetting("RevealSettings", RevealSettings);
				
				// Обновляем API ключ и регион в сервисе при изменении
				if (e.PropertyName == nameof(RevealSettings.RiotApiKey) || 
				    e.PropertyName == nameof(RevealSettings.SelectedRegion))
				{
					_revealService.SetApiConfiguration(RevealSettings.RiotApiKey, RevealSettings.SelectedRegion);
				}
			};
		}

		// Сохраняем LeagueSettings при изменениях
		if (LeagueSettings != null)
		{
			LeagueSettings.PropertyChanged += (s, e) =>
			{
				_settingsService.SaveSetting("LeagueSettings", LeagueSettings);
			};
		}
		
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
                else if (e.PropertyName == nameof(UpdateSettings.UpdateMode))
                {
                    try
                    {
                        _settingsService.SaveUpdateSettings(UpdateSettings);
                        _logger.Info($"Update mode saved: {UpdateSettings.UpdateMode}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to save update mode: {ex.Message}");
                    }
                }
			};
		}
		
		// Автоматическая проверка обновлений
		_ = Task.Run(async () => await CheckForUpdatesAsync());
		
		// Автоматическая загрузка данных аккаунтов при запуске (если LCU подключен)
		_ = Task.Run(async () =>
		{
			await Task.Delay(5000); // Ждем 5 секунд после запуска
			await AutoLoadAccountInfoAsync();
		});
	}

	[RelayCommand]
	private void BrowseLeagueInstall()
	{
		try
		{
			var dlg = new Microsoft.Win32.OpenFileDialog
			{
				FileName = "LeagueClient.exe",
				Filter = "LeagueClient.exe|LeagueClient.exe|Все файлы|*.*",
				CheckFileExists = true
			};
			if (dlg.ShowDialog() == true)
			{
				var dir = System.IO.Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
				LeagueSettings.InstallDirectory = dir;
				LeagueSettings.PreferManualPath = true;
				_settingsService.SaveSetting("LeagueSettings", LeagueSettings);
			}
		}
		catch { }
	}

	[RelayCommand]
	private void ValidateLeaguePath()
	{
		try
		{
			var dir = LeagueSettings?.InstallDirectory ?? string.Empty;
			if (string.IsNullOrWhiteSpace(dir) || !System.IO.Directory.Exists(dir))
			{
				MessageWindow.Show("Некорректная папка установки LoL", "Проверка пути", MessageWindow.MessageType.Error);
				return;
			}
			var lf = System.IO.Path.Combine(dir, "lockfile");
			if (!System.IO.File.Exists(lf))
			{
				MessageWindow.Show("Файл lockfile не найден в указанной папке.", "Проверка пути", MessageWindow.MessageType.Warning);
				return;
			}
			MessageWindow.Show("Путь валиден (lockfile найден)", "Проверка пути", MessageWindow.MessageType.Information);
		}
		catch (Exception ex)
		{
			MessageWindow.Show($"Ошибка проверки: {ex.Message}", "Проверка пути", MessageWindow.MessageType.Error);
		}
	}
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
	private void OpenAutomation()
	{
		SelectedTabIndex = 4;
	}

	[RelayCommand]
	private void OpenSpy()
	{
		if (RevealSettings?.IsEnabled == true)
		{
			SelectedTabIndex = 5;
		}
		else
		{
			SelectedTabIndex = 0;
		}
	}

	[RelayCommand]
	private void OpenAddAccount()
	{
		IsEditMode = false;
		EditingAccount = null;
		ClearForm();
		SelectedTabIndex = 7;
	}

	[RelayCommand]
	private void EditSelected()
	{
		if (SelectedAccount == null) return;
		IsEditMode = true;
		EditingAccount = SelectedAccount;
		NewUsername = SelectedAccount.Username;
		NewNote = SelectedAccount.Note;
		
		try
		{
			if (!string.IsNullOrEmpty(SelectedAccount.EncryptedPassword))
			{
				NewPassword = _accountsStorage.Unprotect(SelectedAccount.EncryptedPassword);
				if (string.IsNullOrEmpty(NewPassword))
				{
					_logger.Warning($"Не удалось расшифровать пароль для аккаунта {SelectedAccount.Username}");
				}
			}
			else
			{
				NewPassword = string.Empty;
				_logger.Warning($"EncryptedPassword пустой для аккаунта {SelectedAccount.Username}");
			}
		}
		catch (Exception ex)
		{
			_logger.Error($"Ошибка при расшифровке пароля для {SelectedAccount.Username}: {ex.Message}");
			NewPassword = string.Empty;
		}
		
		SelectedTabIndex = 7;
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
			
			// Всегда загружаем заново, чтобы отображались актуальные релизы по текущему каналу
			ChangelogText = await _updateService.Value.GetChangelogAsync();
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
		// Очищаем кэш чтобы при следующем открытии загрузились актуальные данные
		ChangelogText = string.Empty;
	}

	private async Task CheckForUpdatesAsync()
	{
		try
		{
			var hasUpdates = await _updateService.Value.CheckForUpdatesAsync();
			
			if (hasUpdates)
			{
				await Application.Current.Dispatcher.InvokeAsync(() =>
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

			// Перезагружаем настройках после проверки обновлений для обновления времени последней проверки в UI
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
		if (value == 5 && RevealSettings?.IsEnabled != true)
		{
			SelectedTabIndex = 0;
		}
	}

	private void EnsureLogsTail()
	{
		if (_logsCts != null) return;
		_logsCts = new System.Threading.CancellationTokenSource();
		_ = Task.Run(() => TailLogsAsync(_logsCts.Token));
	}

	private System.Threading.CancellationTokenSource? _connectivityCts;

	private void EnsureConnectivityLoop()
	{
		if (_connectivityCts != null) return;
		_connectivityCts = new System.Threading.CancellationTokenSource();
		var token = _connectivityCts.Token;
		_ = Task.Run(async () => await ConnectivityLoopAsync(token));
	}

	private void StopConnectivityLoop()
	{
		var cts = _connectivityCts;
		_connectivityCts = null;
		if (cts == null) return;
		try { cts.Cancel(); } catch { }
		try { cts.Dispose(); } catch { }
	}

	private bool _lastLcuConnectedState = false;
	private DateTime _lastAccountInfoUpdate = DateTime.MinValue;

	private async Task ConnectivityLoopAsync(System.Threading.CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			try
			{
				var status = await _riotClientService.ProbeConnectivityAsync();
				await Application.Current.Dispatcher.InvokeAsync(() =>
				{
					// Показываем дружелюбный тост, если есть проблема; иначе скрываем
					// Если всё ок – не показываем ничего
					if (status.LcuHttpOk)
					{
						HasConnectionIssue = false;
						ConnectionIssueText = string.Empty;
						// Не показываем тост при норме
						_lastConnectionIssueToast = string.Empty;
						_lastConnectionIssueAt = DateTime.MinValue;
						
						// Автоматически загружаем данные об аккаунте при подключении LCU
						if (!_lastLcuConnectedState || (DateTime.UtcNow - _lastAccountInfoUpdate).TotalMinutes > 5)
						{
							_lastLcuConnectedState = true;
							_ = Task.Run(async () => await AutoLoadAccountInfoAsync());
						}
						
						return;
					}
					
					_lastLcuConnectedState = false;

					// Человечные тексты
					string msg;
					if (LeagueSettings?.PreferManualPath == true && !string.IsNullOrWhiteSpace(LeagueSettings.InstallDirectory))
					{
						var lf = System.IO.Path.Combine(LeagueSettings.InstallDirectory!, "lockfile");
						if (!System.IO.File.Exists(lf))
						{
							msg = "Указанная папка League некорректна — не найден файл lockfile. Проверь путь в настройках.";
							HasConnectionIssue = true;
							ConnectionIssueText = msg;
							return;
						}
					}

					if (!status.IsRiotClientRunning && !status.IsLeagueRunning && !status.LcuLockfileFound)
					{
						msg = "Клиент не запущен. Открой Riot Client и войди в аккаунт.";
					}
					else if (status.IsRiotClientRunning && !status.IsLeagueRunning && !status.LcuLockfileFound)
					{
						msg = "Riot Client запущен. Запусти League of Legends из лаунчера.";
					}
					else if (status.IsLeagueRunning && !status.LcuLockfileFound)
					{
						msg = "League запущен, но не найден lockfile. Подожди 5–10 секунд или перезапусти League.";
					}
					else if (status.LcuLockfileFound && !status.LcuHttpOk)
					{
						var p = status.LcuPort.HasValue ? $":{status.LcuPort.Value}" : string.Empty;
						msg = $"Клиент LoL найден, но не отвечает{p}. Возможно, он запускается. Подожди немного и не закрывай окно входа.";
					}
					else
					{
						msg = "Не удалось подключиться к клиенту LoL. Проверь, что игра запущена.";
					}

					HasConnectionIssue = true;
					ConnectionIssueText = msg;

					// Показываем тост только при изменении сообщения или спустя интервал
					// НЕ показываем во время процесса логина (смены аккаунта)
					bool shouldToast = !IsLoggingIn 
						&& (!string.Equals(msg, _lastConnectionIssueToast, StringComparison.Ordinal)
						|| (DateTime.UtcNow - _lastConnectionIssueAt).TotalSeconds > 6);
					if (shouldToast)
					{
						_lastConnectionIssueToast = msg;
						_lastConnectionIssueAt = DateTime.UtcNow;
						try { (App.Current?.MainWindow as Views.MainWindow)?.ShowToast(msg, "!", "#E67E22"); } catch { }
					}
				});
			}
			catch { }
			try { await Task.Delay(1500, token); } catch (OperationCanceledException) { break; }
		}
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
					await Application.Current.Dispatcher.InvokeAsync(() =>
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
						await Application.Current.Dispatcher.InvokeAsync(() =>
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
        
		EnsureConnectivityLoop();
        IsLoggingIn = true;
        LoginStatus = "Подготовка к входу...";
        _loginCts?.Cancel();
        _loginCts = new System.Threading.CancellationTokenSource();
        
        try
        {
            var password = _accountsStorage.Unprotect(SelectedAccount.EncryptedPassword);
            _logger.Info($"Login requested for {SelectedAccount.Username}");
            
            LoginStatus = $"Вход в {SelectedAccount.Username}...";
            await _riotClientService.LoginAsync(SelectedAccount.Username, password, _loginCts.Token);
            
            LoginStatus = "Получение информации об аккаунте...";
            
            await Task.Delay(3000, _loginCts.Token);
            
            var accountInfo = await _riotClientService.GetAccountInfoAsync();
            if (accountInfo.HasValue && SelectedAccount != null)
            {
                var expectedUsername = SelectedAccount.Username;
                var loggedInUsername = accountInfo.Value.Username;
                
                // Проверяем, что вошли именно в тот аккаунт, который был выбран
                bool usernameMatches = false;
                if (!string.IsNullOrEmpty(loggedInUsername))
                {
                    // Сравниваем username без учета регистра и пробелов
                    usernameMatches = string.Equals(
                        expectedUsername.Trim(), 
                        loggedInUsername.Trim(), 
                        StringComparison.OrdinalIgnoreCase);
                    
                    if (!usernameMatches)
                    {
                        _logger.Warning($"Username mismatch! Expected: {expectedUsername}, Got: {loggedInUsername}. Skipping data update to prevent wrong account assignment.");
                        LoginStatus = $"Предупреждение: вошли в другой аккаунт ({loggedInUsername})";
                        await Task.Delay(3000, _loginCts.Token);
                        LoginStatus = string.Empty;
                        return;
                    }
                }
                
                _logger.Info($"Loading account info for {SelectedAccount.Username}: RiotId={accountInfo.Value.RiotId}, SummonerName={accountInfo.Value.SummonerName}, Rank={accountInfo.Value.Rank}, AvatarUrl={accountInfo.Value.AvatarUrl}, RankIconUrl={accountInfo.Value.RankIconUrl}, Username={loggedInUsername}");
                
                // Обновляем данные только для выбранного аккаунта
                SelectedAccount.AvatarUrl = accountInfo.Value.AvatarUrl;
                SelectedAccount.SummonerName = accountInfo.Value.SummonerName;
                SelectedAccount.Rank = accountInfo.Value.Rank;
                SelectedAccount.RankDisplay = accountInfo.Value.Rank;
                SelectedAccount.RiotId = accountInfo.Value.RiotId;
                SelectedAccount.RankIconUrl = accountInfo.Value.RankIconUrl;
                _accountsStorage.SaveAccounts(Accounts);
                _logger.Info($"Account info updated for {SelectedAccount.Username}: {accountInfo.Value.RiotId} ({accountInfo.Value.Rank})");
            }
            else if (SelectedAccount != null)
            {
                _logger.Warning($"Failed to get account info for {SelectedAccount.Username}");
            }
            
            LoginStatus = "Готово! Вход выполнен успешно";
            
            HasConnectionIssue = false;
            ConnectionIssueText = string.Empty;
            _lastConnectionIssueToast = string.Empty;
            
            await Task.Delay(1500, _loginCts.Token);
            LoginStatus = string.Empty;
        }
        catch (OperationCanceledException)
        {
            LoginStatus = "Вход отменён";
            await Task.Delay(1000);
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
			StopConnectivityLoop();
            _loginCts?.Dispose();
            _loginCts = null;
        }
    }

    [RelayCommand]
    private void CancelLogin()
    {
		try { _loginCts?.Cancel(); } catch { }
		StopConnectivityLoop();
    }

    [RelayCommand]
    private void OpenLogs()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _logger.LogFilePath, UseShellExecute = true }); }
        catch { }
    }

    private async Task AutoLoadAccountInfoAsync()
    {
        if (IsLoggingIn) return;
        
        try
        {
            var accountInfo = await _riotClientService.GetAccountInfoAsync();
            if (!accountInfo.HasValue)
            {
                _logger.Debug("AutoLoadAccountInfoAsync: No account info returned from LCU");
                return;
            }
            
            _logger.Info($"AutoLoadAccountInfoAsync: Got account info - RiotId={accountInfo.Value.RiotId}, SummonerName={accountInfo.Value.SummonerName}, Rank={accountInfo.Value.Rank}, AvatarUrl={accountInfo.Value.AvatarUrl}, RankIconUrl={accountInfo.Value.RankIconUrl}, Username={accountInfo.Value.Username}");
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AccountRecord? matchingAccount = null;
                
                // Сначала ищем по username (самый надежный способ)
                if (!string.IsNullOrEmpty(accountInfo.Value.Username))
                {
                    matchingAccount = Accounts.FirstOrDefault(a => 
                        string.Equals(a.Username.Trim(), accountInfo.Value.Username.Trim(), StringComparison.OrdinalIgnoreCase));
                    
                    if (matchingAccount != null)
                    {
                        _logger.Info($"AutoLoadAccountInfoAsync: Found matching account by Username: {matchingAccount.Username}");
                    }
                }
                
                // Если не нашли по username, ищем по RiotId
                if (matchingAccount == null && !string.IsNullOrEmpty(accountInfo.Value.RiotId))
                {
                    matchingAccount = Accounts.FirstOrDefault(a => 
                        !string.IsNullOrEmpty(a.RiotId) && a.RiotId == accountInfo.Value.RiotId);
                    
                    if (matchingAccount != null)
                    {
                        _logger.Info($"AutoLoadAccountInfoAsync: Found matching account by RiotId: {matchingAccount.Username}");
                    }
                }
                
                if (matchingAccount == null)
                {
                    _logger.Debug($"AutoLoadAccountInfoAsync: No matching account found for Username={accountInfo.Value.Username}, RiotId={accountInfo.Value.RiotId}. Skipping auto-update.");
                    return;
                }
                
                // Обновляем данные только если они изменились
                bool needsUpdate = false;
                
                if (matchingAccount.AvatarUrl != accountInfo.Value.AvatarUrl && !string.IsNullOrEmpty(accountInfo.Value.AvatarUrl))
                {
                    matchingAccount.AvatarUrl = accountInfo.Value.AvatarUrl;
                    needsUpdate = true;
                    _logger.Info($"AutoLoadAccountInfoAsync: Updated AvatarUrl to {accountInfo.Value.AvatarUrl}");
                }
                
                if (matchingAccount.SummonerName != accountInfo.Value.SummonerName && !string.IsNullOrEmpty(accountInfo.Value.SummonerName))
                {
                    matchingAccount.SummonerName = accountInfo.Value.SummonerName;
                    needsUpdate = true;
                }
                
                if (matchingAccount.Rank != accountInfo.Value.Rank && !string.IsNullOrEmpty(accountInfo.Value.Rank))
                {
                    matchingAccount.Rank = accountInfo.Value.Rank;
                    matchingAccount.RankDisplay = accountInfo.Value.Rank;
                    needsUpdate = true;
                    _logger.Info($"AutoLoadAccountInfoAsync: Updated Rank to {accountInfo.Value.Rank}");
                }
                else if (matchingAccount.RankDisplay != accountInfo.Value.Rank && !string.IsNullOrEmpty(accountInfo.Value.Rank))
                {
                    matchingAccount.RankDisplay = accountInfo.Value.Rank;
                    needsUpdate = true;
                }
                
                if (matchingAccount.RiotId != accountInfo.Value.RiotId && !string.IsNullOrEmpty(accountInfo.Value.RiotId))
                {
                    matchingAccount.RiotId = accountInfo.Value.RiotId;
                    needsUpdate = true;
                }
                
                if (matchingAccount.RankIconUrl != accountInfo.Value.RankIconUrl && !string.IsNullOrEmpty(accountInfo.Value.RankIconUrl))
                {
                    matchingAccount.RankIconUrl = accountInfo.Value.RankIconUrl;
                    needsUpdate = true;
                    _logger.Info($"AutoLoadAccountInfoAsync: Updated RankIconUrl to {accountInfo.Value.RankIconUrl}");
                }
                
                if (needsUpdate)
                {
                    _accountsStorage.SaveAccounts(Accounts);
                    _logger.Info($"Auto-updated account info for {matchingAccount.Username}: {accountInfo.Value.RiotId} ({accountInfo.Value.Rank})");
                    _lastAccountInfoUpdate = DateTime.UtcNow;
                }
                else
                {
                    _logger.Debug($"AutoLoadAccountInfoAsync: No updates needed for {matchingAccount.Username}");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to auto-load account info: {ex.Message}");
        }
    }

    public void SaveAccountsOrder()
    {
        _accountsStorage.SaveAccounts(Accounts);
        _logger.Info("Account order saved");
    }

    [RelayCommand]
    private async Task RefreshAccountInfo()
    {
        if (IsLoggingIn || SelectedAccount == null) return;
        
        try
        {
            LoginStatus = "Обновление данных аккаунта...";
            
            var accountInfo = await _riotClientService.GetAccountInfoAsync();
            if (!accountInfo.HasValue)
            {
                LoginStatus = "Не удалось получить данные из клиента";
                await Task.Delay(2000);
                LoginStatus = string.Empty;
                return;
            }
            
            var loggedInUsername = accountInfo.Value.Username;
            var expectedUsername = SelectedAccount.Username;
            
            // Проверяем соответствие username
            bool usernameMatches = false;
            if (!string.IsNullOrEmpty(loggedInUsername))
            {
                usernameMatches = string.Equals(
                    expectedUsername.Trim(), 
                    loggedInUsername.Trim(), 
                    StringComparison.OrdinalIgnoreCase);
            }
            
            if (!usernameMatches)
            {
                _logger.Warning($"RefreshAccountInfo: Username mismatch! Expected: {expectedUsername}, Got: {loggedInUsername}");
                LoginStatus = $"Предупреждение: в клиенте открыт другой аккаунт ({loggedInUsername ?? "неизвестно"})";
                await Task.Delay(3000);
                LoginStatus = string.Empty;
                return;
            }
            
            // Обновляем данные для выбранного аккаунта
            _logger.Info($"RefreshAccountInfo: Updating data for {SelectedAccount.Username}: RiotId={accountInfo.Value.RiotId}, SummonerName={accountInfo.Value.SummonerName}, Rank={accountInfo.Value.Rank}");
            
            SelectedAccount.AvatarUrl = accountInfo.Value.AvatarUrl;
            SelectedAccount.SummonerName = accountInfo.Value.SummonerName;
            SelectedAccount.Rank = accountInfo.Value.Rank;
            SelectedAccount.RankDisplay = accountInfo.Value.Rank;
            SelectedAccount.RiotId = accountInfo.Value.RiotId;
            SelectedAccount.RankIconUrl = accountInfo.Value.RankIconUrl;
            
            _accountsStorage.SaveAccounts(Accounts);
            
            LoginStatus = "Данные обновлены";
            await Task.Delay(1500);
            LoginStatus = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to refresh account info: {ex.Message}");
            LoginStatus = $"Ошибка: {ex.Message}";
            await Task.Delay(3000);
            LoginStatus = string.Empty;
        }
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

    private async Task RefreshFilteredLogsAsync()
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
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
    
    private void RefreshFilteredLogs()
    {
        _ = RefreshFilteredLogsAsync();
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
                var password = PasswordInputWindow.ShowDialog(
                    "Пароль для шифрования",
                    "Введите пароль для шифрования файла экспорта.\nФайл можно будет открыть на любом компьютере с этим паролем.",
                    Application.Current.MainWindow);
                
                if (password == null)
                {
                    return;
                }
                
                if (string.IsNullOrWhiteSpace(password))
                {
                    MessageWindow.Show("Пароль не может быть пустым.", "Ошибка", MessageWindow.MessageType.Error);
                    return;
                }

                _logger.Info($"Начат экспорт {selectedAccounts.Count} аккаунтов в файл: {Path.GetFileName(saveFileDialog.FileName)}");
                _accountsStorage.ExportAccounts(saveFileDialog.FileName, password, selectedAccounts);
                
                var message = $"Успешно экспортировано {selectedAccounts.Count} аккаунт(ов)!\n\nФайл: {Path.GetFileName(saveFileDialog.FileName)}\n\nПароль сохранен в файле. Запомните его для импорта на другом компьютере.";
                MessageWindow.Show(message, "Экспорт завершен", MessageWindow.MessageType.Information);
                _logger.Info($"Экспорт завершен успешно: {selectedAccounts.Count} аккаунтов");
                
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
                
                var fileExt = Path.GetExtension(selectedFile).ToLower();
                string? password = null;
                
                if (fileExt == ".lolm")
                {
                    try
                    {
                        var json = File.ReadAllText(selectedFile, Encoding.UTF8);
                        var jsonObj = JObject.Parse(json);
                        
                        if (jsonObj.ContainsKey("Version") && jsonObj["Version"]?.ToObject<int>() == 3)
                        {
                            password = PasswordInputWindow.ShowDialog(
                                "Пароль для расшифровки",
                                "Введите пароль для расшифровки файла экспорта.",
                                Application.Current.MainWindow);
                            
                            if (password == null)
                            {
                                return;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                
                var result = MessageWindow.Show(
                    "Импорт добавит новые аккаунты и обновит существующие с теми же именами.\n\nПродолжить импорт?", 
                    "Подтверждение импорта", 
                    MessageWindow.MessageType.Question, 
                    MessageWindow.MessageButtons.YesNo);
                
                if (result == true)
                {
                    var accountsCountBefore = Accounts.Count;
                    _accountsStorage.ImportAccounts(selectedFile, password);
                    
                    Accounts.Clear();
                    foreach (var acc in _accountsStorage.LoadAll())
                    {
                        acc.PropertyChanged += OnAccountSelectionChanged;
                        Accounts.Add(acc);
                    }
                    
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
            
            var errorMsg = ex.Message.Contains("пароль") || ex.Message.Contains("Неверный пароль")
                ? ex.Message
                : ex.Message.Contains("расшифровать") 
                    ? "Не удалось расшифровать файл. Проверьте правильность пароля или убедитесь, что файл не поврежден."
                    : $"Ошибка импорта: {ex.Message}";
            
            MessageWindow.Show(errorMsg, "Ошибка импорта", MessageWindow.MessageType.Error);
        }
    }

	[RelayCommand]
	private void ReportBug()
	{
		try
		{
			var url = "https://github.com/RaspizDIYs/lol-manager/issues/new";
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
			{
				FileName = url,
				UseShellExecute = true
			});
			_logger.Info($"Opened GitHub issue creation page: {url}");
		}
		catch (Exception ex)
		{
			_logger.Error($"Failed to open GitHub issues: {ex.Message}");
			MessageWindow.Show($"Ошибка открытия страницы GitHub: {ex.Message}", "Ошибка", MessageWindow.MessageType.Error);
		}
	}

	[RelayCommand]
	private void OpenGitHub()
	{
		try
		{
			var url = "https://github.com/RaspizDIYs/lol-manager";
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
			{
				FileName = url,
				UseShellExecute = true
			});
			_logger.Info($"Opened GitHub repository: {url}");
		}
		catch (Exception ex)
		{
			_logger.Error($"Failed to open GitHub repository: {ex.Message}");
			MessageWindow.Show($"Ошибка открытия GitHub: {ex.Message}", "Ошибка", MessageWindow.MessageType.Error);
		}
		}

	[RelayCommand]
	private void OpenDiscord()
	{
		try
		{
			var url = "https://discord.gg/dmx5GqHDcN";
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
			{
				FileName = url,
				UseShellExecute = true
			});
			_logger.Info($"Opened Discord: {url}");
		}
		catch (Exception ex)
		{
			_logger.Error($"Failed to open Discord: {ex.Message}");
			MessageWindow.Show($"Ошибка открытия Discord: {ex.Message}", "Ошибка", MessageWindow.MessageType.Error);
		}
	}

	[RelayCommand]
	private async Task TestRevealConnection()
	{
		try
		{
			RevealApiStatus = "Проверка подключения...";
			RevealApiStatusColor = "Orange";
			
			if (string.IsNullOrWhiteSpace(RevealSettings.RiotApiKey))
			{
				RevealApiStatus = "API ключ не указан";
				RevealApiStatusColor = "Red";
				return;
			}
			
			_revealService.SetApiConfiguration(RevealSettings.RiotApiKey, RevealSettings.SelectedRegion);
			var isValid = await _revealService.TestApiKeyAsync();
			
			if (isValid)
			{
				RevealApiStatus = $"Подключение успешно ({RevealSettings.SelectedRegion.ToUpper()})";
				RevealApiStatusColor = "Green";
			}
			else
			{
				RevealApiStatus = "Неверный API ключ или регион";
				RevealApiStatusColor = "Red";
			}
		}
		catch (Exception ex)
		{
			RevealApiStatus = $"Ошибка: {ex.Message}";
			RevealApiStatusColor = "Red";
			_logger.Error($"RevealConnection test failed: {ex.Message}");
		}
	}

	[RelayCommand]
	private async Task GetTeamInfo()
	{
		try
		{
			RevealTeamStatus = "Получение информации о команде...";
			RevealTeamStatusColor = "Orange";
			
			// Обновляем API ключ и регион в сервисе
			_revealService.SetApiConfiguration(RevealSettings.RiotApiKey, RevealSettings.SelectedRegion);
			
			var teamInfo = await _revealService.GetTeamInfoAsync();
			
			if (teamInfo != null && teamInfo.Count > 0)
			{
				TeamPlayers.Clear();
				foreach (var player in teamInfo)
				{
					TeamPlayers.Add(player);
				}
				
				HasTeamInfo = true;
				RevealTeamStatus = $"Найдено {teamInfo.Count} игроков";
				RevealTeamStatusColor = "Green";
			}
			else
			{
				RevealTeamStatus = "Команда не найдена. Нужно быть в чемпионском селекте.";
				RevealTeamStatusColor = "Orange";
				HasTeamInfo = false;
			}
		}
		catch (Exception ex)
		{
			RevealTeamStatus = $"Ошибка: {ex.Message}";
			RevealTeamStatusColor = "Red";
			_logger.Error($"GetTeamInfo failed: {ex.Message}");
			HasTeamInfo = false;
		}
	}

	[RelayCommand]
	private void OpenUgg(string uggLink)
	{
		try
		{
			if (!string.IsNullOrEmpty(uggLink))
			{
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
				{
					FileName = uggLink,
					UseShellExecute = true
				});
				_logger.Info($"Opened U.GG link: {uggLink}");
			}
		}
		catch (Exception ex)
		{
			_logger.Error($"Failed to open U.GG link: {ex.Message}");
		}
	}


}
