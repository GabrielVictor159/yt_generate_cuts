using Hangfire;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.QuerysVideoProcessing;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.RemoveVideos;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Jobs;

namespace YT.Generate.Cuts.Worker.VideoProcessing.Services;

/// <summary>
/// Limpeza dos arquivos de vídeo cujos cortes já foram todos gerados.
/// </summary>
/// <remarks>
/// Aqui o defeito visível no log era outro: a exceção de um vídeo subia e
/// abortava a rodada inteira. Com três vídeos elegíveis, só o primeiro era
/// tentado e os outros dois nunca eram limpos — a cada minuto, para sempre.
/// Agora cada vídeo é isolado, e a rodada segue.
/// </remarks>
public class RemoveVideoCutCreatedService
{
    public const string LockResource = "processing:remove-videos";

    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(1);

    private readonly SerialJobRunner _runner;
    private readonly ILogger<RemoveVideoCutCreatedService> _logger;

    public RemoveVideoCutCreatedService(SerialJobRunner runner, ILogger<RemoveVideoCutCreatedService> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    [Queue("processing")]
    public async Task RemoveVideosAsync(CancellationToken ct)
    {
        try
        {
            var pendingIds = await LoadPendingIdsAsync(ct);

            if (pendingIds.Count == 0)
            {
                _logger.LogDebug("### [SKIP] Nenhum vídeo para limpar.");
                return;
            }

            _logger.LogInformation("### [START] {Count} vídeo(s) com cortes concluídos para limpar.", pendingIds.Count);

            var result = await _runner.RunAsync(LockResource, LockWait, pendingIds, RemoveOneAsync, ct);

            if (result.Busy)
            {
                _logger.LogInformation("### [SKIP] Já existe uma limpeza em andamento. Encerrando esta rodada.");
                return;
            }

            _logger.LogInformation("### [END] Rodada finalizada. {Processed} vídeo(s) limpo(s).", result.Processed);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("O job foi interrompido pelo desligamento do sistema.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ERRO] Falha no ciclo de limpeza de vídeos.");
        }
    }

    private async Task<bool> RemoveOneAsync(IServiceProvider scope, long videoId, CancellationToken ct)
    {
        var dispatcher = scope.GetRequiredService<IAppDispatcher>();

        try
        {
            var videos = await dispatcher.Send(
                new GetAllVideo(v => v.Id == videoId && v.Status == VideoStatusEnum.Process, v => v.Cuts), ct);

            var video = videos.FirstOrDefault();

            if (video is null)
            {
                _logger.LogInformation("### [SKIP] O vídeo {Id} não está mais elegível para limpeza.", videoId);
                return false;
            }

            await dispatcher.Send(new RemoveVideosCommand(video), ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Era exatamente isto que faltava: a falha de um vídeo não pode
            // impedir a limpeza dos demais.
            _logger.LogError(ex, "[ERRO] Falha ao limpar o vídeo {Id}. Seguindo para o próximo.", videoId);
            return false;
        }
    }

    private Task<List<long>> LoadPendingIdsAsync(CancellationToken ct) =>
        _runner.InScopeAsync(async scope =>
        {
            var videos = await scope.GetRequiredService<IAppDispatcher>()
                .Send(new GetAllVideo(
                    v => v.Status == VideoStatusEnum.Process &&
                         !v.Cuts.Any(c => c.Status == CutStatusEnum.Created),
                    v => v.Cuts), ct);

            return videos.Select(v => v.Id).OrderBy(id => id).ToList();
        });
}
