namespace YT.Generate.Cuts.Domain.Enums;

public enum VideoStatusEnum
{
    Created = 1,
    Download = 2,
    Process = 3,
    Removed = 4,

    /// <summary>
    /// O vídeo foi identificado no canal mas o download falhou (indisponível,
    /// bloqueado por região, limite de requisições do YouTube etc.).
    /// A coluna é um integer, então incluir este valor não exige migration.
    /// </summary>
    Failed = 5,
}
