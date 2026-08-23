using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using YT.Generate.Cuts.Domain.Entities;
using YT.Generate.Cuts.Domain.Enums;

namespace YT.Generate.Cuts.App.WebAssembly.Services;

/// <summary>
/// Excecao com a mensagem que a API devolveu, para a tela poder mostrar o motivo
/// real da falha em vez de um "Bad Request" seco.
/// </summary>
public class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public ApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

public class ApiService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public ApiService(HttpClient http)
    {
        _http = http;
    }

    // ------------------------------------------------------------------
    // Monitoring Channels
    // ------------------------------------------------------------------
    public async Task<List<MonitoringChannel>> GetMonitoringChannelsAsync()
    {
        var response = await _http.GetAsync("api/MonitoringChannels");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<MonitoringChannel>>(_json)
            ?? new List<MonitoringChannel>();
    }

    public async Task<MonitoringChannel?> GetMonitoringChannelAsync(long id)
    {
        var response = await _http.GetAsync($"api/MonitoringChannels/{id}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<MonitoringChannel>(_json);
    }

    public async Task<MonitoringChannel?> CreateMonitoringChannelAsync(
        string name, string url, int? minCutSeconds = null, int? maxCutSeconds = null,
        int? maxPendingCuts = null, long? editionConfigurationId = null, string? defaultTags = null)
    {
        var response = await _http.PostAsJsonAsync("api/MonitoringChannels",
            new { name, url, minCutSeconds, maxCutSeconds, maxPendingCuts, editionConfigurationId, defaultTags });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<MonitoringChannel>(_json);
    }

    public async Task UpdateMonitoringChannelAsync(
        long id, string? name, string? url,
        int? minCutSeconds = null, int? maxCutSeconds = null, bool clearCutDuration = false,
        int? maxPendingCuts = null, bool clearMaxPendingCuts = false,
        long? editionConfigurationId = null, bool clearEditionConfiguration = false,
        string? defaultTags = null)
    {
        var response = await _http.PutAsJsonAsync($"api/MonitoringChannels/{id}",
            new
            {
                name, url, minCutSeconds, maxCutSeconds, clearCutDuration,
                maxPendingCuts, clearMaxPendingCuts,
                editionConfigurationId, clearEditionConfiguration, defaultTags
            });
        await EnsureSuccessAsync(response);
    }

    public async Task DeleteMonitoringChannelAsync(long id)
    {
        var response = await _http.DeleteAsync($"api/MonitoringChannels/{id}");
        await EnsureSuccessAsync(response);
    }

    public async Task TriggerMonitoringAsync(long id)
    {
        var response = await _http.PostAsync($"api/MonitoringChannels/{id}/monitor", null);
        await EnsureSuccessAsync(response);
    }

    // ------------------------------------------------------------------
    // Publish Channels
    // ------------------------------------------------------------------
    public async Task<List<PublishChannel>> GetPublishChannelsAsync()
    {
        var response = await _http.GetAsync("api/PublishChannels");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<PublishChannel>>(_json)
            ?? new List<PublishChannel>();
    }

    public async Task<PublishChannel?> GetPublishChannelAsync(long id)
    {
        var response = await _http.GetAsync($"api/PublishChannels/{id}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PublishChannel>(_json);
    }

    public async Task<PublishChannel?> CreatePublishChannelAsync(string name, string login, string password, TypePublishEnum typePublish)
    {
        var response = await _http.PostAsJsonAsync("api/PublishChannels",
            new { name, login, password, typePublish = typePublish.ToString() });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PublishChannel>(_json);
    }

    public async Task UpdatePublishChannelAsync(long id, string? name, string? login, string? password, TypePublishEnum? typePublish)
    {
        var response = await _http.PutAsJsonAsync($"api/PublishChannels/{id}",
            new { name, login, password, typePublish = typePublish?.ToString() });
        await EnsureSuccessAsync(response);
    }

    public async Task DeletePublishChannelAsync(long id)
    {
        var response = await _http.DeleteAsync($"api/PublishChannels/{id}");
        await EnsureSuccessAsync(response);
    }

    // ------------------------------------------------------------------
    // Videos
    // ------------------------------------------------------------------
    public async Task<List<Video>> GetVideosAsync(VideoStatusEnum? status = null)
    {
        var url = status.HasValue ? $"api/Videos?status={status.Value}" : "api/Videos";
        var response = await _http.GetAsync(url);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<Video>>(_json) ?? new List<Video>();
    }

    public async Task<Video?> GetVideoAsync(long id)
    {
        var response = await _http.GetAsync($"api/Videos/{id}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<Video>(_json);
    }

    public async Task DeleteVideoAsync(long id)
    {
        var response = await _http.DeleteAsync($"api/Videos/{id}");
        await EnsureSuccessAsync(response);
    }

    // ------------------------------------------------------------------
    // Edition profiles
    // ------------------------------------------------------------------
    public async Task<List<EditionConfiguration>> GetEditionConfigurationsAsync()
    {
        var response = await _http.GetAsync("api/EditionConfigurations");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<EditionConfiguration>>(_json)
               ?? new List<EditionConfiguration>();
    }

    public async Task<EditionConfiguration?> CreateEditionConfigurationAsync(
        string name, int width, int height, bool burnSubtitles)
    {
        var response = await _http.PostAsJsonAsync("api/EditionConfigurations",
            new { name, width, height, burnSubtitles });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<EditionConfiguration>(_json);
    }

    public async Task UpdateEditionConfigurationAsync(
        long id, string name, int width, int height, bool burnSubtitles)
    {
        var response = await _http.PutAsJsonAsync($"api/EditionConfigurations/{id}",
            new { name, width, height, burnSubtitles });
        await EnsureSuccessAsync(response);
    }

    public async Task DeleteEditionConfigurationAsync(long id)
    {
        var response = await _http.DeleteAsync($"api/EditionConfigurations/{id}");
        await EnsureSuccessAsync(response);
    }

    // ------------------------------------------------------------------
    // Cuts
    // ------------------------------------------------------------------

    /// <summary>
    /// Lista os cortes. A API devolve o vídeo e o canal de publicação junto, então
    /// a tela não precisa de uma requisição por linha para mostrar os nomes.
    /// </summary>
    public async Task<List<Cut>> GetCutsAsync(CutStatusEnum? status = null)
    {
        var url = status.HasValue ? $"api/Cuts?status={status.Value}" : "api/Cuts";
        var response = await _http.GetAsync(url);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<Cut>>(_json) ?? new List<Cut>();
    }

    public async Task<Cut?> GetCutAsync(long id)
    {
        var response = await _http.GetAsync($"api/Cuts/{id}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<Cut>(_json);
    }

    public async Task EditCutAsync(long cutId)
    {
        var response = await _http.PostAsync($"api/Cuts/{cutId}/edit", null);
        await EnsureSuccessAsync(response);
    }

    public async Task ProcessCutAsync(long cutId)
    {
        var response = await _http.PostAsync($"api/Cuts/{cutId}/process", null);
        await EnsureSuccessAsync(response);
    }

    public async Task DeleteCutAsync(long cutId)
    {
        var response = await _http.DeleteAsync($"api/Cuts/{cutId}");
        await EnsureSuccessAsync(response);
    }

    public async Task<PublishResult?> PublishCutAsync(long cutId)
    {
        var response = await _http.PostAsync($"api/Cuts/{cutId}/publish", null);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PublishResult>(_json);
    }

    public async Task AssignPublishChannelAsync(long cutId, long channelId)
    {
        var response = await _http.PutAsJsonAsync($"api/Cuts/{cutId}/publish-channel", new { publishChannelId = channelId });
        await EnsureSuccessAsync(response);
    }

    // ------------------------------------------------------------------
    /// <summary>
    /// Levanta <see cref="ApiException"/> com a mensagem devolvida pela API.
    /// Cobre ProblemDetails (o formato que o [ApiController] usa para 400),
    /// ValidationProblemDetails e respostas em texto puro.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();
        throw new ApiException(response.StatusCode, ExtractMessage(body, response));
    }

    private static string ExtractMessage(string body, HttpResponseMessage response)
    {
        if (string.IsNullOrWhiteSpace(body))
            return $"A API respondeu {(int)response.StatusCode} ({response.ReasonPhrase}).";

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // ValidationProblemDetails: { "errors": { "Campo": ["msg", ...] } }
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var messages = errors.EnumerateObject()
                    .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array
                        ? p.Value.EnumerateArray().Select(v => v.GetString())
                        : new[] { p.Value.GetString() })
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .ToList();

                if (messages.Count > 0) return string.Join(" ", messages);
            }

            // ProblemDetails: { "detail": "..." } ou { "title": "..." }
            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                var text = detail.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text!;
            }

            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
            {
                var text = title.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text!;
            }
        }
        catch (JsonException)
        {
            // corpo nao e JSON: cai no texto puro abaixo
        }

        return body.Length > 300 ? body[..300] : body;
    }
}

public class PublishResult
{
    public string? PostId { get; set; }
    public string? Url { get; set; }
    public string? Error { get; set; }
}
