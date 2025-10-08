using System.Net.Http;
using System.Text.Json;
using LolManager.Services;
using LolManager.Models;

namespace LolManager.Services;

public class RevealService
{
    private readonly HttpClient _httpClient;
    private readonly IRiotClientService _riotClientService;
    private readonly ILogger _logger;
    private string _riotApiKey = string.Empty;
    private string _selectedRegion = "euw1";

    public RevealService(IRiotClientService riotClientService, ILogger logger)
    {
        _httpClient = new HttpClient();
        _riotClientService = riotClientService;
        _logger = logger;
    }

    public void SetApiKey(string apiKey)
    {
        _riotApiKey = apiKey;
        _httpClient.DefaultRequestHeaders.Clear();
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Riot-Token", apiKey);
        }
    }

    public void SetRegion(string region)
    {
        _selectedRegion = region;
    }

    public void SetApiConfiguration(string apiKey, string region)
    {
        SetApiKey(apiKey);
        SetRegion(region);
    }

    public async Task<bool> TestApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(_riotApiKey))
            return false;

        try
        {
            _logger.Info($"Testing API key with region: {_selectedRegion}");
            
            // Используем более простой эндпоинт - platform status (не требует rate limit)
            var response = await _httpClient.GetAsync($"https://{_selectedRegion}.api.riotgames.com/lol/status/v4/platform-data");
            
            _logger.Info($"API test response: {response.StatusCode} - {response.ReasonPhrase}");
            
            // Логгируем заголовки для отладки
            foreach (var header in response.Headers)
            {
                _logger.Info($"Header: {header.Key} = {string.Join(", ", header.Value)}");
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                _logger.Info("API key is valid - got successful response");
                return true;
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // Если 403, попробуем альтернативный способ - через summoner endpoint но с задержкой
                _logger.Info("Status endpoint returned 403, trying alternative test...");
                await Task.Delay(2000); // Ждём 2 секунды
                
                var altResponse = await _httpClient.GetAsync($"https://{_selectedRegion}.api.riotgames.com/lol/summoner/v4/summoners/by-name/RiotAPITestUser123456789");
                _logger.Info($"Alternative test response: {altResponse.StatusCode}");
                
                // Даже 404 означает что ключ работает
                return altResponse.StatusCode != System.Net.HttpStatusCode.Unauthorized && 
                       altResponse.StatusCode != System.Net.HttpStatusCode.Forbidden;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error($"API key validation failed: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"API key test failed: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> GetSummonerByNameAsync(string summonerName, string? region = null)
    {
        if (string.IsNullOrWhiteSpace(_riotApiKey))
        {
            _logger.Warning("GetSummonerByNameAsync: No API key provided");
            return null;
        }

        var targetRegion = region ?? _selectedRegion;
        _logger.Info($"GetSummonerByNameAsync: {summonerName} in region {targetRegion}");

        try
        {
            var url = $"https://{targetRegion}.api.riotgames.com/lol/summoner/v4/summoners/by-name/{Uri.EscapeDataString(summonerName)}";
            _logger.Info($"Making request to: {url}");
            
            var response = await _httpClient.GetStringAsync(url);
            _logger.Info($"Summoner API response for {summonerName}: {response}");
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get summoner {summonerName}: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> GetRankedStatsAsync(string summonerId, string? region = null)
    {
        if (string.IsNullOrWhiteSpace(_riotApiKey))
        {
            _logger.Warning("GetRankedStatsAsync: No API key provided");
            return null;
        }

        var targetRegion = region ?? _selectedRegion;
        _logger.Info($"GetRankedStatsAsync: {summonerId} in region {targetRegion}");

        try
        {
            var url = $"https://{targetRegion}.api.riotgames.com/lol/league/v4/entries/by-summoner/{summonerId}";
            _logger.Info($"Making request to: {url}");
            
            var response = await _httpClient.GetStringAsync(url);
            _logger.Info($"Ranked API response for {summonerId}: {response}");
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get ranked stats for {summonerId}: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> GetRankedStatsByPuuidAsync(string puuid, string? region = null)
    {
        if (string.IsNullOrWhiteSpace(_riotApiKey))
        {
            _logger.Warning("GetRankedStatsByPuuidAsync: No API key provided");
            return null;
        }

        var targetRegion = region ?? _selectedRegion;
        _logger.Info($"GetRankedStatsByPuuidAsync: {puuid} in region {targetRegion}");

        try
        {
            var url = $"https://{targetRegion}.api.riotgames.com/lol/league/v4/entries/by-puuid/{puuid}";
            _logger.Info($"Making request to: {url}");
            
            var response = await _httpClient.GetStringAsync(url);
            _logger.Info($"Ranked API response for PUUID {puuid}: {response}");
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get ranked stats for PUUID {puuid}: {ex.Message}");
            return null;
        }
    }

    public async Task<List<PlayerInfo>?> GetTeamInfoAsync()
    {
        try
        {
            _logger.Info("Starting GetTeamInfoAsync...");
            
            // Получаем информацию о текущей сессии чемпионского селекта
            var champSelectData = await _riotClientService.GetAsync("/lol-champ-select/v1/session");
            if (champSelectData == null)
            {
                _logger.Info("Champ select session not found - not in champion select");
                return null;
            }

            _logger.Info($"Champ select data received: {champSelectData.Length} characters");
            
            var champSelectJson = JsonDocument.Parse(champSelectData);
            if (!champSelectJson.RootElement.TryGetProperty("myTeam", out var myTeam))
            {
                _logger.Info("No myTeam property in champ select data");
                return null;
            }

            var teamInfo = new List<PlayerInfo>();
            var teamArray = myTeam.EnumerateArray().ToList();
            _logger.Info($"Found {teamArray.Count} players in myTeam");
            
            foreach (var player in teamArray)
            {
                _logger.Info($"Processing player: {player}");
                
                var playerInfo = new PlayerInfo();
                
                // Получаем имя игрока напрямую из чемп селекта (работает даже для скрытых!)
                string gameName = "";
                string tagLine = "";
                
                if (player.TryGetProperty("gameName", out var gameNameElement))
                {
                    gameName = gameNameElement.GetString() ?? "";
                }
                
                if (player.TryGetProperty("tagLine", out var tagLineElement))
                {
                    tagLine = tagLineElement.GetString() ?? "";
                }
                
                if (!string.IsNullOrEmpty(gameName) && !string.IsNullOrEmpty(tagLine))
                {
                    playerInfo.Name = $"{gameName}#{tagLine}";
                    _logger.Info($"Player Riot ID from champ select: {playerInfo.Name}");
                }
                else
                {
                    _logger.Warning("No gameName/tagLine in player data");
                    continue;
                }
                
                // Получаем выбранного чемпиона если есть
                if (player.TryGetProperty("championId", out var championIdElement))
                {
                    var championId = championIdElement.GetInt32();
                    if (championId > 0)
                    {
                        playerInfo.Champion = $"Champion {championId}";
                        _logger.Info($"Champion ID: {championId}");
                    }
                }

                // Получаем рейтинговую информацию через Riot API если есть ключ
                if (!string.IsNullOrEmpty(_riotApiKey) && !string.IsNullOrEmpty(playerInfo.Name))
                {
                    _logger.Info($"Fetching ranked info for {playerInfo.Name}...");
                    await FillRankedInfoByPuuidAsync(playerInfo, "");
                }
                else
                {
                    _logger.Info($"Skipping ranked info - API key: {!string.IsNullOrEmpty(_riotApiKey)}, Name: '{playerInfo.Name}'");
                }

                playerInfo.UggLink = GenerateUggLink(playerInfo.Name);
                _logger.Info($"Generated U.GG link: {playerInfo.UggLink}");
                
                teamInfo.Add(playerInfo);
            }

            _logger.Info($"Returning {teamInfo.Count} players");
            return teamInfo;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get team info: {ex.Message}");
            _logger.Error($"Stack trace: {ex.StackTrace}");
            return null;
        }
    }

    private async Task FillRankedInfoAsync(PlayerInfo playerInfo)
    {
        try
        {
            _logger.Info($"FillRankedInfoAsync for player: {playerInfo.Name}");
            
            // Парсим Riot ID (gameName#tagLine)
            string gameName = "";
            string tagLine = "";
            
            if (playerInfo.Name.Contains('#'))
            {
                var parts = playerInfo.Name.Split('#');
                if (parts.Length == 2)
                {
                    gameName = parts[0];
                    tagLine = parts[1];
                    _logger.Info($"Parsed Riot ID - gameName: {gameName}, tagLine: {tagLine}");
                }
            }
            
            if (string.IsNullOrEmpty(gameName) || string.IsNullOrEmpty(tagLine))
            {
                _logger.Warning($"Cannot parse Riot ID from: {playerInfo.Name}");
                return;
            }
            
            // Используем новый Account API для получения PUUID
            var accountResponse = await GetAccountByRiotIdAsync(gameName, tagLine);
            if (accountResponse == null)
            {
                _logger.Warning($"No account response for {playerInfo.Name}");
                return;
            }
            
            _logger.Info($"Account API response: {accountResponse}");
            
            var accountJson = JsonDocument.Parse(accountResponse);
            if (!accountJson.RootElement.TryGetProperty("puuid", out var puuidElement))
            {
                _logger.Warning($"No 'puuid' property in account response for {playerInfo.Name}");
                return;
            }
            
            var puuid = puuidElement.GetString();
            if (string.IsNullOrEmpty(puuid))
            {
                _logger.Warning($"Empty PUUID for {playerInfo.Name}");
                return;
            }
            
            _logger.Info($"PUUID for {playerInfo.Name}: {puuid}");
            
            // Получаем summoner по PUUID
            var summonerResponse = await GetSummonerByPuuidAsync(puuid);
            if (summonerResponse == null)
            {
                _logger.Warning($"No summoner response for PUUID {puuid}");
                return;
            }
            
            var summonerJson = JsonDocument.Parse(summonerResponse);
            if (!summonerJson.RootElement.TryGetProperty("id", out var idElement))
            {
                _logger.Warning($"No 'id' property in summoner response for {playerInfo.Name}");
                return;
            }
            
            var summonerId = idElement.GetString();
            if (string.IsNullOrEmpty(summonerId))
            {
                _logger.Warning($"Empty summoner ID for {playerInfo.Name}");
                return;
            }
            
            _logger.Info($"Summoner ID for {playerInfo.Name}: {summonerId}");

            // Получаем рейтинговую статистику
            var rankedResponse = await GetRankedStatsAsync(summonerId);
            if (rankedResponse == null)
            {
                _logger.Warning($"No ranked response for {playerInfo.Name} (ID: {summonerId})");
                return;
            }

            _logger.Info($"Ranked API response for {playerInfo.Name}: {rankedResponse}");
            
            var rankedArray = JsonDocument.Parse(rankedResponse);
            
            bool foundRankedData = false;
            
            // Ищем Solo/Duo рейтинг
            foreach (var entry in rankedArray.RootElement.EnumerateArray())
            {
                _logger.Info($"Processing ranked entry: {entry}");
                
                if (entry.TryGetProperty("queueType", out var queueType) && 
                    queueType.GetString() == "RANKED_SOLO_5x5")
                {
                    _logger.Info($"Found RANKED_SOLO_5x5 data for {playerInfo.Name}");
                    
                    if (entry.TryGetProperty("tier", out var tier))
                    {
                        playerInfo.Tier = tier.GetString() ?? "";
                        _logger.Info($"Tier: {playerInfo.Tier}");
                    }
                    
                    if (entry.TryGetProperty("rank", out var rank))
                    {
                        playerInfo.Rank = rank.GetString() ?? "";
                        _logger.Info($"Rank: {playerInfo.Rank}");
                    }
                    
                    if (entry.TryGetProperty("leaguePoints", out var lp))
                    {
                        playerInfo.LeaguePoints = lp.GetInt32();
                        _logger.Info($"LP: {playerInfo.LeaguePoints}");
                    }
                    
                    if (entry.TryGetProperty("wins", out var wins))
                    {
                        playerInfo.Wins = wins.GetInt32();
                        _logger.Info($"Wins: {playerInfo.Wins}");
                    }
                    
                    if (entry.TryGetProperty("losses", out var losses))
                    {
                        playerInfo.Losses = losses.GetInt32();
                        _logger.Info($"Losses: {playerInfo.Losses}");
                    }
                    
                    foundRankedData = true;
                    break;
                }
            }
            
            if (!foundRankedData)
            {
                _logger.Info($"No RANKED_SOLO_5x5 data found for {playerInfo.Name} - player is unranked");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get ranked info for {playerInfo.Name}: {ex.Message}");
            _logger.Error($"Stack trace: {ex.StackTrace}");
        }
    }

    private async Task FillRankedInfoByPuuidAsync(PlayerInfo playerInfo, string lcuPuuid)
    {
        try
        {
            _logger.Info($"FillRankedInfoByPuuidAsync for player: {playerInfo.Name}");
            
            // Парсим Riot ID из имени игрока
            if (!playerInfo.Name.Contains('#'))
            {
                _logger.Warning($"Player name doesn't contain Riot ID format: {playerInfo.Name}");
                return;
            }
            
            var parts = playerInfo.Name.Split('#');
            if (parts.Length != 2)
            {
                _logger.Warning($"Invalid Riot ID format: {playerInfo.Name}");
                return;
            }
            
            string gameName = parts[0];
            string tagLine = parts[1];
            
            _logger.Info($"Getting PUUID from Account API for {gameName}#{tagLine}");

            // Получаем правильный PUUID через Account API
            var accountResponse = await GetAccountByRiotIdAsync(gameName, tagLine);
            if (accountResponse == null)
            {
                _logger.Warning($"No account response for {gameName}#{tagLine}");
                return;
            }

            _logger.Info($"Account API response: {accountResponse}");
            
            var accountJson = JsonDocument.Parse(accountResponse);
            if (!accountJson.RootElement.TryGetProperty("puuid", out var puuidElement))
            {
                _logger.Warning($"No 'puuid' property in account response for {playerInfo.Name}");
                return;
            }
            
            var riotApiPuuid = puuidElement.GetString();
            if (string.IsNullOrEmpty(riotApiPuuid))
            {
                _logger.Warning($"Empty PUUID for {playerInfo.Name}");
                return;
            }
            
            _logger.Info($"Riot API PUUID for {playerInfo.Name}: {riotApiPuuid}");

            // Получаем рейтинговую статистику используя новый эндпоинт с PUUID
            var rankedResponse = await GetRankedStatsByPuuidAsync(riotApiPuuid);
            if (rankedResponse == null)
            {
                _logger.Warning($"No ranked response for {playerInfo.Name} (PUUID: {riotApiPuuid})");
                return;
            }

            _logger.Info($"Ranked API response for {playerInfo.Name}: {rankedResponse}");
            
            var rankedArray = JsonDocument.Parse(rankedResponse);
            
            bool foundRankedData = false;
            
            // Ищем Solo/Duo рейтинг
            foreach (var entry in rankedArray.RootElement.EnumerateArray())
            {
                if (entry.TryGetProperty("queueType", out var queueType) && 
                    queueType.GetString() == "RANKED_SOLO_5x5")
                {
                    _logger.Info($"Found RANKED_SOLO_5x5 data for {playerInfo.Name}");
                    
                    if (entry.TryGetProperty("tier", out var tier))
                    {
                        playerInfo.Tier = tier.GetString() ?? "";
                        _logger.Info($"Tier: {playerInfo.Tier}");
                    }
                    
                    if (entry.TryGetProperty("rank", out var rank))
                    {
                        playerInfo.Rank = rank.GetString() ?? "";
                        _logger.Info($"Rank: {playerInfo.Rank}");
                    }
                    
                    if (entry.TryGetProperty("leaguePoints", out var lp))
                    {
                        playerInfo.LeaguePoints = lp.GetInt32();
                        _logger.Info($"LP: {playerInfo.LeaguePoints}");
                    }
                    
                    if (entry.TryGetProperty("wins", out var wins))
                    {
                        playerInfo.Wins = wins.GetInt32();
                        _logger.Info($"Wins: {playerInfo.Wins}");
                    }
                    
                    if (entry.TryGetProperty("losses", out var losses))
                    {
                        playerInfo.Losses = losses.GetInt32();
                        _logger.Info($"Losses: {playerInfo.Losses}");
                    }
                    
                    foundRankedData = true;
                    break;
                }
            }
            
            if (!foundRankedData)
            {
                _logger.Info($"No RANKED_SOLO_5x5 data found for {playerInfo.Name} - player is unranked");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get ranked info for {playerInfo.Name}: {ex.Message}");
            _logger.Error($"Stack trace: {ex.StackTrace}");
        }
    }

    public async Task<bool> SendMessageToChatAsync(string message)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { body = message, type = "chat" });
            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            
            var response = await _riotClientService.PostAsync("/lol-chat/v1/conversations/champ-select/messages", content);
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to send chat message: {ex.Message}");
            return false;
        }
    }

    public string GenerateUggLink(string summonerName, string? region = null)
    {
        if (string.IsNullOrWhiteSpace(summonerName))
        {
            _logger.Warning("Cannot generate U.GG link - summoner name is empty");
            return "https://u.gg/";
        }
        
        var targetRegion = region ?? _selectedRegion;
        
        // U.GG использует полный platform routing код (euw1, na1, и т.д.)
        var uggRegion = targetRegion;
        
        // U.GG теперь использует формат name-tag вместо name#tag
        string nameForUgg = summonerName;
        if (summonerName.Contains('#'))
        {
            var parts = summonerName.Split('#');
            if (parts.Length == 2)
            {
                // Конвертируем Mejaikin#mvp -> Mejaikin-mvp
                nameForUgg = $"{parts[0]}-{parts[1]}";
                _logger.Info($"Converted Riot ID to U.GG format: {summonerName} -> {nameForUgg}");
            }
        }
        
        var encodedName = Uri.EscapeDataString(nameForUgg.Trim());
        var link = $"https://u.gg/lol/profile/{uggRegion}/{encodedName}/overview";
        
        _logger.Info($"Generated U.GG link for '{summonerName}': {link}");
        return link;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    public async Task<string?> GetAccountByRiotIdAsync(string gameName, string tagLine)
    {
        if (string.IsNullOrWhiteSpace(_riotApiKey))
        {
            _logger.Warning("GetAccountByRiotIdAsync: No API key provided");
            return null;
        }

        _logger.Info($"GetAccountByRiotIdAsync: {gameName}#{tagLine}");

        try
        {
            // Account API использует региональную маршрутизацию
            var regionalHost = GetRegionalHost(_selectedRegion);
            var url = $"https://{regionalHost}.api.riotgames.com/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}";
            _logger.Info($"Making request to: {url}");
            
            var response = await _httpClient.GetStringAsync(url);
            _logger.Info($"Account API response for {gameName}#{tagLine}: {response}");
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get account for {gameName}#{tagLine}: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> GetSummonerByPuuidAsync(string puuid, string? region = null)
    {
        if (string.IsNullOrWhiteSpace(_riotApiKey))
        {
            _logger.Warning("GetSummonerByPuuidAsync: No API key provided");
            return null;
        }

        var targetRegion = region ?? _selectedRegion;
        _logger.Info($"GetSummonerByPuuidAsync: {puuid} in region {targetRegion}");

        try
        {
            var url = $"https://{targetRegion}.api.riotgames.com/lol/summoner/v4/summoners/by-puuid/{puuid}";
            _logger.Info($"Making request to: {url}");
            
            var response = await _httpClient.GetStringAsync(url);
            _logger.Info($"Summoner by PUUID response: {response}");
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get summoner by PUUID {puuid}: {ex.Message}");
            return null;
        }
    }

    private string GetRegionalHost(string platformRegion)
    {
        // Конвертируем platform region в regional routing
        return platformRegion switch
        {
            "euw1" or "eune1" or "tr1" or "ru" => "europe",
            "na1" or "br1" or "la1" or "la2" => "americas", 
            "kr" or "jp1" => "asia",
            "oc1" => "sea",
            _ => "europe"
        };
    }
}
