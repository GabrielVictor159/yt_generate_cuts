using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace YT.Generate.Cuts.Application.VideoProcessing.Helpers;

/// <summary>
/// Normaliza e junta listas de tags.
/// </summary>
/// <remarks>
/// As tags vêm de duas origens que não conversam: o modelo, que escreve texto
/// livre ("Guerra na Ucrânia", "#geopolitica", " ECONOMIA "), e o cadastro do
/// canal, digitado por uma pessoa. Sem normalizar, o mesmo assunto entra duas
/// vezes com grafias diferentes e a descrição da publicação fica repetitiva.
/// </remarks>
public static class TagList
{
    /// <summary>Teto de tags por corte. Acima disso, plataformas ignoram ou penalizam.</summary>
    public const int MaxTags = 15;

    private const int MaxTagLength = 40;

    private static readonly Regex Invalid = new(@"[^\p{L}\p{Nd}\s_-]", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Junta as origens preservando a ordem de entrada e descartando repetições.
    /// </summary>
    /// <remarks>
    /// A comparação de duplicidade ignora acento e caixa ("Geopolítica" ==
    /// "geopolitica"), mas o texto guardado é o da primeira ocorrência — a versão
    /// escrita corretamente costuma ser a primeira, e é ela que vai para a
    /// descrição.
    /// </remarks>
    public static List<string> Merge(params IEnumerable<string>?[] sources)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            if (source is null) continue;

            foreach (var raw in source)
            {
                var tag = Normalize(raw);
                if (tag.Length == 0) continue;

                if (!seen.Add(Fold(tag))) continue;

                result.Add(tag);

                if (result.Count >= MaxTags) return result;
            }
        }

        return result;
    }

    /// <summary>Divide uma lista separada por vírgula (ou ponto e vírgula).</summary>
    public static IEnumerable<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Enumerable.Empty<string>()
            : value.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Formato de armazenamento: separado por vírgula.</summary>
    public static string? Join(IEnumerable<string> tags)
    {
        var text = string.Join(", ", tags);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// Tira o "#", pontuação e espaços repetidos, e limita o tamanho.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var value = raw.Trim().TrimStart('#');
        value = Invalid.Replace(value, " ");
        value = Spaces.Replace(value, " ").Trim();

        if (value.Length > MaxTagLength) value = value[..MaxTagLength].Trim();

        return value;
    }

    /// <summary>Chave de comparação: sem acento, sem caixa, sem espaço.</summary>
    private static string Fold(string tag)
    {
        var decomposed = tag.Normalize(NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch) || ch is '-' or '_') continue;
            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }
}
