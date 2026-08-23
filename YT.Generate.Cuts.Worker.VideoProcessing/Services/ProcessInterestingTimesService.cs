using Hangfire;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.InterestingTimes;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.QuerysVideoProcessing;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Jobs;

namespace YT.Generate.Cuts.Worker.VideoProcessing.Services;

/// <summary>
/// Ciclo de extração dos momentos interessantes. Uma execução por vez, global.
/// </summary>
/// <remarks>
/// A exclusão é global de propósito, diferente do monitoramento de canais: o
/// gargalo não é o canal, é o Ollama. O modelo de 27B ocupa a GPU inteira, e duas
/// inferências simultâneas não dobram a vazão — brigam por VRAM e as duas ficam
/// mais lentas, ou o modelo é descarregado e recarregado no meio.
/// <para>
/// A mecânica de "um por vez" está no <see cref="SerialJobRunner"/>, que explica
/// por que o lock é tomado por vídeo e não pelo lote.
/// </para>
/// </remarks>
public class ProcessInterestingTimesService
{
    /// <summary>
    /// Recurso único do lock. Um só nome, sem sufixo de canal ou de vídeo: é o
    /// que faz a exclusão ser global.
    /// </summary>
    public const string LockResource = "processing:interesting-times";

    /// <summary>
    /// Espera ao tentar o lock. Praticamente zero: se outra execução está com a
    /// vez, não há o que fazer nesta rodada — o cron traz a próxima em um minuto.
    /// </summary>
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Teto de vídeos por ciclo. Existe para a rodada ter fim previsível; o que
    /// sobrar é retomado na seguinte.
    /// </summary>
    private const int DefaultMaxVideosPerCycle = 25;

    private readonly SerialJobRunner _runner;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProcessInterestingTimesService> _logger;

    public ProcessInterestingTimesService(
        SerialJobRunner runner,
        IConfiguration configuration,
        ILogger<ProcessInterestingTimesService> logger)
    {
        _runner = runner;
        _configuration = configuration;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    [Queue("processing")]
    public async Task ProcessInterestingTimesAsync(CancellationToken ct)
    {
        try
        {
            var pendingIds = await LoadPendingIdsAsync(ct);

            if (pendingIds.Count == 0)
            {
                _logger.LogDebug("### [SKIP] Nenhum vídeo aguardando extração de tempos.");
                return;
            }

            var max = ReadInt("InterestingTimes:MaxVideosPerCycle", DefaultMaxVideosPerCycle);
            var queue = pendingIds.Take(max).ToList();

            _logger.LogInformation("### [START] {Count} vídeo(s) na fila desta rodada (de {Total} pendente(s)).",
                queue.Count, pendingIds.Count);

            var result = await _runner.RunAsync(LockResource, LockWait, queue, ProcessOneAsync, ct);

            if (result.Busy)
            {
                _logger.LogInformation(
                    "### [SKIP] Já existe uma extração de tempos em andamento (execução única global). " +
                    "Encerrando esta rodada com {Processed} vídeo(s) processado(s).", result.Processed);
                return;
            }

            _logger.LogInformation("### [END] Rodada finalizada. {Processed} vídeo(s) processado(s).", result.Processed);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("O job foi interrompido pelo desligamento do sistema.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ERRO] Falha no ciclo de extração de tempos.");
        }
    }

    private async Task<bool> ProcessOneAsync(IServiceProvider scope, long videoId, CancellationToken ct)
    {
        var dispatcher = scope.GetRequiredService<IAppDispatcher>();

        try
        {
            // O include do canal é necessário: a duração mínima e máxima do corte
            // é configurada por canal monitorado.
            var videos = await dispatcher.Send(
                new GetAllVideo(
                    v => v.Id == videoId && v.Status == VideoStatusEnum.Download,
                    v => v.Channel!),
                ct);

            var video = videos.FirstOrDefault();

            if (video is null)
            {
                _logger.LogInformation(
                    "### [SKIP] O vídeo {Id} não está mais aguardando extração — outra execução já o concluiu " +
                    "ou ele mudou de etapa.", videoId);
                return false;
            }

            _logger.LogInformation("### [VÍDEO] Extraindo tempos de '{Title}' (ID {Id}).", video.Title, videoId);

            await dispatcher.Send(new InterestingTimesCommand(video), ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A falha de um vídeo não pode encerrar a fila da rodada.
            _logger.LogError(ex, "[ERRO] Falha ao extrair tempos do vídeo {Id}. Seguindo para o próximo.", videoId);
            return false;
        }
    }

    /// <summary>
    /// Só os ids: a lista serve para ordenar o trabalho, e cada vídeo é relido na
    /// hora de processar.
    /// </summary>
    private Task<List<long>> LoadPendingIdsAsync(CancellationToken ct) =>
        _runner.InScopeAsync(async scope =>
        {
            var videos = await scope.GetRequiredService<IAppDispatcher>()
                .Send(new GetAllVideo(v => v.Status == VideoStatusEnum.Download), ct);

            // Mais antigos primeiro, para nenhum vídeo ficar para trás quando a
            // fila é maior que o teto do ciclo.
            return videos.Select(v => v.Id).OrderBy(id => id).ToList();
        });

    private int ReadInt(string key, int fallback) =>
        int.TryParse(_configuration[key], out var value) && value > 0 ? value : fallback;
}
