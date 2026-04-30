using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;

namespace YT.Generate.Cuts.Application.VideoExtraction.Commands.MonitoringChannel;
public record MonitoringChannelCommand(
    Domain.Entities.MonitoringChannel Channel,
    bool IncludeShorts = true
) : ICommand;
