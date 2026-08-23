using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Exceptions;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.VideoSource;

namespace YT.Generate.Cuts.Application.VideoExtraction.Youtube;

/// <summary>
/// Catálogo via yt-dlp, usando <c>--flat-playlist --dump-json</c>.
/// </summary>
/// <remarks>
/// Serve como alternativa ao YoutubeExplode para listagem. É mais lento (sobe um
/// processo por chamada), mas usa o extrator que o yt-dlp mantém atualizado.
/// </remarks>
public sealed class YtDlpVideoCatalog : IVideoCatalog
{
    private readonly YtDlpOptions _options;
    private readonly ILogger<YtDlpVideoCatalog> _logger;

    public YtDlpVideoCatalog(YtDlpOptions options, ILogger<YtDlpVideoCatalog> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string ProviderName => "yt-dlp";

    public async Task<CatalogChannel> ResolveChannelAsync(string channelReference, CancellationToken ct)
    {
        // Uma entrada basta para descobrir o id e o nome do canal.
        var entries = await RunAsync(BuildChannelUrl(channelReference), 1, ct);

        var first = entries.FirstOrDefault()
            ?? throw new VideoUnavailableException($"yt-dlp não encontrou o canal '{channelReference}'.");

        var id = first.ChannelId ?? first.UploaderId ?? channelReference;
        var title = first.Channel ?? first.Uploader ?? channelReference;

        return new CatalogChannel(id, title);
    }

    public async Task<IReadOnlyList<CatalogVideo>> GetRecentUploadsAsync(string channelId, int maxResults, CancellationToken ct)
    {
        var entries = await RunAsync(BuildChannelUrl(channelId), maxResults, ct);

        return entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .Select(e => new CatalogVideo(
                Id: e.Id!,
                Title: e.Title ?? e.Id!,
                Url: $"https://www.youtube.com/watch?v={e.Id}",
                Duration: e.Duration is > 0 ? TimeSpan.FromSeconds(e.Duration.Value) : null))
            .ToList();
    }

    // ------------------------------------------------------------------
    private static string BuildChannelUrl(string reference)
    {
        var value = reference.Trim();

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return value.TrimEnd('/').EndsWith("/videos", StringComparison.OrdinalIgnoreCase)
                ? value
                : value.TrimEnd('/') + "/videos";

        if (value.StartsWith('@'))
            return $"https://www.youtube.com/{value}/videos";

        // ID de canal (UC...)
        return $"https://www.youtube.com/channel/{value}/videos";
    }

    private async Task<List<FlatEntry>> RunAsync(string url, int maxResults, CancellationToken ct)
    {
        var args = new List<string>
        {
            "--flat-playlist",
            "--dump-json",
            "--no-warnings",
            "--ignore-no-formats-error",
            "--playlist-end", Math.Max(1, maxResults).ToString(),
            "--retries", _options.Retries.ToString(),
            "--socket-timeout", _options.SocketTimeoutSeconds.ToString(),
        };

        // Mesmo ritmo do download: a listagem também conta para o limite por IP.
        if (_options.SleepRequestsSeconds > 0)
        {
            args.Add("--sleep-requests");
            args.Add(_options.SleepRequestsSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(_options.CookiesFile) && File.Exists(_options.CookiesFile))
        {
            args.Add("--cookies");
            args.Add(_options.CookiesFile);
        }

        args.Add(url);

        BufferedCommandResult result;
        try
        {
            result = await Cli.Wrap(_options.ExecutablePath)
                .WithArguments(args)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new VideoSourceUnavailableException(
                $"Não foi possível executar '{_options.ExecutablePath}': {ex.Message}", ex);
        }

        if (result.ExitCode != 0)
            throw YtDlpErrorTranslator.Translate(result.StandardError, result.ExitCode, url);

        // --dump-json emite um objeto JSON por linha.
        var entries = new List<FlatEntry>();

        foreach (var line in result.StandardOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] != '{') continue;

            try
            {
                var entry = JsonSerializer.Deserialize<FlatEntry>(trimmed);
                if (entry is not null) entries.Add(entry);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug("[yt-dlp] Linha JSON ignorada: {Message}", ex.Message);
            }
        }

        return entries;
    }

    private sealed class FlatEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")] public string? Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("title")] public string? Title { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("duration")] public double? Duration { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("channel")] public string? Channel { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("channel_id")] public string? ChannelId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("uploader")] public string? Uploader { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("uploader_id")] public string? UploaderId { get; set; }
    }
}
