using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoPublish.Commands;
using YT.Generate.Cuts.Domain.Entities;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;
using YT.Generate.Cuts.Infra.Data.Jobs;

namespace YT.Generate.Cuts.Worker.VideoPublish.Services;

/// <summary>
/// Publicação dos cortes prontos. Uma publicação por vez, global.
/// </summary>
/// <remarks>
/// A etapa de entrada mudou: a publicação agora consome cortes em
/// <see cref="CutStatusEnum.Edit"/>, não mais em <c>Process</c>. Entre a geração
/// do arquivo e a publicação passou a existir a edição (resolução e legenda
/// embutida), e publicar um corte não editado entregaria à plataforma um vídeo na
/// proporção errada.
/// <para>
/// Aqui a exclusão importa mais do que nas outras etapas: as plataformas têm
/// limite de requisições, e publicar o mesmo corte duas vezes não tem desfazer.
/// O <c>static SemaphoreSlim</c> anterior valia dentro de um processo só — com
/// duas instâncias, dois ciclos liam a mesma lista e postavam o mesmo corte.
/// <para>
/// Por isso o corte é <b>relido dentro do lock</b>: se outra execução já o
/// publicou, ele não está mais em <see cref="CutStatusEnum.Edit"/> e é
/// ignorado.
/// </para>
/// </remarks>
public class PublishCutsService
{
    public const string LockResource = "publish:cuts";

    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(1);

    private const int DefaultMaxCutsPerCycle = 10;

    private readonly SerialJobRunner _runner;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PublishCutsService> _logger;

    public PublishCutsService(
        SerialJobRunner runner,
        IConfiguration configuration,
        ILogger<PublishCutsService> logger)
    {
        _runner = runner;
        _configuration = configuration;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    [Queue("publish")]
    public async Task PublishPendingCutsAsync(CancellationToken ct)
    {
        try
        {
            var pendingIds = await LoadPendingIdsAsync();

            if (pendingIds.Count == 0)
            {
                _logger.LogDebug("### [SKIP] Nenhum corte pronto para publicar.");
                return;
            }

            var max = ReadInt("Publish:MaxCutsPerCycle", DefaultMaxCutsPerCycle);
            var queue = pendingIds.Take(max).ToList();

            _logger.LogInformation("### [START] {Count} corte(s) na fila desta rodada (de {Total} pronto(s)).",
                queue.Count, pendingIds.Count);

            var result = await _runner.RunAsync(LockResource, LockWait, queue, PublishOneAsync, ct);

            if (result.Busy)
            {
                _logger.LogInformation(
                    "### [SKIP] Já existe uma publicação em andamento (execução única global). " +
                    "Encerrando esta rodada com {Processed} corte(s) publicado(s).", result.Processed);
                return;
            }

            _logger.LogInformation("### [END] Rodada finalizada. {Processed} corte(s) publicado(s).", result.Processed);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("### O job de publicação foi interrompido pelo desligamento do sistema.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "### [ERRO] Falha no ciclo de publicação.");
        }
    }

    private async Task<bool> PublishOneAsync(IServiceProvider scope, long cutId, CancellationToken ct)
    {
        var uow = scope.GetRequiredService<IUnitOfWork>();
        var dispatcher = scope.GetRequiredService<IAppDispatcher>();

        try
        {
            // Releitura dentro do lock: é o que impede a publicação em duplicidade.
            var cut = (await uow.Repository<Cut>()
                    .FindAsync(c => c.Id == cutId &&
                                    c.Status == CutStatus.AwaitingPublication &&
                                    c.PublishChannelId != null))
                .FirstOrDefault();

            if (cut is null)
            {
                _logger.LogInformation(
                    "### [SKIP] O corte {Id} não está mais pronto para publicar — outra execução já o publicou.", cutId);
                return false;
            }

            _logger.LogInformation("### [PUBLISH CUT] Publicando corte: {CutName} (ID {CutId})", cut.Name, cutId);

            var result = await dispatcher.Send(new PublishCutCommand(cut), ct);

            if (result.Success)
            {
                _logger.LogInformation("### [PUBLISH OK] Corte publicado! ID: {PostId}, URL: {PostUrl}",
                    result.PlatformPostId, result.PostUrl);
                return true;
            }

            _logger.LogWarning("### [PUBLISH WARN] Corte {CutName} não publicado: {Error}", cut.Name, result.ErrorMessage);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "### [PUBLISH ERROR] Falha ao publicar o corte {Id}. Seguindo para o próximo.", cutId);
            return false;
        }
    }

    private Task<List<long>> LoadPendingIdsAsync() =>
        _runner.InScopeAsync(async scope =>
        {
            var cuts = await scope.GetRequiredService<IUnitOfWork>().Repository<Cut>()
                .FindAsync(c => c.Status == CutStatus.AwaitingPublication && c.PublishChannelId != null);

            return cuts.Select(c => c.Id).OrderBy(id => id).ToList();
        });

    private int ReadInt(string key, int fallback) =>
        int.TryParse(_configuration[key], out var value) && value > 0 ? value : fallback;
}
