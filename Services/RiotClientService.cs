using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
using System.Linq;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LolManager.Models;

namespace LolManager.Services;

public partial class RiotClientService : IRiotClientService
{
    private record LockfileInfo(string Name, int Pid, int Port, string Password, string Protocol);
    private readonly ILogger _logger;
    private static readonly Dictionary<string, DateTime> _lastLockfileLog = new();

    public RiotClientService(ILogger logger)
    {
        _logger = logger;
    }

    public bool IsRiotClientRunning()
    {
        try
        {
            if (Process.GetProcessesByName("RiotClientServices").Length > 0) return true;
            if (Process.GetProcessesByName("Riot Client").Length > 0) return true;
            try { _ = FindLockfile("RC"); return true; } catch { }
        }
        catch { }
        return false;
    }

    public async Task LoginAsync(string username, string password)
    {
        await LoginAsync(username, password, CancellationToken.None);
    }

    public async Task LoginAsync(string username, string password, CancellationToken externalToken)
    {
        var swAll = Stopwatch.StartNew();
        _logger.LoginFlow("LoginAsync start", "UIA flow in Riot Client");

        // 1) СНАЧАЛА убедиться, что RC запущен
        var sw = Stopwatch.StartNew();
        try
        {
            var svcCount = Process.GetProcessesByName("RiotClientServices").Length;
            var cliCount = Process.GetProcessesByName("Riot Client").Length;
            _logger.ProcessEvent("RiotClient", "Process count check", $"RiotClientServices={svcCount}, 'Riot Client'={cliCount}");
        }
        catch { }
        bool coldStart = false;
        if (!AreBothRiotProcessesRunning())
        {
            _logger.Info("Both RC processes not running. Restarting Riot Client...");
            await KillRiotClientOnlyAsync();
            await StartRiotClientAsync();
            await WaitForBothRiotProcessesAsync(TimeSpan.FromSeconds(15));
            coldStart = true;
        }
        // 1b) Убедиться, что RC lockfile готов
        await WaitForRcLockfileAsync(TimeSpan.FromSeconds(6));
        
        // 2) Проверить, уже ли выполнен вход - если да, выйти
        _logger.LoginFlow("Checking if already logged in", "Before login attempt");
        bool wasAlreadyLoggedIn = await IsRsoAuthorizedAsync();
        if (wasAlreadyLoggedIn)
        {
            _logger.LoginFlow("Already logged in, logging out first", "Ensuring clean state");
            try 
            { 
                await LogoutAsync(); 
                _logger.LoginFlow("Logout completed", "Ready for new login");
                await Task.Delay(1500);
            } 
            catch (Exception ex) 
            { 
                _logger.Warning($"Logout failed: {ex.Message}"); 
            }
        }
        else
        {
            _logger.LoginFlow("Not logged in", "Proceeding with login");
        }
        
        // 2b) Если холодный старт — минимальная пауза для готовности
        if (coldStart)
        {
            _logger.Info("Cold start: waiting for RiotClientUx window...");
            await WaitForRiotUxWindowAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(500);
        }
        // 2c) Запустить прогрев RSO в фоне
        _ = Task.Run(async () => { try { await WarmUpRsoAsync(); } catch { } });

        // 3) Немедленно попытаться UIA-ввод (как только появится окно/DOM)
        sw.Restart();
        _logger.UiEvent("UIA", "starting TryLoginViaUIAutomationAsync");
        externalToken.ThrowIfCancellationRequested();
        bool uiaOk = await TryLoginViaUIAutomationAsync(username, password, TimeSpan.FromSeconds(45));
        _logger.Info($"STEP UIA_INPUT_DONE result={uiaOk} in {sw.ElapsedMilliseconds}ms");

        // 4) Параллельно в фоне убедимся, что RC lockfile готов (для фоллбека запуска)
        var rcLockReady = WaitForRcLockfileAsync(TimeSpan.FromSeconds(30));

        // 5) Агрессивно запускаем League параллельно с проверкой авторизации
        sw.Restart();
        bool isAuthorized = false;
        
        if (uiaOk)
        {
            _logger.LoginFlow("Login credentials submitted", "Checking authorization and launching League");
            
            // Сначала проверяем авторизацию (быстро - каждые 300ms, до 6 сек)
            for (int i = 0; i < 20; i++)
            {
                externalToken.ThrowIfCancellationRequested();
                isAuthorized = await IsRsoAuthorizedAsync();
                if (isAuthorized)
                {
                    _logger.LoginFlow("RSO authorization confirmed", "Login successful");
                    break;
                }
                await Task.Delay(300, externalToken);
            }
            
            if (!isAuthorized)
            {
                _logger.Warning("RSO authorization not confirmed after 6s - login may have failed");
            }
            else
            {
                // Авторизация подтверждена - запускаем League
                _logger.LoginFlow("Authorization confirmed, launching League of Legends", "Starting launch sequence");
                
                try { await rcLockReady; } catch { }
                await Task.Delay(500, externalToken);
                
                var launched = await TryLaunchLeagueViaRiotApiAsync();
                if (!launched) 
                {
                    _logger.LoginFlow("API launch failed, trying direct launch", "Using RiotClientServices");
                    await Task.Delay(500);
                    launched = await TryLaunchLeagueClientAsync();
                }
                
                if (launched)
                {
                    _logger.LoginFlow("League launch initiated", "Waiting for League to start");
                    
                    // Ждём появления LCU lockfile
                    var lcuReady = await WaitForLcuLockfileAsync(TimeSpan.FromSeconds(30));
                    if (lcuReady != null)
                    {
                        _logger.LoginFlow("League started successfully", "LCU is ready");
                    }
                    else
                    {
                        _logger.Warning("League lockfile not detected after 30s");
                    }
                }
                else
                {
                    _logger.Error("All League launch methods failed");
                }
            }
        }
        else
        {
            _logger.Warning("UIA login failed - cannot proceed with League launch");
        }
        _logger.Info($"STEP AUTH_AND_LAUNCH in {sw.ElapsedMilliseconds}ms");
        
        _logger.Info($"TOTAL LOGIN FLOW completed in {swAll.ElapsedMilliseconds}ms");
    }

    private async Task<bool> TryLoginViaUIAutomationAsync(string username, string password, TimeSpan timeout)
    {
        try
        {
            var deadline = DateTime.UtcNow + timeout;
            using var automation = new UIA3Automation();

            Application? rcApp = null;
            for (int i = 0; i < 20 && rcApp == null; i++)
            {
                var p = Process.GetProcessesByName("RiotClientUx").FirstOrDefault()
                        ?? Process.GetProcessesByName("RiotClientUxRender").FirstOrDefault()
                        ?? Process.GetProcessesByName("Riot Client").FirstOrDefault();
                if (p != null)
                {
                    try { rcApp = Application.Attach(p); _logger.Info($"UIA: attached to process {p.ProcessName}[{p.Id}]"); } catch (Exception ex) { _logger.Error($"UIA: attach failed: {ex.Message}"); }
                }
                if (rcApp == null) await Task.Delay(250);
            }
            if (rcApp == null) { _logger.Info("UIA: no RC process to attach"); return false; }

            // Ждём появление главного окна RC быстро
            Window? window = null;
            var deadlineWin = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadlineWin && window == null)
            {
                window = rcApp.GetMainWindow(automation, TimeSpan.FromSeconds(1));
                if (window == null) await Task.Delay(150);
            }
            if (window == null) { _logger.Info("UIA: main window not found within timeout"); return false; }

            IntPtr hwnd = IntPtr.Zero;
            try
            {
                hwnd = new IntPtr(window.Properties.NativeWindowHandle.Value);
                _logger.Info($"UIA: main window handle=0x{window.Properties.NativeWindowHandle.Value:X}");
            }
            catch { }

            // Ждать появления DOM/элементов с ретраями, активно управляя окном
            AutomationElement? riotContent = null;
            TextBox? usernameEl = null;
            TextBox? passwordEl = null;
            AutomationElement? signInElement = null;
            bool contentLogged = false;
            bool usernameLogged = false;
            bool passwordLogged = false;
            bool buttonLogged = false;
            bool signInClicked = false;

            int scanCycles = 0;
            int stableFieldsCycles = 0;
            int lastEditsCount = -1;
            var lastStateLog = DateTime.MinValue;
            
            // Агрессивная активация окна в начале
            if (hwnd != IntPtr.Zero)
            {
                for (int i = 0; i < 2; i++)
                {
                    ShowWindow(hwnd, ShowWindowCommands.Restore);
                    SetForegroundWindow(hwnd);
                    await Task.Delay(50);
                }
                _logger.Info("UIA: window activated");
            }
            
            await Task.Delay(100);
            while (DateTime.UtcNow < deadline)
            {
                // Периодически активируем окно
                if (scanCycles % 10 == 0 && hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, ShowWindowCommands.Restore);
                    SetForegroundWindow(hwnd);
                }

                riotContent ??= window.FindFirstDescendant(cf =>
                                        cf.ByClassName("Chrome_RenderWidgetHostHWND")
                                          .Or(cf.ByClassName("Chrome_WidgetWin_0"))
                                          .Or(cf.ByClassName("Intermediate D3D Window")))
                                   ?? window;
                if (riotContent != null && !contentLogged)
                {
                    try { _logger.Info($"UIA: riotContent class='{riotContent.Properties.ClassName?.Value ?? "null"}'"); } catch { }
                    contentLogged = true;
                }

                // 1) Пробуем по AutomationId
                usernameEl ??= riotContent?.FindFirstDescendant(cf =>
                    cf.ByAutomationId("username").Or(cf.ByAutomationId("login")).Or(cf.ByName("username")).Or(cf.ByName("Login")).Or(cf.ByName("Email")).Or(cf.ByName("Адрес электронной почты")).Or(cf.ByName("Имя пользователя"))
                )?.AsTextBox();
                passwordEl ??= riotContent?.FindFirstDescendant(cf =>
                    cf.ByAutomationId("password").Or(cf.ByName("password")).Or(cf.ByName("Пароль")).Or(cf.ByName("Password"))
                )?.AsTextBox();
                if (usernameEl != null && !usernameLogged) { _logger.Info("UIA: FIELD_DISCOVERY = DIRECT (username/password by AutomationId/Name)"); usernameLogged = true; }
                if (passwordEl != null && !passwordLogged) { _logger.Info("UIA: FIELD_DISCOVERY = DIRECT (username/password by AutomationId/Name)"); passwordLogged = true; }

                // 2) Если пусто — ищем Edit
                if (usernameEl == null || passwordEl == null)
                {
                    var edits = riotContent?.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit)) ?? new FlaUI.Core.AutomationElements.AutomationElement[0];
                    if (edits.Length != lastEditsCount && (DateTime.Now - lastStateLog).TotalMilliseconds > 1000)
                    {
                        _logger.Info($"UIA: edits count in content = {edits.Length}");
                        lastStateLog = DateTime.Now;
                        lastEditsCount = edits.Length;
                    }
                    if (edits.Length >= 2)
                    {
                        usernameEl ??= edits[0].AsTextBox();
                        passwordEl ??= edits[1].AsTextBox();
                        if (usernameEl != null && !usernameLogged) { _logger.Info("UIA: FIELD_DISCOVERY = EDIT_INDEX (username via edits[0])"); usernameLogged = true; }
                        if (passwordEl != null && !passwordLogged) { _logger.Info("UIA: FIELD_DISCOVERY = EDIT_INDEX (password via edits[1])"); passwordLogged = true; }
                    }
                }
                // 2b) Последний шанс: искать по всему окну (иногда контейнер другой)
                if (usernameEl == null || passwordEl == null)
                {
                    var uAlt = window.FindFirstDescendant(cf => cf.ByAutomationId("username").Or(cf.ByAutomationId("login"))
                        .Or(cf.ByName("username")).Or(cf.ByName("Login")).Or(cf.ByName("Email")).Or(cf.ByName("Адрес электронной почты")).Or(cf.ByName("Имя пользователя")))?.AsTextBox();
                    var pAlt = window.FindFirstDescendant(cf => cf.ByAutomationId("password").Or(cf.ByName("password")).Or(cf.ByName("Пароль")).Or(cf.ByName("Password")))?.AsTextBox();
                    usernameEl ??= uAlt;
                    passwordEl ??= pAlt;
                    if (usernameEl != null && !usernameLogged) { _logger.Info("UIA: FIELD_DISCOVERY = WINDOW_LEVEL (username via window-wide search)"); usernameLogged = true; }
                    if (passwordEl != null && !passwordLogged) { _logger.Info("UIA: FIELD_DISCOVERY = WINDOW_LEVEL (password via window-wide search)"); passwordLogged = true; }
                }

                // 3) Кнопка Войти: сначала через чекбокс‑соседа
                if (signInElement == null)
                {
                    var checkbox = riotContent?.FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox));
                    if (checkbox != null && checkbox.Parent != null)
                    {
                        var siblings = checkbox.Parent.FindAllChildren();
                        var index = Array.IndexOf(siblings, checkbox) + 1;
                        for (int i = index; i < siblings.Length; i++)
                        {
                            if (siblings[i].ControlType == ControlType.Button)
                            {
                                signInElement = siblings[i];
                                break;
                            }
                        }
                    }
                    // 4) Фоллбек: любая кнопка по имени
                    if (signInElement == null)
                    {
                        signInElement = riotContent?.FindFirstDescendant(cf =>
                            cf.ByControlType(ControlType.Button)
                              .And(cf.ByName("Sign in").Or(cf.ByName("Sign In")).Or(cf.ByName("Log In")).Or(cf.ByName("Войти"))));
                    }
                    if (signInElement != null && !buttonLogged)
                    {
                        try { _logger.Info($"UIA: SignIn button found: name='{signInElement.Properties.Name.Value}'"); } catch { _logger.Info("UIA: SignIn button found"); }
                        buttonLogged = true;
                    }
                    // 4b) Если полей ещё нет, но видна кнопка входа — нажмём её сразу, чтобы открыть форму
                    if (!signInClicked && signInElement != null && (usernameEl == null || passwordEl == null))
                    {
                        _logger.Info("UIA: clicking SignIn to open form");
                        try { signInElement.AsButton().Invoke(); }
                        catch { try { signInElement.Click(); } catch { } }
                        signInClicked = true;
                        await Task.Delay(300);
                        continue;
                    }
                }

                // 3c) Blind-input фоллбек удалён по запросу

                if (usernameEl != null && passwordEl != null)
                {
                    stableFieldsCycles++;
                    if (stableFieldsCycles >= 1) break;
                }
                else
                {
                    stableFieldsCycles = 0;
                }

                scanCycles++;
                // Каждые 20 циклов пробуем «пнуть» CEF кликом по центру окна, чтобы показать форму
                if (scanCycles % 20 == 0 && hwnd != IntPtr.Zero)
                {
                    try
                    {
                        _logger.Info("UIA: center click to wake up RC content");
                        ClickWindowRelative(hwnd, 0.5, 0.5);
                    }
                    catch { }
                }
                await Task.Delay(80);
            }

            if (usernameEl == null || passwordEl == null)
            {
                _logger.Info("UIA: username/password fields not found (timeout)");
                return false;
            }

            // Агрессивный фокус на поле ввода
            _logger.Info("UIA: setting focus on username field");
            try 
            { 
                usernameEl.Focus();
                await Task.Delay(30);
                usernameEl.Click(moveMouse: false);
                await Task.Delay(30);
            }
            catch (Exception ex)
            {
                _logger.Warning($"UIA: focus/click failed: {ex.Message}, trying alternative");
                try { usernameEl.Focus(); } catch { }
            }

            // Ввод значений строго через UIA ValuePattern (без SendInput)
            _logger.Info("UIA: setting username");
            if (usernameEl.Patterns.Value.IsSupported)
            {
                usernameEl.Patterns.Value.Pattern.SetValue(string.Empty);
                await Task.Delay(30);
                usernameEl.Patterns.Value.Pattern.SetValue(username);
                _logger.Info("UIA: INPUT_METHOD = VALUE_PATTERN (username via ValuePattern.SetValue)");
            }
            else
            {
                usernameEl.Focus();
                await Task.Delay(50);
                usernameEl.Text = username;
                _logger.Info("UIA: INPUT_METHOD = TEXT_PROPERTY (username via Text property)");
            }
            await Task.Delay(80);
            // Верификация: если текст не совпал (обрезало 1-й символ) — повторить до 2 раз
            for (int i = 0; i < 2; i++)
            {
                var current = usernameEl.Text ?? string.Empty;
                if (string.Equals(current, username, StringComparison.Ordinal)) break;
                _logger.Info($"UIA: username mismatch (got {current.Length} chars, expected {username.Length}), retry {i+1}");
                if (usernameEl.Patterns.Value.IsSupported)
                {
                    usernameEl.Patterns.Value.Pattern.SetValue(string.Empty);
                    usernameEl.Patterns.Value.Pattern.SetValue(username);
                }
                else
                {
                    usernameEl.Focus();
                    await Task.Delay(60);
                    usernameEl.Text = username;
                }
                await Task.Delay(80);
            }

            _logger.Info("UIA: setting password");
            if (passwordEl.Patterns.Value.IsSupported)
            {
                passwordEl.Patterns.Value.Pattern.SetValue(string.Empty);
                await Task.Delay(30);
                passwordEl.Patterns.Value.Pattern.SetValue(password);
                _logger.Info("UIA: INPUT_METHOD = VALUE_PATTERN (password via ValuePattern.SetValue)");
            }
            else
            {
                passwordEl.Focus();
                await Task.Delay(50);
                passwordEl.Text = password;
                _logger.Info("UIA: INPUT_METHOD = TEXT_PROPERTY (password via Text property)");
            }
            await Task.Delay(80);
            // Не читаем значение пароля (в CEF может кидать исключение), полагаемся на SetValue

            // Найти и активировать чекбокс "Не выходить" (Remember Me)
            try
            {
                var checkbox = riotContent?.FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox));
                if (checkbox != null)
                {
                    var checkboxControl = checkbox.AsCheckBox();
                    if (checkboxControl.IsChecked != true)
                    {
                        _logger.Info("UIA: activating 'Remember Me' checkbox");
                        checkboxControl.Toggle();
                    }
                    else
                    {
                        _logger.Info("UIA: 'Remember Me' checkbox already checked");
                    }
                }
                else
                {
                    _logger.Info("UIA: 'Remember Me' checkbox not found");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"UIA: failed to activate 'Remember Me' checkbox: {ex.Message}");
            }
            await Task.Delay(50);

            if (signInElement != null)
            {
                _logger.UiEvent("UIA Login", "BUTTON_CLICK", "SignIn button found and clicked");
                try { signInElement.AsButton().Invoke(); }
                catch
                {
                    try { signInElement.Click(); } catch { }
                }
            }
            else
            {
                // Фоллбек: отправить Enter в контекст пароля только если окно RC активное
                try
                {
                    var active = GetForegroundWindow();
                    if (active == new IntPtr(window.Properties.NativeWindowHandle.Value))
                    {
                        passwordEl.Focus();
                        await Task.Delay(30);
                        _logger.UiEvent("UIA Login", "ENTER_KEY", "SignIn button not found, using Enter");
                        SendVirtualKey(VirtualKey.RETURN);
                    }
                    else { _logger.Info("UIA: skip ENTER, RC window not active"); }
                }
                catch { }
            }

            // Подождать появления LCU
            while (DateTime.UtcNow < deadline)
            {
                try { var l = FindLockfile(product: "LCU"); _logger.Info("UIA: LCU detected"); return true; } catch { }
                await Task.Delay(500);
            }
            _logger.Info("UIA: LCU not detected within timeout after submit");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"TryLoginViaUIAutomationAsync error: {ex.Message}");
            return false;
        }
    }

    private async Task TryLoginViaLeagueClientWithRetries(string username, string password, int maxAttempts, int delayMs)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var ok = await TryLoginViaLeagueClientAsync(username, password);
                if (ok) { _logger.Info($"LCU login success on attempt #{attempt} in {sw.ElapsedMilliseconds}ms"); return; }
                _logger.Info($"LCU login attempt #{attempt} not successful (HTTP 4xx/5xx) in {sw.ElapsedMilliseconds}ms. Retrying...");
            }
            catch (Exception ex)
            {
                _logger.Error($"LCU login attempt #{attempt} threw: {ex.Message} after {sw.ElapsedMilliseconds}ms");
            }
            await Task.Delay(delayMs);
        }
        _logger.Error("LCU login failed after retries");
    }

    public async Task KillLeagueAsync(bool includeRiotClient)
    {
        try
        {
            var names = includeRiotClient
                ? new[] { "LeagueClientUx", "LeagueClient", "RiotClientUx", "RiotClientUxRender", "RiotClientServices" }
                : new[] { "LeagueClientUx", "LeagueClient" };
            int killed = 0;
            foreach (var name in names)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { p.Kill(true); killed++; } catch { }
                }
            }
            _logger.Info($"Killed processes ({(includeRiotClient ? "full" : "league-only")}): {killed}");
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            _logger.Error($"KillLeagueAsync error: {ex.Message}");
        }
    }

    public async Task StartLeagueAsync()
    {
        // Сначала надёжный путь через RiotClientServices.exe с аргументами
        if (await TryLaunchLeagueClientAsync()) return;
        // Затем API (может быть недоступен на части билдов)
        if (await TryLaunchLeagueViaRiotApiAsync()) return;
        // Затем попробуем через реестр
        if (await TryLaunchLeagueViaRegistryAsync()) return;
        // Крайний случай — прямой запуск LeagueClient.exe (часто даёт AccessDenied/UAC)
        await TryLaunchLeagueClientDirectAsync();
    }

    public async Task RestartLeagueAsync(bool includeRiotClient)
    {
        await KillLeagueAsync(includeRiotClient);
        await StartLeagueAsync();
    }

    public async Task StartRiotClientAsync()
    {
        try
        {
            var (exe, _) = TryGetRiotClientServicesVersion();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                _logger.Error("RiotClientServices.exe not found for StartRiotClientAsync");
                return;
            }
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            };
            Process.Start(psi);
            _logger.Info($"Started RiotClientServices: {exe}");
            await WaitForRcLockfileAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            _logger.Error($"StartRiotClientAsync error: {ex.Message}");
        }
    }

    public async Task RestartRiotClientAsync()
    {
        try
        {
            await KillRiotClientOnlyAsync();
            await StartRiotClientAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"RestartRiotClientAsync error: {ex.Message}");
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            // 1) Попытка корректного логаута через LCU API
            try
            {
                var lcuLock = FindLockfile(product: "LCU");
                using var lcu = CreateHttpClient(lcuLock.Port, lcuLock.Password);
                try
                {
                    var del = await lcu.DeleteAsync("/lol-login/v1/session");
                    await LogResponse("LCU DELETE /lol-login/v1/session [logout]", del);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Logout LCU error: {ex.Message}");
                }
                try
                {
                    var delRso = await lcu.DeleteAsync("/rso-auth/v1/session");
                    await LogResponse("LCU DELETE /rso-auth/v1/session [logout]", delRso);
                }
                catch { }
            }
            catch { /* LCU не найден — ок, идём дальше к RC */ }

            // 2) Разлогиниться из Riot Client, чтобы вернуться на экран выбора аккаунта
            try
            {
                var rc = FindLockfile(product: "RC");
                using var rcClient = CreateHttpClient(rc.Port, rc.Password);
                // Самый частый эндпоинт логаута RC
                try
                {
                    var resp1 = await rcClient.DeleteAsync("/rso-auth/v1/authorization");
                    await LogResponse("RC DELETE /rso-auth/v1/authorization", resp1);
                }
                catch { }
                // Альтернативный роут в новых билдах
                try
                {
                    var resp2 = await rcClient.DeleteAsync("/rso-auth/v2/authorizations");
                    await LogResponse("RC DELETE /rso-auth/v2/authorizations", resp2);
                }
                catch { }
                // На всякий случай закрыть сам продукт LoL, если RC держит его запущенным
                try
                {
                    var resp3 = await rcClient.DeleteAsync("/product-launcher/v1/products/league_of_legends");
                    await LogResponse("RC DELETE /product-launcher/v1/products/league_of_legends", resp3);
                }
                catch { }
            }
            catch { /* RC не найден — пропускаем */ }
        }
        catch (Exception ex)
        {
            _logger.Error($"LogoutAsync error: {ex}");
        }
    }

    public async Task<string?> FetchLcuOpenApiAsync(string outputJsonPath)
    {
        try
        {
            var lcu = FindLockfile(product: "LCU");
            using var client = CreateHttpClient(lcu.Port, lcu.Password);

            // Известные места swagger/openapi в LCU:
            var candidates = new[]
            {
                "/swagger/v1/swagger.json",
                "/swagger/swagger.json",
                "/openapi.json",
                "/openapi/v3.json",
                "/api-docs",
                "/lol-login/v1/swagger.json",
                "/lol-summoner/v1/swagger.json"
            };

            foreach (var path in candidates)
            {
                try
                {
                    var resp = await client.GetAsync(path);
                    if (resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync();
                        if (!string.IsNullOrWhiteSpace(body) && body.TrimStart().StartsWith("{"))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(outputJsonPath) ?? ".");
                            await File.WriteAllTextAsync(outputJsonPath, body);
                            _logger.Info($"Saved LCU OpenAPI from {path} to {outputJsonPath}");
                            return outputJsonPath;
                        }
                    }
                }
                catch { }
            }

            // Фоллбек: иногда LCU отдаёт /help HTML со списком роутов
            try
            {
                var help = await client.GetAsync("/help");
                var helpBody = await help.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(helpBody))
                {
                    var helpPath = Path.ChangeExtension(outputJsonPath, ".help.html");
                    Directory.CreateDirectory(Path.GetDirectoryName(helpPath) ?? ".");
                    await File.WriteAllTextAsync(helpPath, helpBody);
                    _logger.Info($"Saved LCU /help to {helpPath}");
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            _logger.Error($"FetchLcuOpenApiAsync error: {ex.Message}");
        }
        return null;
    }

    public async Task<int> GenerateLcuEndpointsMarkdownAsync(string outputMarkdownPath, string? alsoSaveRawJsonTo = null)
    {
        string? openApiPath = null;
        try
        {
            if (!string.IsNullOrEmpty(alsoSaveRawJsonTo))
            {
                openApiPath = await FetchLcuOpenApiAsync(alsoSaveRawJsonTo);
            }
            else
            {
                var tmp = Path.Combine(Path.GetTempPath(), "lcu_openapi.json");
                openApiPath = await FetchLcuOpenApiAsync(tmp);
            }

            if (openApiPath == null || !File.Exists(openApiPath))
            {
                // Падаем в режим грубого парсинга /help
                return await GenerateFromHelpFallbackAsync(outputMarkdownPath);
            }

            var json = await File.ReadAllTextAsync(openApiPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("paths", out var pathsEl) || pathsEl.ValueKind != JsonValueKind.Object)
            {
                return await GenerateFromHelpFallbackAsync(outputMarkdownPath);
            }

            var lines = new List<string>();
            lines.Add("# LCU Endpoints\n");
            int count = 0;
            foreach (var pathProp in pathsEl.EnumerateObject().OrderBy(p => p.Name))
            {
                var path = pathProp.Name;
                var methodsEl = pathProp.Value;
                foreach (var methodProp in methodsEl.EnumerateObject())
                {
                    var method = methodProp.Name.ToUpperInvariant();
                    string? summary = null;
                    if (methodProp.Value.TryGetProperty("summary", out var sum)) summary = sum.GetString();
                    if (string.IsNullOrWhiteSpace(summary) && methodProp.Value.TryGetProperty("description", out var desc)) summary = desc.GetString();

                    lines.Add($"- {method} {path} — {summary}");
                    count++;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputMarkdownPath) ?? ".");
            await File.WriteAllLinesAsync(outputMarkdownPath, lines, Encoding.UTF8);
            _logger.Info($"Generated endpoints markdown to {outputMarkdownPath}, count={count}");
            return count;
        }
        catch (Exception ex)
        {
            _logger.Error($"GenerateLcuEndpointsMarkdownAsync error: {ex.Message}");
            return 0;
        }
    }

    private async Task<int> GenerateFromHelpFallbackAsync(string outputMarkdownPath)
    {
        try
        {
            var lcu = FindLockfile(product: "LCU");
            using var client = CreateHttpClient(lcu.Port, lcu.Password);
            var help = await client.GetAsync("/help");
            var html = await help.Content.ReadAsStringAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(outputMarkdownPath) ?? ".");

            // Очень грубый парсинг ссылок вида <a href="GET /lol-...">GET /lol-...</a>
            var lines = new List<string>();
            lines.Add("# LCU Endpoints (from /help)\n");
            int count = 0;
            foreach (var line in html.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Contains("/lol-") || trimmed.Contains("/rso-") || trimmed.Contains("/riot-"))
                {
                    // вытащим метод и путь примитивной эвристикой
                    var methods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" };
                    foreach (var m in methods)
                    {
                        var idx = trimmed.IndexOf(m + " ", StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            var rest = trimmed.Substring(idx);
                            int end = rest.IndexOf('<');
                            var token = end > 0 ? rest.Substring(0, end) : rest;
                            lines.Add("- " + token.Replace("&amp;", "&").Replace("&quot;", "\"").Trim());
                            count++;
                            break;
                        }
                    }
                }
            }
            await File.WriteAllLinesAsync(outputMarkdownPath, lines, Encoding.UTF8);
            _logger.Info($"Generated fallback endpoints from /help to {outputMarkdownPath}, count={count}");
            return count;
        }
        catch (Exception ex)
        {
            _logger.Error($"GenerateFromHelpFallbackAsync error: {ex.Message}");
            return 0;
        }
    }


    private async Task<bool> TryLoginViaLeagueClientAsync(string username, string password)
    {
        LockfileInfo? lcuLock;
        try { lcuLock = FindLockfile(product: "LCU"); _logger.Info($"LCU lockfile: port={lcuLock.Port}"); }
        catch (Exception ex)
        {
            // Не запускаем LCU. Если его нет — выходим, как делают LCU‑тулзы.
            _logger.Error($"LCU lockfile not found: {ex.Message}. Skipping LCU path (client not running).");
            return false;
        }
        using var client = CreateHttpClient(lcuLock.Port, lcuLock.Password);

        // Дождаться готовности HTTP стека LCU (порт может уже быть в lockfile, но слушатель ещё не поднялся)
        var sw = Stopwatch.StartNew();
        await WaitForLcuHttpReadyAsync(client, TimeSpan.FromSeconds(30));
        _logger.Info($"LCU HTTP ready in {sw.ElapsedMilliseconds}ms");

        await LogLcuSessionStateAsync(client, "[LCU before]");

        // Диагностика процессов и пути lockfile
        try
        {
            var procs = string.Join(", ", Process.GetProcessesByName("LeagueClientUx").Select(p => $"LeagueClientUx[{p.Id}]"));
            _logger.Info($"Processes: {procs}");
        }
        catch { }

        // В LCU исторически работает POST /lol-login/v1/session
        // 0) Сброс сессии
        try { var del = await client.DeleteAsync("/lol-login/v1/session"); await LogResponse("LCU DELETE /lol-login/v1/session", del); } catch { }

        var variants = new object[]
        {
            new { username, password, remember = true, persistLogin = true }
        };

        int idx = 0;
        foreach (var v in variants)
        {
            try
            {
                idx++;
                var payload = JsonSerializer.Serialize(v);
                var respA = await client.PostAsync("/lol-login/v1/session", new StringContent(payload, Encoding.UTF8, "application/json"));
                await LogResponse($"LCU POST /lol-login/v1/session [v{idx}]", respA);
                if (respA.IsSuccessStatusCode) return true;
            }
            catch { }
        }

        idx = 0;
        foreach (var v in variants)
        {
            try
            {
                idx++;
                var payload2 = JsonSerializer.Serialize(v);
                var alt = await client.PutAsync("/rso-auth/v1/session/credentials", new StringContent(payload2, Encoding.UTF8, "application/json"));
                await LogResponse($"LCU PUT /rso-auth/v1/session/credentials [v{idx}]", alt);
                if (alt.IsSuccessStatusCode) return true;
            }
            catch { }
        }

        await LogLcuSessionStateAsync(client, "[LCU after]");
        return false;
    }

    private async Task<bool> TryLaunchLeagueViaRiotApiAsync()
    {
        try
        {
            _logger.LoginFlow("Trying to launch League via Riot API", "Checking RC lockfile");
            // Убедиться, что RC lockfile доступен
            try { _ = FindLockfile(product: "RC"); }
            catch { await WaitForRcLockfileAsync(TimeSpan.FromSeconds(6)); }
            var rc = FindLockfile(product: "RC");
            using var rcClient = CreateHttpClient(rc.Port, rc.Password);
            // Поперечная совместимость разных билдов RiotClient API:
            // 1) новый product-launcher endpoint
            _logger.LoginFlow("Trying new product-launcher endpoint", "/products/league_of_legends/patchlines/live/launch");
            var payload = new { additionalArguments = new[] { "--launch-product=league_of_legends", "--launch-patchline=live" } };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await rcClient.PostAsync("/product-launcher/v1/products/league_of_legends/patchlines/live/launch", content);
            _logger.HttpRequest("POST", "/product-launcher/v1/products/league_of_legends/patchlines/live/launch", (int)resp.StatusCode);
            if (resp.IsSuccessStatusCode) 
            {
                _logger.LoginFlow("League launch via new API successful", "League should be starting");
                return true;
            }

            // 2) старый универсальный endpoint (на ряде билдов)
            _logger.LoginFlow("Trying old universal endpoint", "/product-launcher/v1/launch");
            var oldPayload = new { product = "league_of_legends", patchline = "live", additionalArguments = new[] { "--launch-product=league_of_legends", "--launch-patchline=live" } };
            var oldJson = JsonSerializer.Serialize(oldPayload);
            var oldResp = await rcClient.PostAsync("/product-launcher/v1/launch", new StringContent(oldJson, Encoding.UTF8, "application/json"));
            _logger.HttpRequest("POST", "/product-launcher/v1/launch", (int)oldResp.StatusCode);
            if (oldResp.IsSuccessStatusCode)
            {
                _logger.LoginFlow("League launch via old API successful", "League should be starting");
                return true;
            }
            else
            {
                _logger.LoginFlow("Both API endpoints failed", $"New: {(int)resp.StatusCode}, Old: {(int)oldResp.StatusCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"TryLaunchLeagueViaRiotApiAsync error: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> IsRsoAuthorizedAsync()
    {
        try
        {
            var rc = FindLockfile(product: "RC");
            using var rcClient = CreateHttpClient(rc.Port, rc.Password);
            
            // Проверяем v1
            try
            {
                var resp1 = await rcClient.GetAsync("/rso-auth/v1/authorization");
                if ((int)resp1.StatusCode == 200)
                {
                    var json = await resp1.Content.ReadAsStringAsync();
                    if (JsonLooksAuthorized(json)) return true;
                }
            }
            catch { }
            
            // Проверяем v2
            try
            {
                var resp2 = await rcClient.GetAsync("/rso-auth/v2/authorization");
                if ((int)resp2.StatusCode == 200)
                {
                    var json = await resp2.Content.ReadAsStringAsync();
                    if (JsonLooksAuthorized(json)) return true;
                }
            }
            catch { }
        }
        catch { }
        return false;
    }

    private static bool JsonLooksAuthorized(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = doc.RootElement;
            if (root.TryGetProperty("isAuthorized", out var v1) && v1.ValueKind == JsonValueKind.True) return true;
            if (root.TryGetProperty("authorized", out var v2) && v2.ValueKind == JsonValueKind.True) return true;
            if (root.TryGetProperty("state", out var stateEl) && stateEl.GetString()?.Equals("AUTHORIZED", StringComparison.OrdinalIgnoreCase) == true) return true;
            if (root.TryGetProperty("accessToken", out var tok) && tok.ValueKind == JsonValueKind.String && tok.GetString()?.Length > 10) return true;
        }
        catch { }
        return false;
    }

    private async Task WarmUpRsoAsync()
    {
        try
        {
            // Пробуем инициализировать RSO, чтобы RC отрисовал форму входа
            var rc = FindLockfile(product: "RC");
            using var rcClient = CreateHttpClient(rc.Port, rc.Password);
            var initV1 = new
            {
                clientId = "riot-client",
                nonce = "1",
                redirect_uri = "http://localhost/redirect",
                response_type = "token id_token",
                scope = "openid offline_access lol ban profile link"
            };
            var initV3 = new
            {
                clientId = "riot-client",
                nonce = "1",
                redirectUri = "http://localhost/redirect",
                responseType = "token id_token",
                scope = "openid offline_access lol ban profile link"
            };
            try { var r1 = await rcClient.PostAsync("/rso-auth/v1/authorization", JsonContent(initV1)); await LogResponse("RC POST /rso-auth/v1/authorization [warm]", r1); } catch { }
            try { var r3 = await rcClient.PostAsync("/rso-auth/v3/authorization", JsonContent(initV3)); await LogResponse("RC POST /rso-auth/v3/authorization [warm]", r3); } catch { }
        }
        catch { }
    }

    private async Task WaitForRiotUxWindowAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var ux = Process.GetProcessesByName("RiotClientUx").FirstOrDefault()
                      ?? Process.GetProcessesByName("RiotClientUxRender").FirstOrDefault();
                if (ux != null) return; // Ждём только процесс, не MainWindowHandle
            }
            catch { }
            await Task.Delay(100);
        }
    }

    private async Task WaitForLoginFormReadyBeforeUIAAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var p = Process.GetProcessesByName("Riot Client").FirstOrDefault()
                        ?? Process.GetProcessesByName("RiotClientUx").FirstOrDefault()
                        ?? Process.GetProcessesByName("RiotClientUxRender").FirstOrDefault();
                if (p != null)
                {
                    using var automation = new UIA3Automation();
                    var app = Application.Attach(p);
                    var win = app.GetMainWindow(automation, TimeSpan.FromSeconds(1));
                    if (win != null)
                    {
                        var riotContent = win.FindFirstDescendant(cf => cf.ByClassName("Chrome_RenderWidgetHostHWND")
                            .Or(cf.ByClassName("Chrome_WidgetWin_0"))
                            .Or(cf.ByClassName("Intermediate D3D Window")));
                        if (riotContent != null)
                        {
                            var anyElement = riotContent.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
                            if (anyElement != null)
                            {
                                _logger.Info("UIA: CEF content ready, found Edit element");
                                return;
                            }
                        }
                    }
                }
            }
            catch { }
            await Task.Delay(100);
        }
        _logger.Info("UIA: login form not ready after timeout");
    }

    private static StringContent JsonContent(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    // === RSO (Riot Client) авторизация (legacy) ===
    #region Legacy_RC_RSO_Auth
    #if false
    private async Task<bool> TryLoginViaRiotClientAsync(string username, string password, int retries)
    {
        try
        {
            var rc = FindLockfile(product: "RC");
            using var rcClient = CreateHttpClient(rc.Port, rc.Password);

            // Сбросить возможное старое состояние авторизации
            try { var d1 = await rcClient.DeleteAsync("/rso-auth/v1/authorization"); await LogResponse("RC DELETE /rso-auth/v1/authorization", d1); } catch { }
            try { var d2 = await rcClient.DeleteAsync("/rso-auth/v2/authorizations"); await LogResponse("RC DELETE /rso-auth/v2/authorizations", d2); } catch { }

            await EnsureRsoInitializedAsync(rcClient);

            for (int attempt = 1; attempt <= retries; attempt++)
            {
                var submitted = await SubmitRsoCredentialsAllVariants(rcClient, username, password);
                _logger.Info($"RC RSO submit attempt #{attempt} -> {submitted}");
                if (submitted)
                {
                    var authorized = await WaitForRiotAuthorizationQuietAsync(TimeSpan.FromSeconds(10));
                    if (authorized) return true;
                }
                await Task.Delay(700);
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"TryLoginViaRiotClientAsync error: {ex.Message}");
            return false;
        }
    }

    private async Task EnsureRsoInitializedAsync(HttpClient rcClient)
    {
        // Инициализация RSO-сессии в RC: POST v1 и v3 — пробуем оба
        var initV1 = new
        {
            clientId = "riot-client",
            nonce = "1",
            redirect_uri = "http://localhost/redirect",
            response_type = "token id_token",
            scope = "openid offline_access lol ban profile link"
        };
        var initV3 = new
        {
            clientId = "riot-client",
            nonce = "1",
            redirectUri = "http://localhost/redirect",
            responseType = "token id_token",
            scope = "openid offline_access lol ban profile link"
        };

        try { var r1 = await rcClient.PostAsync("/rso-auth/v1/authorization", Json(initV1)); await LogResponse("RC POST /rso-auth/v1/authorization", r1); } catch { }
        try { var r3 = await rcClient.PostAsync("/rso-auth/v3/authorization", Json(initV3)); await LogResponse("RC POST /rso-auth/v3/authorization", r3); } catch { }
    }

    private async Task<bool> SubmitRsoCredentialsAllVariants(HttpClient rcClient, string username, string password)
    {
        var bodyCommon = new { type = "auth", username, password, remember = true, language = "ru_RU" };

        // v1 PUT /authorization
        try
        {
            var r1 = await rcClient.PutAsync("/rso-auth/v1/authorization", Json(bodyCommon));
            await LogResponse("RC PUT /rso-auth/v1/authorization", r1);
            if (r1.IsSuccessStatusCode) return true;
        }
        catch { }

        // v2 PUT /authorizations (singular plural менялся в билдах)
        try
        {
            var r2 = await rcClient.PutAsync("/rso-auth/v2/authorization", Json(bodyCommon));
            await LogResponse("RC PUT /rso-auth/v2/authorization", r2);
            if (r2.IsSuccessStatusCode) return true;
        }
        catch { }

        try
        {
            var r2b = await rcClient.PutAsync("/rso-auth/v2/authorizations", Json(bodyCommon));
            await LogResponse("RC PUT /rso-auth/v2/authorizations", r2b);
            if (r2b.IsSuccessStatusCode) return true;
        }
        catch { }

        // interactive endpoint встречается в ряде версий RC
        try
        {
            var r2i = await rcClient.PutAsync("/rso-auth/v2/authorizations/interactive", Json(bodyCommon));
            await LogResponse("RC PUT /rso-auth/v2/authorizations/interactive", r2i);
            if (r2i.IsSuccessStatusCode) return true;
        }
        catch { }

        return false;
    }

    private static StringContent Json(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private async Task<bool> WaitForRiotAuthorizationQuietAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var rc = FindLockfile(product: "RC");
                using var rcClient = CreateHttpClient(rc.Port, rc.Password);
                // v1
                try
                {
                    var resp1 = await rcClient.GetAsync("/rso-auth/v1/authorization");
                    if ((int)resp1.StatusCode == 200)
                    {
                        var json = await resp1.Content.ReadAsStringAsync();
                        if (JsonLooksAuthorized(json)) return true;
                    }
                }
                catch { }
                // v2 (singular)
                try
                {
                    var resp2 = await rcClient.GetAsync("/rso-auth/v2/authorization");
                    if ((int)resp2.StatusCode == 200)
                    {
                        var json = await resp2.Content.ReadAsStringAsync();
                        if (JsonLooksAuthorized(json)) return true;
                    }
                }
                catch { }
            }
            catch { }
            await Task.Delay(500);
        }
        return false;
    }
    #endif
    #endregion

    // Убрано RSO polling чтобы не заспамливать логи

    private async Task<bool> TryLaunchLeagueClientAsync()
    {
        try
        {
            _logger.LoginFlow("Trying to launch League via RiotClientServices", "Searching for executable");
            
            // Сначала попробуем найти через RiotClientInstalls.json
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var installsPath = Path.Combine(programData, "Riot Games", "RiotClientInstalls.json");
            string? riotClientPath = null;
            
            if (File.Exists(installsPath))
            {
                try
                {
                    var json = File.ReadAllText(installsPath);
                    var installData = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);
                    if (installData?.associated_client != null)
                    {
                        foreach (var client in installData.associated_client)
                        {
                            var path = client?.rc_live?.Value as string;
                            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            {
                                riotClientPath = path;
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to parse RiotClientInstalls.json: {ex.Message}");
                }
            }
            
            // Фоллбек кандидаты для RiotClientServices.exe
            var candidates = new[]
            {
                riotClientPath,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Riot Games", "Riot Client", "RiotClientServices.exe"),
                Path.Combine("C:\\Riot Games", "Riot Client", "RiotClientServices.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Riot Games", "Riot Client", "RiotClientServices.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Riot Games", "Riot Client", "RiotClientServices.exe")
            };
            
            string? exe = null;
            foreach (var p in candidates)
            {
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) { exe = p; break; }
            }
            
            if (exe == null) 
            { 
                _logger.LoginFlow("RiotClientServices.exe not found", "Cannot launch League via RiotClientServices");
                return false; 
            }

            _logger.LoginFlow("Found RiotClientServices.exe", $"Path: {exe}");
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--launch-product=league_of_legends --launch-patchline=live",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            };
            Process.Start(psi);
            _logger.ProcessEvent("RiotClientServices", "Started with League args", $"{exe} {psi.Arguments}");
            await Task.Delay(3000); // Увеличиваем задержку
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Launch League Client failed: {ex.Message}");
            return false;
        }
    }

    // ======== (Удалено) TAB-based RC login/input ========
    #region TabLogin
    // Полностью отключено по просьбе пользователя: UIAutomation-only
    private Task<bool> TryLoginViaTabsAsync(string username, string password, TimeSpan overallTimeout)
        => Task.FromResult(false);

    private IntPtr TryGetRiotClientWindow()
    {
        try
        {
            var procNames = new[] { "RiotClientUx", "RiotClientUxRender", "Riot Client" };
            foreach (var name in procNames)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
                }
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    private void FocusWindow(IntPtr hwnd)
    {
        try
        {
            ShowWindow(hwnd, ShowWindowCommands.Restore);
            SetForegroundWindow(hwnd);
        }
        catch { }
    }

    private void EnsureForegroundWithRetries(IntPtr hwnd, int retries)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                ShowWindow(hwnd, ShowWindowCommands.Restore);
                SetForegroundWindow(hwnd);
                if (GetForegroundWindow() == hwnd) return;
            }
            catch { }
            Thread.Sleep(120);
        }
    }

    private void ClickTopLeft(IntPtr hwnd)
    {
        try
        {
            if (!GetWindowRect(hwnd, out var rect))
            {
                // Фоллбек: просто в (10,10) экрана
                SetCursorPos(10, 10);
                MouseLeftClick();
                return;
            }
            int x = rect.Left + 10;
            int y = rect.Top + 10;
            SetCursorPos(x, y);
            MouseLeftClick();
        }
        catch { }
    }

    private void ClickWindowRelative(IntPtr hwnd, double xPercent, double yPercent)
    {
        try
        {
            if (!GetWindowRect(hwnd, out var rect)) return;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            int x = rect.Left + Math.Max(0, Math.Min(width - 1, (int)(width * xPercent)));
            int y = rect.Top + Math.Max(0, Math.Min(height - 1, (int)(height * yPercent)));
            SetCursorPos(x, y);
            MouseLeftClick();
        }
        catch { }
    }

    private void MouseLeftClick()
    {
        var inputs = new INPUT[2];
        inputs[0] = new INPUT
        {
            type = InputType.MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseEventFlags.LEFTDOWN } }
        };
        inputs[1] = new INPUT
        {
            type = InputType.MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseEventFlags.LEFTUP } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    // Отключено: больше не шлём TAB
    private void SendTabs(int count) { }

    private void SendVirtualKey(VirtualKey key)
    {
        var down = new INPUT
        {
            type = InputType.KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)key, dwFlags = 0 } }
        };
        var up = new INPUT
        {
            type = InputType.KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)key, dwFlags = (uint)KeyEventF.KEYUP } }
        };
        var arr = new[] { down, up };
        SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }

    private void SendUnicodeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (var ch in text)
        {
            // KEYDOWN UNICODE
            inputs.Add(new INPUT
            {
                type = InputType.KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = (uint)KeyEventF.UNICODE }
                }
            });
            // KEYUP UNICODE
            inputs.Add(new INPUT
            {
                type = InputType.KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = (uint)(KeyEventF.UNICODE | KeyEventF.KEYUP) }
                }
            });
        }
        if (inputs.Count > 0)
        {
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommands nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private enum ShowWindowCommands
    {
        Hide = 0,
        ShowNormal = 1,
        ShowMinimized = 2,
        ShowMaximized = 3,
        Restore = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    private enum VirtualKey : ushort
    {
        TAB = 0x09,
        RETURN = 0x0D,
        SPACE = 0x20
    }

    [Flags]
    private enum MouseEventFlags : uint
    {
        LEFTDOWN = 0x0002,
        LEFTUP = 0x0004
    }

    [Flags]
    private enum KeyEventF : uint
    {
        EXTENDEDKEY = 0x0001,
        KEYUP = 0x0002,
        UNICODE = 0x0004,
        SCANCODE = 0x0008
    }

    private enum InputType : uint
    {
        MOUSE = 0,
        KEYBOARD = 1,
        HARDWARE = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public InputType type;
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
        public MouseEventFlags dwFlags;
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
    #endregion

    private async Task<LockfileInfo?> WaitForLcuLockfileAsync(TimeSpan timeout)
    {
        _logger.LoginFlow("Waiting for LCU lockfile", $"Timeout: {timeout.TotalSeconds}s");
        var deadline = DateTime.UtcNow + timeout;
        var startTime = DateTime.UtcNow;
        int attempts = 0;
        
        while (DateTime.UtcNow < deadline)
        {
            attempts++;
            try
            {
                var lcu = FindLockfile(product: "LCU");
                var elapsed = DateTime.UtcNow - startTime;
                _logger.LoginFlow("LCU lockfile found", $"After {elapsed.TotalSeconds:F1}s, {attempts} attempts");
                return lcu;
            }
            catch { }
            await Task.Delay(200);
        }
        
        var totalElapsed = DateTime.UtcNow - startTime;
        _logger.LoginFlow("LCU lockfile timeout", $"After {totalElapsed.TotalSeconds:F1}s, {attempts} attempts - League did not start");
        return null;
    }

    private async Task<bool> TryLaunchLeagueViaRegistryAsync()
    {
        try
        {
            _logger.Info("Trying to launch League via Windows Registry");
            
            // Ищем в реестре путь установки League of Legends
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
            {
                if (key != null)
                {
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        using (var subKey = key.OpenSubKey(subKeyName))
                        {
                            var displayName = subKey?.GetValue("DisplayName")?.ToString();
                            if (displayName != null && displayName.Contains("League of Legends"))
                            {
                                var installLocation = subKey?.GetValue("InstallLocation")?.ToString();
                                if (!string.IsNullOrEmpty(installLocation))
                                {
                                    var leagueExe = Path.Combine(installLocation, "LeagueClient.exe");
                                    if (File.Exists(leagueExe))
                                    {
                                        _logger.Info($"Found League via Registry: {leagueExe}");
                                        var psi = new ProcessStartInfo
                                        {
                                            FileName = leagueExe,
                                            UseShellExecute = true,
                                            WorkingDirectory = installLocation
                                        };
                                        Process.Start(psi);
                                        await Task.Delay(3000);
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            _logger.Info("League of Legends not found in Registry");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Launch League via Registry failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> TryLaunchLeagueClientDirectAsync()
    {
        try
        {
            var leagueExe = ResolveLeagueClientExe();
            if (string.IsNullOrEmpty(leagueExe) || !File.Exists(leagueExe))
            {
                _logger.Error("LeagueClient.exe not found via installs map");
                return false;
            }
            
            _logger.Info($"Attempting direct launch of LeagueClient.exe: {leagueExe}");
            var psi = new ProcessStartInfo
            {
                FileName = leagueExe,
                Arguments = string.Empty,
                UseShellExecute = true, // Используем shell для обхода UAC
                WorkingDirectory = Path.GetDirectoryName(leagueExe) ?? Environment.CurrentDirectory
            };
            
            // Не используем runas если не требуется
            Process.Start(psi);
            _logger.Info($"Started LeagueClient.exe: {leagueExe}");
            await Task.Delay(3000); // Увеличиваем задержку
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Direct launch League Client failed: {ex.Message}");
            return false;
        }
    }

    private async Task WaitForRcLockfileAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                _ = FindLockfile(product: "RC");
                return;
            }
            catch { }
            await Task.Delay(200);
        }
    }

    private async Task KillRiotClientOnlyAsync()
    {
        try
        {
            var names = new[] { "RiotClientUx", "RiotClientUxRender", "RiotClientServices", "Riot Client" };
            int killed = 0;
            foreach (var name in names)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { p.Kill(true); killed++; } catch { }
                }
            }
            _logger.Info($"Killed RiotClient processes: {killed}");
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            _logger.Error($"KillRiotClientOnlyAsync error: {ex.Message}");
        }
    }

    private bool AreBothRiotProcessesRunning()
    {
        try
        {
            var svc = Process.GetProcessesByName("RiotClientServices").Length > 0;
            var cli = Process.GetProcessesByName("Riot Client").Length > 0;
            return svc && cli;
        }
        catch { return false; }
    }

    private bool IsLeagueProcessRunning()
    {
        var leagueProcesses = new[] { "LeagueClient", "LeagueClientUx" };
        foreach (var processName in leagueProcesses)
        {
            if (Process.GetProcessesByName(processName).Length > 0)
            {
                _logger.ProcessEvent(processName, "Process found", "League is running");
                return true;
            }
        }
        _logger.ProcessEvent("League", "Process check", "No League processes found");
        return false;
    }

    private async Task WaitForBothRiotProcessesAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (AreBothRiotProcessesRunning()) return;
            await Task.Delay(200);
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private string ResolveLeagueClientExe()
    {
        try
        {
            // Попытка через RiotClientInstalls.json
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); // C:\ProgramData
            var installsPath = Path.Combine(programData, "Riot Games", "RiotClientInstalls.json");
            if (File.Exists(installsPath))
            {
                var json = File.ReadAllText(installsPath);
                // Пытаемся найти путь LoL в json (простая эвристика)
                var lower = json.ToLowerInvariant();
                // частые ключи
                var keys = new[] { "league of legends", "leagueclient.exe", "leagueclientux.exe" };
                foreach (var key in keys)
                {
                    var idx = lower.IndexOf(key, StringComparison.Ordinal);
                    if (idx > 0)
                    {
                        var start = lower.LastIndexOf("c:\\", idx, idx);
                        if (start >= 0)
                        {
                            var end = lower.IndexOf("\\r", start);
                            var line = end > start ? json.Substring(start, end - start) : json.Substring(start);
                            // пробуем нормальные кандидаты около найденной строки
                            var baseDir = Path.GetDirectoryName(line) ?? "C:\\Riot Games\\League of Legends";
                            var c1 = Path.Combine(baseDir, "LeagueClient.exe");
                            if (File.Exists(c1)) return c1;
                            var c2 = Path.Combine("C:\\Riot Games", "League of Legends", "LeagueClient.exe");
                            if (File.Exists(c2)) return c2;
                        }
                    }
                }
                // Фоллбек — стандартный путь
                var candidate = Path.Combine("C:\\Riot Games", "League of Legends", "LeagueClient.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"ResolveLeagueClientExe error: {ex.Message}");
        }

        // Фоллбэки
        var candidates = new[]
        {
            Path.Combine("C:\\Riot Games", "League of Legends", "LeagueClient.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Riot Games", "League of Legends", "LeagueClient.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Riot Games", "League of Legends", "LeagueClient.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Riot Games", "League of Legends", "LeagueClient.exe")
        };
        foreach (var c in candidates) 
        {
            if (File.Exists(c)) 
            {
                _logger.Info($"Found LeagueClient.exe via fallback: {c}");
                return c;
            }
        }
        // как дополнительный запасной вариант
        var ux = Path.Combine("C:\\Riot Games", "League of Legends", "LeagueClientUx.exe");
        if (File.Exists(ux)) 
        {
            _logger.Info($"Found LeagueClientUx.exe as fallback: {ux}");
            return ux;
        }
        
        _logger.Error("LeagueClient.exe not found in any standard location");
        return string.Empty;
    }

    // Удалён RC RSO warm

    private LockfileInfo FindLockfile(string product)
    {
        string[] candidatePaths = product switch
        {
            "RC" => new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Riot Games", "Riot Client", "Config", "lockfile"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Riot Games", "Riot Client", "lockfile")
            },
            "LCU" => new[]
            {
                Path.Combine("C:\\Riot Games", "League of Legends", "lockfile"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Riot Games", "League of Legends", "lockfile"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Riot Games", "League of Legends", "lockfile"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Riot Games", "League of Legends", "lockfile")
            },
            _ => Array.Empty<string>()
        };

        string? path = null;
        foreach (var p in candidatePaths)
        {
            if (File.Exists(p)) { path = p; break; }
        }

        if (path == null)
            throw new FileNotFoundException(product == "LCU"
                ? "Не найден lockfile League Client. Откройте окно входа LoL."
                : "Не найден lockfile Riot Client. Запустите Riot Client.");

        // Чтобы не спамить каждый вызов, логируем раз в 10 секунд максимум
        try
        {
            var now = DateTime.UtcNow;
            if (_lastLockfileLog.TryGetValue(product, out var last) && (now - last).TotalSeconds < 10)
            {
                // пропускаем лог
            }
            else
            {
                _lastLockfileLog[product] = now;
                _logger.Debug($"Reading lockfile: {path}");
            }
        }
        catch { _logger.Debug($"Reading lockfile: {path}"); }
        var text = ReadAllTextUnlocked(path).Trim();
        // Формат: name:pid:port:password:protocol
        var parts = text.Split(':');
        if (parts.Length < 5)
        {
            _logger.Error($"Bad lockfile content: {text}");
            throw new FormatException($"Некорректный lockfile: {text}");
        }

        return new LockfileInfo(
            parts[0],
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture),
            parts[3],
            parts[4]
        );
    }

    private HttpClient CreateHttpClient(int port, string password)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{port}")
        };
        var byteArray = Encoding.ASCII.GetBytes($"riot:{password}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
        // минимальный UA и клиент-версии (могут влиять на RSO flow в некоторых билдах)
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RiotClient/1.0 (CEF)");
        var (rcExe, rcVer) = TryGetRiotClientServicesVersion();
        if (!string.IsNullOrEmpty(rcVer))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Riot-ClientVersion", rcVer);
        }
        return client;
    }

    private (string exePath, string version) TryGetRiotClientServicesVersion()
    {
        try
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Riot Games", "Riot Client", "RiotClientServices.exe"),
                Path.Combine("C:\\Riot Games", "Riot Client", "RiotClientServices.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Riot Games", "Riot Client", "RiotClientServices.exe")
            };
            foreach (var p in candidates)
            {
                if (File.Exists(p))
                {
                    var fvi = FileVersionInfo.GetVersionInfo(p);
                    return (p, fvi.FileVersion ?? "");
                }
            }
        }
        catch { }
        return (string.Empty, string.Empty);
    }

    private static string ReadAllTextUnlocked(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return sr.ReadToEnd();
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
        using var fallbackFs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var fallbackSr = new StreamReader(fallbackFs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return fallbackSr.ReadToEnd();
    }

    private async Task LogResponse(string label, HttpResponseMessage resp)
    {
        string body = string.Empty;
        try { body = await resp.Content.ReadAsStringAsync(); }
        catch { }
        
        // Извлекаем метод и URL из label
        var parts = label.Split(' ', 3);
        if (parts.Length >= 3)
        {
            var method = parts[1];
            var url = parts[2];
            _logger.HttpRequest(method, url, (int)resp.StatusCode, body);
        }
        else
        {
            // Fallback для старого формата
        _logger.Info($"{label} -> {(int)resp.StatusCode} {resp.ReasonPhrase} | {body}");
        }
    }

    private async Task WaitForLcuHttpReadyAsync(HttpClient client, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var resp = await client.GetAsync("/help");
                if (resp.IsSuccessStatusCode || (int)resp.StatusCode == 404)
                {
                    _logger.Info($"LCU /help -> {(int)resp.StatusCode}");
                    return;
                }
            }
            catch { }
            await Task.Delay(500);
        }
    }

    private async Task LogLcuSessionStateAsync(HttpClient client, string label)
    {
        try
        {
            var resp = await client.GetAsync("/lol-login/v1/session");
            string state = string.Empty;
            try
            {
                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                if (doc.RootElement.TryGetProperty("state", out var st)) state = st.GetString() ?? string.Empty;
            }
            catch { }
            _logger.Info($"{label} GET /lol-login/v1/session -> {(int)resp.StatusCode} {resp.ReasonPhrase} | state={state}");
        }
        catch (Exception ex) { _logger.Error($"{label} session state error: {ex.Message}"); }

        try
        {
            var resp2 = await client.GetAsync("/lol-summoner/v1/current-summoner");
            _logger.Info($"{label} GET /lol-summoner/v1/current-summoner -> {(int)resp2.StatusCode} {resp2.ReasonPhrase}");
        }
        catch (Exception ex) { _logger.Error($"{label} current-summoner error: {ex.Message}"); }
    }

    // Методы для работы с рунами
    public async Task<bool> ApplyRunePageAsync(Models.RunePage runePage)
    {
        try
        {
            var lcuLock = FindLockfile("LCU");
            if (lcuLock == null)
            {
                _logger.Error("LCU not found - League Client not running");
                return false;
            }

            using var client = CreateHttpClient(lcuLock.Port, lcuLock.Password);

            // Создаем объект страницы рун для LCU API
            var lcuRunePage = new
            {
                name = runePage.Name,
                primaryStyleId = runePage.PrimaryPathId,
                subStyleId = runePage.SecondaryPathId,
                selectedPerkIds = new[]
                {
                    runePage.PrimaryKeystoneId,
                    runePage.PrimarySlot1Id,
                    runePage.PrimarySlot2Id,
                    runePage.PrimarySlot3Id,
                    runePage.SecondarySlot1Id != 0 ? runePage.SecondarySlot1Id : 0,
                    runePage.SecondarySlot2Id != 0 ? runePage.SecondarySlot2Id : 0,
                    runePage.SecondarySlot3Id != 0 ? runePage.SecondarySlot3Id : 0,
                    runePage.StatMod1Id,
                    runePage.StatMod2Id,
                    runePage.StatMod3Id
                }.Where(id => id != 0).ToArray(),
                current = true
            };

            var json = JsonSerializer.Serialize(lcuRunePage);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.Info($"Applying rune page: {runePage.Name}");

            // Удаляем дубликат по имени (если есть редактируемая страница с тем же именем)
            try
            {
                var existingPages = await GetRunePagesAsync();
                var duplicate = existingPages.FirstOrDefault(p => p.isEditable && string.Equals(p.name, runePage.Name, StringComparison.OrdinalIgnoreCase));
                if (duplicate != null)
                {
                    _logger.Info($"Found existing editable rune page with same name (id={duplicate.id}), deleting before apply");
                    await client.DeleteAsync($"/lol-perks/v1/pages/{duplicate.id}");
                }

                // Если достигнут лимит редактируемых страниц, очистим самую старую не-актуальную
                var editable = existingPages.Where(p => p.isEditable).ToList();
                // Исторически лимит 20; оставим небольшой запас
                if (editable.Count >= 20)
                {
                    var toDelete = editable.FirstOrDefault(p => !p.current) ?? editable.First();
                    _logger.Warning($"Editable rune pages limit reached ({editable.Count}). Deleting page id={toDelete.id} name='{toDelete.name}'");
                    await client.DeleteAsync($"/lol-perks/v1/pages/{toDelete.id}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Pre-apply cleanup failed: {ex.Message}");
            }

            // Создаем страницу рун
            var response = await client.PostAsync("/lol-perks/v1/pages", content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.Info($"Rune page applied successfully: {responseContent}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error($"Failed to apply rune page: {(int)response.StatusCode} {response.ReasonPhrase} | {errorContent}");

                // На случай ошибки из-за лимита — предпримем одну попытку очистки и повтора
                try
                {
                    var pages = await GetRunePagesAsync();
                    var editable = pages.Where(p => p.isEditable).ToList();
                    if (editable.Count > 0)
                    {
                        var toDelete = editable.FirstOrDefault(p => !p.current) ?? editable.First();
                        _logger.Warning($"Retry: deleting page id={toDelete.id} then retrying apply");
                        await client.DeleteAsync($"/lol-perks/v1/pages/{toDelete.id}");
                        var retry = await client.PostAsync("/lol-perks/v1/pages", content);
                        if (retry.IsSuccessStatusCode)
                        {
                            var ok = await retry.Content.ReadAsStringAsync();
                            _logger.Info($"Rune page applied on retry: {ok}");
                            return true;
                        }
                        else
                        {
                            var err2 = await retry.Content.ReadAsStringAsync();
                            _logger.Error($"Retry failed: {(int)retry.StatusCode} {retry.ReasonPhrase} | {err2}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Retry cleanup failed: {ex.Message}");
                }

                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error applying rune page: {ex.Message}");
            return false;
        }
    }

    public async Task<List<LcuRunePage>> GetRunePagesAsync()
    {
        try
        {
            var lcuLock = FindLockfile("LCU");
            if (lcuLock == null)
            {
                _logger.Error("LCU not found - League Client not running");
                return new List<LcuRunePage>();
            }

            using var client = CreateHttpClient(lcuLock.Port, lcuLock.Password);
            var response = await client.GetAsync("/lol-perks/v1/pages");

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var runePages = JsonSerializer.Deserialize<List<LcuRunePage>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return runePages ?? new List<LcuRunePage>();
            }
            else
            {
                _logger.Error($"Failed to get rune pages: {(int)response.StatusCode} {response.ReasonPhrase}");
                return new List<LcuRunePage>();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error getting rune pages: {ex.Message}");
            return new List<LcuRunePage>();
        }
    }


    public async Task<List<LcuPerk>> GetPerksAsync()
    {
        try
        {
            var lcu = FindLockfile("LCU");
            if (lcu == null) return new List<LcuPerk>();
            
            using var client = CreateHttpClient(lcu.Port, lcu.Password);
            var resp = await client.GetAsync("/lol-perks/v1/perks");
            
            if (!resp.IsSuccessStatusCode) return new List<LcuPerk>();
            
            var json = await resp.Content.ReadAsStringAsync();
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var perks = JsonSerializer.Deserialize<List<LcuPerk>>(json, opts) ?? new List<LcuPerk>();
            return perks;
        }
        catch
        {
            return new List<LcuPerk>();
        }
    }

	public async Task<string?> GetAsync(string endpoint)
	{
		var lcu = await WaitForLcuLockfileAsync(TimeSpan.FromSeconds(5));
		if (lcu == null)
		{
			_logger.Error("LCU not found for GET request");
			return null;
		}

		try
		{
			using var client = CreateHttpClient(lcu.Port, lcu.Password);
			var response = await client.GetAsync(endpoint);
			if (response.IsSuccessStatusCode)
			{
				return await response.Content.ReadAsStringAsync();
			}
			return null;
		}
		catch (Exception ex)
		{
			_logger.Error($"GET {endpoint} failed: {ex.Message}");
			return null;
		}
	}

	public async Task<string?> PostAsync(string endpoint, HttpContent content)
	{
		var lcu = await WaitForLcuLockfileAsync(TimeSpan.FromSeconds(5));
		if (lcu == null)
		{
			_logger.Error("LCU not found for POST request");
			return null;
		}

		try
		{
			using var client = CreateHttpClient(lcu.Port, lcu.Password);
			var response = await client.PostAsync(endpoint, content);
			if (response.IsSuccessStatusCode)
			{
				return await response.Content.ReadAsStringAsync();
			}
			return null;
		}
		catch (Exception ex)
		{
			_logger.Error($"POST {endpoint} failed: {ex.Message}");
			return null;
		}
		// close PostAsync
	}

	    public async Task<(int Port, string Password)?> GetLcuAuthAsync()
    {
        try
        {
            var lcu = FindLockfile("LCU");
            return (lcu.Port, lcu.Password);
        }
        catch
        {
            // Попробуем короткое ожидание на случай гоночного состояния
            try
            {
                var ready = await WaitForLcuLockfileAsync(TimeSpan.FromSeconds(3));
                if (ready != null)
                {
                    return (ready.Port, ready.Password);
                }
            }
            catch { }
        }
        return null;
	    }

    public async Task<RunePage?> GetRecommendedRunePageAsync(int championId)
    {
        try
        {
            _logger.Info($"Fetching recommended runes for champion ID: {championId}");
            using var client = new HttpClient();
            var json = await client.GetStringAsync("http://cdn.merakianalytics.com/riot/lol/resources/latest/en-US/championrates.json");

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataElement))
                return null;

            if (!dataElement.TryGetProperty(championId.ToString(), out var champElement))
                return null;
            
            var runePage = new RunePage { Name = $"Auto for {championId}" };

            // Устанавливаем Primary Path и Keystone
            var primaryPathId = champElement.GetProperty("primaryStyleId").GetInt32();
            runePage.PrimaryPathId = primaryPathId;

            var perks = champElement.GetProperty("perkIds").EnumerateArray().Select(p => p.GetInt32()).ToList();
            runePage.PrimaryKeystoneId = perks[0];
            runePage.PrimarySlot1Id = perks[1];
            runePage.PrimarySlot2Id = perks[2];
            runePage.PrimarySlot3Id = perks[3];
            
            runePage.SecondaryPathId = champElement.GetProperty("subStyleId").GetInt32();
            runePage.SecondarySlot1Id = perks[4];
            runePage.SecondarySlot2Id = perks[5];

            runePage.StatMod1Id = perks[6];
            runePage.StatMod2Id = perks[7];
            runePage.StatMod3Id = perks[8];

            return runePage;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get recommended rune page for champ {championId}: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> GetCurrentSummonerNameAsync()
    {
        try
        {
            var lcu = FindLockfile("LCU");
            if (lcu == null) return null;
            using var client = CreateHttpClient(lcu.Port, lcu.Password);
            var resp = await client.GetAsync("/lol-summoner/v1/current-summoner");
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("displayName", out var name) ? name.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> GetCurrentSummonerPuuidAsync()
    {
        try
        {
            var lcu = FindLockfile("LCU");
            if (lcu == null) return string.Empty;
            using var client = CreateHttpClient(lcu.Port, lcu.Password);
            var resp = await client.GetAsync("/lol-summoner/v1/current-summoner");
            if (!resp.IsSuccessStatusCode) return string.Empty;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("puuid", out var puuid) ? puuid.GetString() ?? string.Empty : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}


