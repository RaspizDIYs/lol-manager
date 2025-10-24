using System;

namespace LolManager.Models;

public enum AutoAcceptMethod
{
    WebSocket,
    Polling,
    UIA,
    Auto
}

public static class AutoAcceptMethodExtensions
{
    public static AutoAcceptMethod Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return AutoAcceptMethod.Polling;
        if (Enum.TryParse<AutoAcceptMethod>(value, true, out var method)) return method;
        return AutoAcceptMethod.Polling;
    }
}


