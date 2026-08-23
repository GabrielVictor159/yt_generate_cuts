using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace YT.Generate.Cuts.Application.VideoProcessing.Helpers;

/// <summary>Uma fala da legenda, já indexada.</summary>
/// <param name="Id">Índice sequencial usado como chave no prompt.</param>
public sealed record SubtitleCue(int Id, TimeSpan Start, TimeSpan End, string Text);

public static class SubtitleOptimizer
{
    private static readonly Regex TimestampLine = new(
        @"^\s*(?<from>\d{1,2}:\d{2}:\d{2}[,\.]\d{1,3}|\d{1,2}:\d{2}[,\.]\d{1,3})\s*-->\s*(?<to>\d{1,2}:\d{2}:\d{2}[,\.]\d{1,3}|\d{1,2}:\d{2}[,\.]\d{1,3})",
        RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex Markup = new(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>
    /// Mantido para compatibilidade. Devolve "timestamp texto" por linha.
    /// Prefira <see cref="ParseCues"/>: o prefixo de timestamp consome
    /// aproximadamente 45% do payload enviado ao modelo.
    /// </summary>
    public static string CleanSrt(string rawSrt)
    {
        var cues = ParseCues(rawSrt);
        var sb = new StringBuilder();

        foreach (var cue in cues)
            sb.AppendLine($"{Format(cue.Start)} --> {Format(cue.End)} {cue.Text}");

        return sb.ToString();

        static string Format(TimeSpan t) => t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) + ",000";
    }

    /// <summary>
    /// Converte o SRT em falas indexadas. Falas consecutivas com o mesmo texto
    /// são unidas — legendas automáticas do YouTube repetem a linha enquanto ela
    /// "rola" na tela, e isso inflaria o prompt sem agregar informação.
    /// </summary>
    public static List<SubtitleCue> ParseCues(string rawSrt)
    {
        var result = new List<SubtitleCue>();
        if (string.IsNullOrWhiteSpace(rawSrt)) return result;

        var lines = rawSrt.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        TimeSpan? start = null, end = null;
        var text = new StringBuilder();

        foreach (var line in lines)
        {
            var match = TimestampLine.Match(line);

            if (match.Success)
            {
                Flush();
                start = ParseTime(match.Groups["from"].Value);
                end = ParseTime(match.Groups["to"].Value);
                continue;
            }

            var trimmed = line.Trim();

            // A linha em branco é o separador de bloco no SRT. Fechar a fala
            // aqui é o que impede o índice do bloco seguinte de ser lido como
            // continuação do texto anterior.
            if (trimmed.Length == 0)
            {
                Flush();
                continue;
            }

            // Índice do bloco SRT: descartado, usamos nossa própria numeração.
            if (int.TryParse(trimmed, out _) && text.Length == 0)
                continue;

            text.Append(Markup.Replace(trimmed, string.Empty)).Append(' ');
        }

        Flush();
        return result;

        void Flush()
        {
            if (start is null || end is null || text.Length == 0)
            {
                text.Clear();
                return;
            }

            var clean = Whitespace.Replace(text.ToString(), " ").Trim();
            text.Clear();

            if (clean.Length == 0) return;

            // Une a fala repetida da legenda "rolante".
            var previous = result.Count > 0 ? result[^1] : null;
            if (previous is not null && string.Equals(previous.Text, clean, StringComparison.OrdinalIgnoreCase))
            {
                result[^1] = previous with { End = end.Value };
                return;
            }

            result.Add(new SubtitleCue(result.Count + 1, start.Value, end.Value, clean));
        }
    }

    /// <summary>
    /// Formata as falas para o prompt como <c>[id @segundos] texto</c>.
    /// O modelo devolve apenas os ids; o tempo em segundos existe só para ele
    /// julgar a duração do corte. Quem resolve id → tempo é o código, então um
    /// tempo alucinado deixa de ser possível.
    /// </summary>
    public static string RenderForPrompt(IEnumerable<SubtitleCue> cues)
    {
        var sb = new StringBuilder();

        foreach (var cue in cues)
            sb.AppendLine($"[{cue.Id} @{(int)cue.Start.TotalSeconds}s] {cue.Text}");

        return sb.ToString();
    }

    /// <summary>
    /// Divide as falas em janelas de tempo com sobreposição, para que um bom
    /// momento na fronteira não seja perdido. Antes o corte era por caractere,
    /// no meio da fala.
    /// </summary>
    public static List<List<SubtitleCue>> BuildWindows(
        List<SubtitleCue> cues, TimeSpan windowSize, TimeSpan overlap)
    {
        var windows = new List<List<SubtitleCue>>();
        if (cues.Count == 0) return windows;

        if (windowSize <= TimeSpan.Zero)
        {
            windows.Add(cues);
            return windows;
        }

        if (overlap < TimeSpan.Zero || overlap >= windowSize)
            overlap = TimeSpan.Zero;

        var step = windowSize - overlap;
        var last = cues[^1].End;

        for (var from = TimeSpan.Zero; from < last; from += step)
        {
            var to = from + windowSize;
            var slice = cues.Where(c => c.Start >= from && c.Start < to).ToList();

            if (slice.Count > 0)
                windows.Add(slice);

            if (to >= last) break;
        }

        return windows;
    }

    /// <summary>Aceita <c>HH:mm:ss,fff</c>, <c>HH:mm:ss.fff</c> e <c>mm:ss,fff</c>.</summary>
    private static TimeSpan ParseTime(string value)
    {
        var inv = CultureInfo.InvariantCulture;
        var parts = value.Replace(',', '.').Split(':');

        int hours = 0, minutes;
        double seconds;

        if (parts.Length == 3)
        {
            hours = int.Parse(parts[0], inv);
            minutes = int.Parse(parts[1], inv);
            seconds = double.Parse(parts[2], inv);
        }
        else
        {
            minutes = int.Parse(parts[0], inv);
            seconds = double.Parse(parts[1], inv);
        }

        return new TimeSpan(hours, minutes, 0) + TimeSpan.FromSeconds(seconds);
    }
}
