using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LolManager.Models;

namespace LolManager.Services;

public class CustomizationService
{
    private readonly ILogger _logger;
    private readonly IRiotClientService _riotClientService;

    public CustomizationService(ILogger logger, IRiotClientService riotClientService)
    {
        _logger = logger;
        _riotClientService = riotClientService;
    }

    public async Task<bool> SetProfileStatusAsync(string status)
    {
        try
        {
            var lcuAuth = await _riotClientService.GetLcuAuthAsync();
            if (lcuAuth == null)
            {
                _logger.Error("LCU недоступен для установки статуса");
                return false;
            }

            var (port, password) = lcuAuth.Value;
            using var client = CreateHttpClient(port, password);

            var statusJson = JsonSerializer.Serialize(new { statusMessage = status });
            var content = new StringContent(
                statusJson,
                Encoding.UTF8,
                "application/json"
            );

            var response = await client.PutAsync("/lol-chat/v1/me", content);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.Info($"✅ Статус профиля установлен: {status}");
                return true;
            }
            else
            {
                _logger.Error($"❌ Не удалось установить статус: {response.StatusCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка установки статуса: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SetProfileBackgroundAsync(int championId)
    {
        try
        {
            var lcuAuth = await _riotClientService.GetLcuAuthAsync();
            if (lcuAuth == null)
            {
                _logger.Error("LCU недоступен для установки фона");
                return false;
            }

            var (port, password) = lcuAuth.Value;
            using var client = CreateHttpClient(port, password);

            var profileJson = JsonSerializer.Serialize(new { backgroundSkinId = championId });
            var content = new StringContent(profileJson, Encoding.UTF8, "application/json");

            var response = await client.PutAsync("/lol-summoner/v1/current-summoner/summoner-profile", content);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.Info($"✅ Фон профиля установлен: Champion ID {championId}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error($"❌ Не удалось установить фон через PUT: {response.StatusCode} - {errorContent}");
                
                var patchResponse = await client.PatchAsync("/lol-summoner/v1/current-summoner/summoner-profile", content);
                if (patchResponse.IsSuccessStatusCode)
                {
                    _logger.Info($"✅ Фон профиля установлен через PATCH: Champion ID {championId}");
                    return true;
                }
                else
                {
                    var patchError = await patchResponse.Content.ReadAsStringAsync();
                    _logger.Error($"❌ Не удалось установить фон через PATCH: {patchResponse.StatusCode} - {patchError}");
                }
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка установки фона: {ex.Message}");
            return false;
        }
    }

    public async Task<List<ChallengeInfo>> GetChallengesAsync()
    {
        try
        {
            var lcuAuth = await _riotClientService.GetLcuAuthAsync();
            if (lcuAuth == null)
            {
                _logger.Error("LCU недоступен для получения челенджей");
                return new List<ChallengeInfo>();
            }

            var (port, password) = lcuAuth.Value;
            using var client = CreateHttpClient(port, password);

            var response = await client.GetAsync("/lol-challenges/v1/challenges/local-player");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.Error($"❌ Не удалось получить челенджи через /local-player: {response.StatusCode}");
                
                var altResponse = await client.GetAsync("/lol-challenges/v1/challenges");
                if (!altResponse.IsSuccessStatusCode)
                {
                    _logger.Error($"❌ Не удалось получить челенджи через /challenges: {altResponse.StatusCode}");
                    return new List<ChallengeInfo>();
                }
                
                var altJson = await altResponse.Content.ReadAsStringAsync();
                using var altDoc = JsonDocument.Parse(altJson);
                var altRoot = altDoc.RootElement;
                
                return ParseChallengesFromRoot(altRoot);
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return ParseChallengesFromRoot(root);
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка получения челенджей: {ex.Message}");
            return new List<ChallengeInfo>();
        }
    }

    private List<ChallengeInfo> ParseChallengesFromRoot(JsonElement root)
    {
        var challenges = new List<ChallengeInfo>();

        JsonElement challengesArray;
        if (root.TryGetProperty("challenges", out challengesArray) && challengesArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var challenge in challengesArray.EnumerateArray())
            {
                if (challenge.TryGetProperty("id", out var id) &&
                    challenge.TryGetProperty("localizedNames", out var names))
                {
                    var challengeInfo = new ChallengeInfo
                    {
                        Id = id.GetInt64()
                    };

                    if (names.TryGetProperty("name", out var nameObj) && nameObj.TryGetProperty("RU", out var ruName))
                    {
                        challengeInfo.Name = ruName.GetString() ?? string.Empty;
                    }
                    else if (names.TryGetProperty("name", out var nameObj2) && nameObj2.TryGetProperty("EN_US", out var enName))
                    {
                        challengeInfo.Name = enName.GetString() ?? string.Empty;
                    }

                    if (names.TryGetProperty("description", out var descObj) && descObj.TryGetProperty("RU", out var ruDesc))
                    {
                        challengeInfo.Description = ruDesc.GetString() ?? string.Empty;
                    }
                    else if (names.TryGetProperty("description", out var descObj2) && descObj2.TryGetProperty("EN_US", out var enDesc))
                    {
                        challengeInfo.Description = enDesc.GetString() ?? string.Empty;
                    }

                    if (challenge.TryGetProperty("iconPath", out var iconPath))
                    {
                        challengeInfo.IconUrl = iconPath.GetString() ?? string.Empty;
                    }

                    if (challenge.TryGetProperty("category", out var category))
                    {
                        challengeInfo.Category = category.GetString() ?? string.Empty;
                    }

                    if (!string.IsNullOrEmpty(challengeInfo.Name))
                    {
                        challenges.Add(challengeInfo);
                    }
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var challenge in root.EnumerateArray())
            {
                if (challenge.TryGetProperty("id", out var id))
                {
                    var challengeInfo = new ChallengeInfo
                    {
                        Id = id.GetInt64()
                    };

                    if (challenge.TryGetProperty("localizedNames", out var names))
                    {
                        if (names.TryGetProperty("name", out var nameObj) && nameObj.TryGetProperty("RU", out var ruName))
                        {
                            challengeInfo.Name = ruName.GetString() ?? string.Empty;
                        }
                        else if (names.TryGetProperty("name", out var nameObj2) && nameObj2.TryGetProperty("EN_US", out var enName))
                        {
                            challengeInfo.Name = enName.GetString() ?? string.Empty;
                        }
                    }

                    if (challenge.TryGetProperty("iconPath", out var iconPath))
                    {
                        challengeInfo.IconUrl = iconPath.GetString() ?? string.Empty;
                    }

                    if (!string.IsNullOrEmpty(challengeInfo.Name))
                    {
                        challenges.Add(challengeInfo);
                    }
                }
            }
        }

        _logger.Info($"✅ Загружено {challenges.Count} челенджей");
        return challenges;
    }

    public async Task<bool> SetChallengeTokensAsync(List<long> challengeIds)
    {
        try
        {
            var lcuAuth = await _riotClientService.GetLcuAuthAsync();
            if (lcuAuth == null)
            {
                _logger.Error("LCU недоступен для установки челенджей");
                return false;
            }

            var (port, password) = lcuAuth.Value;
            using var client = CreateHttpClient(port, password);

            var tokens = challengeIds.Select(id => new { id }).ToArray();
            var json = JsonSerializer.Serialize(new { tokens });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("/lol-challenges/v1/update-player-preferences", content);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.Info($"✅ Челенджи установлены: {string.Join(", ", challengeIds)}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error($"❌ Не удалось установить челенджи через POST: {response.StatusCode} - {errorContent}");
                
                var putJson = JsonSerializer.Serialize(new { preferredChallenges = challengeIds.ToArray() });
                var putContent = new StringContent(putJson, Encoding.UTF8, "application/json");
                var putResponse = await client.PutAsync("/lol-challenges/v1/player-data/preferences", putContent);
                
                if (putResponse.IsSuccessStatusCode)
                {
                    _logger.Info($"✅ Челенджи установлены через PUT: {string.Join(", ", challengeIds)}");
                    return true;
                }
                else
                {
                    var putError = await putResponse.Content.ReadAsStringAsync();
                    _logger.Error($"❌ Не удалось установить челенджи через PUT: {putResponse.StatusCode} - {putError}");
                }
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка установки челенджей: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ClearSelectedChallengeTokensAsync()
    {
        return await SetChallengeTokensAsync(new List<long>());
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
            Timeout = TimeSpan.FromSeconds(5)
        };
        
        var base64Auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{password}"));
        client.DefaultRequestHeaders.Add("Authorization", $"Basic {base64Auth}");
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        return client;
    }
}

