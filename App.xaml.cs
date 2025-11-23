using System.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Timers;
using System.Threading;
using Velopack;
using LolManager.Services;
using System.IO.MemoryMappedFiles;
using System.IO;
using System.Text;
using H.NotifyIcon;
using System.Windows.Controls;

namespace LolManager;

public partial class App : Application
{
    private static System.Timers.Timer? _heartbeat;
    private readonly Dictionary<Type, object> _services = new();
    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;
    private Thread? _ipcThread;
    public TaskbarIcon? TrayIcon { get; private set; }
    private ILogger? _logger;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private static bool _isHandlingException = false;
    
    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        if (_isHandlingException)
        {
            // Предотвращаем рекурсию
            e.Handled = true;
            return;
        }
        
        _isHandlingException = true;
        _logger?.Error($"[APP] Необработанная ошибка: {e.Exception}");
        
        try
        {
            // Используем только стандартный MessageBox для избежания рекурсии
            MessageBox.Show($"Произошла ошибка: {e.Exception.Message}\n\nПриложение продолжит работу.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // Если даже MessageBox не работает, просто игнорируем
        }
        finally
        {
            _isHandlingException = false;
        }
        
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        _logger?.Error($"[APP] Критическая ошибка: {exception}");
        try
        {
            Views.MessageWindow.Show($"Критическая ошибка: {exception?.Message ?? "Неизвестная ошибка"}", "Критическая ошибка", Views.MessageWindow.MessageType.Error);
        }
        catch
        {
            // Fallback если наше окно не работает
            MessageBox.Show($"Критическая ошибка: {exception?.Message ?? "Неизвестная ошибка"}", "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    protected override void OnStartup(StartupEventArgs e)
    {
        // Velopack должен быть самым первым!
        try 
        {
            VelopackApp.Build().Run();
        }
        catch (Exception ex)
        {
            // Logger еще не инициализирован
            Trace.WriteLine($"[APP] Velopack startup error: {ex.Message}");
        }

        const string mutexName = "LolManager_SingleInstance_Mutex";
        const string eventName = "LolManager_ShowWindow_Event";
        
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        
        if (!createdNew)
        {
            try
            {
                var showEvent = EventWaitHandle.OpenExisting(eventName);
                showEvent.Set();
            }
            catch { }
            
            Shutdown();
            return;
        }
        
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        
        // Регистрация сервисов
        RegisterServices();
        
        _logger = GetService<ILogger>();
        _logger?.Info("[APP] Инициализация трея на уровне приложения");
        
        InitializeTrayIcon();
        
        base.OnStartup(e);

        // Явное создание главного окна
        MainWindow = new Views.MainWindow();
        MainWindow.Show();
        
        // Запускаем прослушку второй копии ТОЛЬКО когда окно уже готово
        _ipcThread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (_showEvent.WaitOne())
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (MainWindow != null)
                            {
                                if (MainWindow.WindowState == WindowState.Minimized)
                                    MainWindow.WindowState = WindowState.Normal;
                                    
                                MainWindow.Show();
                                MainWindow.Activate();
                                MainWindow.Topmost = true;  // Временный Topmost чтобы точно вылезти
                                MainWindow.Topmost = false;
                                MainWindow.Focus();
                            }
                        });
                    }
                }
                catch { }
            }
        })
        {
            IsBackground = true,
            Name = "IPC Listener"
        };
        _ipcThread.Start();
        
        // Автоматическая проверка обновлений при запуске (без ожидания)
        _ = CheckForUpdatesOnStartupAsync();

        // Восстановление данных после обновления (если есть флаг)
        try
        {
            GetService<IUpdateService>().ValidateUserDataAfterUpdate();
            GetService<IUpdateService>().CleanupInstallerCache();
        }
        catch { }
        
        try
        {
            _heartbeat = new System.Timers.Timer(60_000);
            _heartbeat.AutoReset = true;
            _heartbeat.Elapsed += (_, __) =>
            {
                try
                {
                    using var p = Process.GetCurrentProcess();
                    var ws = p.WorkingSet64;
                    var cpu = p.TotalProcessorTime;
                    var th = p.Threads.Count;
                    var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var line = $"{ts} [HEARTBEAT] WS={(ws/1024/1024)}MB CPU={cpu} Threads={th}";
                    var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LolManager", "debug.log");
                    System.IO.File.AppendAllText(path, line + "\r\n");
                }
                catch { }
            };
            _heartbeat.Start();
        }
        catch { }
    }

    private void RegisterServices()
    {
        var logger = new FileLogger();
        var settingsService = new SettingsService();
        var riotClientService = new RiotClientService(logger);
        var dataDragonService = new DataDragonService(logger);
        var autoAcceptService = new AutoAcceptService(logger, riotClientService, dataDragonService, settingsService);
        var runeDataService = new RuneDataService();
        var runePagesStorage = new RunePagesStorage(logger);
        var updateService = new UpdateService(logger, settingsService);
        var customizationService = new CustomizationService(logger, riotClientService);
        
        _services[typeof(ILogger)] = logger;
        _services[typeof(ISettingsService)] = settingsService;
        _services[typeof(IRiotClientService)] = riotClientService;
        _services[typeof(DataDragonService)] = dataDragonService;
        _services[typeof(AutoAcceptService)] = autoAcceptService;
        _services[typeof(RuneDataService)] = runeDataService;
        _services[typeof(IRunePagesStorage)] = runePagesStorage;
        _services[typeof(IUpdateService)] = updateService;
        _services[typeof(CustomizationService)] = customizationService;
    }

    public T GetService<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var service))
        {
            return (T)service;
        }
        throw new InvalidOperationException($"Service of type {typeof(T).Name} not registered");
    }

    private void InitializeTrayIcon()
    {
        try
        {
            _logger?.Info("[APP] Создание TaskbarIcon...");
            
            TrayIcon = new TaskbarIcon();
            _logger?.Info("[APP] TaskbarIcon создан");

            try
            {
                var iconUri = new Uri("pack://application:,,,/icon.ico");
                var iconBitmap = new System.Windows.Media.Imaging.BitmapImage(iconUri);
                TrayIcon.IconSource = iconBitmap;
                _logger?.Info($"[APP] Иконка загружена: {iconBitmap.Width}x{iconBitmap.Height}");
            }
            catch (Exception ex)
            {
                _logger?.Error($"[APP] Ошибка загрузки иконки: {ex}");
            }

            TrayIcon.ToolTipText = "LoL Account Manager\n(F12 - показать окно)";
            
            var contextMenu = new ContextMenu();
            
            var showItem = new MenuItem { Header = "Открыть (F12)" };
            showItem.Click += (s, e) =>
            {
                _logger?.Info("[APP] Трей: Открыть");
                Dispatcher.Invoke(() =>
                {
                    if (MainWindow != null)
                    {
                        MainWindow.Show();
                        MainWindow.WindowState = WindowState.Normal;
                        MainWindow.Activate();
                    }
                });
            };
            
            var exitItem = new MenuItem { Header = "Закрыть" };
            exitItem.Click += (s, e) =>
            {
                _logger?.Info("[APP] Трей: Закрыть приложение");
                Dispatcher.Invoke(() => Shutdown());
            };
            
            contextMenu.Items.Add(showItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);
            
            TrayIcon.ContextMenu = contextMenu;
            
            TrayIcon.TrayMouseDoubleClick += (s, e) =>
            {
                _logger?.Info("[APP] Двойной клик по трею");
                Dispatcher.Invoke(() =>
                {
                    if (MainWindow != null)
                    {
                        MainWindow.Show();
                        MainWindow.WindowState = WindowState.Normal;
                        MainWindow.Activate();
                    }
                });
            };
            
            TrayIcon.Visibility = Visibility.Visible;
            TrayIcon.ForceCreate();
            _logger?.Info($"[APP] TaskbarIcon инициализирован и отображен, Visibility={TrayIcon.Visibility}");
        }
        catch (Exception ex)
        {
            _logger?.Error($"[APP] КРИТИЧЕСКАЯ ОШИБКА инициализации трея: {ex}");
            TrayIcon = null;
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            // Даём время на загрузку MainWindow
            await Task.Delay(3000);
            
            _logger?.Info("[APP] 🔄 Автоматическая проверка обновлений при запуске...");
            
            var updateService = GetService<IUpdateService>();

            // Соблюдаем интервал проверки, если автообновления включены
            var settings = GetService<ISettingsService>().LoadUpdateSettings();
            if (!settings.AutoUpdateEnabled)
            {
                _logger?.Info("[APP] Автообновления выключены — пропускаем автоматическую проверку");
                return;
            }
            var nextAllowed = settings.LastCheckTime.AddHours(Math.Max(1, settings.CheckIntervalHours));
            if (DateTime.UtcNow < nextAllowed)
            {
                _logger?.Info($"[APP] Рано проверять (до {nextAllowed:u}), пропускаем");
                return;
            }
            
            var hasUpdates = await updateService.CheckForUpdatesAsync(forceCheck: false);
            
            if (hasUpdates)
            {
                _logger?.Info("[APP] ✅ Найдены обновления!");
                
                // Показываем уведомление внутри главного окна
                Dispatcher.Invoke(() =>
                {
                    if (MainWindow is Views.MainWindow mainWin)
                    {
                        var versionToShow = updateService.LatestAvailableVersion ?? updateService.CurrentVersion;
                        mainWin.ShowUpdateNotification(versionToShow, async () =>
                        {
                            await updateService.UpdateAsync();
                        });
                    }
                });
            }
            else
            {
                _logger?.Info("[APP] ℹ️ Обновлений не найдено или интервал проверки не истёк.");
            }
        }
        catch (Exception ex)
        {
            _logger?.Error($"[APP] Ошибка проверки обновлений при запуске: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("[APP] Выход из приложения, очистка ресурсов");
        try { GetService<AutoAcceptService>()?.Shutdown(); } catch { }
        try { _heartbeat?.Stop(); _heartbeat?.Dispose(); } catch { }
        try { _showEvent?.Dispose(); } catch { }
        try { TrayIcon?.Dispose(); } catch { }
        
        if (_mutex != null)
        {
            try 
            { 
                _mutex.ReleaseMutex(); 
            } 
            catch (ApplicationException) 
            { 
                // Мьютекс не был захвачен текущим потоком - это нормально при закрытии
            }
            _mutex.Dispose();
        }
        
        base.OnExit(e);
    }
}

