using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using LolManager.Models;

namespace LolManager.Services;

public interface IRiotClientService
{
    Task LoginAsync(string username, string password);
    Task LoginAsync(string username, string password, System.Threading.CancellationToken cancellationToken);
    Task KillLeagueAsync(bool includeRiotClient);
    Task StartLeagueAsync();
    Task RestartLeagueAsync(bool includeRiotClient);
    Task LogoutAsync();
    Task StartRiotClientAsync();
    Task RestartRiotClientAsync();
    bool IsRiotClientRunning();

	/// <summary>
	/// Пытается скачать OpenAPI/Swagger JSON из LCU по известным путям и сохранить в файл. Возвращает путь к файлу или null.
	/// </summary>
    Task<string?> FetchLcuOpenApiAsync(string outputJsonPath);

	/// <summary>
	/// Генерирует Markdown по эндпоинтам LCU (на основе OpenAPI, если найден; иначе сохраняет /help как HTML). Возвращает количество найденных роутов.
	/// </summary>
	Task<int> GenerateLcuEndpointsMarkdownAsync(string outputMarkdownPath, string? alsoSaveRawJsonTo = null);

	/// <summary>
	/// Выполняет GET запрос к LCU API
	/// </summary>
	Task<string?> GetAsync(string endpoint);

	/// <summary>
	/// Выполняет POST запрос к LCU API
	/// </summary>
	Task<string?> PostAsync(string endpoint, HttpContent content);

    /// <summary>
    /// Возвращает порт и пароль LCU (из lockfile), если клиент запущен.
    /// </summary>
    Task<(int Port, string Password)?> GetLcuAuthAsync();

    /// <summary>
    /// Создает и делает текущей страницу рун в LCU (при необходимости удаляет старую/лишнюю страницу).
    /// </summary>
    Task<bool> ApplyRunePageAsync(RunePage runePage);

    /// <summary>
    /// Получить список доступных перков (включая осколки) из LCU.
    /// </summary>
    Task<List<LcuPerk>> GetPerksAsync();

    Task<RunePage?> GetRecommendedRunePageAsync(int championId);
    Task<List<LcuRunePage>> GetRunePagesAsync();
    Task<string?> GetCurrentSummonerNameAsync();
    Task<string> GetCurrentSummonerPuuidAsync();
}


