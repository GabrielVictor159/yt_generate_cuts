using Hangfire;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.InterestingTimes;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.QuerysVideoProcessing;

namespace YT.Generate.Cuts.Worker.VideoProcessing.Services;

public class ProcessInterestingTimesService
{
    private readonly IAppDispatcher _appDispatcher;
    private readonly ILogger<ProcessInterestingTimesService> _logger;

    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public ProcessInterestingTimesService(IAppDispatcher appDispatcher, ILogger<ProcessInterestingTimesService> logger)
    {
        _appDispatcher = appDispatcher;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task ProcessInterestingTimesAsync(CancellationToken ct)
    {
        if (!await _semaphore.WaitAsync(0, ct))
        {
            _logger.LogInformation("### [SKIP] Já existe uma execução em andamento. Ignorando esta rodada.");
            return;
        }

        try
        {
            _logger.LogInformation("### [START] Iniciando ciclo de extração de tempos dos videos...");

            var allVideosNotProcess = await _appDispatcher.Send(new GetAllVideo(e => e.Status == Domain.Enums.VideoStatusEnum.Download), ct);

            foreach (var video in allVideosNotProcess)
            {
                if (ct.IsCancellationRequested)
                    break;

                await _appDispatcher.Send(new InterestingTimesCommand(video), ct);
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
