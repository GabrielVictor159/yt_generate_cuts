using System.Globalization;
using System.Text;

namespace YT.Generate.Cuts.Application.VideoProcessing.Helpers;

/// <summary>
/// Recorta a legenda do vídeo na janela de um corte, rebaseando os tempos para
/// começar em zero.
/// </summary>
/// <remarks>
/// O rebaseamento é o ponto todo. A legenda original marca "aos 5min12s do
/// vídeo"; o corte que começa em 5min10s dura 30 segundos e, para um editor,
/// essa mesma fala está aos 2 segundos. Guardar a legenda com os tempos
/// originais obrigaria quem for inserir as legendas a refazer a conta — e a
/// conhecer o instante em que o corte começou, informação que não viaja junto do
/// arquivo.
/// <para>
/// Falas que cruzam a borda são aparadas, não descartadas: uma frase que começa
/// antes do corte e continua dentro dele aparece no vídeo e precisa aparecer na
/// legenda.
/// </para>
/// </remarks>
public static class SubtitleSlicer
{
    /// <summary>
    /// Duração mínima de uma fala aparada. Abaixo disso ela pisca na tela sem dar
    /// tempo de ler, então é descartada.
    /// </summary>
    private static readonly TimeSpan MinimumCueDuration = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Recorta as falas contidas na janela <paramref name="start"/> a
    /// <paramref name="end"/>.
    /// </summary>
    public static List<SubtitleCue> Slice(IEnumerable<SubtitleCue> cues, TimeSpan start, TimeSpan end)
    {
        var result = new List<SubtitleCue>();

        if (end <= start) return result;

        var index = 1;

        foreach (var cue in cues.OrderBy(c => c.Start))
        {
            // Interseção com a janela. Sem sobreposição, a fala não pertence ao
            // corte.
            var from = cue.Start > start ? cue.Start : start;
            var to = cue.End < end ? cue.End : end;

            if (to <= from) continue;
            if (to - from < MinimumCueDuration) continue;
            if (string.IsNullOrWhiteSpace(cue.Text)) continue;

            result.Add(new SubtitleCue(index++, from - start, to - start, cue.Text));
        }

        return result;
    }

    /// <summary>Escreve as falas no formato SRT.</summary>
    public static string Render(IEnumerable<SubtitleCue> cues)
    {
        var sb = new StringBuilder();

        foreach (var cue in cues)
        {
            sb.Append(cue.Id).Append('\n');
            sb.Append(Format(cue.Start)).Append(" --> ").Append(Format(cue.End)).Append('\n');
            sb.Append(cue.Text).Append('\n').Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Recorta o arquivo de legenda do vídeo e grava o resultado.
    /// </summary>
    /// <returns>
    /// O caminho gravado, ou <c>null</c> quando não há legenda de origem ou
    /// nenhuma fala cai dentro da janela — corte sem legenda é um resultado
    /// legítimo, não um erro.
    /// </returns>
    public static string? SliceFile(string? sourcePath, TimeSpan start, TimeSpan end, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return null;

        var cues = Slice(SubtitleOptimizer.ParseCues(File.ReadAllText(sourcePath)), start, end);

        if (cues.Count == 0) return null;

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(targetPath, Render(cues), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return targetPath;
    }

    /// <summary>SRT usa vírgula como separador de milissegundos.</summary>
    private static string Format(TimeSpan value) =>
        value.ToString(@"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);
}
