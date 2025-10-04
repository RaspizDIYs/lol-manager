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
    
    protected override void OnStartup(StartupEventArgs e)
    {
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
        _ipcThread = new Thread(() =>
        {
            while (true)
            {
                if (_showEvent.WaitOne())
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (MainWindow != null)
                        {
                            MainWindow.Show();
                            MainWindow.WindowState = WindowState.Normal;
                            MainWindow.Activate();
                        }
                    });
                }
            }
        })
        {
            IsBackground = true
        };
        _ipcThread.Start();
        
        try
        {
            VelopackApp.Build().Run();
        }
        catch { }
        
        // Регистрация сервисов
        RegisterServices();
        
        _logger = GetService<ILogger>();
        _logger?.Info("[APP] Инициализация трея на уровне приложения");
        
        InitializeTrayIcon();
        
        base.OnStartup(e);
        
        // Автоматическая проверка обновлений при запуске (без ожидания)
        _ = CheckForUpdatesOnStartupAsync();
        
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
        var autoAcceptService = new AutoAcceptService(logger, riotClientService, dataDragonService);
        
        _services[typeof(ILogger)] = logger;
        _services[typeof(ISettingsService)] = settingsService;
        _services[typeof(IRiotClientService)] = riotClientService;
        _services[typeof(DataDragonService)] = dataDragonService;
        _services[typeof(AutoAcceptService)] = autoAcceptService;
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

            TrayIcon.ToolTipText = "LoL Account Manager";
            
            var contextMenu = new ContextMenu();
            
            var showItem = new MenuItem { Header = "Открыть" };
            showItem.Click += (s, e) =>
            {
                _logger?.Info("[APP] Трей: Открыть");
                if (MainWindow != null)
                {
                    MainWindow.Show();
                    MainWindow.WindowState = WindowState.Normal;
                    MainWindow.Activate();
                }
            };
            
            var exitItem = new MenuItem { Header = "Закрыть" };
            exitItem.Click += (s, e) =>
            {
                _logger?.Info("[APP] Трей: Закрыть приложение");
                Shutdown();
            };
            
            contextMenu.Items.Add(showItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);
            
            TrayIcon.ContextMenu = contextMenu;
            
            TrayIcon.TrayMouseDoubleClick += (s, e) =>
            {
                _logger?.Info("[APP] Двойной клик по трею");
                if (MainWindow != null)
                {
                    MainWindow.Show();
                    MainWindow.WindowState = WindowState.Normal;
                    MainWindow.Activate();
                }
            };
            
            TrayIcon.Visibility = Visibility.Visible;
            _logger?.Info($"[APP] TaskbarIcon инициализирован, Visibility={TrayIcon.Visibility}");
        }
        catch (Exception ex)
        {
            _logger?.Error($"[APP] КРИТИЧЕСКАЯ ОШИБКА инициализации трея: {ex}");
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            // Даём время на загрузку MainWindow
            await Task.Delay(3000);
            
            _logger?.Info("[APP] 🔄 Автоматическая проверка обновлений при запуске...");
            
            var settingsService = GetService<ISettingsService>();
            var updateService = new UpdateService(_logger!, settingsService);
            
            var hasUpdates = await updateService.CheckForUpdatesAsync(forceCheck: false);
            
            if (hasUpdates)
            {
                _logger?.Info("[APP] ✅ Найдены обновления!");
                
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (MainWindow != null)
                        {
                            var updateWindow = new Views.UpdateWindow(updateService);
                            updateWindow.Owner = MainWindow;
                            updateWindow.ShowDialog();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error($"[APP] Ошибка показа окна обновления: {ex.Message}");
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
        try { _heartbeat?.Stop(); _heartbeat?.Dispose(); } catch { }
        try { _showEvent?.Dispose(); } catch { }
        try { TrayIcon?.Dispose(); } catch { }
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

