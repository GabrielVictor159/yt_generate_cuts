using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Domain.Entities;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Application.VideoExtraction.Commands.QuerysVideoExtraction;

public class QuerysVideoExtractionCommandHandler :
    ICommandHandler<GetAllMonitoringChannelsByPublishChannelCommand, List<Domain.Entities.MonitoringChannel>>,
    ICommandHandler<GetAllPublishChannel, List<PublishChannel>>,
    ICommandHandler<GetAllMonitoringChannels, List<Domain.Entities.MonitoringChannel>>
{
    private readonly ILogger<QuerysVideoExtractionCommandHandler> _logger;
    private readonly IUnitOfWork _uow;

    public QuerysVideoExtractionCommandHandler(ILogger<QuerysVideoExtractionCommandHandler> logger, IUnitOfWork uow)
    {
        _logger = logger;
        _uow = uow;
    }

    public async Task<List<PublishChannel>> Handle(GetAllPublishChannel command, CancellationToken ct)
    {
        _logger.LogInformation("[QUERY] Iniciando busca de canais de publicação (PublishChannels).");

        var result = await _uow.Repository<PublishChannel>()
            .FindAsync(command.expression ?? (x => true), command.includes);

        var list = result.ToList();

        _logger.LogInformation("[QUERY] Busca finalizada. Encontrados {Count} canais de publicação.", list.Count);

        return list;
    }

    public async Task<List<Domain.Entities.MonitoringChannel>> Handle(GetAllMonitoringChannelsByPublishChannelCommand command, CancellationToken ct)
    {
        _logger.LogInformation("[QUERY] Buscando canais monitorados para o canal de publicação ID: {PublishChannelId}", command.publishChannelId);

        var result = await _uow.Repository<Domain.Entities.MonitoringChannel>().FindAsync(
            x => x.Monitorings.Any(m => m.PublishChannelId == command.publishChannelId),
            include => include.Videos 
        );

        return result.ToList();
    }

    public async Task<List<Domain.Entities.MonitoringChannel>> Handle(GetAllMonitoringChannels command, CancellationToken ct)
    {
        _logger.LogInformation("[QUERY] Buscando canais monitorados ");

        var result = await _uow.Repository<Domain.Entities.MonitoringChannel>().FindAsync(command.expression ?? (e => true),
            command.includes
        );

        return result.ToList();
    }
}