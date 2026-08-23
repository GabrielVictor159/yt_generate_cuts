namespace YT.Generate.Cuts.Application.VideoProcessing.Helpers;

/// <summary>
/// Isola o objeto JSON dentro da resposta de um LLM.
/// </summary>
/// <remarks>
/// O Ollama entrega o raciocínio de modelos com thinking num campo separado
/// (<c>thinking</c>), então normalmente o conteúdo já vem limpo. Mas quando o
/// template de thinking do modelo não é reconhecido, o bloco
/// <c>&lt;think&gt;…&lt;/think&gt;</c> vaza para o conteúdo — e aí a resposta
/// inteira seria descartada. Esta classe existe para que esse caso degrade em
/// vez de perder a janela.
/// </remarks>
public static class LlmResponseSanitizer
{
    private const string ThinkClose = "</think>";
    private const string ThinkOpen = "<think>";

    /// <summary>
    /// Devolve apenas o trecho entre a primeira <c>{</c> e a última <c>}</c>,
    /// descartando bloco de raciocínio, cerca markdown e texto introdutório.
    /// </summary>
    public static string ExtractJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var text = raw;

        // Bloco de raciocínio fechado: tudo até o fechamento é descartado.
        var thinkEnd = text.LastIndexOf(ThinkClose, StringComparison.OrdinalIgnoreCase);
        if (thinkEnd >= 0)
            text = text[(thinkEnd + ThinkClose.Length)..];

        // Bloco aberto e nunca fechado (geração truncada): não há JSON confiável
        // depois dele se não houver chave de abertura à frente.
        var thinkStart = text.IndexOf(ThinkOpen, StringComparison.OrdinalIgnoreCase);
        if (thinkStart >= 0)
        {
            var braceAfter = text.IndexOf('{', thinkStart);
            text = braceAfter >= 0 ? text[braceAfter..] : string.Empty;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        return start >= 0 && end > start ? text[start..(end + 1)] : text.Trim();
    }
}
