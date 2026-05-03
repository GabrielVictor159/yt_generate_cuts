using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;

namespace YT.Generate.Cuts.Application.VideoExtraction.Commands.Download;
public record DownloadVideoCommand(string videoUri, string saveDirectory) : ICommand<DownloadVideoCommandResponse>;

public record DownloadVideoCommandResponse(
    string videoPath,
    string subtitlePath,
    string language,
    TimeSpan duration);