using System;
using System.IO;
using System.Text;

namespace LolManager.Services;

public class FileLogger : ILogger
{
    private readonly string _logFile;
    private readonly object _lock = new();

    public FileLogger()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LolManager");
        Directory.CreateDirectory(dir);
        _logFile = Path.Combine(dir, "debug.log");
    }

    public string LogFilePath => _logFile;

    public void Info(string message) => Write("INFO", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        lock (_lock)
        {
            File.AppendAllText(_logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}\r\n", Encoding.UTF8);
        }
    }
}


