
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.InterestingTimes;

namespace YT.Generate.Cuts.Application.VideoProcessing.Commands.RemoveVideos;

public record RemoveVideosCommand(Domain.Entities.Video Video) : ICommand;

