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
using System.Linq;
using System.Runtime.InteropServices;
using System.Collections.Generic;

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

    public bool IsRiotClientRunning()
    {
        try
        {
            // Быстрая проверка по процессам и/или RC lockfile
            if (Process.GetProcessesByName("RiotClientServices").Length > 0) return true;
            if (Process.GetProcessesByName("Riot Client").Length > 0) return true;
            try { _ = FindLockfile(product: "RC"); return true; } catch { }
        }
        catch { }
        return false;
    }

    public async Task LoginAsync(string username, string password)
    {
        var swAll = Stopwatch.StartNew();
        _logger.Info("LoginAsync start (TAB flow in Riot Client)");

        // 1) Убедиться, что RC запущен
        var sw = Stopwatch.StartNew();
        if (!AreBothRiotProcessesRunning())
        {
            _logger.Info("Both RC processes not running. Restarting Riot Client...");
            await KillRiotClientOnlyAsync();
            await StartRiotClientAsync();
            await WaitForBothRiotProcessesAsync(TimeSpan.FromSeconds(25));
        }
        await WaitForRcLockfileAsync(TimeSpan.FromSeconds(20));
        _logger.Info($"STEP RC_READY in {sw.ElapsedMilliseconds}ms");

        // 2) Фокус RC окно и выполнить сценарий TAB-ввода
        sw.Restart();
        bool tabOk = await TryLoginViaTabsAsync(username, password, TimeSpan.FromSeconds(25));
        _logger.Info($"STEP TAB_INPUT_DONE result={tabOk} in {sw.ElapsedMilliseconds}ms");

        // 3) Дождаться появления LCU после запуска LoL (Space+Enter на тайле)
        sw.Restart();
        var lcu = await WaitForLcuLockfileAsync(TimeSpan.FromSeconds(45));
        if (lcu == null)
        {
            _logger.Info("LCU not detected after TAB flow. Trying API/args launch as fallback...");
            var launched = await TryLaunchLeagueViaRiotApiAsync();
            if (!launched) launched = await TryLaunchLeagueClientAsync();
            if (launched)
            {
                lcu = await WaitForLcuLockfileAsync(TimeSpan.FromSeconds(30));
            }
        }
        _logger.Info($"STEP LCU_READY={lcu != null} in {sw.ElapsedMilliseconds}ms, total={swAll.ElapsedMilliseconds}ms");
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
            var oldJson = JsonSerializer.Serialize(oldPayload);
            var oldResp = await rcClient.PostAsync("/product-launcher/v1/launch", new StringContent(oldJson, Encoding.UTF8, "application/json"));
            _logger.Info($"RC POST /product-launcher/v1/launch -> {(int)oldResp.StatusCode} {oldResp.ReasonPhrase}");
            return oldResp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Error($"TryLaunchLeagueViaRiotApiAsync error: {ex.Message}");
            return false;
        }
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
    #endif
    #endregion

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

    // ======== TAB-based RC login/input ========
    #region TabLogin
    private async Task<bool> TryLoginViaTabsAsync(string username, string password, TimeSpan overallTimeout)
    {
        try
        {
            var deadline = DateTime.UtcNow + overallTimeout;
            IntPtr hwnd = IntPtr.Zero;
            for (int i = 0; i < 20 && hwnd == IntPtr.Zero; i++)
            {
                hwnd = TryGetRiotClientWindow();
                if (hwnd == IntPtr.Zero) await Task.Delay(500);
            }
            if (hwnd == IntPtr.Zero)
            {
                _logger.Error("Riot Client window not found for TAB flow");
                return false;
            }

            FocusWindow(hwnd);
            await Task.Delay(200);

            // Клик в левый верхний угол окна
            ClickTopLeft(hwnd);
            await Task.Delay(120);

            // 3x TAB
            SendTabs(3);
            await Task.Delay(120);

            // Ввод логина (UNICODE)
            SendUnicodeText(username);
            await Task.Delay(120);

            // 1x TAB
            SendTabs(1);
            await Task.Delay(120);

            // Ввод пароля (UNICODE)
            SendUnicodeText(password);
            await Task.Delay(120);

            // 4x TAB -> фокус на тайле LoL (ожидаемо)
            SendTabs(4);
            await Task.Delay(120);

            // Space -> click top-left -> Enter
            SendVirtualKey(VirtualKey.SPACE);
            await Task.Delay(120);
            ClickTopLeft(hwnd);
            await Task.Delay(120);
            SendVirtualKey(VirtualKey.RETURN);

            // Немного подождать, пока RC обработает и начнёт запуск
            while (DateTime.UtcNow < deadline)
            {
                // Если появился LCU lockfile, выходим
                try { var lcu = FindLockfile(product: "LCU"); return true; } catch { }
                await Task.Delay(500);
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"TryLoginViaTabsAsync error: {ex.Message}");
            return false;
        }
    }

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

    private void SendTabs(int count)
    {
        for (int i = 0; i < count; i++) SendVirtualKey(VirtualKey.TAB);
    }

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


