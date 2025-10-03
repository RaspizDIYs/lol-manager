using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LolManager.Services;

public class AutoAcceptService
{
    private readonly ILogger _logger;
    private readonly IRiotClientService _riotClientService;
    private bool _isEnabled = false;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _websocketTask;
    private double _lastAcceptedReadyCheckTimer = -1;
    
    public event EventHandler<string>? MatchAccepted;

    public AutoAcceptService(ILogger logger, IRiotClientService riotClientService)
    {
        _logger = logger;
        _riotClientService = riotClientService;
    }
    
    public void SetEnabled(bool enabled)
    {
        if (_isEnabled == enabled) return;
        
        _isEnabled = enabled;
        
        if (enabled)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _websocketTask = Task.Run(() => RunWebSocketListenerAsync(_cancellationTokenSource.Token));
            _logger.Info("Автопринятие игры включено (WebSocket)");
        }
        else
        {
            _cancellationTokenSource?.Cancel();
            _websocketTask = null;
            _logger.Info("Автопринятие игры выключено");
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
                
                // Проверяем только ready-check события
                if (!message.Contains("ready-check") && !message.Contains("ReadyCheck"))
                    continue;
                
                // Должен быть InProgress и НЕ Accepted (playerResponse == None)
                if (!message.Contains("\"state\":\"InProgress\""))
                    continue;
                    
                if (!message.Contains("\"playerResponse\":\"None\""))
                    continue;
                
                // Извлекаем таймер для дедупликации
                double currentTimer = -1;
                try
                {
                    var timerMatch = System.Text.RegularExpressions.Regex.Match(message, @"""timer"":([\d.]+)");
                    if (timerMatch.Success)
                        currentTimer = double.Parse(timerMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch { }
                
                // Пропускаем если уже принимали этот ready-check (в пределах 0.5 сек)
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
    
    private HttpClient CreateHttpClient(int port, string password)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true // Игнорировать проверку сертификата
        };
        
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{port}/")
        };
        
        var base64Auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{password}"));
        client.DefaultRequestHeaders.Add("Authorization", $"Basic {base64Auth}");
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        return client;
    }
}