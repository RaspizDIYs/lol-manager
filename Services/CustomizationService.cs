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
            var content = new StringContent(statusJson, Encoding.UTF8, "application/json");

            var response = await client.PutAsync("/lol-chat/v1/me", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.Info($"✅ Статус профиля установлен: {status}, ответ: {responseContent}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error($"❌ Не удалось установить статус: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка установки статуса: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SetProfileAvailabilityAsync(string availability)
    {
        try
        {
            var lcuAuth = await _riotClientService.GetLcuAuthAsync();
            if (lcuAuth == null)
            {
                _logger.Error("LCU недоступен для установки доступности");
                return false;
            }

            var (port, password) = lcuAuth.Value;
            using var client = CreateHttpClient(port, password);

            var availabilityJson = JsonSerializer.Serialize(new { availability = availability });
            var content = new StringContent(availabilityJson, Encoding.UTF8, "application/json");

            var response = await client.PutAsync("/lol-chat/v1/me", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.Info($"✅ Доступность профиля установлена: {availability}, ответ: {responseContent}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error($"❌ Не удалось установить доступность: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка установки доступности: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SetProfileIconAsync(int iconId)
    {
        try
        {
            var lcuAuth = await _riotClientService.GetLcuAuthAsync();
            if (lcuAuth == null)
            {
                _logger.Error("LCU недоступен для установки иконки");
                return false;
            }

            var (port, password) = lcuAuth.Value;
            using var client = CreateHttpClient(port, password);

            var iconJson = JsonSerializer.Serialize(new { icon = iconId });
            var content = new StringContent(iconJson, Encoding.UTF8, "application/json");

            var response = await client.PutAsync("/lol-chat/v1/me", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.Info($"✅ Иконка профиля установлена: {iconId}, ответ: {responseContent}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error($"❌ Не удалось установить иконку: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка установки иконки: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SetProfileBackgroundAsync(int backgroundSkinId)
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

            _logger.Info($"Попытка установить фон профиля: backgroundSkinId={backgroundSkinId}");

            var getResponse = await client.GetAsync("/lol-summoner/v1/current-summoner/summoner-profile");
            if (!getResponse.IsSuccessStatusCode)
            {
                var getError = await getResponse.Content.ReadAsStringAsync();
                _logger.Error($"❌ Не удалось получить текущий профиль: {getResponse.StatusCode} - {getError}");
                return false;
            }

            var currentProfileJson = await getResponse.Content.ReadAsStringAsync();
            _logger.Debug($"Текущий профиль: {currentProfileJson}");

            using var currentDoc = JsonDocument.Parse(currentProfileJson);
            var currentProfile = currentDoc.RootElement;

            var updatedProfile = new Dictionary<string, object>();
            
            foreach (var prop in currentProfile.EnumerateObject())
            {
                if (prop.Name == "backgroundSkinId")
                {
                    updatedProfile["backgroundSkinId"] = backgroundSkinId;
                    continue;
                }
                
                updatedProfile[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => prop.Value.GetInt32(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Array => prop.Value.EnumerateArray().Select(e => e.GetString()).ToArray(),
                    JsonValueKind.Object => prop.Value.GetRawText(),
                    _ => prop.Value.GetRawText()
                };
            }

            if (!updatedProfile.ContainsKey("backgroundSkinId"))
            {
                updatedProfile["backgroundSkinId"] = backgroundSkinId;
            }

            var options = new JsonSerializerOptions { WriteIndented = false };
            
            var keyValuePayload = new { key = "backgroundSkinId", value = backgroundSkinId };
            var keyValueJson = JsonSerializer.Serialize(keyValuePayload, options);
            _logger.Debug($"Отправляемые данные (key-value формат из league-tools): {keyValueJson}");
            var keyValueContent = new StringContent(keyValueJson, Encoding.UTF8, "application/json");

            var minimalPayload = new { backgroundSkinId = backgroundSkinId };
            var minimalJson = JsonSerializer.Serialize(minimalPayload, options);
            _logger.Debug($"Отправляемые данные (минимальный payload): {minimalJson}");
            var minimalContent = new StringContent(minimalJson, Encoding.UTF8, "application/json");

            var fullProfileJson = JsonSerializer.Serialize(updatedProfile, options);
            _logger.Debug($"Отправляемые данные (полный payload): {fullProfileJson}");
            var fullContent = new StringContent(fullProfileJson, Encoding.UTF8, "application/json");

            var endpoints = new[]
            {
                ("POST", "/lol-summoner/v1/current-summoner/summoner-profile", keyValueContent),
                ("POST", "/lol-summoner/v1/current-summoner/summoner-profile/", keyValueContent),
                ("PUT", "/lol-summoner/v1/current-summoner/summoner-profile", fullContent),
                ("PUT", "/lol-summoner/v1/current-summoner/summoner-profile", minimalContent),
                ("POST", "/lol-summoner/v1/current-summoner/summoner-profile", fullContent),
                ("POST", "/lol-summoner/v1/current-summoner/summoner-profile", minimalContent),
                ("PATCH", "/lol-summoner/v1/current-summoner/summoner-profile", keyValueContent)
            };

            foreach (var (method, endpoint, content) in endpoints)
            {
                try
                {
                    HttpResponseMessage? response = null;
                    switch (method)
                    {
                        case "POST":
                            response = await client.PostAsync(endpoint, content);
                            break;
                        case "PUT":
                            response = await client.PutAsync(endpoint, content);
                            break;
                        case "PATCH":
                            var request = new HttpRequestMessage(new HttpMethod("PATCH"), endpoint) { Content = content };
                            response = await client.SendAsync(request);
                            break;
                    }

                    if (response != null && response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        _logger.Info($"✅ Фон профиля установлен через {method} {endpoint}: backgroundSkinId={backgroundSkinId}, ответ: {responseContent}");
                        return true;
                    }
                    else if (response != null)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.Debug($"❌ {method} {endpoint}: {response.StatusCode} - {errorContent}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"❌ Ошибка при {method} {endpoint}: {ex.Message}");
                }
            }

            _logger.Error($"❌ Не удалось установить фон через все доступные методы и эндпоинты");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка установки фона: {ex.Message}\n{ex.StackTrace}");
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
                return new List<ChallengeInfo>();
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var challenges = new List<ChallengeInfo>();
            
            foreach (var challenge in root.EnumerateObject())
            {
                var challengeValue = challenge.Value;
                if (challengeValue.TryGetProperty("currentLevel", out var levelProp))
                {
                    var level = levelProp.GetString();
                    if (level == null || level == "NONE") continue;
                    
                    if (challengeValue.TryGetProperty("id", out var idProp) &&
                        challengeValue.TryGetProperty("localizedNames", out var names))
                    {
                        var challengeInfo = new ChallengeInfo
                        {
                            Id = idProp.GetInt64()
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

                        if (challengeValue.TryGetProperty("iconPath", out var iconPath))
                        {
                            challengeInfo.IconUrl = iconPath.GetString() ?? string.Empty;
                        }

                        if (challengeValue.TryGetProperty("category", out var category))
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

            _logger.Info($"✅ Загружено {challenges.Count} челенджей");
            return challenges;
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

    public async Task<bool> SetChallengeTokensAsync(List<long> challengeIds, long titleId = -1)
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

            var payload = new
            {
                challengeIds = challengeIds.ToArray(),
                title = titleId == -1 ? string.Empty : titleId.ToString()
            };
            
            var options = new JsonSerializerOptions { WriteIndented = false };
            var json = JsonSerializer.Serialize(payload, options);
            _logger.Debug($"Отправляемые данные челенджей: {json}");
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("/lol-challenges/v1/update-player-preferences", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.Info($"✅ Челенджи установлены: {string.Join(", ", challengeIds)}, ответ: {responseContent}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error($"❌ Не удалось установить челенджи: {response.StatusCode} - {errorContent}");
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
        return await SetChallengeTokensAsync(new List<long>(), -1);
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
            Timeout = TimeSpan.FromSeconds(10)
        };
        
        var base64Auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{password}"));
        client.DefaultRequestHeaders.Add("Authorization", $"Basic {base64Auth}");
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        return client;
    }
}

