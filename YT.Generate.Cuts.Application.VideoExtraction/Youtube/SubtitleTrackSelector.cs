namespace YT.Generate.Cuts.Application.VideoExtraction.Youtube;

/// <summary>Faixa de legenda escolhida para download.</summary>
/// <param name="Tag">Tag exata como o provedor a publica (ex.: <c>pt-BR</c>).</param>
/// <param name="IsAutomatic">
/// Verdadeiro quando a faixa vem da transcrição automática — muda a flag que o
/// yt-dlp precisa receber (<c>--write-auto-subs</c> em vez de <c>--write-subs</c>).
/// </param>
/// <param name="Language">Idioma normalizado (só a parte antes do "-").</param>
public sealed record SubtitleTrack(string Tag, bool IsAutomatic, string Language);

/// <summary>
/// Escolhe UMA faixa de legenda entre as disponíveis.
/// </summary>
/// <remarks>
/// Esta classe existe por causa de um erro concreto: o código anterior delegava a
/// escolha ao yt-dlp com <c>--sub-langs "pt.*,en.*"</c> mais
/// <c>--write-subs --write-auto-subs</c>. Esse padrão é uma expressão regular, não
/// uma preferência: casava <c>pt</c>, <c>pt-BR</c>, <c>pt-PT</c>, <c>pt-orig</c>,
/// <c>en</c>, <c>en-US</c>, <c>en-orig</c>… e baixava TODAS, nas duas variantes.
/// Uma dezena de requisições em rajada ao endpoint de legendas por vídeo — que é
/// exatamente o que produzia o <c>HTTP Error 429</c>.
/// <para>
/// Decidindo aqui, em código testável, sobra uma única requisição de legenda por
/// vídeo, e a regra de preferência fica explícita em vez de escondida na semântica
/// de regex de um argumento de linha de comando.
/// </para>
/// </remarks>
public static class SubtitleTrackSelector
{
    /// <summary>Faixas que nunca servem como legenda do vídeo.</summary>
    private static readonly string[] Ignored = { "live_chat", "rechat" };

    /// <summary>
    /// Ordem de preferência, para cada idioma pedido:
    /// <list type="number">
    /// <item>legenda humana com a tag exata (<c>pt</c>);</item>
    /// <item>legenda humana de variante regional (<c>pt-BR</c>, <c>pt-PT</c>);</item>
    /// <item>transcrição automática com a tag exata;</item>
    /// <item>transcrição automática de variante regional (inclui <c>pt-orig</c>).</item>
    /// </list>
    /// Legenda humana de outro idioma vem antes da automática do mesmo idioma
    /// porque a automática erra nomes próprios e pontuação — e é justamente a
    /// pontuação que delimita as frases usadas para escolher os cortes.
    /// <para>
    /// Não há queda para um idioma fora da lista pedida: uma legenda em coreano
    /// alimentaria o modelo com um texto que ele não deve interpretar como sendo o
    /// do vídeo. Sem faixa compatível, o vídeo segue sem legenda.
    /// </para>
    /// </summary>
    public static SubtitleTrack? Choose(
        IEnumerable<string>? manualTags,
        IEnumerable<string>? automaticTags,
        IReadOnlyList<string> preferredLanguages)
    {
        var manual = Clean(manualTags);
        var automatic = Clean(automaticTags);

        var languages = preferredLanguages is { Count: > 0 }
            ? preferredLanguages
            : new[] { "pt", "en" };

        foreach (var language in languages)
        {
            var normalized = Normalize(language);
            if (normalized.Length == 0) continue;

            var track =
                Exact(manual, normalized, isAutomatic: false) ??
                Variant(manual, normalized, isAutomatic: false) ??
                Exact(automatic, normalized, isAutomatic: true) ??
                Variant(automatic, normalized, isAutomatic: true);

            if (track is not null) return track;
        }

        return null;
    }

    private static SubtitleTrack? Exact(List<string> tags, string language, bool isAutomatic)
    {
        var match = tags.FirstOrDefault(t => string.Equals(t, language, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : new SubtitleTrack(match, isAutomatic, language);
    }

    /// <summary>
    /// Variante regional. A ordenação é alfabética apenas para o resultado ser
    /// determinístico — o dicionário do yt-dlp não garante ordem, e um teste que
    /// depende dela quebraria sem motivo.
    /// </summary>
    private static SubtitleTrack? Variant(List<string> tags, string language, bool isAutomatic)
    {
        var match = tags
            .Where(t => t.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return match is null ? null : new SubtitleTrack(match, isAutomatic, language);
    }

    private static List<string> Clean(IEnumerable<string>? tags) =>
        (tags ?? Enumerable.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Where(t => !Ignored.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToList();

    /// <summary>"pt-BR" pedido na configuração vale como "pt".</summary>
    private static string Normalize(string language)
    {
        var value = (language ?? string.Empty).Trim();
        var dash = value.IndexOf('-');
        return dash > 0 ? value[..dash] : value;
    }
}
