using System.Windows;
using System;
using System.Diagnostics;
using System.Timers;
using Velopack;

namespace LolManager;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static System.Timers.Timer? _heartbeat;
    
    protected override void OnStartup(StartupEventArgs e)
    {
        // Инициализация Velopack в самом начале
        try
        {
            VelopackApp.Build().Run();
        }
        catch { }
        
        base.OnStartup(e);
        
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

    protected override void OnExit(ExitEventArgs e)
    {
        try { _heartbeat?.Stop(); _heartbeat?.Dispose(); } catch { }
        base.OnExit(e);
    }
}

