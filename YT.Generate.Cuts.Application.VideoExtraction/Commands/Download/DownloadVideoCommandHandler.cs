using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.VideoSource;

namespace YT.Generate.Cuts.Application.VideoExtraction.Commands.Download;

/// <summary>
/// Orquestra o download. Não conhece provedor: fala apenas com
/// <see cref="IVideoDownloader"/>, que pode ser yt-dlp, YoutubeExplode ou a
/// cadeia de fallback entre os dois.
/// </summary>
public class DownloadVideoCommandHandler : ICommandHandler<DownloadVideoCommand, DownloadVideoCommandResponse>
{
    private const int DefaultMaxResolution = 720;

    private readonly ILogger<DownloadVideoCommandHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly IVideoDownloader _downloader;

    public DownloadVideoCommandHandler(
        ILogger<DownloadVideoCommandHandler> logger,
        IConfiguration configuration,
        IVideoDownloader downloader)
    {
        _logger = logger;
        _configuration = configuration;
        _downloader = downloader;
    }

    public async Task<DownloadVideoCommandResponse> Handle(DownloadVideoCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Iniciando download do video e legendas: {videoUri} (provedor: {Provider})",
            command.videoUri, _downloader.ProviderName);

        if (!int.TryParse(_configuration["MaxResolution"], out var maxResolution) || maxResolution <= 0)
            maxResolution = DefaultMaxResolution;

        var request = new VideoDownloadRequest(
            VideoUrl: command.videoUri,
            TargetDirectory: command.saveDirectory,
            MaxHeight: maxResolution,
            PreferredSubtitleLanguages: ReadSubtitleLanguages());

        var result = await _downloader.DownloadAsync(request, ct);

        _logger.LogInformation("Download concluído! Video: {VPath} | Legenda: {SPath} | Idioma: {Lang} | Duração: {Dur}",
            result.VideoPath, result.SubtitlePath ?? "(sem legenda)", result.SubtitleLanguage ?? "-", result.Duration);

        return new DownloadVideoCommandResponse(
            result.VideoPath,
            result.SubtitlePath ?? string.Empty,
            result.SubtitleLanguage ?? string.Empty,
            result.Duration);
    }

    /// <summary>
    /// Idiomas de legenda em ordem de preferência, de
    /// <c>Subtitles__PreferredLanguages</c> (ex.: <c>pt,en,es</c>).
    /// </summary>
    private IReadOnlyList<string> ReadSubtitleLanguages()
    {
        var configured = _configuration["Subtitles:PreferredLanguages"];

        if (string.IsNullOrWhiteSpace(configured))
            return new[] { "pt", "en" };

        var languages = configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        return languages.Length > 0 ? languages : new[] { "pt", "en" };
    }
}
