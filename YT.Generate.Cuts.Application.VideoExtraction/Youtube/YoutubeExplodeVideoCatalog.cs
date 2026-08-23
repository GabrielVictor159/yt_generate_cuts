using Microsoft.Extensions.Logging;
using YoutubeExplode.Exceptions;
using YT.Generate.Cuts.Application.Abstractions.Exceptions;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.VideoSource;

namespace YT.Generate.Cuts.Application.VideoExtraction.Youtube;

/// <summary>
/// Catálogo via YoutubeExplode. É o padrão para listagem: resolve canal e
/// uploads em processo, sem subir um executável externo.
/// </summary>
public sealed class YoutubeExplodeVideoCatalog : IVideoCatalog
{
    private readonly YoutubeClientProvider _youtube;
    private readonly ILogger<YoutubeExplodeVideoCatalog> _logger;

    public YoutubeExplodeVideoCatalog(YoutubeClientProvider youtube, ILogger<YoutubeExplodeVideoCatalog> logger)
    {
        _youtube = youtube;
        _logger = logger;
    }

    public string ProviderName => "YoutubeExplode";

    public async Task<CatalogChannel> ResolveChannelAsync(string channelReference, CancellationToken ct)
    {
        try
        {
            var channel = channelReference.Contains('@')
                ? await _youtube.Client.Channels.GetByHandleAsync(channelReference, ct)
                : await _youtube.Client.Channels.GetAsync(channelReference, ct);

            return new CatalogChannel(channel.Id.Value, channel.Title);
        }
        catch (Exception ex)
        {
            throw VideoSourceErrors.Translate(ex, channelReference);
        }
    }

    public async Task<IReadOnlyList<CatalogVideo>> GetRecentUploadsAsync(string channelId, int maxResults, CancellationToken ct)
    {
        var uploads = new List<CatalogVideo>();

        try
        {
            await foreach (var upload in _youtube.Client.Channels.GetUploadsAsync(channelId, ct))
            {
                uploads.Add(new CatalogVideo(
                    Id: upload.Id.Value,
                    Title: upload.Title,
                    Url: $"https://www.youtube.com/watch?v={upload.Id.Value}",
                    Duration: upload.Duration));

                if (uploads.Count >= maxResults) break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Se já temos parte da lista, seguimos com ela: melhor processar os
            // uploads recentes que deram certo do que perder o ciclo inteiro.
            if (uploads.Count > 0)
            {
                _logger.LogWarning("[{Provider}] Listagem interrompida em {Count} item(ns): {Message}",
                    ProviderName, uploads.Count, ex.Message);
                return uploads;
            }

            throw VideoSourceErrors.Translate(ex, channelId);
        }

        return uploads;
    }
}
