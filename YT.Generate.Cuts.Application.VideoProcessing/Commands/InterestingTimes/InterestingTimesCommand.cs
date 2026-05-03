
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;

namespace YT.Generate.Cuts.Application.VideoProcessing.Commands.InterestingTimes;

public record InterestingTimesCommand (Domain.Entities.Video Video) : ICommand<InterestingTimesCommandResponse>;

public record InterestingTimesCommandResponse(List<Domain.Entities.Cut> Cuts);

