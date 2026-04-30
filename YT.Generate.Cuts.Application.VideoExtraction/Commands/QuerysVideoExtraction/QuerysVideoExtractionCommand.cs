using System.Linq.Expressions;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Domain.Entities;

namespace YT.Generate.Cuts.Application.VideoExtraction.Commands.QuerysVideoExtraction;

public record GetAllMonitoringChannelsByPublishChannelCommand(long publishChannelId)
    : ICommand<List<Domain.Entities.MonitoringChannel>>;

public record GetAllPublishChannel(
    Expression<Func<PublishChannel, bool>>? expression,
    params Expression<Func<PublishChannel, object>>[] includes)
    : ICommand<List<PublishChannel>>;

public record GetAllMonitoringChannels(
    Expression<Func<Domain.Entities.MonitoringChannel, bool>>? expression,
    params Expression<Func<Domain.Entities.MonitoringChannel, object>>[] includes)
    : ICommand<List<Domain.Entities.MonitoringChannel>>;