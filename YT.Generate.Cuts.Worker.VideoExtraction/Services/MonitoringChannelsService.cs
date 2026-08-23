using Hangfire;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoExtraction.Commands.MonitoringChannel;
using YT.Generate.Cuts.Application.VideoExtraction.Commands.QuerysVideoExtraction;
using YT.Generate.Cuts.Infra.Data.Jobs;

namespace YT.Generate.Cuts.Worker.VideoExtraction.Services;

public class MonitoringChannelsService
{
    /// <summary>
    /// Tempo de espera ao tentar pegar o lock. Zero na prática: se outra
    /// execução já detém o recurso, esta rodada é simplesmente descartada.
    /// </summary>
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(1);

    private const string FanOutLockResource = "monitoring-channels:fan-out";
    private const string ChannelLockPrefix = "monitoring-channel:";

    private readonly IAppDispatcher _appDispatcher;
    private readonly ILogger<MonitoringChannelsService> _logger;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly IJobLock _jobLock;

    public MonitoringChannelsService(
        IAppDispatcher appDispatcher,
        ILogger<MonitoringChannelsService> logger,
        IBackgroundJobClient backgroundJobs,
        IJobLock jobLock)
    {
        _appDispatcher = appDispatcher;
        _logger = logger;
        _backgroundJobs = backgroundJobs;
        _jobLock = jobLock;
    }

    /// <summary>
    /// Job recorrente: apenas distribui um job por canal monitorado.
    /// Assim um canal lento deixa de bloquear os demais, e a exclusão mútua
    /// passa a ser por canal.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    [Queue("extraction")]
    public async Task MonitoringNewVideosAsync(CancellationToken ct)
    {
        // O lock é o do IJobLock (advisory lock do PostgreSQL), não mais o do
        // Hangfire: aquele é uma linha com carimbo de tempo que ninguém apaga
        // quando o processo morre, e deixava o recurso travado por até uma hora
        // depois de um encerramento abrupto. O advisory lock cai com a conexão.
        var ran = await _jobLock.TryRunAsync(FanOutLockResource, LockWait, async () =>
        {
            try
            {
                _logger.LogInformation("### [START] Distribuindo o monitoramento por canal...");

                var channels = await _appDispatcher.Send(new GetAllMonitoringChannels(null), ct);

                foreach (var channel in channels)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    var channelId = channel.Id;
                    _backgroundJobs.Enqueue<MonitoringChannelsService>(
                        s => s.MonitorChannelAsync(channelId, CancellationToken.None));

                    _logger.LogInformation("### [ENQUEUE] Canal {Channel} (ID {Id}) enfileirado.", channel.Name, channelId);
                }

                _logger.LogInformation("### [END] {Count} canal(is) enfileirado(s).", channels.Count);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("A distribuição foi interrompida pelo desligamento do sistema.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERRO] Falha ao distribuir o monitoramento.");
            }
        });

        if (!ran)
            _logger.LogInformation("### [SKIP] Já existe uma distribuição de monitoramento em andamento. Ignorando esta rodada.");
    }

    /// <summary>
    /// Monitora um único canal, com exclusão mútua por canal: enquanto este job
    /// roda para o canal X, qualquer outra execução para o mesmo X é descartada.
    /// Canais diferentes seguem em paralelo.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    [Queue("extraction")]
    public async Task MonitorChannelAsync(long channelId, CancellationToken ct)
    {
        var ran = await _jobLock.TryRunAsync($"{ChannelLockPrefix}{channelId}", LockWait, async () =>
        {
            try
            {
                var channels = await _appDispatcher.Send(new GetAllMonitoringChannels(c => c.Id == channelId), ct);
                var channel = channels.FirstOrDefault();

                if (channel is null)
                {
                    _logger.LogWarning("### [SKIP] Canal {Id} não encontrado — provavelmente removido após o enfileiramento.", channelId);
                    return;
                }

                _logger.LogInformation("### [START] Monitorando o canal {Channel} (ID {Id}).", channel.Name, channelId);

                await _appDispatcher.Send(new MonitoringChannelCommand(channel, IncludeShorts: false), ct);

                _logger.LogInformation("### [END] Monitoramento do canal {Channel} finalizado.", channel.Name);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("O monitoramento do canal {Id} foi interrompido pelo desligamento do sistema.", channelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERRO] Falha durante o monitoramento do canal {Id}.", channelId);
            }
        });

        if (!ran)
            _logger.LogInformation("### [SKIP] O canal {Id} já está sendo monitorado por outra execução.", channelId);
    }
}
