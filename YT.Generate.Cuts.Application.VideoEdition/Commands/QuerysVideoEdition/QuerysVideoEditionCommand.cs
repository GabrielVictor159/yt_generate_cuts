using System.Linq.Expressions;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;

namespace YT.Generate.Cuts.Application.VideoEdition.Commands.QuerysVideoEdition;

public record GetCutsToEdit(
    Expression<Func<Domain.Entities.Cut, bool>>? expression,
    params Expression<Func<Domain.Entities.Cut, object>>[] includes)
    : ICommand<List<Domain.Entities.Cut>>;
