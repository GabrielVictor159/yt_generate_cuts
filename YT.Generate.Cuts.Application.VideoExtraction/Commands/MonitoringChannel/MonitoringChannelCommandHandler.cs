using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Common;
using YT.Generate.Cuts.Application.Abstractions.Exceptions;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.VideoSource;
using YT.Generate.Cuts.Application.VideoExtraction.Commands.Download;
using YT.Generate.Cuts.Application.VideoExtraction.Youtube;
using YT.Generate.Cuts.Domain.Configuration;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Application.VideoExtraction.Commands.MonitoringChannel;
public class MonitoringChannelCommandHandler : ICommandHandler<MonitoringChannelCommand>
{
    /// <summary>Duração máxima, em segundos, para um vídeo ser tratado como short.</summary>
    private const int DefaultShortMaxSeconds = 300;

    /// <summary>
    /// Quantos uploads recentes olhar por ciclo. O monitoramento roda a cada
    /// minuto, então não faz sentido paginar o histórico do canal — e um teto
    /// mantém previsível o volume de requisições ao provedor.
    /// </summary>
    private const int DefaultMaxUploadsPerCycle = 50;

    private readonly ILogger<MonitoringChannelCommandHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWork _uow;
    private readonly IAppDispatcher _appDispatcher;
    private readonly IVideoCatalog _catalog;

    public MonitoringChannelCommandHandler(ILogger<MonitoringChannelCommandHandler> logger,
        IConfiguration configuration, IUnitOfWork uow, IAppDispatcher appDispatcher,
        IVideoCatalog catalog)
    {
        _logger = logger;
        _configuration = configuration;
        _uow = uow;
        _appDispatcher = appDispatcher;
        _catalog = catalog;
    }

    public async Task Handle(MonitoringChannelCommand command, CancellationToken ct)
    {
        _logger.LogInformation("[INÍCIO] Iniciando monitoramento para o canal: {ChannelName}", command.Channel.Name);

        // ------------------------------------------------------------------
        // Etapa 1: resolver o canal. Falhar aqui é falha de canal (URL/handle
        // inválido, canal removido) e vale propagar para quem chamou.
        // ------------------------------------------------------------------
        CatalogChannel channel;
        IReadOnlyList<CatalogVideo> uploads;
        try
        {
            command.Channel.Videos = (await _uow.Repository<Domain.Entities.Video>()
                .FindAsync(e => e.ChannelId == command.Channel.Id)).ToList();

            _logger.LogInformation("Total de vídeos carregados do banco local: {Count}", command.Channel.Videos.Count);

            _logger.LogDebug("Resolvendo o canal em {Provider}: {Url}", _catalog.ProviderName, command.Channel.Url);
            channel = await _catalog.ResolveChannelAsync(command.Channel.Url, ct);

            var maxUploads = ReadInt("VideoSource:MaxUploadsPerCycle", DefaultMaxUploadsPerCycle);
            uploads = await _catalog.GetRecentUploadsAsync(channel.Id, maxUploads, ct);
        }
        catch (VideoSourceNetworkException ex)
        {
            // Sem rota até o YouTube não há o que corrigir no canal nem no
            // registro: é infraestrutura. Registrar como falha de canal apontaria
            // para o lugar errado, e propagar geraria uma segunda pilha idêntica.
            _logger.LogWarning(
                "[REDE] O container não alcançou o YouTube ({Message}). Não é problema do canal '{Channel}' " +
                "nem da biblioteca — verifique a saída de rede do container. Nova tentativa no próximo ciclo.",
                ex.Message, command.Channel.Name);
            return;
        }
        catch (VideoSourceRateLimitException ex)
        {
            _logger.LogWarning("[LIMITE] O provedor recusou a listagem do canal '{Channel}': {Message}",
                command.Channel.Name, ex.Message);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ERRO] Não foi possível resolver o canal '{Channel}' ({Url}).",
                command.Channel.Name, command.Channel.Url);
            throw;
        }

        // Comparar por ID do vídeo em vez de pela URL crua: as URLs vindas da
        // listagem de uploads carregam "&list=UU...", então comparar strings
        // deixaria passar o mesmo vídeo em formatos diferentes.
        var knownVideoIds = command.Channel.Videos
            .Select(v => TryGetVideoId(v.Url))
            .Where(id => id is not null)
            .Select(id => id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var shortMaxSeconds = int.TryParse(_configuration["ShortMaxSeconds"], out var configured)
            ? configured
            : DefaultShortMaxSeconds;

        var downloadPath = _configuration["DownloadPath"] ?? "/app/downloads";

        // Teto de vídeos por etapa, contabilizado só para ESTE canal.
        // Os vídeos do canal já estão carregados acima, então a contagem sai da
        // memória — sem consulta extra ao banco.
        var limits = ReadStageLimits();
        var countByStatus = CountByStatus(command.Channel.Videos);

        if (limits.LimitedStatuses.Any())
        {
            _logger.LogDebug("[LIMITE] Ocupação atual do canal {Channel}: {Ocupacao}",
                command.Channel.Name,
                string.Join(", ", limits.LimitedStatuses
                    .Select(s => $"{s}={countByStatus.GetValueOrDefault(s)}/{limits.LimitFor(s)}")));
        }

        // ------------------------------------------------------------------
        // Etapa 2: percorrer os uploads. A falha de UM vídeo não pode derrubar
        // o ciclo do canal inteiro — era o que acontecia antes.
        // ------------------------------------------------------------------
        _logger.LogInformation("Canal identificado: {Title} ({Id}) — {Count} upload(s) recentes de {Provider}.",
            channel.Title, channel.Id, uploads.Count, _catalog.ProviderName);

        foreach (var upload in uploads)
        {
            if (ct.IsCancellationRequested)
                break;

            // O teto é verificado ANTES de tocar no banco: nenhum registro novo
            // entra enquanto alguma etapa deste canal estiver cheia.
            if (IsAnyStageFull(limits, countByStatus, out var fullStatus))
            {
                _logger.LogInformation(
                    "[LIMITE] Canal {Channel} já tem {Count} vídeo(s) em {Status} (teto {Limit}). " +
                    "Nenhum vídeo novo será cadastrado até que uma vaga seja liberada.",
                    command.Channel.Name, countByStatus.GetValueOrDefault(fullStatus), fullStatus, limits.LimitFor(fullStatus));
                break;
            }

            var videoId = upload.Id;

            if (knownVideoIds.Contains(videoId))
                continue;

            // A duração já vem no próprio item da listagem de uploads. Antes o
            // código fazia um Videos.GetAsync por vídeo só para ler isso: uma
            // requisição extra ao YouTube para cada upload do canal.
            var isShort = upload.Duration.HasValue && upload.Duration.Value.TotalSeconds <= shortMaxSeconds;

            if (!command.IncludeShorts && isShort)
            {
                _logger.LogInformation("[SKIP] Shorts ignorado por configuração: {Title}", upload.Title);
                continue;
            }

            var videoUrl = upload.Url;

            // Um SaveChanges já é atômico por si só. As transações explícitas
            // que existiam aqui envolviam um único insert e foram removidas —
            // eram elas que geravam o ObjectDisposedException no rollback.
            var newVideo = new Domain.Entities.Video()
            {
                Title = upload.Title,
                Url = videoUrl,
                ChannelId = command.Channel.Id,
                Duration = upload.Duration,
                Status = Domain.Enums.VideoStatusEnum.Created
            };

            await _uow.Repository<Domain.Entities.Video>().AddAsync(newVideo);
            await _uow.CommitAsync();
            knownVideoIds.Add(videoId);
            Move(countByStatus, from: null, to: VideoStatusEnum.Created);

            _logger.LogInformation("Novo registro de vídeo inserido com sucesso para a URL: {Url}", videoUrl);
            _logger.LogInformation("Disparando DownloadVideoCommand para o diretório: {Path}", downloadPath);

            try
            {
                var responseDownload = await _appDispatcher.Send(new DownloadVideoCommand(videoUrl, downloadPath), ct);

                if (responseDownload is null)
                {
                    _logger.LogWarning("[DOWNLOAD FAIL] O comando de download retornou nulo para o vídeo: {Title}", upload.Title);
                    await MarkAsFailedAsync(newVideo, countByStatus);
                    continue;
                }

                _logger.LogInformation("[DOWNLOAD OK] Arquivos baixados. Vídeo: {VPath}, Legenda: {SPath}",
                    responseDownload.videoPath, responseDownload.subtitlePath);

                newVideo.VideoPath = responseDownload.videoPath;
                newVideo.SubtitlePath = string.IsNullOrWhiteSpace(responseDownload.subtitlePath)
                    ? null
                    : responseDownload.subtitlePath;
                newVideo.Language = responseDownload.language;
                newVideo.Status = Domain.Enums.VideoStatusEnum.Download;

                if (responseDownload.duration > TimeSpan.Zero)
                    newVideo.Duration = responseDownload.duration;

                await _uow.CommitAsync();
                Move(countByStatus, from: VideoStatusEnum.Created, to: VideoStatusEnum.Download);
                _logger.LogInformation("Paths de arquivo atualizados no banco de dados.");

                // Um vídeo por ciclo, como no comportamento original.
                _logger.LogInformation("[FIM DO LOOP] Encerrando monitoramento após o primeiro download concluído.");
                break;
            }
            catch (VideoSourceRateLimitException ex)
            {
                // Limite de requisições é condição do lado do YouTube e passa com
                // o tempo — não é defeito deste vídeo. Marcá-lo como Failed o
                // condenaria por um problema alheio (era o comportamento anterior,
                // e bastava um 429 para perder o vídeo em definitivo). Deixá-lo em
                // Created também não serve: nos próximos ciclos ele contaria como
                // "já conhecido" e nunca seria retentado. Então descartamos o
                // registro, igual ao tratamento de falha de rede.
                _logger.LogWarning(
                    "[LIMITE] O YouTube recusou por excesso de requisições ({Message}). O registro de {Title} será " +
                    "removido para nova tentativa no próximo ciclo. Encerrando o ciclo do canal {Channel}.",
                    ex.Message, upload.Title, command.Channel.Name);

                await DiscardAsync(newVideo, countByStatus);
                knownVideoIds.Remove(videoId);
                break;
            }
            catch (VideoUnavailableException ex)
            {
                // Vídeo indisponível, privado, exclusivo para membros ou pago:
                // marcamos e seguimos, senão este vídeo travaria o canal para sempre.
                _logger.LogWarning(ex, "[INDISPONÍVEL] {Title} ({Url}) não pôde ser baixado. Seguindo para o próximo.",
                    upload.Title, videoUrl);
                await MarkAsFailedAsync(newVideo, countByStatus);
                continue;
            }
            catch (VideoSourceNetworkException ex)
            {
                // Queda de rede não é defeito do vídeo. Marcá-lo como Failed o
                // condenaria para sempre — e deixá-lo em Created também, porque
                // nos próximos ciclos ele contaria como "já conhecido" e nunca
                // seria retentado. Removemos o registro recém-criado para que a
                // próxima execução tente de novo do zero.
                _logger.LogWarning(
                    "[REDE] Sem conectividade ao baixar {Title} ({Message}). O registro será removido para nova " +
                    "tentativa no próximo ciclo. Verifique a saída de rede do container.",
                    upload.Title, ex.Message);

                await DiscardAsync(newVideo, countByStatus);
                knownVideoIds.Remove(videoId);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[FALHA] Erro ao baixar {Title} ({Url}). Seguindo para o próximo vídeo.",
                    upload.Title, videoUrl);
                await MarkAsFailedAsync(newVideo, countByStatus);
                continue;
            }
        }

        _logger.LogInformation("[CONCLUÍDO] Método Handle finalizado para o canal {ChannelName}.", command.Channel.Name);
    }

    /// <summary>
    /// Marca o registro como <see cref="Domain.Enums.VideoStatusEnum.Failed"/>.
    /// Antes, um download com falha deixava a linha em Created e sem arquivos:
    /// o vídeo era considerado "já conhecido" nos ciclos seguintes e nunca mais
    /// era tratado, acumulando registros inúteis no banco.
    /// </summary>
    private async Task MarkAsFailedAsync(
        Domain.Entities.Video video,
        Dictionary<VideoStatusEnum, int> countByStatus)
    {
        var previous = video.Status;

        try
        {
            video.Status = VideoStatusEnum.Failed;
            await _uow.CommitAsync();

            // Failed não ocupa vaga: a vaga da etapa anterior é liberada.
            Move(countByStatus, from: previous, to: null);
        }
        catch (Exception ex)
        {
            // Nunca deixar a falha ao registrar o erro substituir o erro original.
            _logger.LogError(ex, "[ERRO] Não foi possível marcar o vídeo {Id} como Failed.", video.Id);
        }
    }

    // ----------------------------------------------------------------------
    // Teto por etapa
    // ----------------------------------------------------------------------

    private VideoStageLimits ReadStageLimits() => new()
    {
        Created = ReadLimit(nameof(VideoStageLimits.Created)),
        Download = ReadLimit(nameof(VideoStageLimits.Download)),
        Process = ReadLimit(nameof(VideoStageLimits.Process)),
        Removed = ReadLimit(nameof(VideoStageLimits.Removed)),
    };

    private int ReadInt(string key, int fallback) =>
        int.TryParse(_configuration[key], out var value) && value > 0 ? value : fallback;

    private int ReadLimit(string status) =>
        int.TryParse(_configuration[$"{VideoStageLimits.SectionName}:{status}"], out var value) && value > 0
            ? value
            : 0;

    /// <summary>
    /// Contagem por status dos vídeos do canal. <see cref="VideoStatusEnum.Failed"/>
    /// fica de fora: é estado terminal e não ocupa vaga.
    /// </summary>
    private static Dictionary<VideoStatusEnum, int> CountByStatus(IEnumerable<Domain.Entities.Video> videos) =>
        videos
            .Where(v => v.Status != VideoStatusEnum.Failed)
            .GroupBy(v => v.Status)
            .ToDictionary(g => g.Key, g => g.Count());

    private static bool IsAnyStageFull(
        VideoStageLimits limits,
        Dictionary<VideoStatusEnum, int> countByStatus,
        out VideoStatusEnum fullStatus)
    {
        foreach (var status in limits.LimitedStatuses)
        {
            if (!limits.HasRoom(status, countByStatus.GetValueOrDefault(status)))
            {
                fullStatus = status;
                return true;
            }
        }

        fullStatus = default;
        return false;
    }

    /// <summary>
    /// Move a contagem de uma etapa para outra. <c>null</c> representa "fora do
    /// controle de vagas" — a entrada de um vídeo novo, ou a saída para Failed.
    /// </summary>
    private static void Move(
        Dictionary<VideoStatusEnum, int> countByStatus,
        VideoStatusEnum? from,
        VideoStatusEnum? to)
    {
        if (from is not null)
        {
            var current = countByStatus.GetValueOrDefault(from.Value);
            countByStatus[from.Value] = current > 0 ? current - 1 : 0;
        }

        if (to is not null)
            countByStatus[to.Value] = countByStatus.GetValueOrDefault(to.Value) + 1;
    }

    /// <summary>
    /// Remove o registro recém-inserido, devolvendo o canal ao estado anterior à
    /// tentativa. Usado quando a falha é de infraestrutura e não do vídeo.
    /// </summary>
    private async Task DiscardAsync(
        Domain.Entities.Video video,
        Dictionary<VideoStatusEnum, int> countByStatus)
    {
        try
        {
            var previous = video.Status;
            _uow.Repository<Domain.Entities.Video>().Delete(video);
            await _uow.CommitAsync();
            Move(countByStatus, from: previous, to: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ERRO] Não foi possível remover o registro do vídeo {Id}.", video.Id);
        }
    }

    /// <summary>
    /// Distingue indisponibilidade de rede (sem rota, DNS, TLS, timeout de
    /// conexão) de um erro de conteúdo. Percorre as exceções internas porque o
    /// SocketException costuma vir embrulhado em HttpRequestException.
    /// </summary>
    private static bool IsNetworkFailure(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is System.Net.Sockets.SocketException or System.Net.Http.HttpRequestException)
                return true;

            // Timeout do HttpClient chega como TaskCanceledException com
            // TimeoutException dentro.
            if (ex is TimeoutException)
                return true;
        }

        return false;
    }

    /// <summary>Mensagem da exceção mais interna, que é a que diz o que houve.</summary>
    private static string RootMessage(Exception exception)
    {
        var ex = exception;
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex.Message;
    }

    private static string BuildCanonicalUrl(string videoId)
        => $"https://www.youtube.com/watch?v={videoId}";

    /// <summary>
    /// Extrai o id do vídeo de qualquer forma de URL já gravada no banco.
    /// Feito aqui, com regex, para o handler não depender do parser de um
    /// provedor específico — a comparação precisa funcionar tanto para as URLs
    /// antigas (com "&amp;list=UU...") quanto para as canônicas.
    /// </summary>
    internal static string? TryGetVideoId(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var value = url.Trim();

        var match = VideoIdPattern.Match(value);
        if (match.Success) return match.Groups["id"].Value;

        // Já é um id puro.
        return BareVideoId.IsMatch(value) ? value : null;
    }

    private static readonly System.Text.RegularExpressions.Regex VideoIdPattern = new(
        @"(?:v=|/shorts/|/embed/|/live/|youtu\.be/)(?<id>[A-Za-z0-9_-]{11})",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex BareVideoId = new(
        @"^[A-Za-z0-9_-]{11}$",
        System.Text.RegularExpressions.RegexOptions.Compiled);
}
