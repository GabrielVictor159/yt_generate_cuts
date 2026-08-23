using System.Net.Sockets;
using YT.Generate.Cuts.Application.Abstractions.Exceptions;

namespace YT.Generate.Cuts.Application.VideoExtraction.Youtube;

/// <summary>
/// Traduz exceções do YoutubeExplode e do runtime para as exceções da abstração.
/// </summary>
public static class VideoSourceErrors
{
    public static VideoSourceException Translate(Exception exception, string reference, string? videoId = null)
    {
        if (exception is VideoSourceException already) return already;

        if (IsNetwork(exception))
            return new VideoSourceNetworkException(
                $"Sem conectividade ao acessar {reference}: {RootMessage(exception)}", exception);

        return exception switch
        {
            YoutubeExplode.Exceptions.RequestLimitExceededException =>
                new VideoSourceRateLimitException(
                    $"O YouTube recusou por excesso de requisições ({reference}).", exception),

            // VideoUnavailableException e VideoRequiresPurchaseException herdam
            // de VideoUnplayableException, então este caso cobre os três.
            YoutubeExplode.Exceptions.VideoUnplayableException =>
                new VideoUnavailableException(
                    $"O vídeo não pôde ser obtido ({reference}): {exception.Message}", videoId, exception),

            YoutubeExplode.Exceptions.PlaylistUnavailableException =>
                new VideoUnavailableException(
                    $"Playlist ou canal indisponível ({reference}): {exception.Message}", videoId, exception),

            YoutubeExplode.Exceptions.YoutubeExplodeException =>
                new VideoUnavailableException(
                    $"Falha do provedor em {reference}: {exception.Message}", videoId, exception),

            _ => new VideoUnavailableException(
                    $"Falha inesperada em {reference}: {exception.Message}", videoId, exception),
        };
    }

    /// <summary>
    /// Percorre as exceções internas: o <see cref="SocketException"/> costuma vir
    /// embrulhado em <see cref="HttpRequestException"/>.
    /// </summary>
    public static bool IsNetwork(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is SocketException or HttpRequestException or TimeoutException)
                return true;
        }

        return false;
    }

    public static string RootMessage(Exception exception)
    {
        var ex = exception;
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex.Message;
    }
}
