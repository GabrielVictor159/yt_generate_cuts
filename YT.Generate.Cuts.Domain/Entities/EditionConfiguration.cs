namespace YT.Generate.Cuts.Domain.Entities;

/// <summary>
/// Configuração de edição dos cortes: resolução de saída e legenda embutida.
/// </summary>
/// <remarks>
/// É uma entidade própria, e não campos no canal monitorado, porque o mesmo
/// perfil ("Vertical 1080x1920 com legenda") normalmente serve vários canais —
/// duplicá-lo em cada um levaria a configurações divergindo com o tempo. O canal
/// aponta para ela; <c>null</c> no canal significa usar o padrão global de
/// configuração.
/// </remarks>
public class EditionConfiguration
{
    public long Id { get; set; }

    /// <summary>Nome do perfil, para escolher na interface.</summary>
    public required string Name { get; set; }

    /// <summary>Largura de saída, em pixels.</summary>
    public required int Width { get; set; }

    /// <summary>Altura de saída, em pixels.</summary>
    public required int Height { get; set; }

    /// <summary>
    /// Quando verdadeiro, a legenda do corte é queimada no vídeo.
    /// </summary>
    /// <remarks>
    /// Depende de o corte ter legenda (<c>Cut.SubtitlePath</c>), que por sua vez
    /// depende de o vídeo original ter tido legenda. Sem ela o corte é editado
    /// normalmente, apenas sem texto — perder o corte por falta de legenda seria
    /// pior do que entregá-lo sem.
    /// </remarks>
    public bool BurnSubtitles { get; set; }

    public DateTime CreationDate { get; set; } = DateTime.UtcNow;

    #region Foreign Keys
    public virtual List<MonitoringChannel> MonitoringChannels { get; set; } = new();
    #endregion
}
