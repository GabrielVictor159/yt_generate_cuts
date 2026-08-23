using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;

namespace YT.Generate.Cuts.Application.VideoEdition.Commands.EditCut;

public record EditCutCommand(Domain.Entities.Cut Cut) : ICommand<EditCutCommandResponse>;

/// <param name="EditedPath">Arquivo editado, que passa a ser o arquivo do corte.</param>
/// <param name="Width">Largura aplicada.</param>
/// <param name="Height">Altura aplicada.</param>
/// <param name="SubtitlesBurned">
/// Se a legenda foi realmente embutida. Pode ser <c>false</c> mesmo com a flag
/// ligada, quando o corte não tem legenda.
/// </param>
public record EditCutCommandResponse(string EditedPath, int Width, int Height, bool SubtitlesBurned);
