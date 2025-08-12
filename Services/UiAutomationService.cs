using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using LolManager.Services; // for ILogger
// UIA-библиотека не используется в финальной минималистичной логике (ориентируемся на кейборд-инпут)

namespace LolManager.Services;

public class UiAutomationService : IUiAutomationService
{
    private readonly ILogger _logger;

    // Тонкая настройка скоростей (ускоряем до предела)
    private static class Tunables
    {
        public static int WaitUiaFieldsMs = 1000;      // ожидание появления полей (UIA)
        public static int AfterCenterClickMs = 60;     // пауза после клика в центр
        public static int StepDelayMs = 40;            // между шагами (TAB/вставка)
        public static int BeforeSubmitMs = 60;         // перед Enter
        public static int FallbackButtonDelayMs = 80;  // перед кликом по кнопке
    }

    public UiAutomationService() : this(new FileLogger()) { }
    public UiAutomationService(ILogger logger)
    {
        _logger = logger;
    }
    public bool FocusRiotClient()
    {
        try
        {
            var hWnd = FindRiotClientWindow();
            if (hWnd == IntPtr.Zero)
            {
                _logger.Info("[UI] RiotClient window not found");
                return false;
            }
            var ok = BringToFront(hWnd);
            _logger.Info($"[UI] FocusRiotClient hWnd=0x{hWnd.ToInt64():X} ok={ok}");
            return ok;
        }
        catch { return false; }
    }

    public bool TryLogin(string username, string password)
    {
        try
        {
            var hWnd = FindRiotClientWindow();
            if (hWnd == IntPtr.Zero)
            {
                _logger.Info("[UI] TryLogin: window not found");
                return false;
            }

            if (!EnsureForeground(hWnd))
            {
                _logger.Info("[UI] TryLogin: EnsureForeground failed");
                return false;
            }
            // Дождаться, пока RC дорисует страницу (стабильные размеры окна до 12с)
            if (!WaitForStableWindow(hWnd, TimeSpan.FromSeconds(12)))
            {
                _logger.Info("[UI] Window size not stabilized within timeout");
            }

            // Дождаться появления полей ввода (UIA), чтобы не стрелять TAB до готовности
            if (!WaitForLoginFieldsUIA(hWnd, TimeSpan.FromMilliseconds(Tunables.WaitUiaFieldsMs)))
            {
                _logger.Info($"[UI] UIA login fields not detected within {Tunables.WaitUiaFieldsMs}ms, continuing with keyboard flow");
            }

            // Детерминированный быстрый сценарий:
            // центр → короткая пауза → ввод логина → TAB к паролю → ввод → Enter
            EnsureForeground(hWnd);
            ClickCenter(hWnd);
            Thread.Sleep(Tunables.AfterCenterClickMs);
            EnsureForeground(hWnd);
            Paste(username);
            Thread.Sleep(Tunables.StepDelayMs);
            EnsureForeground(hWnd);
            SendKeyPress(VK_TAB);
            Thread.Sleep(Tunables.StepDelayMs);
            EnsureForeground(hWnd);
            Paste(password);
            Thread.Sleep(Tunables.StepDelayMs);
            EnsureForeground(hWnd);
            SendKeyPress(VK_RETURN);
            _logger.Info("[UI] KBLogin (fast->user, Tab->pass) submitted");

            // Добив: клик в область кнопки Войти + Enter, если первый Enter не сработал
            Thread.Sleep(Tunables.FallbackButtonDelayMs);
            if (GetWindowRect(hWnd, out var r2))
            {
                int w = Math.Max(400, r2.Right - r2.Left);
                int h = Math.Max(300, r2.Bottom - r2.Top);
                int buttonX = r2.Left + (int)(w * 0.40);
                int buttonY = r2.Top + (int)(h * 0.60);
                _logger.Info($"[UI] Click login button fallback at ({buttonX},{buttonY})");
                EnsureForeground(hWnd);
                ClickAt(hWnd, buttonX, buttonY);
                Thread.Sleep(Tunables.StepDelayMs);
                EnsureForeground(hWnd);
                SendKeyPress(VK_RETURN);
            }
            return true;
        }
        catch (Exception ex) { _logger.Error($"[UI] TryLogin error: {ex.Message}"); return false; }
    }

    private bool TryLoginOnce(IntPtr hWnd, string username, string password)
    {
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            bool loggedEditsOnce = false;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using (var automation = new UIA3Automation())
                    {
                        var root = automation.FromHandle(hWnd);
                        var window = root.AsWindow();
                        var edits = window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
                        if (!loggedEditsOnce)
                        {
                            _logger.Info($"[UI] TryLoginOnce: edits={edits.Length}");
                            loggedEditsOnce = true;
                        }
                        if (edits.Length >= 2)
                        {
                            var loginBox = edits.FirstOrDefault(e => (e.Name ?? string.Empty).IndexOf("email", StringComparison.OrdinalIgnoreCase) >= 0
                                                                      || (e.Name ?? string.Empty).IndexOf("логин", StringComparison.OrdinalIgnoreCase) >= 0
                                                                      || (e.Name ?? string.Empty).IndexOf("имя пользователя", StringComparison.OrdinalIgnoreCase) >= 0
                                                                      || (e.AutomationId ?? string.Empty).IndexOf("username", StringComparison.OrdinalIgnoreCase) >= 0)
                                           ?? edits.ElementAtOrDefault(0);
                            var passBox = edits.FirstOrDefault(e => (e.Name ?? string.Empty).IndexOf("pass", StringComparison.OrdinalIgnoreCase) >= 0
                                                                    || (e.Name ?? string.Empty).IndexOf("пароль", StringComparison.OrdinalIgnoreCase) >= 0
                                                                    || (e.AutomationId ?? string.Empty).IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
                                           ?? edits.ElementAtOrDefault(Math.Min(1, edits.Length - 1));

                            if (loginBox != null && passBox != null)
                            {
                                _logger.Info($"[UI] TryLoginOnce: loginBox='{loginBox.Name}', passBox='{passBox.Name}'");
                                // Активация окна/CEF
                                ClickCenter(hWnd);
                                Thread.Sleep(120);
                                EnsureForeground(hWnd);

                                loginBox.AsTextBox()?.Focus();
                                Thread.Sleep(80);
                                if (GetForegroundWindow() != hWnd) { EnsureForeground(hWnd); Thread.Sleep(50); }
                                Paste(username);
                                Thread.Sleep(100);

                                passBox.AsTextBox()?.Focus();
                                Thread.Sleep(80);
                                if (GetForegroundWindow() != hWnd) { EnsureForeground(hWnd); Thread.Sleep(50); }
                                Paste(password);
                                Thread.Sleep(100);

                                if (GetForegroundWindow() != hWnd) { EnsureForeground(hWnd); Thread.Sleep(50); }
                                SendKeyPress(VK_RETURN);
                                _logger.Info("[UI] TryLoginOnce submitted");
                                return true;
                            }
                        }
                    }
                }
                catch { }
                Thread.Sleep(250);
            }
            _logger.Info("[UI] TryLoginOnce: login fields not available within 10s");
        }
        catch (Exception ex) { _logger.Error($"[UI] TryLoginOnce error: {ex.Message}"); }
        return false;
    }

    private bool TryLoginFallback(IntPtr hWnd, string username, string password)
    {
        try
        {
            if (GetForegroundWindow() != hWnd) { EnsureForeground(hWnd); Thread.Sleep(50); }
            ClickCenter(hWnd);
            Thread.Sleep(150);
            for (int i = 0; i < 10; i++) { SendKeyCombo(VK_SHIFT, VK_TAB); Thread.Sleep(60); }
            if (GetForegroundWindow() != hWnd) { EnsureForeground(hWnd); Thread.Sleep(50); }
            Paste(username);
            Thread.Sleep(100);
            SendKeyPress(VK_TAB);
            Thread.Sleep(80);
            if (GetForegroundWindow() != hWnd) { EnsureForeground(hWnd); Thread.Sleep(50); }
            Paste(password);
            Thread.Sleep(100);
            if (GetForegroundWindow() != hWnd) { EnsureForeground(hWnd); Thread.Sleep(50); }
            SendKeyPress(VK_RETURN);
            _logger.Info("[UI] TryLoginFallback submitted");
            return true;
        }
        catch (Exception ex) { _logger.Error($"[UI] TryLoginFallback error: {ex.Message}"); return false; }
    }

    private bool TryLoginFallbackForward(IntPtr hWnd, string username, string password)
    {
        try
        {
            if (GetForegroundWindow() != hWnd) { EnsureForeground(hWnd); Thread.Sleep(50); }
            ClickCenter(hWnd);
            Thread.Sleep(150);
            for (int i = 0; i < 10; i++) { SendKeyPress(VK_TAB); Thread.Sleep(60); }
            if (GetForegroundWindow() != hWnd) { EnsureForeground(hWnd); Thread.Sleep(50); }
            Paste(username);
            Thread.Sleep(100);
            SendKeyPress(VK_TAB);
            Thread.Sleep(80);
            if (GetForegroundWindow() != hWnd) { EnsureForeground(hWnd); Thread.Sleep(50); }
            Paste(password);
            Thread.Sleep(100);
            if (GetForegroundWindow() != hWnd) { EnsureForeground(hWnd); Thread.Sleep(50); }
            SendKeyPress(VK_RETURN);
            _logger.Info("[UI] TryLoginFallbackForward submitted");
            return true;
        }
        catch (Exception ex) { _logger.Error($"[UI] TryLoginFallbackForward error: {ex.Message}"); return false; }
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", SetLastError = true)] private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, uint dwExtraInfo);
    // уже объявлено ниже
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr SetActiveWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    private static bool BringToFront(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        ShowWindow(handle, 9);
        Thread.Sleep(20);
        return SetForegroundWindow(handle);
    }

    private static bool EnsureForeground(IntPtr hWnd, int attempts = 5)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (GetForegroundWindow() == hWnd) return true;
            ShowWindow(hWnd, 9); // SW_RESTORE
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
            Thread.Sleep(30);
        }
        return GetForegroundWindow() == hWnd;
    }

    private static uint GetWindowThreadIdSafe(IntPtr hWnd)
    {
        try
        {
            GetWindowThreadProcessId(hWnd, out _);
            // Trick: calling GetWindowThreadProcessId already returns thread id via return value in native, but P/Invoke signature above returns via out only pid
            // В .NET упростим: вернём текущий поток, Attach всё равно помогает с BringWindowToTop
            return GetCurrentThreadId();
        }
        catch { return 0; }
    }

    private static void ClickCenter(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        if (!GetWindowRect(hWnd, out var r)) return;
        var cx = (r.Left + r.Right) / 2;
        var cy = (r.Top + r.Bottom) / 2;
        SetCursorPos(cx, cy);
        mouse_event(0x0002, 0, 0, 0, 0);
        mouse_event(0x0004, 0, 0, 0, 0);
    }

    private static void Paste(string text)
    {
        string? backup = null;
        try { backup = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch { }
        try { Clipboard.SetText(text); } catch { }
        // Ctrl+A
        SendKeyCombo(VK_CONTROL, VK_A);
        // Delete
        SendKeyPress(VK_DELETE);
        // Ctrl+V
        SendKeyCombo(VK_CONTROL, VK_V);
        if (!string.IsNullOrEmpty(backup)) { try { Clipboard.SetText(backup); } catch { } }
    }

    private static IntPtr FindRiotClientWindow()
    {
        // Строгое таргетирование только окна процесса "Riot Client"
        foreach (var p in Process.GetProcessesByName("Riot Client"))
        {
            if (p.MainWindowHandle != IntPtr.Zero && IsWindowVisible(p.MainWindowHandle))
                return p.MainWindowHandle;

            var w = FindTopWindowForPid(p.Id);
            if (w != IntPtr.Zero) return w;
        }
        return IntPtr.Zero;
    }

    private static IntPtr FindTopWindowForPid(int pid)
    {
        IntPtr best = IntPtr.Zero;
        int bestArea = 0;
        EnumWindows((h, l) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out var wndPid);
            if (wndPid != (uint)pid) return true;
            if (!GetWindowRect(h, out var r)) return true;
            var area = Math.Max(0, (r.Right - r.Left)) * Math.Max(0, (r.Bottom - r.Top));
            if (area > bestArea)
            {
                bestArea = area;
                best = h;
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        int len = GetWindowTextLength(hWnd);
        if (len <= 0) return string.Empty;
        var sb = new System.Text.StringBuilder(len + 2);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    private static string GetWindowClass(IntPtr hWnd)
    {
        var sb = new System.Text.StringBuilder(256);
        if (GetClassName(hWnd, sb, sb.Capacity) > 0) return sb.ToString();
        return string.Empty;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, System.Text.StringBuilder lpExeName, ref int lpdwSize);

    private static string TryGetProcessPath(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            try { return proc.MainModule?.FileName ?? string.Empty; }
            catch { }

            const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
            var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h != IntPtr.Zero)
            {
                try
                {
                    var sb = new System.Text.StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(h, 0, sb, ref size)) return sb.ToString();
                }
                finally { CloseHandle(h); }
            }
        }
        catch { }
        return string.Empty;
    }

    private static void ClickAt(IntPtr hWnd, int x, int y)
    {
        if (!GetWindowRect(hWnd, out var r)) return;
        var px = Math.Clamp(x, r.Left + 10, r.Right - 10);
        var py = Math.Clamp(y, r.Top + 10, r.Bottom - 10);
        SetCursorPos(px, py);
        mouse_event(0x0002, 0, 0, 0, 0);
        mouse_event(0x0004, 0, 0, 0, 0);
    }

    private static bool WaitForStableWindow(IntPtr hWnd, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        RECT prev = default;
        bool hasPrev = false;
        while (DateTime.UtcNow < deadline)
        {
            if (!GetWindowRect(hWnd, out var r)) { Thread.Sleep(100); continue; }
            if (hasPrev)
            {
                var areaPrev = Math.Max(0, (prev.Right - prev.Left)) * Math.Max(0, (prev.Bottom - prev.Top));
                var areaNow = Math.Max(0, (r.Right - r.Left)) * Math.Max(0, (r.Bottom - r.Top));
                if (areaNow > 0 && Math.Abs(areaNow - areaPrev) < 500) return true;
            }
            prev = r;
            hasPrev = true;
            Thread.Sleep(200);
        }
        return false;
    }

    // ===== Keyboard/Mouse helpers without Windows.Forms =====
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_DELETE = 0x2E;
    private const ushort VK_A = 0x41;
    private const ushort VK_V = 0x56;

    private static void SendKeyDown(ushort vk)
    {
        try
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                
                U = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }
        catch { /* swallow to avoid stuck input on some drivers */ }
    }

    private static void SendKeyUp(ushort vk)
    {
        try
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }
        catch { }
    }

    private static void SendKeyPress(ushort vk)
    {
        SendKeyDown(vk);
        Thread.Sleep(2);
        SendKeyUp(vk);
    }

    private static void SendKeyCombo(ushort modifierVk, ushort keyVk)
    {
        SendKeyDown(modifierVk);
        Thread.Sleep(2);
        SendKeyPress(keyVk);
        Thread.Sleep(2);
        SendKeyUp(modifierVk);
    }

    private bool WaitForLoginFieldsUIA(IntPtr hWnd, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var automation = new UIA3Automation();
                var root = automation.FromHandle(hWnd);
                var window = root.AsWindow();
                var edits = window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
                if (edits.Length >= 2)
                {
                    _logger.Info($"[UI] UIA fields ready: edits={edits.Length}");
                    return true;
                }
            }
            catch { }
            Thread.Sleep(200);
        }
        return false;
    }
}


