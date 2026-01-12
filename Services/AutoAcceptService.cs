using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LolManager.Models;
using System.Diagnostics;
using FlaUI.UIA3;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using System.Text.RegularExpressions;

namespace LolManager.Services;

public class AutoAcceptService
{
    private readonly ILogger _logger;
    private readonly IRiotClientService _riotClientService;
    private readonly DataDragonService _dataDragonService;
    private readonly ISettingsService _settingsService;
    private bool _isAutoAcceptEnabled = false;
    private CancellationTokenSource? _wsCts;
    private CancellationTokenSource? _pollCts;
    private CancellationTokenSource? _uiaCts;
    private Task? _websocketTask;
    private Task? _pollingTask;
    private Task? _uiaTask;
    private double _lastAcceptedReadyCheckTimer = -1;
    private AutomationSettings? _automationSettings;
    private int _hasPickedChampion;
    private int _hasBannedChampion;
    private int _hasSetSummonerSpells;
    private int _wsFailures;
    private volatile bool _forcePolling;
    private static readonly Regex _timerRegex = new("\"timer\":([\\d.]+)", RegexOptions.Compiled);
    private int _settingsVersion;
    private DateTime _lastEnsureCheck = DateTime.MinValue;
    private Dictionary<string, int>? _championNameToIdCache;
    private Dictionary<string, int>? _spellNameToIdCache;
	private CancellationTokenSource? _champSelectCts;
	private long _champSelectVersion;
	private long _lastChampSelectGameId = -1;
    
    private AutoAcceptMethod CurrentMethod => AutoAcceptMethodExtensions.Parse(_automationSettings?.AutoAcceptMethod);
    private bool IsMethodWebSocket => CurrentMethod == AutoAcceptMethod.WebSocket || CurrentMethod == AutoAcceptMethod.Auto;
    private bool IsMethodPolling => CurrentMethod == AutoAcceptMethod.Polling;
    private bool IsMethodUIA => CurrentMethod == AutoAcceptMethod.UIA;
    private bool ShouldWebSocketBeActive => (_isAutoAcceptEnabled && IsMethodWebSocket) || (_automationSettings?.IsEnabled == true);
    private bool ShouldPollingBeActive => (_isAutoAcceptEnabled && IsMethodPolling) || (_isAutoAcceptEnabled && IsMethodWebSocket && _forcePolling);
    private bool ShouldUiaBeActive => (_isAutoAcceptEnabled && IsMethodUIA);
    
    public event EventHandler<string>? MatchAccepted;

    public AutoAcceptService(ILogger logger, IRiotClientService riotClientService, DataDragonService dataDragonService, ISettingsService settingsService)
    {
        _logger = logger;
        _riotClientService = riotClientService;
        _dataDragonService = dataDragonService;
        _settingsService = settingsService;
        
        // Загружаем настройки автоматизации при старте
        try
        {
            var settings = _settingsService.LoadSetting<AutomationSettings>("AutomationSettings", new AutomationSettings());
            SetAutomationSettings(settings);
        }
        catch (Exception ex)
        {
            _logger.Error($"Не удалось загрузить настройки автоматизации при старте: {ex.Message}");
        }
    }
    
    public void SetAutomationSettings(AutomationSettings? settings)
    {
        _automationSettings = settings;
        Interlocked.Increment(ref _settingsVersion);
        ResetChampSelectState();
        
        _championNameToIdCache = null;
        _spellNameToIdCache = null;
        
        if (settings != null && settings.IsEnabled)
        {
            _logger.Info($"🤖 Настройки автоматизации обновлены:");
            _logger.Info($"  • Чемпион (пик 1): {settings.ChampionToPick1 ?? "(не выбрано)"}");
            _logger.Info($"  • Чемпион (пик 2): {settings.ChampionToPick2 ?? "(не выбрано)"}");
            _logger.Info($"  • Чемпион (пик 3): {settings.ChampionToPick3 ?? "(не выбрано)"}");
            _logger.Info($"  • Чемпион (бан): {settings.ChampionToBan ?? "(не выбрано)"}");
            _logger.Info($"  • Заклинание 1: {settings.SummonerSpell1 ?? "(не выбрано)"}");
            _logger.Info($"  • Заклинание 2: {settings.SummonerSpell2 ?? "(не выбрано)"}");
        }
        
        UpdateWebSocketState();
    }
    
    public void SetEnabled(bool enabled)
    {
        if (_isAutoAcceptEnabled == enabled) return;
        
        _isAutoAcceptEnabled = enabled;
        _logger.Info($"Автопринятие {(enabled ? "включено" : "выключено")}");
        
        // Обновляем состояние WebSocket
        UpdateWebSocketState();
    }

    public void Shutdown()
    {
        try { _wsCts?.Cancel(); } catch { }
        try { _pollCts?.Cancel(); } catch { }
        try { _uiaCts?.Cancel(); } catch { }
		try { _champSelectCts?.Cancel(); } catch { }
    }
    
    private void UpdateWebSocketState()
    {
        bool shouldWebSocketBeActive = ShouldWebSocketBeActive;
        bool isWebSocketActive = _websocketTask != null && !_websocketTask.IsCompleted;
        
        bool shouldPollingBeActive = ShouldPollingBeActive;
        bool isPollingActive = _pollingTask != null && !_pollingTask.IsCompleted;
        bool shouldUiaBeActive = ShouldUiaBeActive;
        bool isUiaActive = _uiaTask != null && !_uiaTask.IsCompleted;
        
        if (shouldWebSocketBeActive && !isWebSocketActive)
        {
            ResetChampSelectState();
            _wsCts?.Cancel();
            _wsCts = new CancellationTokenSource();
            _websocketTask = Task.Run(() => RunWebSocketListenerAsync(_wsCts.Token));
            _logger.Info("✅ WebSocket запущен");
        }
        else if (!shouldWebSocketBeActive && isWebSocketActive)
        {
            _wsCts?.Cancel();
            _websocketTask = null;
            _logger.Info("❌ WebSocket остановлен");
        }
        
        if (shouldPollingBeActive && !isPollingActive)
        {
            _pollCts?.Cancel();
            _pollCts = new CancellationTokenSource();
            _pollingTask = Task.Run(() => RunPollingListenerAsync(_pollCts.Token));
            _logger.Info("✅ Polling запущен");
        }
        else if (!shouldPollingBeActive && isPollingActive)
        {
            _pollCts?.Cancel();
            _pollingTask = null;
            _logger.Info("❌ Polling остановлен");
        }

        if (shouldUiaBeActive && !isUiaActive)
        {
            _uiaCts?.Cancel();
            _uiaCts = new CancellationTokenSource();
            _uiaTask = Task.Run(() => RunUiaReadyCheckLoopAsync(_uiaCts.Token));
            _logger.Info("✅ UIA ready-check запущен");
        }
        else if (!shouldUiaBeActive && isUiaActive)
        {
            _uiaCts?.Cancel();
            _uiaTask = null;
            _logger.Info("❌ UIA ready-check остановлен");
        }

        var methodLabel = IsMethodWebSocket ? "WebSocket" : IsMethodPolling ? "Polling" : IsMethodUIA ? "UIA" : "(none)";
        _logger.Info($"AutoAccept: state -> method={methodLabel}, isAutoAccept={_isAutoAcceptEnabled}, automation={_automationSettings?.IsEnabled == true}, WSActive={!(!_websocketTask?.IsCompleted ?? false)}, PollingActive={!(!_pollingTask?.IsCompleted ?? false)}, UIAActive={!(!_uiaTask?.IsCompleted ?? false)}");
    }
    
    private void ResetChampSelectState()
    {
        Interlocked.Exchange(ref _hasPickedChampion, 0);
        Interlocked.Exchange(ref _hasBannedChampion, 0);
        Interlocked.Exchange(ref _hasSetSummonerSpells, 0);
    }
    
    private async Task RunPollingListenerAsync(CancellationToken cancellationToken)
    {
        _logger.Info("AutoAccept: Polling listener запущен (брутфорс метод)");
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var lcuInfo = await FindLcuLockfileInfoAsync();
                if (lcuInfo == null)
                {
                    await Task.Delay(2000, cancellationToken);
                    continue;
                }
                
                var (port, password) = lcuInfo.Value;
                using var client = CreateHttpClient(port, password);
                
                try
                {
                    var response = await client.GetAsync("/lol-matchmaking/v1/ready-check");
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(content);
                        var root = doc.RootElement;
                        
                        if (root.TryGetProperty("state", out var state) && state.GetString() == "InProgress" &&
                            root.TryGetProperty("playerResponse", out var playerResponse) && playerResponse.GetString() == "None")
                        {
                            double currentTimer = -1;
                            if (root.TryGetProperty("timer", out var timerProp))
                            {
                                currentTimer = timerProp.GetDouble();
                            }
                            
                            if (currentTimer > 0 && _lastAcceptedReadyCheckTimer > 0 && 
                                Math.Abs(currentTimer - _lastAcceptedReadyCheckTimer) < 0.5)
                            {
                                await Task.Delay(300, cancellationToken);
                                continue;
                            }
                            
                            _lastAcceptedReadyCheckTimer = currentTimer;
                            _logger.Info($"AutoAccept (Polling): 🎯 Ready-check обнаружен (таймер: {currentTimer:F1}s), принимаю...");
                            
                            await AcceptMatchAsync(port, password);
                        }
                    }
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException)
                {
                }
                
                await Task.Delay(300, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.Info("AutoAccept: Polling listener остановлен");
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"AutoAccept Polling ошибка: {ex.GetType().Name} - {ex.Message}");
                try
                {
                    await Task.Delay(2000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task RunWebSocketListenerAsync(CancellationToken cancellationToken)
    {
        _logger.Info("AutoAccept: WebSocket listener запущен, начинаю поиск LCU...");
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.Info("AutoAccept: Поиск LCU lockfile...");
                var lcuInfo = await FindLcuLockfileInfoAsync();
                if (lcuInfo == null)
                {
                    _logger.Info("AutoAccept: LCU lockfile не найден, жду 3 сек...");
                    await Task.Delay(3000, cancellationToken);
                    continue;
                }
                
                var (port, password) = lcuInfo.Value;
                _logger.Info($"AutoAccept: LCU найден на порту {port}, подключаюсь к WebSocket...");
                
                await ConnectWebSocketAsync(port, password, cancellationToken);
                _wsFailures = 0;
            }
            catch (OperationCanceledException)
            {
                _logger.Info("AutoAccept: WebSocket listener остановлен");
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"AutoAccept WebSocket ошибка: {ex.GetType().Name} - {ex.Message}");
                _logger.Warning($"Переподключение через 5 сек...");
                _wsFailures++;
                if (_isAutoAcceptEnabled && IsMethodWebSocket && _wsFailures >= 3)
                {
                    if (!_forcePolling)
                    {
                        _logger.Warning("WS нестабилен: временно включаю Polling в качестве фоллбэка");
                        _forcePolling = true;
                        UpdateWebSocketState();
                    }
                }
                try
                {
                    await Task.Delay(5000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
    
    private async Task ConnectWebSocketAsync(int port, string password, CancellationToken cancellationToken)
    {
        // Быстрое ожидание готовности HTTP стека LCU, иначе WS даёт 403 на раннем старте
        try
        {
            using var httpProbe = CreateHttpClient(port, password);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var resp = await httpProbe.GetAsync("/help", cancellationToken);
                    if (resp.IsSuccessStatusCode) break;
                }
                catch { }
                await Task.Delay(200, cancellationToken);
            }
        }
        catch { }

        using var ws = new ClientWebSocket();
        ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{password}"));
        var origin = $"https://127.0.0.1:{port}";
        ws.Options.SetRequestHeader("Authorization", $"Basic {auth}");
        ws.Options.SetRequestHeader("Origin", origin);
        ws.Options.SetRequestHeader("User-Agent", "RiotClient/1.0 (CEF)");
        try { ws.Options.AddSubProtocol("wamp"); } catch { }

        var uri = new Uri($"wss://127.0.0.1:{port}/");

        await ws.ConnectAsync(uri, cancellationToken);
        if (_forcePolling)
        {
            _forcePolling = false; // Сброс фоллбэка при успешном коннекте
            UpdateWebSocketState();
        }
        await ws.SendAsync(
            Encoding.UTF8.GetBytes("[5, \"OnJsonApiEvent\"]"),
            WebSocketMessageType.Text,
            true,
            cancellationToken
        );
        _logger.Info("AutoAccept: WebSocket подключен, ожидаю события...");
        
        var buffer = new byte[64 * 1024];
        var messageBuilder = new StringBuilder();
        
        while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.Info("WebSocket receive cancelled (expected) - shutting down listener");
                break;
            }
            
            if (result.MessageType == WebSocketMessageType.Close)
                break;
            
            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            
            if (result.EndOfMessage)
            {
                var message = messageBuilder.ToString();
                messageBuilder.Clear();
                
                // Обработка ready-check события (только если включено автопринятие)
                if (_isAutoAcceptEnabled && (message.Contains("ready-check") || message.Contains("ReadyCheck")))
                {
                    if (message.Contains("\"state\":\"InProgress\"") && message.Contains("\"playerResponse\":\"None\""))
                    {
                        double currentTimer = -1;
                        try
                        {
                            var timerMatch = _timerRegex.Match(message);
                            if (timerMatch.Success)
                                currentTimer = double.Parse(timerMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                        }
                        catch { }
                        
                        if (currentTimer > 0 && _lastAcceptedReadyCheckTimer > 0 && 
                            Math.Abs(currentTimer - _lastAcceptedReadyCheckTimer) < 0.5)
                            continue;
                        
                        _lastAcceptedReadyCheckTimer = currentTimer;
                        _logger.Info($"AutoAccept: 🎯 Ready-check обнаружен (таймер: {currentTimer:F1}s), принимаю...");
                        
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await AcceptMatchAsync(port, password);
        }
        catch (Exception ex)
        {
                                _logger.Error($"❌ Ошибка автопринятия: {ex.Message}");
                            }
                        }, CancellationToken.None);
                    }
                }
                
                // Обработка champ-select события
                if (message.Contains("champ-select"))
                {
					var version = Interlocked.Increment(ref _champSelectVersion);
					var cts = new CancellationTokenSource();
					var prev = Interlocked.Exchange(ref _champSelectCts, cts);
					try { prev?.Cancel(); } catch { }
					try { prev?.Dispose(); } catch { }

					_ = Task.Run(async () =>
					{
						try
						{
							await HandleChampSelectAsync(message, port, password, version, cts.Token);
						}
						catch (OperationCanceledException)
						{
						}
						catch (Exception ex)
						{
							_logger.Error($"❌ Ошибка автоматизации чемпион селекта: {ex.Message}");
						}
					}, CancellationToken.None);
                }
            }
        }
        
        if (ws.State == WebSocketState.Open)
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
    }
    
    private int _acceptInProgress;
    private async Task AcceptMatchAsync(int port, string password)
    {
        if (Interlocked.CompareExchange(ref _acceptInProgress, 1, 0) != 0) return;
        using var client = CreateHttpClient(port, password);
        
        _logger.Info("Ready-check обнаружен! Автопринятие...");
        var response = await client.PostAsync("/lol-matchmaking/v1/ready-check/accept", null);
        
        if (response.IsSuccessStatusCode)
        {
            _logger.Info("✓ Матч автоматически принят");
            MatchAccepted?.Invoke(this, "Матч принят автоматически");
        }
        else
        {
            _logger.Warning($"Не удалось принять матч: {response.StatusCode}");
            // UIA-фолбэк: попробуем нажать кнопку «Принять» в UI
            try
            {
                var clicked = await TryAcceptViaUiAutomationAsync(TimeSpan.FromSeconds(2));
                if (clicked)
                {
                    _logger.Info("✓ Принятие через UIA выполнено (фолбэк)");
                    MatchAccepted?.Invoke(this, "Матч принят через UIA");
                }
                else
                {
                    _logger.Warning("UIA-фолбэк не нашёл кнопку принятия");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка UIA-фолбэка автопринятия: {ex.Message}");
            }
        }
        Interlocked.Exchange(ref _acceptInProgress, 0);
    }

    private async Task<bool> TryAcceptViaUiAutomationAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        try
        {
            using var automation = new UIA3Automation();
            while (DateTime.UtcNow < deadline)
            {
                var p = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault()
                        ?? Process.GetProcessesByName("LeagueClientUxRender").FirstOrDefault();
                if (p != null)
                {
                    Application? app = null;
                    try { app = Application.Attach(p); } catch { }
                    if (app != null)
                    {
                        var win = app.GetMainWindow(automation, TimeSpan.FromMilliseconds(500));
                        if (win != null)
                        {
                            var root = win;
                            // Ищем кнопки «Принять»/«Accept» в любом месте дерева
                            var accept = root.FindFirstDescendant(cf =>
                                cf.ByControlType(ControlType.Button).And(
                                    cf.ByName("Принять").Or(cf.ByName("Accept")).Or(cf.ByName("ACCEPT")).Or(cf.ByName("Принять матч"))
                                ));
                            if (accept != null)
                            {
                                try { accept.AsButton().Invoke(); }
                                catch { try { accept.Click(); } catch { } }
                                return true;
                            }
                        }
                    }
                }
                await Task.Delay(120);
            }
        }
        catch { }
        return false;
    }

    private async Task RunUiaReadyCheckLoopAsync(CancellationToken token)
    {
        _logger.Info("AutoAccept: UIA ready-check loop запущен");
        while (!token.IsCancellationRequested)
        {
            try
            {
                var accepted = await TryAcceptViaUiAutomationAsync(TimeSpan.FromMilliseconds(500));
                if (accepted)
                {
                    _logger.Info("AutoAccept: UIA принял матч");
                    MatchAccepted?.Invoke(this, "Матч принят через UIA");
                    await Task.Delay(1500, token);
                }
            }
            catch { }
            try { await Task.Delay(250, token); } catch (OperationCanceledException) { break; }
        }
        _logger.Info("AutoAccept: UIA ready-check loop остановлен");
    }
    
    private async Task<(int Port, string Password)?> FindLcuLockfileInfoAsync()
    {
        try
        {
            var auth = await _riotClientService.GetLcuAuthAsync();
            if (auth != null) return auth.Value;
        }
        catch { }

        // Фоллбек: используем расширённый поиск сервиса
        try
        {
            var rc = await _riotClientService.GetLcuAuthAsync();
            if (rc != null) return rc.Value;
        }
        catch { }

        return null;
    }

    private async Task HandleChampSelectAsync(string message, int port, string password, long version, CancellationToken cancellationToken)
    {
        if (_automationSettings == null || !_automationSettings.IsEnabled)
        {
            return;
        }

		cancellationToken.ThrowIfCancellationRequested();
		if (version != Volatile.Read(ref _champSelectVersion)) return;
        
        var pick1 = _automationSettings.ChampionToPick1 ?? string.Empty;
        var pick2 = _automationSettings.ChampionToPick2 ?? string.Empty;
        var pick3 = _automationSettings.ChampionToPick3 ?? string.Empty;
        var ban = _automationSettings.ChampionToBan ?? string.Empty;
        
        if (pick1 == "(Не выбрано)") pick1 = string.Empty;
        if (pick2 == "(Не выбрано)") pick2 = string.Empty;
        if (pick3 == "(Не выбрано)") pick3 = string.Empty;
        if (ban == "(Не выбрано)") ban = string.Empty;
        
        var picks = new[] { pick1, pick2, pick3 }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        
        if (picks.Count == 0 && string.IsNullOrWhiteSpace(ban))
        {
            return;
        }

        using var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;
        
        // Формат WebSocket: [8,"OnJsonApiEvent",{"data":...,"eventType":"...","uri":"..."}]
        // Нужно взять третий элемент массива (индекс 2)
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 3)
        {
            return;
        }
        
        var eventData = root[2];
        
        // Проверяем что это событие сессии чемпион селекта
        if (!eventData.TryGetProperty("uri", out var uri) || 
            uri.GetString()?.Contains("/lol-champ-select/v1/session") != true)
        {
            return; // Это не событие сессии, игнорируем
        }
        
        if (eventData.TryGetProperty("eventType", out var eventType) && eventType.GetString() == "Delete")
        {
            ResetChampSelectState();
			_lastEnsureCheck = DateTime.MinValue;
			Interlocked.Exchange(ref _lastChampSelectGameId, -1);
            return;
        }
        
        if (!eventData.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null || data.ValueKind == JsonValueKind.Array)
        {
            return;
        }

		// Если сменился gameId — сбрасываем флаги (иначе можно «протащить» прошлый пик/бан в новую сессию)
		try
		{
			if (data.TryGetProperty("gameId", out var gid) && gid.ValueKind == JsonValueKind.Number)
			{
				var gameId = gid.GetInt64();
				var prevGameId = Interlocked.Exchange(ref _lastChampSelectGameId, gameId);
				if (prevGameId != gameId)
				{
					ResetChampSelectState();
					_lastEnsureCheck = DateTime.MinValue;
				}
			}
		}
		catch { }

        if (!data.TryGetProperty("localPlayerCellId", out var localCellId))
        {
            return;
        }
        
        int myCell = localCellId.GetInt32();

		cancellationToken.ThrowIfCancellationRequested();
		if (version != Volatile.Read(ref _champSelectVersion)) return;

        // Получаем список забаненных чемпионов
        var bannedChampionIds = new HashSet<int>();
        if (data.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
        {
            foreach (var actionGroup in actions.EnumerateArray())
            {
                foreach (var action in actionGroup.EnumerateArray())
                {
                    if (!action.TryGetProperty("type", out var actionType))
                        continue;
                    
                    var type = actionType.GetString();
                    
                    // Собираем все завершенные баны
                    if (type == "ban")
                    {
                        if (action.TryGetProperty("completed", out var banCompleted) && banCompleted.GetBoolean())
                        {
                            if (action.TryGetProperty("championId", out var banChampId))
                            {
                                try
                                {
                                    var champId = banChampId.GetInt32();
                                    if (champId > 0)
                                    {
                                        bannedChampionIds.Add(champId);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
        }

        // Выбираем первый доступный чемпион из списка пиков
        string? selectedPick = null;
        if (picks.Count > 0)
        {
            foreach (var pick in picks)
            {
                var championId = await GetChampionIdByNameAsync(pick);
                if (championId > 0 && !bannedChampionIds.Contains(championId))
                {
                    selectedPick = pick;
                    break;
                }
                else if (championId > 0 && bannedChampionIds.Contains(championId))
                {
                    _logger.Info($"🚫 Чемпион [{pick}] забанен, проверяю следующий...");
                }
            }
            
            if (selectedPick == null && picks.Count > 0)
            {
                _logger.Warning($"⚠️ Все выбранные чемпионы забанены: {string.Join(", ", picks)}");
            }
        }

        if (data.TryGetProperty("actions", out actions) && actions.ValueKind == JsonValueKind.Array)
        {
            foreach (var actionGroup in actions.EnumerateArray())
            {
                foreach (var action in actionGroup.EnumerateArray())
                {
                    if (!action.TryGetProperty("actorCellId", out var actorCell) || actorCell.GetInt32() != myCell)
                        continue;
                    
                    if (!action.TryGetProperty("completed", out var completed) || completed.GetBoolean())
                        continue;
                    
                    if (!action.TryGetProperty("type", out var actionType))
                        continue;
                    
                    var type = actionType.GetString();
                    var actionId = action.GetProperty("id").GetInt64();
                    
                    if (type == "ban")
                    {
                        if (!string.IsNullOrWhiteSpace(ban) && Interlocked.CompareExchange(ref _hasBannedChampion, 1, 0) == 0)
                        {
							cancellationToken.ThrowIfCancellationRequested();
							if (version != Volatile.Read(ref _champSelectVersion)) return;
                            await BanChampionAsync(port, password, actionId, ban, cancellationToken);
                        }
                    }
                    else if (type == "pick")
                    {
                        if (!string.IsNullOrWhiteSpace(selectedPick) && Interlocked.CompareExchange(ref _hasPickedChampion, 1, 0) == 0)
                        {
							cancellationToken.ThrowIfCancellationRequested();
							if (version != Volatile.Read(ref _champSelectVersion)) return;
                            await PickChampionAsync(port, password, actionId, selectedPick, cancellationToken);
                        }
                    }
                }
            }
        }

        // Идемпотентная синхронизация выбора/бана с актуальными настройками (на случай изменения во время сессии)
        try
        {
			cancellationToken.ThrowIfCancellationRequested();
			if (version != Volatile.Read(ref _champSelectVersion)) return;
            await EnsureDesiredSelectionAsync(port, password, data, myCell, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Warning($"EnsureDesiredSelectionAsync ошибка: {ex.Message}");
        }

        // Устанавливаем саммонер спеллы (если они настроены)
        if ((!string.IsNullOrWhiteSpace(_automationSettings.SummonerSpell1) || 
             !string.IsNullOrWhiteSpace(_automationSettings.SummonerSpell2)))
        {
            if (Interlocked.CompareExchange(ref _hasSetSummonerSpells, 1, 0) == 0)
            {
                await Task.Delay(500, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				if (version != Volatile.Read(ref _champSelectVersion)) return;
                await SetSummonerSpellsAsync(port, password, cancellationToken);
            }
        }

        // 5. Устанавливаем руны
        try
        {
            var championName = selectedPick ?? _automationSettings?.ChampionToPick1;
            bool runesApplied = false;

            if (false && _automationSettings?.AutoRuneGenerationEnabled == true && !string.IsNullOrWhiteSpace(championName))
            {
                var championId = await GetChampionIdByNameAsync(championName);
                if (championId > 0)
                {
                    _logger.Info($"[AutoAccept] Auto-runes enabled. Fetching for {championName} (ID: {championId})");
                    var recommendedPage = await _riotClientService.GetRecommendedRunePageAsync(championId);
                    if (recommendedPage != null)
                    {
                        recommendedPage.Name = $"LM | {championName}";
                        await _riotClientService.ApplyRunePageAsync(recommendedPage);
                        runesApplied = true;
                    }
                    else
                    {
                        _logger.Warning($"[AutoAccept] Could not get recommended runes for {championName}.");
                    }
                }
            }

            // Fallback to selected page if auto-runes are disabled or failed
            if (!runesApplied)
            {
                if (!string.IsNullOrWhiteSpace(_automationSettings?.SelectedRunePageName) && _automationSettings.SelectedRunePageName != "(Не выбрано)")
                {
                    var selectedPage = _automationSettings.RunePages.FirstOrDefault(p => p.Name == _automationSettings.SelectedRunePageName);
                    if (selectedPage != null)
                    {
                        _logger.Info($"[AutoAccept] Applying selected rune page as fallback: {selectedPage.Name}");
                        await _riotClientService.ApplyRunePageAsync(selectedPage);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[AutoAccept] Failed to apply runes: {ex.Message}");
        }

        var summonerName = await _riotClientService.GetCurrentSummonerNameAsync();
        _logger.Info($"[AutoAccept] Completed PreMatch actions for {summonerName}");
    }

    private async Task BanChampionAsync(int port, string password, long actionId, string championName, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info($"🚫 Запрос автобана: [{championName}]");
            
            var championId = await GetChampionIdByNameAsync(championName);
            if (championId < 0)
            {
                _logger.Error($"❌ Не удалось найти ID чемпиона для бана: [{championName}]");
                return;
            }
            
            _logger.Info($"🔍 Найден ID чемпиона: {championName} = {championId}");
            
            try
            {
                var delayActive = _automationSettings?.IsPickDelayEnabled == true;
                var delaySec = Math.Clamp(_automationSettings?.PickDelaySeconds ?? 0, 0, 30);
                if (delayActive && delaySec > 0)
                {
                    _logger.Info($"⏳ Задержка перед баном: {delaySec}s");
                    await Task.Delay(TimeSpan.FromSeconds(delaySec), cancellationToken);
                }
            }
            catch { }
            
            using var client = CreateHttpClient(port, password);

			var banActionOk = await IsMyChampSelectActionActiveAsync(client, actionId, expectedType: "ban", cancellationToken);
			if (!banActionOk)
			{
				_logger.Info($"Ban: actionId={actionId} уже неактуален (phase changed) — пропускаю");
				return;
			}

            var content = new StringContent(
                $"{{\"championId\":{championId},\"completed\":true}}",
                Encoding.UTF8,
                "application/json"
            );
            var response = await client.PatchAsync($"/lol-champ-select/v1/session/actions/{actionId}", content, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.Info($"✅ Автобан выполнен: {championName} (ID:{championId})");
            }
            else
            {
                _logger.Error($"❌ Не удалось забанить {championName}: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка автобана: {ex.Message}");
        }
    }

    private async Task PickChampionAsync(int port, string password, long actionId, string championName, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info($"⭐ Запрос автопика: [{championName}]");
            
            var championId = await GetChampionIdByNameAsync(championName);
            if (championId < 0)
            {
                _logger.Error($"❌ Не удалось найти ID чемпиона для выбора: [{championName}]");
                return;
            }
            
            _logger.Info($"🔍 Найден ID чемпиона: {championName} = {championId}");
            
            try
            {
                var delayActive = _automationSettings?.IsPickDelayEnabled == true;
                var delaySec = Math.Clamp(_automationSettings?.PickDelaySeconds ?? 0, 0, 30);
                if (delayActive && delaySec > 0)
                {
                    _logger.Info($"⏳ Задержка перед пиком: {delaySec}s");
                    await Task.Delay(TimeSpan.FromSeconds(delaySec), cancellationToken);
                }
            }
            catch { }
            
            using var client = CreateHttpClient(port, password);

			var pickActionOk = await IsMyChampSelectActionActiveAsync(client, actionId, expectedType: "pick", cancellationToken);
			if (!pickActionOk)
			{
				_logger.Info($"Pick: actionId={actionId} уже неактуален (phase changed) — пропускаю");
				return;
			}

            var content = new StringContent(
                $"{{\"championId\":{championId},\"completed\":true}}",
                Encoding.UTF8,
                "application/json"
            );
            var response = await client.PatchAsync($"/lol-champ-select/v1/session/actions/{actionId}", content, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.Info($"✅ Автопик выполнен: {championName} (ID:{championId})");
            }
            else
            {
                _logger.Error($"❌ Не удалось выбрать {championName}: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка автовыбора: {ex.Message}");
        }
    }

    private async Task SetSummonerSpellsAsync(int port, string password, CancellationToken cancellationToken)
    {
        if (_automationSettings == null) return;
        
        using var client = CreateHttpClient(port, password);
        
        // Устанавливаем spell1
        if (!string.IsNullOrWhiteSpace(_automationSettings.SummonerSpell1))
        {
            try
            {
                var spell1Id = await GetSummonerSpellIdByNameAsync(_automationSettings.SummonerSpell1);
                if (spell1Id > 0)
                {
                    var content1 = new StringContent(
                        $"{{\"spell1Id\":{spell1Id}}}",
                        Encoding.UTF8,
                        "application/json"
                    );
                    var response1 = await client.PatchAsync("/lol-champ-select/v1/session/my-selection", content1, cancellationToken);
                    
                    if (response1.IsSuccessStatusCode)
                    {
                        _logger.Info($"✓ Spell 1 установлен: {_automationSettings.SummonerSpell1}");
                    }
                    else
                    {
                        _logger.Warning($"Не удалось установить spell 1: {response1.StatusCode}");
                    }
                }
                else
                {
                    _logger.Warning($"Не удалось найти ID заклинания: {_automationSettings.SummonerSpell1}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка установки spell 1: {ex.Message}");
            }
        }
        
        // Устанавливаем spell2
        if (!string.IsNullOrWhiteSpace(_automationSettings.SummonerSpell2))
        {
            try
            {
                var spell2Id = await GetSummonerSpellIdByNameAsync(_automationSettings.SummonerSpell2);
                if (spell2Id > 0)
                {
                    var content2 = new StringContent(
                        $"{{\"spell2Id\":{spell2Id}}}",
                        Encoding.UTF8,
                        "application/json"
                    );
                    var response2 = await client.PatchAsync("/lol-champ-select/v1/session/my-selection", content2, cancellationToken);
                    
                    if (response2.IsSuccessStatusCode)
                    {
                        _logger.Info($"✓ Spell 2 установлен: {_automationSettings.SummonerSpell2}");
                    }
                    else
                    {
                        _logger.Warning($"Не удалось установить spell 2: {response2.StatusCode}");
                    }
                }
                else
                {
                    _logger.Warning($"Не удалось найти ID заклинания: {_automationSettings.SummonerSpell2}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка установки spell 2: {ex.Message}");
            }
        }
    }

    private async Task EnsureDesiredSelectionAsync(int port, string password, JsonElement data, int myCell, CancellationToken cancellationToken)
    {
        if (_automationSettings == null || !_automationSettings.IsEnabled)
            return;

        // Троттлинг, чтобы не спамить LCU
        var now = DateTime.UtcNow;
        if ((now - _lastEnsureCheck).TotalMilliseconds < 450)
            return;
        _lastEnsureCheck = now;

        var pick1 = _automationSettings.ChampionToPick1 ?? string.Empty;
        var pick2 = _automationSettings.ChampionToPick2 ?? string.Empty;
        var pick3 = _automationSettings.ChampionToPick3 ?? string.Empty;
        if (pick1 == "(Не выбрано)") pick1 = string.Empty;
        if (pick2 == "(Не выбрано)") pick2 = string.Empty;
        if (pick3 == "(Не выбрано)") pick3 = string.Empty;
        
        var picks = new[] { pick1, pick2, pick3 }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        var desiredBanName = _automationSettings.ChampionToBan ?? string.Empty;

        using var client = CreateHttpClient(port, password);

		// Берём свежую сессию: WS-событие может быть не последним, а у нас ещё и есть задержки/параллельность
		JsonElement session;
		try
		{
			var resp = await client.GetAsync("/lol-champ-select/v1/session", cancellationToken);
			if (!resp.IsSuccessStatusCode) return;
			using var freshDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
			session = freshDoc.RootElement.Clone();
		}
		catch
		{
			return;
		}

		bool hasActions = session.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array;
		if (!hasActions) return;

		if (!session.TryGetProperty("localPlayerCellId", out var localCellId) || localCellId.ValueKind != JsonValueKind.Number)
			return;
		myCell = localCellId.GetInt32();

        // Получаем список забаненных чемпионов
        var bannedChampionIds = new HashSet<int>();
        long? myPickActionId = null;
        long? myBanActionId = null;
        int currentPickChampionId = 0;
        int currentBanChampionId = 0;

        foreach (var actionGroup in actions.EnumerateArray())
        {
            foreach (var action in actionGroup.EnumerateArray())
            {
                if (!action.TryGetProperty("type", out var actionType))
                    continue;

                var type = actionType.GetString();

                // Собираем все завершенные баны
                if (type == "ban")
                {
                    if (action.TryGetProperty("completed", out var banCompleted) && banCompleted.GetBoolean())
                    {
                        if (action.TryGetProperty("championId", out var banChampId))
                        {
                            try
                            {
                                var champId = banChampId.GetInt32();
                                if (champId > 0)
                                {
                                    bannedChampionIds.Add(champId);
                                }
                            }
                            catch { }
                        }
                    }
                }

                if (!action.TryGetProperty("actorCellId", out var actorCell) || actorCell.GetInt32() != myCell)
                    continue;

				// Берём только текущие активные действия, иначе легко схватить actionId «из прошлого»
				if (!action.TryGetProperty("completed", out var completedEl) || completedEl.GetBoolean())
					continue;

                var actionId = action.GetProperty("id").GetInt64();
                int actChampId = 0;
                if (action.TryGetProperty("championId", out var ch))
                {
                    try { actChampId = ch.GetInt32(); } catch { actChampId = 0; }
                }
                if (type == "pick")
                {
                    myPickActionId = actionId;
                    currentPickChampionId = actChampId;
                }
                else if (type == "ban")
                {
                    myBanActionId = actionId;
                    currentBanChampionId = actChampId;
                }
            }
        }

        // Выбираем первый доступный чемпион из списка пиков
        string? desiredPickName = null;
        if (picks.Count > 0)
        {
            foreach (var pick in picks)
            {
                var championId = await GetChampionIdByNameAsync(pick);
                if (championId > 0 && !bannedChampionIds.Contains(championId))
                {
                    desiredPickName = pick;
                    break;
                }
                else if (championId > 0 && bannedChampionIds.Contains(championId))
                {
                    _logger.Info($"🚫 Ensure: Чемпион [{pick}] забанен, проверяю следующий...");
                }
            }
            
            if (desiredPickName == null && picks.Count > 0)
            {
                _logger.Warning($"⚠️ Ensure: Все выбранные чемпионы забанены: {string.Join(", ", picks)}");
            }
        }

        if (!string.IsNullOrWhiteSpace(desiredPickName) && myPickActionId.HasValue)
        {
            var desiredPickId = await GetChampionIdByNameAsync(desiredPickName);
            if (desiredPickId > 0 && desiredPickId != currentPickChampionId)
            {
                try
                {
					var ok = await IsMyChampSelectActionActiveAsync(client, myPickActionId.Value, expectedType: "pick", cancellationToken);
					if (!ok) return;
                    var content = new StringContent($"{{\"championId\":{desiredPickId},\"completed\":true}}", Encoding.UTF8, "application/json");
                    var resp = await client.PatchAsync($"/lol-champ-select/v1/session/actions/{myPickActionId.Value}", content, cancellationToken);
                    _logger.Info(resp.IsSuccessStatusCode
                        ? $"Ensure: обновил PICK -> {desiredPickName} (ID:{desiredPickId})"
                        : $"Ensure: не удалось обновить PICK -> {resp.StatusCode}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Ensure PICK ошибка: {ex.Message}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(desiredBanName) && myBanActionId.HasValue)
        {
            var desiredBanId = await GetChampionIdByNameAsync(desiredBanName);
            if (desiredBanId > 0 && desiredBanId != currentBanChampionId)
            {
                try
                {
					var ok = await IsMyChampSelectActionActiveAsync(client, myBanActionId.Value, expectedType: "ban", cancellationToken);
					if (!ok) return;
                    var content = new StringContent($"{{\"championId\":{desiredBanId},\"completed\":true}}", Encoding.UTF8, "application/json");
                    var resp = await client.PatchAsync($"/lol-champ-select/v1/session/actions/{myBanActionId.Value}", content, cancellationToken);
                    _logger.Info(resp.IsSuccessStatusCode
                        ? $"Ensure: обновил BAN -> {desiredBanName} (ID:{desiredBanId})"
                        : $"Ensure: не удалось обновить BAN -> {resp.StatusCode}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Ensure BAN ошибка: {ex.Message}");
                }
            }
        }
    }

	private static async Task<bool> IsMyChampSelectActionActiveAsync(HttpClient client, long actionId, string expectedType, CancellationToken cancellationToken)
	{
		try
		{
			var resp = await client.GetAsync("/lol-champ-select/v1/session", cancellationToken);
			if (!resp.IsSuccessStatusCode) return false;
			using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
			var root = doc.RootElement;

			if (!root.TryGetProperty("localPlayerCellId", out var localCellId) || localCellId.ValueKind != JsonValueKind.Number)
				return false;
			var myCell = localCellId.GetInt32();

			if (!root.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array)
				return false;

			foreach (var actionGroup in actions.EnumerateArray())
			{
				foreach (var action in actionGroup.EnumerateArray())
				{
					if (!action.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
						continue;
					if (idEl.GetInt64() != actionId)
						continue;

					if (!action.TryGetProperty("type", out var typeEl))
						return false;
					if (!string.Equals(typeEl.GetString(), expectedType, StringComparison.OrdinalIgnoreCase))
						return false;

					if (!action.TryGetProperty("actorCellId", out var actorCell) || actorCell.ValueKind != JsonValueKind.Number || actorCell.GetInt32() != myCell)
						return false;

					if (!action.TryGetProperty("completed", out var completedEl) || completedEl.ValueKind != JsonValueKind.False)
						return false;

					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

    private async Task<int> GetChampionIdByNameAsync(string displayName)
    {
        try
        {
            if (_championNameToIdCache == null)
            {
                var champions = await _dataDragonService.GetChampionsAsync();
                _championNameToIdCache = new Dictionary<string, int>();
                foreach (var kvp in champions)
                {
                    if (int.TryParse(kvp.Value, out var id))
                    {
                        _championNameToIdCache[kvp.Key] = id;
                    }
                }
                _logger.Info($"Кеш чемпионов загружен: {_championNameToIdCache.Count} записей");
            }

            if (_championNameToIdCache.TryGetValue(displayName, out var cachedId))
            {
                return cachedId;
            }
            
            _logger.Warning($"Чемпион '{displayName}' не найден в кеше");
            return -1;
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка получения ID чемпиона: {ex.Message}");
            return -1;
        }
    }

    private async Task<int> GetSummonerSpellIdByNameAsync(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return 0;
        
        try
        {
            if (_spellNameToIdCache == null)
            {
                var spells = await _dataDragonService.GetSummonerSpellsAsync();
                _spellNameToIdCache = new Dictionary<string, int>();
                foreach (var kvp in spells)
                {
                    if (int.TryParse(kvp.Value, out var id))
                    {
                        _spellNameToIdCache[kvp.Key] = id;
                    }
                }
                _logger.Info($"Кеш заклинаний загружен: {_spellNameToIdCache.Count} записей");
            }

            if (_spellNameToIdCache.TryGetValue(displayName, out var cachedId))
            {
                return cachedId;
            }
            
            _logger.Warning($"Заклинание '{displayName}' не найдено в кеше");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка получения ID заклинания: {ex.Message}");
            return 0;
        }
    }
    
    private HttpClient CreateHttpClient(int port, string password)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{port}/"),
            Timeout = TimeSpan.FromSeconds(5) // Уменьшаем таймаут с 100 до 5 секунд
        };
        
        var base64Auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{password}"));
        client.DefaultRequestHeaders.Add("Authorization", $"Basic {base64Auth}");
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        return client;
    }
}