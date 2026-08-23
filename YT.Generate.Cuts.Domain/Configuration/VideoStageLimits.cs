using YT.Generate.Cuts.Domain.Enums;

namespace YT.Generate.Cuts.Domain.Configuration;

/// <summary>
/// Teto de vídeos por status (etapa), aplicado <b>por canal monitorado</b>.
/// Zero significa sem limite.
/// </summary>
/// <remarks>
/// <see cref="VideoStatusEnum.Failed"/> nunca é limitado: é estado terminal e,
/// se ocupasse vaga, um canal com vídeos indisponíveis travaria para sempre.
/// <para>
/// Atenção ao configurar <see cref="Process"/> e <see cref="Removed"/>: são
/// etapas finais e nada as esvazia automaticamente, então um teto nelas
/// interrompe a entrada de vídeos novos em definitivo. Por isso o padrão é
/// deixá-las sem limite e restringir apenas as etapas em andamento.
/// </para>
/// </remarks>
public sealed class VideoStageLimits
{
    public const string SectionName = "VideoStageLimits";

    /// <summary>Máximo de vídeos aguardando download.</summary>
    public int Created { get; init; }

    /// <summary>Máximo de vídeos baixados e aguardando processamento.</summary>
    public int Download { get; init; }

    /// <summary>Máximo de vídeos já processados em cortes. 0 = sem limite (recomendado).</summary>
    public int Process { get; init; }

    /// <summary>Máximo de vídeos com arquivo removido. 0 = sem limite (recomendado).</summary>
    public int Removed { get; init; }

    /// <summary>Teto configurado para o status, ou 0 quando não há limite.</summary>
    public int LimitFor(VideoStatusEnum status) => status switch
    {
        VideoStatusEnum.Created => Created,
        VideoStatusEnum.Download => Download,
        VideoStatusEnum.Process => Process,
        VideoStatusEnum.Removed => Removed,
        _ => 0
    };

    /// <summary>Status que possuem teto configurado (Failed nunca entra).</summary>
    public IEnumerable<VideoStatusEnum> LimitedStatuses =>
        new[] { VideoStatusEnum.Created, VideoStatusEnum.Download, VideoStatusEnum.Process, VideoStatusEnum.Removed }
            .Where(s => LimitFor(s) > 0);

    /// <summary>
    /// Indica se ainda cabe mais um vídeo no status informado.
    /// </summary>
    public bool HasRoom(VideoStatusEnum status, int currentCount)
    {
        var limit = LimitFor(status);
        return limit <= 0 || currentCount < limit;
    }
}
