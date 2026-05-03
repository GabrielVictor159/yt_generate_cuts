using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Domain.Entities;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Application.VideoProcessing.Commands.QuerysVideoProcessing;

public class QuerysVideoProcessingCommandHandler :
    ICommandHandler<GetAllVideo, List<Domain.Entities.Video>>
{
    private readonly ILogger<QuerysVideoProcessingCommandHandler> _logger;
    private readonly IUnitOfWork _uow;

    public QuerysVideoProcessingCommandHandler(ILogger<QuerysVideoProcessingCommandHandler> logger, IUnitOfWork uow)
    {
        _logger = logger;
        _uow = uow;
    }
    public async Task<List<Domain.Entities.Video>> Handle(GetAllVideo command, CancellationToken ct)
    {
        _logger.LogInformation("[QUERY] Iniciando busca de canais de publicação (PublishChannels).");

        var result = await _uow.Repository<Domain.Entities.Video>()
            .FindAsync(command.expression ?? (x => true), command.includes);

        var list = result.ToList();

        _logger.LogInformation("[QUERY] Busca finalizada. Encontrados {Count} canais de publicação.", list.Count);

        return list;
    }
}
