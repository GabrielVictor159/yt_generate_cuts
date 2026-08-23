
using YT.Generate.Cuts.Domain.Enums;

namespace YT.Generate.Cuts.Domain.Entities;
public class Cut
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required TimeOnly InitialTime { get; set; }
    public required TimeOnly FinallyTime { get; set; }
    public required string CutPath { get; set; }

    /// <summary>
    /// Legenda do corte, recortada da legenda original do vídeo e com os tempos
    /// rebaseados para começar em zero.
    /// </summary>
    /// <remarks>
    /// Guardada como arquivo, ao lado do mp4 do corte, porque é isso que um
    /// editor consome: o worker de edição vai queimar essas falas no vídeo, e
    /// para isso precisa dos tempos relativos ao <b>corte</b>, não ao vídeo
    /// original. O recorte é feito na geração do corte, enquanto a legenda do
    /// vídeo ainda existe em disco — depois que todos os cortes são gerados, a
    /// etapa de limpeza apaga os arquivos do vídeo.
    /// <para><c>null</c> quando o vídeo não tinha legenda ou nenhuma fala caía
    /// dentro da janela do corte.</para>
    /// </remarks>
    public string? SubtitlePath { get; set; }

    /// <summary>Idioma da legenda do corte, herdado do vídeo.</summary>
    public string? SubtitleLanguage { get; set; }

    /// <summary>
    /// Tags do corte, separadas por vírgula: as que o modelo extraiu do trecho
    /// mais as tags padrão do canal monitorado.
    /// </summary>
    public string? Tags { get; set; }

    public DateTime CreationDate { get; set; } = DateTime.UtcNow;
    public required CutStatusEnum Status { get; set; } 
    #region Foreign Keys
    public long? VideoId { get; set; }
    public virtual Video? Video { get; set; }
    public long? PublishChannelId { get; set; }
    public virtual PublishChannel? PublishChannel { get; set; }
    #endregion
}
