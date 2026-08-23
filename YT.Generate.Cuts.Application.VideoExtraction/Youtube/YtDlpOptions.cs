using Microsoft.Extensions.Configuration;

namespace YT.Generate.Cuts.Application.VideoExtraction.Youtube;

/// <summary>Configuração do provedor yt-dlp.</summary>
public sealed class YtDlpOptions
{
    public const string SectionName = "YtDlp";

    /// <summary>Caminho do executável. No container, <c>/usr/local/bin/yt-dlp</c>.</summary>
    public string ExecutablePath { get; init; } = "yt-dlp";

    /// <summary>
    /// Caminho do ffmpeg. <b>Vazio é o valor recomendado</b>: o yt-dlp procura no
    /// PATH, que é onde o ffmpeg está no container.
    /// </summary>
    /// <remarks>
    /// Preencher com o nome do executável ("ffmpeg") em vez de um caminho é o que
    /// quebrava o merge: o <c>--ffmpeg-location</c> exige um caminho de arquivo ou
    /// de diretório e, com um nome simples, o yt-dlp avisa que não existe e segue
    /// <b>sem</b> ffmpeg — baixando os dois fluxos e não produzindo o arquivo
    /// final. Por isso um valor sem separador de diretório é ignorado.
    /// </remarks>
    public string? FfmpegPath { get; init; }

    /// <summary>
    /// Seletor de formato do yt-dlp. Vazio usa o padrão do downloader, que
    /// prefere mp4+m4a para o merge ser trivial. <c>{height}</c> é substituído
    /// pela resolução máxima pedida.
    /// </summary>
    public string? FormatSelector { get; init; }

    /// <summary>Arquivo de cookies no formato Netscape, opcional.</summary>
    public string? CookiesFile { get; init; }

    /// <summary>Tentativas do próprio yt-dlp para cada fragmento.</summary>
    public int Retries { get; init; } = 3;

    /// <summary>Timeout de socket, em segundos.</summary>
    public int SocketTimeoutSeconds { get; init; } = 30;

    /// <summary>Teto de tempo para o processo inteiro, em minutos.</summary>
    public int ProcessTimeoutMinutes { get; init; } = 30;

    /// <summary>
    /// Teto de tempo para a etapa de legendas, em minutos. Separado do vídeo de
    /// propósito: legenda é acessório e não deve poder segurar o worker pelo mesmo
    /// tempo que um download de 1080p.
    /// </summary>
    public int SubtitleTimeoutMinutes { get; init; } = 5;

    /// <summary>
    /// Pausa entre requisições ao provedor, em segundos. Aceita fração (ex.: 0,5).
    /// Espalhar as requisições é o que evita o <c>HTTP 429</c> — custa alguns
    /// segundos por vídeo e poupa o ciclo inteiro do canal.
    /// </summary>
    public double SleepRequestsSeconds { get; init; } = 1;

    /// <summary>Pausa antes de cada requisição de legenda, em segundos.</summary>
    public int SleepSubtitlesSeconds { get; init; } = 2;

    /// <summary>
    /// Permite desligar a busca de legendas. Com <c>false</c>, o vídeo é baixado e
    /// nenhuma requisição de legenda é feita — útil como escape imediato se o
    /// YouTube apertar o limite desse endpoint.
    /// </summary>
    public bool DownloadSubtitles { get; init; } = true;

    /// <summary>
    /// Clientes do extrator do YouTube, em ordem. Vazio deixa o padrão do
    /// yt-dlp, que é o recomendado — mudar isto só faz sentido para contornar
    /// um bloqueio específico.
    /// </summary>
    public string? PlayerClients { get; init; }

    public static YtDlpOptions FromConfiguration(IConfiguration configuration)
    {
        string? Value(string key) => configuration[$"{SectionName}:{key}"];

        int Int(string key, int fallback) =>
            int.TryParse(Value(key), out var parsed) && parsed > 0 ? parsed : fallback;

        // Aceita zero: "não pausar" é uma escolha legítima aqui.
        double Seconds(string key, double fallback) =>
            double.TryParse(Value(key), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
                ? parsed
                : fallback;

        int NonNegative(string key, int fallback) =>
            int.TryParse(Value(key), out var parsed) && parsed >= 0 ? parsed : fallback;

        bool Bool(string key, bool fallback) =>
            bool.TryParse(Value(key), out var parsed) ? parsed : fallback;

        return new YtDlpOptions
        {
            ExecutablePath = string.IsNullOrWhiteSpace(Value(nameof(ExecutablePath))) ? "yt-dlp" : Value(nameof(ExecutablePath))!,
            // Sem valor explícito, cai na configuração compartilhada do ffmpeg;
            // sem ela também, fica nulo e o yt-dlp resolve pelo PATH.
            FfmpegPath = string.IsNullOrWhiteSpace(Value(nameof(FfmpegPath)))
                ? configuration["FFmpegSettings:BinaryPath"]
                : Value(nameof(FfmpegPath)),
            FormatSelector = Value(nameof(FormatSelector)),
            // Reaproveita o mesmo arquivo de cookies do YoutubeExplode quando
            // não houver um específico.
            CookiesFile = string.IsNullOrWhiteSpace(Value(nameof(CookiesFile)))
                ? configuration["YouTube:CookiesFile"]
                : Value(nameof(CookiesFile)),
            Retries = Int(nameof(Retries), 3),
            SocketTimeoutSeconds = Int(nameof(SocketTimeoutSeconds), 30),
            ProcessTimeoutMinutes = Int(nameof(ProcessTimeoutMinutes), 30),
            SubtitleTimeoutMinutes = Int(nameof(SubtitleTimeoutMinutes), 5),
            SleepRequestsSeconds = Seconds(nameof(SleepRequestsSeconds), 1),
            SleepSubtitlesSeconds = NonNegative(nameof(SleepSubtitlesSeconds), 2),
            DownloadSubtitles = Bool(nameof(DownloadSubtitles), true),
            PlayerClients = Value(nameof(PlayerClients)),
        };
    }
}
