using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;

namespace YT.Generate.Cuts.Application.VideoPublish.Commands;

public record PublishCutCommand(Domain.Entities.Cut Cut) : ICommand<PublishCutCommandResponse>;

public record PublishCutCommandResponse(
    bool Success,
    string? PlatformPostId,
    string? PostUrl,
    string? ErrorMessage,
    Domain.Entities.Cut UpdatedCut);