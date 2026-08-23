using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Exceptions;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.VideoSource;

namespace YT.Generate.Cuts.Application.VideoExtraction.Youtube;

/// <summary>
/// Tenta os provedores em ordem até um conseguir.
/// </summary>
/// <remarks>
/// É a peça central desta arquitetura. O acesso ao YouTube é uma corrida
/// armamentista: o que funciona hoje pode parar amanhã. Com a cadeia, a queda de
/// um provedor degrada o desempenho em vez de derrubar o fluxo — e o log diz
/// exatamente quem atendeu, o que torna visível quando o primário para de servir.
/// <para>
/// Duas exceções encerram a cadeia na hora, porque insistir com outro provedor
/// não ajudaria: <see cref="VideoSourceRateLimitException"/> (o bloqueio é do
/// lado do YouTube, por IP) e <see cref="VideoSourceNetworkException"/> (não há
/// rota para ninguém).
/// </para>
/// </remarks>
public sealed class FallbackVideoDownloader : IVideoDownloader
{
    private readonly IReadOnlyList<IVideoDownloader> _providers;
    private readonly ILogger<FallbackVideoDownloader> _logger;

    public FallbackVideoDownloader(IReadOnlyList<IVideoDownloader> providers, ILogger<FallbackVideoDownloader> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public string ProviderName => string.Join(" → ", _providers.Select(p => p.ProviderName));

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        foreach (var provider in _providers)
            if (await provider.IsAvailableAsync(ct))
                return true;

        return false;
    }

    public async Task<VideoDownloadResult> DownloadAsync(VideoDownloadRequest request, CancellationToken ct)
    {
        if (_providers.Count == 0)
            throw new VideoSourceUnavailableException("Nenhum provedor de download configurado.");

        VideoSourceException? last = null;

        for (int i = 0; i < _providers.Count; i++)
            {
            var provider = _providers[i];
            ct.ThrowIfCancellationRequested();

            if (!await provider.IsAvailableAsync(ct))
            {
                _logger.LogWarning("[FALLBACK] {Provider} indisponível neste ambiente. Pulando.", provider.ProviderName);
                last = new VideoSourceUnavailableException($"{provider.ProviderName} indisponível.");
                continue;
            }

            try
            {
                var result = await provider.DownloadAsync(request, ct);

                if (i > 0)
                    _logger.LogWarning("[FALLBACK] {Provider} atendeu depois de {Failed} tentativa(s) sem sucesso.",
                        provider.ProviderName, i);

                return result;
            }
            catch (VideoSourceRateLimitException)
            {
                _logger.LogWarning("[FALLBACK] {Provider} recusou por limite de requisições. " +
                    "Encerrando a cadeia: o bloqueio é do lado do YouTube e vale para os outros provedores também.",
                    provider.ProviderName);
                throw;
            }
            catch (VideoSourceNetworkException)
            {
                _logger.LogWarning("[FALLBACK] {Provider} sem conectividade. Encerrando a cadeia: " +
                    "não há rota para nenhum provedor.", provider.ProviderName);
                throw;
            }
            catch (VideoSourceException ex)
            {
                last = ex;
                var next = i + 1 < _providers.Count ? _providers[i + 1].ProviderName : null;

                _logger.LogWarning("[FALLBACK] {Provider} falhou ({Message}).{Next}",
                    provider.ProviderName, ex.Message,
                    next is null ? " Sem mais provedores." : $" Tentando {next}.");
            }
        }

        throw last ?? new VideoUnavailableException($"Nenhum provedor conseguiu baixar {request.VideoUrl}.");
    }
}
