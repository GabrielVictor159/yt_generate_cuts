namespace YT.Generate.Cuts.Domain.Entities;
public class MonitoringChannel
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public DateTime CreationDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Duração mínima, em segundos, dos cortes gerados para este canal.
    /// <c>null</c> usa o padrão global (<c>CutDuration:MinSeconds</c>).
    /// </summary>
    public int? MinCutSeconds { get; set; }

    /// <summary>
    /// Duração máxima, em segundos, dos cortes gerados para este canal.
    /// <c>null</c> usa o padrão global (<c>CutDuration:MaxSeconds</c>).
    /// </summary>
    public int? MaxCutSeconds { get; set; }

    /// <summary>
    /// Teto de cortes deste canal ainda não publicados. <c>null</c> usa o padrão
    /// global (<c>Cuts:MaxPendingPerChannel</c>); zero significa sem limite.
    /// </summary>
    /// <remarks>
    /// Existe porque a publicação é o único ponto do fluxo que consome cortes, e
    /// ela só acontece para cortes com canal de publicação definido. Sem canal
    /// definido, os cortes ficam parados em Process para sempre — enquanto o
    /// monitoramento segue baixando vídeos e gerando cortes novos, porque os
    /// vídeos avançam até Removed e liberam as vagas do teto por etapa. O
    /// resultado é acúmulo indefinido de cortes e de arquivos em disco. Este teto
    /// faz o fluxo <b>parar</b> nesse caso, em vez de crescer sem fim.
    /// </remarks>
    public int? MaxPendingCuts { get; set; }

    /// <summary>
    /// Tags padrão do canal, separadas por vírgula. Entram em todo corte deste
    /// canal, somadas às que o modelo extrair do vídeo.
    /// </summary>
    /// <remarks>
    /// Uma coluna de texto, e não uma tabela de tags: aqui a tag é um rótulo que
    /// vai para a descrição da publicação, não uma entidade com identidade própria
    /// — não há nada a consultar "por tag" no fluxo. Uma tabela adicionaria join e
    /// migration sem responder a nenhuma pergunta que o sistema faça.
    /// </remarks>
    public string? DefaultTags { get; set; }

    #region Foreign Keys

    /// <summary>
    /// Perfil de edição aplicado aos cortes deste canal. <c>null</c> usa o padrão
    /// global (seção <c>Edition</c>).
    /// </summary>
    public long? EditionConfigurationId { get; set; }
    public virtual EditionConfiguration? EditionConfiguration { get; set; }

    public virtual List<Video> Videos { get; set; } = new();
    public virtual List<Monitoring> Monitorings { get; set; } = new();
    #endregion
}
