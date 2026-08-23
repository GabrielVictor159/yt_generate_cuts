using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;

namespace YT.Generate.Cuts.Application.VideoProcessing.Commands.ProcessCuts;

public record ProcessCutsCommand(Domain.Entities.Cut Cut) : ICommand<ProcessCutsCommandResponse>;

public record ProcessCutsCommandResponse(string CutPath, TimeSpan Duration);