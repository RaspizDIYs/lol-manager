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
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Threading;
using System.Net;

namespace LolManager.Services;

public class RiotClientService : IRiotClientService
{
    private record LockfileInfo(string Name, int Pid, int Port, string Password, string Protocol);
    private readonly ILogger _logger;

    public RiotClientService() : this(new FileLogger()) {}
    public RiotClientService(ILogger logger)
    {
        _logger = logger;
    }

    public async Task LoginAsync(string username, string password)
    {
        var swAll = Stopwatch.StartNew();
        _logger.Info("LoginAsync start (Launch -> LCU login)");
        // 1) Убедиться, что RC поднят и его lockfile доступен
        var sw = Stopwatch.StartNew();
        await WaitForRcLockfileAsync(TimeSpan.FromSeconds(20));
        _logger.Info($"STEP RC_READY in {sw.ElapsedMilliseconds}ms");

        // 2) Поднять LoL через RC и дождаться LCU
        sw.Restart();
        var launched = await TryLaunchLeagueViaRiotApiAsync();
        if (!launched)
        {
            _logger.Info("RC API launch failed. Trying RiotClientServices arguments...");
            launched = await TryLaunchLeagueClientAsync();
        }
        _logger.Info($"STEP LAUNCH_DONE in {sw.ElapsedMilliseconds}ms, launched={launched}");

        sw.Restart();
        var lcu = await WaitForLcuLockfileAsync(TimeSpan.FromSeconds(45));
        int relaunches = 0;
        while (lcu == null && relaunches < 2)
        {
            await TryLaunchLeagueClientAsync();
            await Task.Delay(5000);
            lcu = await WaitForLcuLockfileAsync(TimeSpan.FromSeconds(10));
            relaunches++;
        }
        if (lcu == null)
        {
            _logger.Error("LCU lockfile didn't appear after RC launch attempts");
            return;
        }
        _logger.Info($"STEP LCU_READY in {sw.ElapsedMilliseconds}ms, port={lcu.Port}");

        // 4) Логин в LCU напрямую
        sw.Restart();
        await TryLoginViaLeagueClientWithRetries(username, password, maxAttempts: 8, delayMs: 1500);
        _logger.Info($"STEP LCU_LOGIN_FLOW in {sw.ElapsedMilliseconds}ms, total={swAll.ElapsedMilliseconds}ms");
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
            await WaitForRcLockfileAsync(TimeSpan.FromSeconds(8));
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
            new { username, password, remember = true, persistLogin = true },
            new { username, password, remember = true },
            new { username, password, persistLogin = true },
            new { username, password }
        };

        int idx = 0;
        foreach (var v in variants)
        {
            try
            {
                idx++;
                var respA = await client.PostAsync("/lol-login/v1/session", Json(v));
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
                var alt = await client.PutAsync("/rso-auth/v1/session/credentials", Json(v));
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
            // Убедиться, что RC lockfile доступен
            try { _ = FindLockfile(product: "RC"); }
            catch { await WaitForRcLockfileAsync(TimeSpan.FromSeconds(6)); }
            var rc = FindLockfile(product: "RC");
            using var rcClient = CreateHttpClient(rc.Port, rc.Password);
            // Поперечная совместимость разных билдов RiotClient API:
            // 1) новый product-launcher endpoint
            var payload = new { additionalArguments = new[] { "--launch-product=league_of_legends", "--launch-patchline=live" } };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await rcClient.PostAsync("/product-launcher/v1/products/league_of_legends/patchlines/live/launch", content);
            // не спамим тело в логах на 404
            _logger.Info($"RC POST /product-launcher/.../launch -> {(int)resp.StatusCode} {resp.ReasonPhrase}");
            if (resp.IsSuccessStatusCode) return true;

            // 2) старый универсальный endpoint (на ряде билдов)
            var oldPayload = new { product = "league_of_legends", patchline = "live", additionalArguments = new[] { "--launch-product=league_of_legends", "--launch-patchline=live" } };
            var oldResp = await rcClient.PostAsync("/product-launcher/v1/launch", Json(oldPayload));
            _logger.Info($"RC POST /product-launcher/v1/launch -> {(int)oldResp.StatusCode} {oldResp.ReasonPhrase}");
            return oldResp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Error($"TryLaunchLeagueViaRiotApiAsync error: {ex.Message}");
            return false;
        }
    }

    // === RSO (Riot Client) авторизация, максимально совместимая с новыми/старыми билд-путями ===
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

    // Убрано RSO polling чтобы не заспамливать логи

    private async Task<bool> TryLaunchLeagueClientAsync()
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
            string? exe = null;
            foreach (var p in candidates)
            {
                if (File.Exists(p)) { exe = p; break; }
            }
            if (exe == null) { _logger.Error("RiotClientServices.exe not found"); return false; }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--launch-product=league_of_legends --launch-patchline=live",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            };
            Process.Start(psi);
            _logger.Info($"Started RiotClientServices: {exe} {psi.Arguments}");
            await Task.Delay(2000);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Launch League Client failed: {ex.Message}");
            return false;
        }
    }

    private async Task<LockfileInfo?> WaitForLcuLockfileAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var lcu = FindLockfile(product: "LCU");
                return lcu;
            }
            catch { }
            await Task.Delay(200);
        }
        return null;
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
            var psi = new ProcessStartInfo
            {
                FileName = leagueExe,
                Arguments = string.Empty,
                UseShellExecute = true,
                Verb = IsAdministrator() ? "open" : "runas",
                WorkingDirectory = Path.GetDirectoryName(leagueExe) ?? Environment.CurrentDirectory
            };
            Process.Start(psi);
            _logger.Info($"Started LeagueClient.exe: {leagueExe}");
            await Task.Delay(2000);
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
            var names = new[] { "RiotClientUx", "RiotClientUxRender", "RiotClientServices" };
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
                "Riot Games", "League of Legends", "LeagueClient.exe")
        };
        foreach (var c in candidates) if (File.Exists(c)) return c;
        // как дополнительный запасной вариант
        var ux = Path.Combine("C:\\Riot Games", "League of Legends", "LeagueClientUx.exe");
        if (File.Exists(ux)) return ux;
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

        _logger.Info($"Reading lockfile: {path}");
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
        _logger.Info($"{label} -> {(int)resp.StatusCode} {resp.ReasonPhrase} | {body}");
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
}


