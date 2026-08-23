namespace YT.Generate.Cuts.Publish.Abstractions;

/// <summary>
/// Interface para plugins de publicação em plataformas (TikTok, YouTube, Instagram, etc.)
/// </summary>
public interface IPublishPlugin
{
    /// <summary>
    /// Nome da plataforma
    /// </summary>
    string PlatformName { get; }

    /// <summary>
    /// Publica um vídeo na plataforma
    /// </summary>
    /// <param name="videoPath">Caminho do arquivo de vídeo</param>
    /// <param name="title">Título do vídeo</param>
    /// <param name="description">Descrição do vídeo</param>
    /// <param name="credentials">Credenciais de login (username/password ou token)</param>
    /// <param name="ct">Token de cancelamento</param>
    /// <returns>Resultado da publicação</returns>
    Task<PublishResult> PublishAsync(
        string videoPath,
        string title,
        string? description,
        PublishCredentials credentials,
        CancellationToken ct = default);
}

/// <summary>
/// Credenciais para publicação
/// </summary>
public record PublishCredentials(
    string Login,
    string Password,
    string? AccessToken = null,
    string? SessionId = null);

/// <summary>
/// Resultado da publicação
/// </summary>
public class PublishResult
{
    public bool Success { get; set; }
    public string? PlatformPostId { get; set; }
    public string? PostUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

    public static PublishResult Ok(string platformPostId, string? postUrl = null)
        => new() { Success = true, PlatformPostId = platformPostId, PostUrl = postUrl };

    public static PublishResult Fail(string errorMessage)
        => new() { Success = false, ErrorMessage = errorMessage };
}