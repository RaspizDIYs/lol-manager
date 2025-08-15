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
    public void Warning(string message) => Write("WARN", message);
    public void Debug(string message) => Write("DEBUG", message);
    
    public void HttpRequest(string method, string url, int statusCode, string? response = null)
    {
        var statusText = GetStatusText(statusCode);
        var message = response != null 
            ? $"🌐 {method} {url} -> {statusCode} {statusText} | {response}"
            : $"🌐 {method} {url} -> {statusCode} {statusText}";
        Write("HTTP", message);
    }
    
    public void ProcessEvent(string processName, string action, string? details = null)
    {
        var message = details != null 
            ? $"⚙️  {processName} | {action} | {details}"
            : $"⚙️  {processName} | {action}";
        Write("PROC", message);
    }
    
    public void UiEvent(string component, string action, string? result = null)
    {
        var message = result != null 
            ? $"🖱️  {component} -> {action} | {result}"
            : $"🖱️  {component} -> {action}";
        Write("UI", message);
    }
    
    public void LoginFlow(string step, string? details = null)
    {
        var message = details != null 
            ? $"🔐 {step} | {details}"
            : $"🔐 {step}";
        Write("LOGIN", message);
    }

    private void Write(string level, string message)
    {
        lock (_lock)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logEntry = FormatLogEntry(timestamp, level, message);
            File.AppendAllText(_logFile, logEntry + "\r\n", Encoding.UTF8);
        }
    }

    private static string GetStatusText(int statusCode) => statusCode switch
    {
        200 => "OK",
        201 => "Created",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        500 => "Internal Server Error",
        _ => "Unknown"
    };

    private static string FormatLogEntry(string timestamp, string level, string message)
    {
        var levelPadded = level switch
        {
            "INFO" => "INFO ",
            "ERROR" => "ERROR",
            "WARN" => "WARN ",
            "DEBUG" => "DEBUG",
            "HTTP" => "HTTP ",
            "PROC" => "PROC ",
            "UI" => "UI   ",
            "LOGIN" => "LOGIN",
            _ => level.PadRight(5)
        };

        // Если сообщение содержит структурированные данные, форматируем их
        if (message.Contains(" -> ") || message.Contains(" | "))
        {
            return $"[{timestamp}] {levelPadded} {FormatStructuredMessage(message)}";
        }

        // Обычные сообщения
        return $"[{timestamp}] {levelPadded} {message}";
    }

    private static string FormatStructuredMessage(string message)
    {
        // HTTP запросы с иконками: "🌐 GET /path -> 200 OK | response"
        if (message.StartsWith("🌐") && message.Contains(" -> ") && message.Contains(" | "))
        {
            var parts = message.Split(new[] { " -> ", " | " }, StringSplitOptions.None);
            if (parts.Length >= 3)
            {
                var request = parts[0].Trim();
                var status = parts[1].Trim();
                var response = parts[2].Trim();
                
                // Укорачиваем длинные JSON ответы
                if (response.Length > 100)
                {
                    response = response.Substring(0, 97) + "...";
                }
                
                return $"{request}\n    └─ Status: {status}\n    └─ Response: {response}";
            }
        }
        
        // HTTP запросы без response: "🌐 GET /path -> 200 OK"
        if (message.StartsWith("🌐") && message.Contains(" -> "))
        {
            var parts = message.Split(" -> ", StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                var request = parts[0].Trim();
                var status = parts[1].Trim();
                
                return $"{request}\n    └─ Status: {status}";
            }
        }
        
        // Process события: "⚙️ ProcessName | Action | Details"
        if (message.StartsWith("⚙️") && message.Contains(" | "))
        {
            var parts = message.Split(" | ", StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                var process = parts[0].Trim();
                var action = parts[1].Trim();
                var details = parts.Length > 2 ? parts[2].Trim() : null;
                
                return details != null 
                    ? $"{process}\n    └─ Action: {action}\n    └─ Details: {details}"
                    : $"{process}\n    └─ Action: {action}";
            }
        }
        
        // UI события: "🖱️ Component -> Action | Result"
        if (message.StartsWith("🖱️") && message.Contains(" -> "))
        {
            var parts = message.Split(new[] { " -> ", " | " }, StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                var component = parts[0].Trim();
                var action = parts[1].Trim();
                var result = parts.Length > 2 ? parts[2].Trim() : null;
                
                return result != null 
                    ? $"{component}\n    └─ Action: {action}\n    └─ Result: {result}"
                    : $"{component}\n    └─ Action: {action}";
            }
        }
        
        // Login flow: "🔐 Step | Details"
        if (message.StartsWith("🔐") && message.Contains(" | "))
        {
            var parts = message.Split(" | ", StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                var step = parts[0].Trim();
                var details = parts[1].Trim();
                
                return $"{step}\n    └─ {details}";
            }
        }

        // Обычные структурированные сообщения (старый формат)
        if (message.Contains(" -> ") && message.Contains(" | "))
        {
            var parts = message.Split(new[] { " -> ", " | " }, StringSplitOptions.None);
            if (parts.Length >= 3)
            {
                var request = parts[0].Trim();
                var status = parts[1].Trim();
                var response = parts[2].Trim();
                
                return $"{request}\n    └─ {status}\n    └─ {response}";
            }
        }
        
        if (message.Contains(" -> "))
        {
            var parts = message.Split(" -> ", StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                var request = parts[0].Trim();
                var status = parts[1].Trim();
                
                return $"{request}\n    └─ {status}";
            }
        }

        return message;
    }
}


