namespace YT.Generate.Cuts.Application.Abstractions.Exceptions;

/// <summary>
/// Base das falhas de um provedor de vídeo.
/// </summary>
/// <remarks>
/// Estas exceções existem para que os handlers não precisem conhecer os tipos
/// do YoutubeExplode nem interpretar a saída do yt-dlp. Cada provedor traduz o
/// que aconteceu para uma destas, e a regra de negócio decide o que fazer a
/// partir do <i>significado</i> — não da biblioteca que falhou.
/// </remarks>
public abstract class VideoSourceException : Exception
{
    protected VideoSourceException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// O vídeo não pode ser obtido e isso não vai mudar sozinho: removido, privado,
/// exclusivo para membros, pago ou bloqueado por região.
/// </summary>
public sealed class VideoUnavailableException : VideoSourceException
{
    public string? VideoId { get; }

    public VideoUnavailableException(string message, string? videoId = null, Exception? inner = null)
        : base(message, inner) => VideoId = videoId;
}

/// <summary>
/// O provedor recusou por excesso de requisições ou verificação anti-bot.
/// Tende a resolver esperando, então quem chama deve recuar em vez de insistir.
/// </summary>
public sealed class VideoSourceRateLimitException : VideoSourceException
{
    public VideoSourceRateLimitException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Falha de infraestrutura: sem rota, DNS, TLS ou timeout. Não é defeito do
/// vídeo, então nada deve ser marcado como definitivamente falho por causa dela.
/// </summary>
public sealed class VideoSourceNetworkException : VideoSourceException
{
    public VideoSourceNetworkException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// O provedor está indisponível no ambiente — executável ausente, sem permissão
/// de execução, versão incompatível.
/// </summary>
public sealed class VideoSourceUnavailableException : VideoSourceException
{
    public VideoSourceUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
