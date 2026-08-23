using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Application.VideoEdition.Commands.QuerysVideoEdition;

public class QuerysVideoEditionCommandHandler : ICommandHandler<GetCutsToEdit, List<Domain.Entities.Cut>>
{
    private readonly ILogger<QuerysVideoEditionCommandHandler> _logger;
    private readonly IUnitOfWork _uow;

    public QuerysVideoEditionCommandHandler(ILogger<QuerysVideoEditionCommandHandler> logger, IUnitOfWork uow)
    {
        _logger = logger;
        _uow = uow;
    }

    public async Task<List<Domain.Entities.Cut>> Handle(GetCutsToEdit command, CancellationToken ct)
    {
        var result = await _uow.Repository<Domain.Entities.Cut>()
            .FindAsync(command.expression ?? (x => true), command.includes);

        var list = result.ToList();

        _logger.LogInformation("[QUERY] Busca de cortes para edição finalizada. Encontrados {Count}.", list.Count);

        return list;
    }
}
