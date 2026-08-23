namespace YT.Generate.Cuts.Application.Abstractions.Interfaces.VideoSource;

/// <param name="VideoUrl">URL canônica do vídeo.</param>
/// <param name="TargetDirectory">Pasta onde os arquivos serão gravados.</param>
/// <param name="MaxHeight">Altura máxima do vídeo, em pixels.</param>
/// <param name="PreferredSubtitleLanguages">
/// Idiomas de legenda em ordem de preferência (ex.: <c>pt</c>, <c>en</c>).
/// </param>
public sealed record VideoDownloadRequest(
    string VideoUrl,
    string TargetDirectory,
    int MaxHeight,
    IReadOnlyList<string> PreferredSubtitleLanguages);

/// <param name="SubtitlePath">
/// <c>null</c> quando não havia legenda. Nunca aponta para arquivo inexistente.
/// </param>
/// <param name="Duration">
/// <see cref="TimeSpan.Zero"/> quando o provedor não informou; quem chama já
/// costuma ter a duração vinda do catálogo.
/// </param>
public sealed record VideoDownloadResult(
    string VideoPath,
    string? SubtitlePath,
    string? SubtitleLanguage,
    TimeSpan Duration);

/// <summary>
/// Baixa vídeo e legenda. Implementações diferentes cobrem provedores
/// diferentes, e podem ser encadeadas em fallback.
/// </summary>
public interface IVideoDownloader
{
    /// <summary>Nome do provedor, para log e diagnóstico.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Indica se o provedor está utilizável neste ambiente — por exemplo, se o
    /// executável existe. Evita entrar numa cadeia de fallback que já se sabe
    /// que vai falhar.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct);

    Task<VideoDownloadResult> DownloadAsync(VideoDownloadRequest request, CancellationToken ct);
}
