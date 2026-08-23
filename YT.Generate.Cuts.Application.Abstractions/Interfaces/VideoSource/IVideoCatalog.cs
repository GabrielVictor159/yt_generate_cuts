namespace YT.Generate.Cuts.Application.Abstractions.Interfaces.VideoSource;

/// <summary>Canal resolvido no provedor.</summary>
public sealed record CatalogChannel(string Id, string Title);

/// <summary>Vídeo listado no canal, antes de qualquer download.</summary>
public sealed record CatalogVideo(string Id, string Title, string Url, TimeSpan? Duration);

/// <summary>
/// Leitura do catálogo: resolver um canal e listar os uploads recentes.
/// </summary>
/// <remarks>
/// A listagem é <b>limitada</b> de propósito, em vez de um stream infinito.
/// O monitoramento roda de minuto em minuto, então nunca é preciso paginar o
/// histórico inteiro — e um limite mantém o volume de requisições ao provedor
/// previsível, o que já foi causa de bloqueio por excesso de chamadas.
/// Uma lista finita também permite trocar de provedor em caso de falha, coisa
/// que não é possível no meio de um stream já iniciado.
/// </remarks>
public interface IVideoCatalog
{
    /// <summary>Nome do provedor, para log e diagnóstico.</summary>
    string ProviderName { get; }

    /// <summary>Resolve o canal a partir de handle, ID ou URL.</summary>
    Task<CatalogChannel> ResolveChannelAsync(string channelReference, CancellationToken ct);

    /// <summary>Uploads mais recentes do canal, do mais novo para o mais antigo.</summary>
    Task<IReadOnlyList<CatalogVideo>> GetRecentUploadsAsync(string channelId, int maxResults, CancellationToken ct);
}
