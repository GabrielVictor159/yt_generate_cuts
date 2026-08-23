using Hangfire;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoEdition.Commands.EditCut;
using YT.Generate.Cuts.Application.VideoEdition.Commands.QuerysVideoEdition;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Jobs;

namespace YT.Generate.Cuts.Worker.VideoEdition.Services;

/// <summary>
/// Edição dos cortes: um corte por vez, global.
/// </summary>
/// <remarks>
/// Mesma razão da geração de cortes: o ffmpeg satura CPU e disco, e edições em
/// paralelo não terminam antes — só se atrapalham. O lock é por corte, não pelo
/// lote, para que uma rodada longa não fique presa a uma única aquisição.
/// </remarks>
public class EditingCutsService
{
    public const string LockResource = "edition:cuts";

    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(1);

    private const int DefaultMaxCutsPerCycle = 25;

    private readonly SerialJobRunner _runner;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EditingCutsService> _logger;

    public EditingCutsService(
        SerialJobRunner runner,
        IConfiguration configuration,
        ILogger<EditingCutsService> logger)
    {
        _runner = runner;
        _configuration = configuration;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    [Queue("edition")]
    public async Task EditCutsAsync(CancellationToken ct)
    {
        try
        {
            var pendingIds = await LoadPendingIdsAsync(ct);

            if (pendingIds.Count == 0)
            {
                _logger.LogDebug("### [SKIP] Nenhum corte aguardando edição.");
                return;
            }

            var max = ReadInt("Edition:MaxCutsPerCycle", DefaultMaxCutsPerCycle);
            var queue = pendingIds.Take(max).ToList();

            _logger.LogInformation("### [START] {Count} corte(s) na fila desta rodada (de {Total} pendente(s)).",
                queue.Count, pendingIds.Count);

            var result = await _runner.RunAsync(LockResource, LockWait, queue, EditOneAsync, ct);

            if (result.Busy)
            {
                _logger.LogInformation(
                    "### [SKIP] Já existe uma edição em andamento (execução única global). " +
                    "Encerrando esta rodada com {Processed} corte(s) editado(s).", result.Processed);
                return;
            }

            _logger.LogInformation("### [END] Rodada finalizada. {Processed} corte(s) editado(s).", result.Processed);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("O job foi interrompido pelo desligamento do sistema.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ERRO] Falha no ciclo de edição de cortes.");
        }
    }

    private async Task<bool> EditOneAsync(IServiceProvider scope, long cutId, CancellationToken ct)
    {
        var dispatcher = scope.GetRequiredService<IAppDispatcher>();

        try
        {
            // Releitura dentro do lock, com o canal e o perfil de edição: é daqui
            // que sai a resolução a aplicar. Se outra execução já editou este
            // corte, ele não está mais em Process e é ignorado.
            var cuts = await dispatcher.Send(
                new GetCutsToEdit(
                    c => c.Id == cutId && c.Status == CutStatus.AwaitingEdition,
                    c => c.Video!,
                    c => c.Video!.Channel!,
                    c => c.Video!.Channel!.EditionConfiguration!),
                ct);

            var cut = cuts.FirstOrDefault();

            if (cut is null)
            {
                _logger.LogInformation(
                    "### [SKIP] O corte {Id} não está mais aguardando edição — outra execução já o editou.", cutId);
                return false;
            }

            _logger.LogInformation("### [EDIT CUT] Editando: {Name} (ID {Id})", cut.Name, cutId);

            var result = await dispatcher.Send(new EditCutCommand(cut), ct);

            _logger.LogInformation("### [EDIT OK] {Path} — {Width}x{Height}, legenda {Subtitle}.",
                result.EditedPath, result.Width, result.Height,
                result.SubtitlesBurned ? "embutida" : "não embutida");

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "### [EDIT ERROR] Falha ao editar o corte {Id}. Seguindo para o próximo.", cutId);
            return false;
        }
    }

    private Task<List<long>> LoadPendingIdsAsync(CancellationToken ct) =>
        _runner.InScopeAsync(async scope =>
        {
            var cuts = await scope.GetRequiredService<IAppDispatcher>()
                .Send(new GetCutsToEdit(c => c.Status == CutStatus.AwaitingEdition), ct);

            return cuts.Select(c => c.Id).OrderBy(id => id).ToList();
        });

    private int ReadInt(string key, int fallback) =>
        int.TryParse(_configuration[key], out var value) && value > 0 ? value : fallback;
}
