using Hangfire;
using System.Threading;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoExtraction.Commands.MonitoringChannel;
using YT.Generate.Cuts.Application.VideoExtraction.Commands.QuerysVideoExtraction;

namespace YT.Generate.Cuts.Worker.VideoExtraction.Services;

public class MonitoringChannelsService
{
    private readonly IAppDispatcher _appDispatcher;
    private readonly ILogger<MonitoringChannelsService> _logger;

    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public MonitoringChannelsService(IAppDispatcher appDispatcher, ILogger<MonitoringChannelsService> logger)
    {
        _appDispatcher = appDispatcher;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task MonitoringNewVideosAsync(CancellationToken ct)
    {
        if (!await _semaphore.WaitAsync(0, ct))
        {
            _logger.LogInformation("### [SKIP] Já existe uma execução em andamento. Ignorando esta rodada.");
            return;
        }

        try
        {
            _logger.LogInformation("### [START] Iniciando ciclo de monitoramento...");

            var allMonitoringChannel = await _appDispatcher.Send(new GetAllMonitoringChannels(e => true), ct);

            foreach (var monitoring in allMonitoringChannel)
            {
                if (ct.IsCancellationRequested)
                    break;

                await _appDispatcher.Send(new MonitoringChannelCommand(monitoring,IncludeShorts: false), ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("O Job foi interrompido pelo desligamento do sistema.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ERRO] Falha durante o monitoramento.");
        }
        finally
        {
            _semaphore.Release();
            _logger.LogInformation("### [END] Ciclo de monitoramento finalizado e trava liberada.");
        }
    }
}