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

namespace LolManager.Services;

public class AutoAcceptService
{
    private readonly ILogger _logger;
    private readonly IRiotClientService _riotClientService;
    private readonly DataDragonService _dataDragonService;
    private bool _isAutoAcceptEnabled = false;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _websocketTask;
    private double _lastAcceptedReadyCheckTimer = -1;
    private AutomationSettings? _automationSettings;
    private int _hasPickedChampion;
    private int _hasBannedChampion;
    private int _hasSetSummonerSpells;
    
    private bool ShouldWebSocketBeActive => _isAutoAcceptEnabled || (_automationSettings?.IsEnabled == true);
    
    public event EventHandler<string>? MatchAccepted;

    public AutoAcceptService(ILogger logger, IRiotClientService riotClientService, DataDragonService dataDragonService)
    {
        _logger = logger;
        _riotClientService = riotClientService;
        _dataDragonService = dataDragonService;
        
        // Загружаем настройки автоматизации при старте
        try
        {
            var settingsService = new SettingsService();
            var settings = settingsService.LoadSetting<AutomationSettings>("AutomationSettings", new AutomationSettings());
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
        
        if (settings != null)
        {
            _logger.Info($"🤖 Настройки автоматизации обновлены:");
            _logger.Info($"  • Включено: {settings.IsEnabled}");
            _logger.Info($"  • Чемпион (пик): {settings.ChampionToPick ?? "(не выбрано)"}");
            _logger.Info($"  • Чемпион (бан): {settings.ChampionToBan ?? "(не выбрано)"}");
            _logger.Info($"  • Заклинание 1: {settings.SummonerSpell1 ?? "(не выбрано)"}");
            _logger.Info($"  • Заклинание 2: {settings.SummonerSpell2 ?? "(не выбрано)"}");
        }
        else
        {
            _logger.Info("🤖 Настройки автоматизации очищены");
        }
        
        // Обновляем состояние WebSocket
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
    
    private void UpdateWebSocketState()
    {
        bool shouldBeActive = ShouldWebSocketBeActive;
        bool isActive = _websocketTask != null && !_websocketTask.IsCompleted;
        
        _logger.Info($"🔌 WebSocket состояние: должен быть активен={shouldBeActive}, сейчас активен={isActive}");
        _logger.Info($"   AutoAccept={_isAutoAcceptEnabled}, Automation={_automationSettings?.IsEnabled == true}");
        
        if (shouldBeActive && !isActive)
        {
            // Запускаем WebSocket
            ResetChampSelectState();
            _cancellationTokenSource = new CancellationTokenSource();
            _websocketTask = Task.Run(() => RunWebSocketListenerAsync(_cancellationTokenSource.Token));
            _logger.Info("✅ WebSocket запущен");
        }
        else if (!shouldBeActive && isActive)
        {
            // Останавливаем WebSocket
            _cancellationTokenSource?.Cancel();
            _websocketTask = null;
            _logger.Info("❌ WebSocket остановлен");
        }
    }
    
    private void ResetChampSelectState()
    {
        Interlocked.Exchange(ref _hasPickedChampion, 0);
        Interlocked.Exchange(ref _hasBannedChampion, 0);
        Interlocked.Exchange(ref _hasSetSummonerSpells, 0);
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
        using var ws = new ClientWebSocket();
        ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{password}"));
        ws.Options.SetRequestHeader("Authorization", $"Basic {auth}");
        
        var uri = new Uri($"wss://127.0.0.1:{port}/");
        
        await ws.ConnectAsync(uri, cancellationToken);
        await ws.SendAsync(
            Encoding.UTF8.GetBytes("[5, \"OnJsonApiEvent\"]"),
            WebSocketMessageType.Text,
            true,
            cancellationToken
        );
        _logger.Info("AutoAccept: WebSocket подключен, ожидаю события...");
        
        var buffer = new byte[8192];
        var messageBuilder = new StringBuilder();
        
        while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            
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
                            var timerMatch = System.Text.RegularExpressions.Regex.Match(message, @"""timer"":([\d.]+)");
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
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleChampSelectAsync(message, port, password);
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
    
    private async Task AcceptMatchAsync(int port, string password)
    {
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
        }
    }
    
    private async Task<(int Port, string Password)?> FindLcuLockfileInfoAsync()
        {
            string[] candidatePaths = new[]
            {
                Path.Combine("C:\\Riot Games", "League of Legends", "lockfile"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Riot Games", "League of Legends", "lockfile"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Riot Games", "League of Legends", "lockfile"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Riot Games", "League of Legends", "lockfile")
            };

        foreach (var path in candidatePaths)
        {
            if (!File.Exists(path)) continue;
            
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var content = await sr.ReadToEndAsync();
                var parts = content.Split(':');
                
                if (parts.Length >= 5)
                {
                    _logger.Info($"AutoAccept: Найден lockfile: {path}");
                    return (int.Parse(parts[2]), parts[3]);
                }
            }
            catch (IOException ex)
            {
                _logger.Warning($"AutoAccept: Ошибка чтения {path}: {ex.Message}");
                continue;
            }
            catch (Exception ex)
            {
                _logger.Warning($"AutoAccept: Ошибка парсинга lockfile: {ex.Message}");
                continue;
            }
        }
        
                return null;
            }

    private async Task HandleChampSelectAsync(string message, int port, string password)
    {
        if (_automationSettings == null)
        {
            _logger.Warning("⚠️ HandleChampSelectAsync: настройки null");
            return;
        }
        
        if (!_automationSettings.IsEnabled)
        {
            _logger.Info("⏸️ HandleChampSelectAsync: автоматизация ВЫКЛЮЧЕНА, пропускаю");
            return;
        }
        
        var pick = _automationSettings.ChampionToPick ?? string.Empty;
        var ban = _automationSettings.ChampionToBan ?? string.Empty;
        
        // Очистка значений "(Не выбрано)" на всякий случай
        if (pick == "(Не выбрано)") pick = string.Empty;
        if (ban == "(Не выбрано)") ban = string.Empty;
        
        _logger.Info($"🎯 HandleChampSelectAsync: IsEnabled=TRUE, Pick=[{pick}], Ban=[{ban}]");
        
        // Если ничего не настроено - выходим
        if (string.IsNullOrWhiteSpace(pick) && string.IsNullOrWhiteSpace(ban))
        {
            _logger.Info("⏭️ Нечего делать: чемпионы не выбраны");
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
            !uri.GetString()?.Contains("/lol-champ-select/v1/session") == true)
        {
            return; // Это не событие сессии, игнорируем
        }
        
        // Проверяем тип события
        if (eventData.TryGetProperty("eventType", out var eventType) && eventType.GetString() == "Delete")
        {
            // Сессия завершена - сбрасываем флаги для следующего матча
            _logger.Info("🔄 Сессия champ-select завершена, сброс флагов");
            ResetChampSelectState();
            return;
        }
        
        if (!eventData.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null || data.ValueKind == JsonValueKind.Array)
        {
            return; // Нет данных или это массив, а не объект сессии
        }

        _logger.Info("✅ Получена валидная сессия champ-select!");

        // Получаем локального игрока
        if (!data.TryGetProperty("localPlayerCellId", out var localCellId))
        {
            _logger.Warning("HandleChampSelectAsync: не найден localPlayerCellId");
            return;
        }
        
        int myCell = localCellId.GetInt32();
        _logger.Info($"🎮 Моя ячейка: {myCell}");

        // Обрабатываем действия
        if (data.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
        {
            _logger.Info($"🔍 Обрабатываю действия (уже забанено: {_hasBannedChampion != 0}, уже выбрано: {_hasPickedChampion != 0})");
            
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
                    
                    _logger.Info($"🎯 Найдено действие: type={type}, actionId={actionId}, completed={completed.GetBoolean()}");
                    
                    // Бан
                    if (type == "ban")
                    {
                        if (!string.IsNullOrWhiteSpace(ban))
                        {
                            if (Interlocked.CompareExchange(ref _hasBannedChampion, 1, 0) == 0)
                            {
                                _logger.Info($"🚫 Выполняю бан: [{ban}]");
                                await BanChampionAsync(port, password, actionId, ban);
                            }
                            else
                            {
                                _logger.Info($"⏭️ Бан уже выполнен ранее");
                            }
                        }
                        else
                        {
                            _logger.Info($"⏭️ Пропускаю бан: чемпион не выбран");
                        }
                    }
                    // Выбор
                    else if (type == "pick")
                    {
                        if (!string.IsNullOrWhiteSpace(pick))
                        {
                            if (Interlocked.CompareExchange(ref _hasPickedChampion, 1, 0) == 0)
                            {
                                _logger.Info($"✨ Выполняю пик: [{pick}]");
                                await PickChampionAsync(port, password, actionId, pick);
                            }
                            else
                            {
                                _logger.Info($"⏭️ Пик уже выполнен ранее");
                            }
                        }
                        else
                        {
                            _logger.Info($"⏭️ Пропускаю пик: чемпион не выбран");
                        }
                    }
                }
            }
        }
        else
        {
            _logger.Info("ℹ️ Нет действий в данных champ-select");
        }

        // Устанавливаем саммонер спеллы (если они настроены)
        if ((!string.IsNullOrWhiteSpace(_automationSettings.SummonerSpell1) || 
             !string.IsNullOrWhiteSpace(_automationSettings.SummonerSpell2)))
        {
            if (Interlocked.CompareExchange(ref _hasSetSummonerSpells, 1, 0) == 0)
            {
                // Небольшая задержка чтобы дать время на загрузку чемпион селекта
                await Task.Delay(500);
                await SetSummonerSpellsAsync(port, password);
            }
        }
    }

    private async Task BanChampionAsync(int port, string password, long actionId, string championName)
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
            
            using var client = CreateHttpClient(port, password);
            var content = new StringContent(
                $"{{\"championId\":{championId},\"completed\":true}}",
                Encoding.UTF8,
                "application/json"
            );
            var response = await client.PatchAsync($"/lol-champ-select/v1/session/actions/{actionId}", content);
            
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

    private async Task PickChampionAsync(int port, string password, long actionId, string championName)
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
            
            using var client = CreateHttpClient(port, password);
            var content = new StringContent(
                $"{{\"championId\":{championId},\"completed\":true}}",
                Encoding.UTF8,
                "application/json"
            );
            var response = await client.PatchAsync($"/lol-champ-select/v1/session/actions/{actionId}", content);
            
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

    private async Task SetSummonerSpellsAsync(int port, string password)
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
                    var response1 = await client.PatchAsync("/lol-champ-select/v1/session/my-selection", content1);
                    
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
                    var response2 = await client.PatchAsync("/lol-champ-select/v1/session/my-selection", content2);
                    
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

    private async Task<int> GetChampionIdByNameAsync(string displayName)
    {
        try
        {
            var champions = await _dataDragonService.GetChampionsAsync();
            if (champions.TryGetValue(displayName, out var idStr) && int.TryParse(idStr, out var id))
            {
                return id;
            }
            _logger.Warning($"Чемпион '{displayName}' не найден в Data Dragon");
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
            var spells = await _dataDragonService.GetSummonerSpellsAsync();
            if (spells.TryGetValue(displayName, out var idStr) && int.TryParse(idStr, out var id))
            {
                return id;
            }
            _logger.Warning($"Заклинание '{displayName}' не найдено в Data Dragon");
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