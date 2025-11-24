using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace LolManager.Services;

public static class BindingUtils
{
    private static readonly Dictionary<string, string> KeyCodeMapping = new()
    {
        ["Escape"] = "[Esc]",
        ["Digit1"] = "[1]", ["Digit2"] = "[2]", ["Digit3"] = "[3]", ["Digit4"] = "[4]", ["Digit5"] = "[5]",
        ["Digit6"] = "[6]", ["Digit7"] = "[7]", ["Digit8"] = "[8]", ["Digit9"] = "[9]", ["Digit0"] = "[0]",
        ["Minus"] = "[-]", ["Equal"] = "[=]", ["Backspace"] = "[Back]", ["Tab"] = "[Tab]",
        ["KeyQ"] = "[q]", ["KeyW"] = "[w]", ["KeyE"] = "[e]", ["KeyR"] = "[r]", ["KeyT"] = "[t]",
        ["KeyY"] = "[y]", ["KeyU"] = "[u]", ["KeyI"] = "[i]", ["KeyO"] = "[o]", ["KeyP"] = "[p]",
        ["BracketLeft"] = "[[]", ["BracketRight"] = "[]]", ["Enter"] = "[Return]",
        ["KeyA"] = "[a]", ["KeyS"] = "[s]", ["KeyD"] = "[d]", ["KeyF"] = "[f]", ["KeyG"] = "[g]",
        ["KeyH"] = "[h]", ["KeyJ"] = "[j]", ["KeyK"] = "[k]", ["KeyL"] = "[l]",
        ["Semicolon"] = "[Semicolon]", ["Quote"] = "[']", ["Backquote"] = "[`]", ["Backslash"] = "[Backslash]",
        ["KeyZ"] = "[z]", ["KeyX"] = "[x]", ["KeyC"] = "[c]", ["KeyV"] = "[v]", ["KeyB"] = "[b]",
        ["KeyN"] = "[n]", ["KeyM"] = "[m]", ["Comma"] = "[,]", ["Period"] = "[.]", ["Slash"] = "[/]",
        ["Space"] = "[Space]", ["Insert"] = "[Ins]", ["Delete"] = "[Del]", ["Home"] = "[Home]",
        ["End"] = "[End]", ["PageUp"] = "[PgUp]", ["PageDown"] = "[PgDn]",
        ["ArrowUp"] = "[Up Arrow]", ["ArrowDown"] = "[Down Arrow]", ["ArrowLeft"] = "[Left Arrow]", ["ArrowRight"] = "[Right Arrow]",
        ["F1"] = "[F1]", ["F2"] = "[F2]", ["F3"] = "[F3]", ["F4"] = "[F4]", ["F5"] = "[F5]",
        ["F6"] = "[F6]", ["F7"] = "[F7]", ["F8"] = "[F8]", ["F9"] = "[F9]", ["F10"] = "[F10]",
        ["F11"] = "[F11]", ["F12"] = "[F12]",
        ["Numpad0"] = "[Num0]", ["Numpad1"] = "[Num1]", ["Numpad2"] = "[Num2]", ["Numpad3"] = "[Num3]",
        ["Numpad4"] = "[Num4]", ["Numpad5"] = "[Num5]", ["Numpad6"] = "[Num6]", ["Numpad7"] = "[Num7]",
        ["Numpad8"] = "[Num8]", ["Numpad9"] = "[Num9]",
        ["NumpadAdd"] = "[Num+]", ["NumpadSubtract"] = "[Num-]", ["NumpadMultiply"] = "[Num*]",
        ["NumpadDivide"] = "[Num/]", ["NumpadDecimal"] = "[Num.]", ["NumpadEnter"] = "[NumEnter]",
        ["MetaLeft"] = "[L Win]", ["MetaRight"] = "[R Win]"
    };

    private static readonly Dictionary<string, string> KeyDisplayMapping = new()
    {
        ["[Esc]"] = "Esc", ["[1]"] = "1", ["[2]"] = "2", ["[3]"] = "3", ["[4]"] = "4", ["[5]"] = "5",
        ["[6]"] = "6", ["[7]"] = "7", ["[8]"] = "8", ["[9]"] = "9", ["[0]"] = "0",
        ["[-]"] = "-", ["[=]"] = "=", ["[Back]"] = "Backspace", ["[Tab]"] = "Tab",
        ["[q]"] = "Q", ["[w]"] = "W", ["[e]"] = "E", ["[r]"] = "R", ["[t]"] = "T",
        ["[y]"] = "Y", ["[u]"] = "U", ["[i]"] = "I", ["[o]"] = "O", ["[p]"] = "P",
        ["[[]"] = "[", ["[]]"] = "]", ["[Return]"] = "Enter",
        ["[a]"] = "A", ["[s]"] = "S", ["[d]"] = "D", ["[f]"] = "F", ["[g]"] = "G",
        ["[h]"] = "H", ["[j]"] = "J", ["[k]"] = "K", ["[l]"] = "L",
        ["[Semicolon]"] = ";", ["[']"] = "'", ["[`]"] = "`", ["[Backslash]"] = "\\",
        ["[z]"] = "Z", ["[x]"] = "X", ["[c]"] = "C", ["[v]"] = "V", ["[b]"] = "B",
        ["[n]"] = "N", ["[m]"] = "M", ["[,]"] = ",", ["[.]"] = ".", ["[/]"] = "/",
        ["[Space]"] = "Space", ["[Ins]"] = "Insert", ["[Del]"] = "Delete",
        ["[Home]"] = "Home", ["[End]"] = "End", ["[PgUp]"] = "PageUp", ["[PgDn]"] = "PageDown",
        ["[Up Arrow]"] = "↑", ["[Down Arrow]"] = "↓", ["[Left Arrow]"] = "←", ["[Right Arrow]"] = "→",
        ["[F1]"] = "F1", ["[F2]"] = "F2", ["[F3]"] = "F3", ["[F4]"] = "F4", ["[F5]"] = "F5",
        ["[F6]"] = "F6", ["[F7]"] = "F7", ["[F8]"] = "F8", ["[F9]"] = "F9", ["[F10]"] = "F10",
        ["[F11]"] = "F11", ["[F12]"] = "F12",
        ["[Num0]"] = "Num0", ["[Num1]"] = "Num1", ["[Num2]"] = "Num2", ["[Num3]"] = "Num3",
        ["[Num4]"] = "Num4", ["[Num5]"] = "Num5", ["[Num6]"] = "Num6", ["[Num7]"] = "Num7",
        ["[Num8]"] = "Num8", ["[Num9]"] = "Num9",
        ["[Num+]"] = "Num+", ["[Num-]"] = "Num-", ["[Num*]"] = "Num*",
        ["[Num/]"] = "Num/", ["[Num.]"] = "Num.", ["[NumEnter]"] = "NumEnter",
        ["[L Win]"] = "Win", ["[R Win]"] = "Win"
    };

    public static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static string[] FromSavedToArray(string saved)
    {
        if (string.IsNullOrEmpty(saved))
            return Array.Empty<string>();

        if (saved.IndexOf("[,]", StringComparison.Ordinal) == -1)
        {
            return saved.Split(',', StringSplitOptions.RemoveEmptyEntries);
        }

        return SplitSaved(saved);
    }

    private static string[] SplitSaved(string e)
    {
        for (int n = e.IndexOf(','); n != -1;)
        {
            if (n == 0)
            {
                return new[] { "", e.Substring(1) };
            }

            if (e[n - 1] != '[')
            {
                return new[] { e.Substring(0, n), e.Substring(n + 1) };
            }

            n = e.IndexOf(',', n + 1);
        }

        return new[] { e };
    }

    public static string GetModifiers(bool shiftKey, bool ctrlKey, bool altKey, bool metaKey)
    {
        var result = string.Empty;
        
        if (metaKey)
        {
            result += IsWindows() ? "[Win]" : "[Cmd]";
        }
        
        if (shiftKey) result += "[Shift]";
        if (ctrlKey) result += "[Ctrl]";
        if (altKey) result += "[Alt]";
        
        return result;
    }

    public static string FormatBinding(string binding)
    {
        if (string.IsNullOrEmpty(binding))
            return string.Empty;

        var parts = ExtractModifiers(binding);
        if (parts.Count == 0)
            return binding;

        parts.Sort((a, b) => GetModifierOrder(a).CompareTo(GetModifierOrder(b)));

        var modifiers = parts.Where(p => p != parts.Last()).Select(FormatModifier).ToList();
        var mainKey = parts.Last();

        if (KeyDisplayMapping.TryGetValue(mainKey, out var displayKey))
        {
            return modifiers.Count > 0 ? string.Join(" + ", modifiers) + " + " + displayKey : displayKey;
        }

        return binding;
    }

    private static List<string> ExtractModifiers(string binding)
    {
        var result = new List<string>();
        var regex = new Regex(@"\[([^\]]+)\]");
        var matches = regex.Matches(binding);

        foreach (Match match in matches)
        {
            result.Add("[" + match.Groups[1].Value + "]");
        }

        return result;
    }

    private static int GetModifierOrder(string modifier)
    {
        return modifier.ToLowerInvariant() switch
        {
            "[ctrl]" => 1,
            "[alt]" => 2,
            "[shift]" => 3,
            "[cmd]" or "[win]" => 4,
            _ => 5
        };
    }

    private static string FormatModifier(string modifier)
    {
        return modifier.ToLowerInvariant() switch
        {
            "[ctrl]" => "Ctrl",
            "[alt]" => "Alt",
            "[shift]" => "Shift",
            "[cmd]" => "Cmd",
            "[win]" => "Win",
            _ => modifier
        };
    }

    public static string NormalizeKeybindingString(string binding)
    {
        if (string.IsNullOrEmpty(binding))
            return string.Empty;

        var lower = binding.ToLowerInvariant();
        var parts = ExtractModifiers(lower);
        
        if (parts.Count > 2)
        {
            parts.Sort((a, b) => GetModifierOrder(a).CompareTo(GetModifierOrder(b)));
        }

        return string.Join("", parts);
    }
}

