using System;

namespace LolManager.Services;

public interface ILogger
{
    void Info(string message);
    void Error(string message);
    string LogFilePath { get; }
}


