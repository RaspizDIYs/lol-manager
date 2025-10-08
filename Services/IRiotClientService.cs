using System.Net.Http;
using System.Threading.Tasks;

namespace LolManager.Services;

public interface IRiotClientService
{
    Task LoginAsync(string username, string password);
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
}


