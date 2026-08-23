using System.Globalization;

namespace YT.Generate.Cuts.Application.VideoEdition.Helpers;

/// <summary>Resolução e legenda que a edição deve aplicar.</summary>
public sealed record EditionSettings(int Width, int Height, bool BurnSubtitles, string Preset, int Crf);

/// <summary>
/// Monta o filtro e os argumentos do ffmpeg para a edição de um corte.
/// </summary>
public static class EditionArguments
{
    /// <summary>
    /// Cadeia de filtros: ajusta o vídeo à resolução pedida e, se houver legenda,
    /// queima o texto por cima.
    /// </summary>
    /// <remarks>
    /// Duas decisões que não são estéticas:
    /// <list type="bullet">
    /// <item>
    /// <c>scale</c> com <c>force_original_aspect_ratio=decrease</c> seguido de
    /// <c>pad</c>: o corte vem de um vídeo 16:9 e o destino costuma ser vertical
    /// 9:16. Escalar direto para a resolução final distorceria a imagem; assim ela
    /// cabe inteira e o resto vira barra preta.
    /// </item>
    /// <item>
    /// A legenda entra <b>depois</b> do scale. Queimada antes, o texto seria
    /// redimensionado junto com a imagem e chegaria borrado (ou minúsculo, no caso
    /// de reduzir). Depois, é desenhado já na resolução de saída.
    /// </item>
    /// </list>
    /// <c>setsar=1</c> fecha a conta: sem ele, um vídeo com pixel não quadrado
    /// mantém o SAR de origem e o player exibe a saída esticada apesar das
    /// dimensões corretas.
    /// </remarks>
    public static string BuildFilter(int width, int height, string? subtitlePath)
    {
        var filter =
            $"scale={width}:{height}:force_original_aspect_ratio=decrease," +
            $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black," +
            "setsar=1";

        if (!string.IsNullOrWhiteSpace(subtitlePath))
            filter += $",subtitles=filename='{EscapeForFilter(subtitlePath!)}'";

        return filter;
    }

    /// <summary>
    /// Escapa o caminho para dentro de um filtergraph.
    /// </summary>
    /// <remarks>
    /// O filtergraph do ffmpeg tem dois níveis de interpretação: a barra invertida
    /// escapa dentro do argumento, e os dois-pontos separam argumentos. Num
    /// caminho do Windows (<c>C:\cortes\a.srt</c>) os dois aparecem, e sem escapar
    /// o ffmpeg entende "opção desconhecida" em vez de nome de arquivo. Os nomes
    /// que geramos são seguros, mas o caminho é configurável.
    /// </remarks>
    public static string EscapeForFilter(string path) =>
        path.Replace("\\", "\\\\")
            .Replace(":", "\\:")
            .Replace("'", "\\'")
            .Replace("[", "\\[")
            .Replace("]", "\\]")
            .Replace(",", "\\,");

    /// <summary>Argumentos completos da edição.</summary>
    public static IReadOnlyList<string> Build(
        string inputPath, string outputPath, string? subtitlePath, EditionSettings settings)
    {
        return new List<string>
        {
            "-hide_banner",
            "-nostdin",
            "-loglevel", "error",
            "-y",
            "-i", inputPath,
            "-vf", BuildFilter(settings.Width, settings.Height, subtitlePath),
            "-c:v", "libx264",
            "-preset", settings.Preset,
            "-crf", settings.Crf.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-b:a", "128k",
            // Índice no começo do arquivo: as plataformas de publicação leem em
            // streaming.
            "-movflags", "+faststart",
            outputPath,
        };
    }
}
