namespace YT.Generate.Cuts.Domain.Enums;

public enum CutStatusEnum
{
    /// <summary>Identificado pela IA; o arquivo do corte ainda não existe.</summary>
    Created = 1,

    /// <summary>O arquivo do corte foi gerado a partir do vídeo original.</summary>
    Process = 2,

    /// <summary>Publicado na plataforma. Estado terminal.</summary>
    Publish = 3,

    /// <summary>
    /// Editado conforme a configuração de edição do canal (resolução e legenda
    /// embutida). É a etapa entre <see cref="Process"/> e <see cref="Publish"/>.
    /// </summary>
    /// <remarks>
    /// O valor 4 vem depois de <see cref="Publish"/> por um motivo prático: a
    /// coluna guarda o inteiro, e renumerar os status existentes reinterpretaria
    /// todos os cortes já gravados no banco. A ordem da enumeração não é a ordem
    /// do fluxo — quem define o fluxo é <see cref="CutStatus"/>.
    /// </remarks>
    Edit = 4,
}
