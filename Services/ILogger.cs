using System;

namespace LolManager.Services;

public interface ILogger
{
    void Info(string message);
    void Error(string message);
    void Warning(string message);
    void Debug(string message);
    void HttpRequest(string method, string url, int statusCode, string? response = null);
    void ProcessEvent(string processName, string action, string? details = null);
    void UiEvent(string component, string action, string? result = null);
    void LoginFlow(string step, string? details = null);
    string LogFilePath { get; }
}


