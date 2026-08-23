using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Publish.Abstractions;

namespace YT.Generate.Cuts.Publish.TikTok;

/// <summary>
/// Plugin de publicação para TikTok utilizando a API não oficial do TikTok.
/// Utiliza autenticação por cookie/sessão para upload de vídeos.
/// </summary>
public class TikTokPublishPlugin : IPublishPlugin
{
    private readonly ILogger<TikTokPublishPlugin> _logger;
    private readonly HttpClient _httpClient;

    public string PlatformName => "TikTok";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public TikTokPublishPlugin(ILogger<TikTokPublishPlugin> logger, HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<PublishResult> PublishAsync(
        string videoPath,
        string title,
        string? description,
        PublishCredentials credentials,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[TikTok] Iniciando publicação: {Title}", title);

        try
        {
            if (!File.Exists(videoPath))
            {
                _logger.LogError("[TikTok] Arquivo de vídeo não encontrado: {Path}", videoPath);
                return PublishResult.Fail($"Arquivo de vídeo não encontrado: {videoPath}");
            }

            var fileInfo = new FileInfo(videoPath);
            _logger.LogInformation("[TikTok] Arquivo: {Path} ({Size} MB)",
                videoPath, fileInfo.Length / 1024.0 / 1024.0);

            // Configurar sessão
            await ConfigureSessionAsync(credentials, ct);

            // Fazer upload do vídeo
            var uploadResult = await UploadVideoAsync(videoPath, ct);
            if (!uploadResult.Success)
                return uploadResult;

            // Publicar o vídeo com título e descrição
            var publishResult = await PublishVideoAsync(
                uploadResult.PlatformPostId!,
                title,
                description ?? title,
                ct);

            return publishResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[TikTok] Publicação cancelada: {Title}", title);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TikTok] Erro ao publicar vídeo: {Title}", title);
            return PublishResult.Fail($"Erro ao publicar no TikTok: {ex.Message}");
        }
    }

    private async Task ConfigureSessionAsync(PublishCredentials credentials, CancellationToken ct)
    {
        _logger.LogInformation("[TikTok] Configurando sessão para usuário: {Login}", credentials.Login);

        // Se tiver um sessionId, configurar o cookie diretamente
        if (!string.IsNullOrEmpty(credentials.SessionId))
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Cookie", $"sessionid={credentials.SessionId}");
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _logger.LogInformation("[TikTok] Sessão configurada via SessionId");
            return;
        }

        // Se tiver access token, configurar header de autenticação
        if (!string.IsNullOrEmpty(credentials.AccessToken))
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
            _logger.LogInformation("[TikTok] Sessão configurada via AccessToken");
            return;
        }

        _logger.LogWarning("[TikTok] Nenhuma credencial de sessão fornecida. Tentando login por email/senha...");
        await LoginAsync(credentials.Login, credentials.Password, ct);
    }

    private async Task LoginAsync(string email, string password, CancellationToken ct)
    {
        // Login via API não oficial do TikTok
        var loginPayload = new
        {
            service = "https://www.tiktok.com/",
            csrf_token = string.Empty,
            username = email,
            password = password
        };

        var content = new StringContent(
            JsonSerializer.Serialize(loginPayload, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(
            "https://www.tiktok.com/api/v1/auth/login/",
            content,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("[TikTok] Falha no login: {StatusCode} - {Error}",
                response.StatusCode, errorBody);
            throw new InvalidOperationException($"Falha no login do TikTok: {response.StatusCode}");
        }

        _logger.LogInformation("[TikTok] Login realizado com sucesso");
    }

    private async Task<PublishResult> UploadVideoAsync(string videoPath, CancellationToken ct)
    {
        _logger.LogInformation("[TikTok] Iniciando upload do vídeo...");

        try
        {
            using var fileStream = File.OpenRead(videoPath);
            using var content = new MultipartFormDataContent();

            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            content.Add(fileContent, "video", Path.GetFileName(videoPath));

            var response = await _httpClient.PostAsync(
                "https://www.tiktok.com/api/v1/video/upload/",
                content,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("[TikTok] Falha no upload: {StatusCode} - {Error}",
                    response.StatusCode, errorBody);
                return PublishResult.Fail($"Falha no upload: {response.StatusCode}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("[TikTok] Upload concluído com sucesso");

            // Extrair o ID do vídeo da resposta
            try
            {
                var uploadResponse = JsonSerializer.Deserialize<TikTokUploadResponse>(responseBody, _jsonOptions);
                if (uploadResponse?.VideoId != null)
                {
                    return PublishResult.Ok(uploadResponse.VideoId,
                        $"https://www.tiktok.com/@{uploadResponse.VideoId}");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("[TikTok] Não foi possível parsear resposta do upload: {Error}", ex.Message);
            }

            // Fallback: retornar hash do path como ID
            return PublishResult.Ok($"upload_{Path.GetFileNameWithoutExtension(videoPath)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TikTok] Erro durante upload");
            return PublishResult.Fail($"Erro no upload: {ex.Message}");
        }
    }

    private async Task<PublishResult> PublishVideoAsync(
        string videoId, string title, string description, CancellationToken ct)
    {
        _logger.LogInformation("[TikTok] Publicando vídeo {VideoId} com título: {Title}", videoId, title);

        try
        {
            var publishPayload = new
            {
                video_id = videoId,
                title = title,
                description = description,
                privacy_level = 0, // 0 = Público
                allow_duet = true,
                allow_stitch = true,
                allow_comment = true
            };

            var content = new StringContent(
                JsonSerializer.Serialize(publishPayload, _jsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(
                "https://www.tiktok.com/api/v1/video/publish/",
                content,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("[TikTok] Falha na publicação: {StatusCode} - {Error}",
                    response.StatusCode, errorBody);
                return PublishResult.Fail($"Falha na publicação: {response.StatusCode}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("[TikTok] Vídeo publicado com sucesso! ID: {VideoId}", videoId);

            return PublishResult.Ok(videoId, $"https://www.tiktok.com/@{videoId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TikTok] Erro ao publicar vídeo");
            return PublishResult.Fail($"Erro na publicação: {ex.Message}");
        }
    }

    private class TikTokUploadResponse
    {
        public string? VideoId { get; set; }
        public string? UploadUrl { get; set; }
        public bool Success { get; set; }
    }
}