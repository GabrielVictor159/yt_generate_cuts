using Hangfire;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.ProcessCuts;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.QuerysVideoProcessing;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Jobs;

namespace YT.Generate.Cuts.Worker.VideoProcessing.Services;

/// <summary>
/// Geração dos arquivos de corte. Um corte por vez, global.
/// </summary>
/// <remarks>
/// O ffmpeg satura CPU e disco: cortes em paralelo não terminam antes, só se
/// atrapalham. E, como no InterestingTimes, o <c>static SemaphoreSlim</c> anterior
/// valia dentro de um processo só.
/// <para>
/// O lock é por corte, não pelo lote — um lote grande de ffmpeg passa de
/// <c>DistributedLockTimeout</c> e o lock seria tomado no meio.
/// </para>
/// </remarks>
public class ProcessingCutsService
{
    public const string LockResource = "processing:cuts";

    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(1);

    private const int DefaultMaxCutsPerCycle = 25;

    private readonly SerialJobRunner _runner;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProcessingCutsService> _logger;

    public ProcessingCutsService(
        SerialJobRunner runner,
        IConfiguration configuration,
        ILogger<ProcessingCutsService> logger)
    {
        _runner = runner;
        _configuration = configuration;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    [Queue("processing")]
    public async Task ProcessCutsAsync(CancellationToken ct)
    {
        try
        {
            var pendingIds = await LoadPendingIdsAsync(ct);

            if (pendingIds.Count == 0)
            {
                _logger.LogDebug("### [SKIP] Nenhum corte aguardando geração de arquivo.");
                return;
            }

            var max = ReadInt("ProcessCuts:MaxCutsPerCycle", DefaultMaxCutsPerCycle);
            var queue = pendingIds.Take(max).ToList();

            _logger.LogInformation("### [START] {Count} corte(s) na fila desta rodada (de {Total} pendente(s)).",
                queue.Count, pendingIds.Count);

            var result = await _runner.RunAsync(LockResource, LockWait, queue, ProcessOneAsync, ct);

            if (result.Busy)
            {
                _logger.LogInformation(
                    "### [SKIP] Já existe uma geração de cortes em andamento (execução única global). " +
                    "Encerrando esta rodada com {Processed} corte(s) gerado(s).", result.Processed);
                return;
            }

            _logger.LogInformation("### [END] Rodada finalizada. {Processed} corte(s) gerado(s).", result.Processed);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("O job foi interrompido pelo desligamento do sistema.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ERRO] Falha no ciclo de geração de cortes.");
        }
    }

    private async Task<bool> ProcessOneAsync(IServiceProvider scope, long cutId, CancellationToken ct)
    {
        var dispatcher = scope.GetRequiredService<IAppDispatcher>();

        try
        {
            // Releitura dentro do lock: se outra execução já gerou este corte, ele
            // não está mais em Created e não pode ser gerado de novo.
            var cuts = await dispatcher.Send(
                new GetAllCuts(c => c.Id == cutId && c.Status == CutStatusEnum.Created), ct);

            var cut = cuts.FirstOrDefault();

            if (cut is null)
            {
                _logger.LogInformation(
                    "### [SKIP] O corte {Id} não está mais aguardando geração — outra execução já o gerou.", cutId);
                return false;
            }

            _logger.LogInformation("### [PROCESS CUT] Gerando arquivo de corte: {CutName} (ID {CutId})", cut.Name, cutId);

            var result = await dispatcher.Send(new ProcessCutsCommand(cut), ct);

            _logger.LogInformation("### [PROCESS OK] Corte gerado: {Path} ({Duration})", result.CutPath, result.Duration);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "### [PROCESS ERROR] Falha ao gerar o corte {Id}. Seguindo para o próximo.", cutId);
            return false;
        }
    }

    private Task<List<long>> LoadPendingIdsAsync(CancellationToken ct) =>
        _runner.InScopeAsync(async scope =>
        {
            var cuts = await scope.GetRequiredService<IAppDispatcher>()
                .Send(new GetAllCuts(c => c.Status == CutStatusEnum.Created && c.VideoId != null), ct);

            return cuts.Select(c => c.Id).OrderBy(id => id).ToList();
        });

    private int ReadInt(string key, int fallback) =>
        int.TryParse(_configuration[key], out var value) && value > 0 ? value : fallback;
}
