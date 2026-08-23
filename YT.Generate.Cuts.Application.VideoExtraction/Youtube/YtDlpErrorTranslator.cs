using YT.Generate.Cuts.Application.Abstractions.Exceptions;

namespace YT.Generate.Cuts.Application.VideoExtraction.Youtube;

/// <summary>
/// Traduz a saída de erro do yt-dlp nas exceções da abstração.
/// </summary>
/// <remarks>
/// O yt-dlp devolve exit code 1 para praticamente tudo, então o significado só
/// está no texto do stderr. Concentrar essa leitura aqui mantém a regra de
/// negócio livre de <c>Contains("...")</c> espalhado.
/// </remarks>
public static class YtDlpErrorTranslator
{
    private static readonly string[] Unavailable =
    {
        "video unavailable",
        "is not available",
        "private video",
        "members-only",
        "members only",
        "this video is available to this channel's members",
        "removed by the uploader",
        "account associated with this video has been terminated",
        "video has been removed",
        "who has blocked it in your country",
        "not available in your country",
        "requires payment",
        "join this channel",
        "age-restricted",
        "confirm your age",
        "no video formats found",
        "requested format is not available",
    };

    private static readonly string[] RateLimited =
    {
        "http error 429",
        "too many requests",
        "sign in to confirm you're not a bot",
        "sign in to confirm your age",
        "confirm you’re not a bot",
        "please sign in",
        "rate-limit",
        "rate limit",
        "throttled",

        // Observado no yt-dlp real: é o YouTube recusando o endpoint de player,
        // e a condição é global (vale para todos os vídeos), não do vídeo em si.
        // Classificar como indisponibilidade faria o worker varrer o canal
        // marcando tudo como Failed; como limite, ele recua e tenta depois.
        "failed to extract any player response",
        "unable to extract player response",
        "the following content is not available on this app",
    };

    private static readonly string[] Network =
    {
        "network is unreachable",
        "temporary failure in name resolution",
        "name or service not known",
        "failed to resolve",
        "connection refused",
        "connection reset",
        "connection timed out",
        "timed out",
        "no route to host",
        "ssl:",
        "certificate verify failed",
        "urlopen error",
        "unable to download webpage",
    };

    /// <summary>
    /// Escolhe a exceção que corresponde ao erro. A ordem importa: bloqueio por
    /// bot e limite de requisições vêm antes de indisponibilidade, porque a
    /// mensagem costuma conter as duas coisas e o tratamento é diferente —
    /// esperar em vez de desistir do vídeo.
    /// </summary>
    public static VideoSourceException Translate(string? stdErr, int exitCode, string videoUrl, string? videoId = null)
    {
        var text = (stdErr ?? string.Empty).ToLowerInvariant();
        var detail = Summarize(stdErr) ?? $"yt-dlp terminou com código {exitCode}";

        if (Matches(text, RateLimited))
            return new VideoSourceRateLimitException($"yt-dlp: {detail}");

        if (Matches(text, Network))
            return new VideoSourceNetworkException($"yt-dlp: {detail}");

        if (Matches(text, Unavailable))
            return new VideoUnavailableException($"yt-dlp: {detail}", videoId);

        // Sem correspondência, tratamos como indisponibilidade do vídeo: é o
        // caso mais comum e o handler segue para o próximo em vez de travar.
        return new VideoUnavailableException(
            $"yt-dlp falhou para {videoUrl} (código {exitCode}): {detail}", videoId);
    }

    private static bool Matches(string text, string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.Ordinal));

    /// <summary>Primeira linha de erro relevante, para a mensagem não virar um despejo.</summary>
    private static string? Summarize(string? stdErr)
    {
        if (string.IsNullOrWhiteSpace(stdErr)) return null;

        var line = stdErr
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            ?? stdErr.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

        if (line is null) return null;

        return line.Length > 400 ? line[..400] + "…" : line;
    }
}
