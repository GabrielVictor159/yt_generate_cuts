using System.Linq.Expressions;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;

namespace YT.Generate.Cuts.Application.VideoProcessing.Commands.QuerysVideoProcessing;

public record GetAllVideo(
    Expression<Func<Domain.Entities.Video, bool>>? expression,
    params Expression<Func<Domain.Entities.Video, object>>[] includes)
    : ICommand<List<Domain.Entities.Video>>;

public record GetAllCuts(
    Expression<Func<Domain.Entities.Cut, bool>>? expression,
    params Expression<Func<Domain.Entities.Cut, object>>[] includes)
    : ICommand<List<Domain.Entities.Cut>>;