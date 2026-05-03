using System.Globalization;
using YT.Generate.Cuts.Application.VideoProcessing.Resources;

namespace YT.Generate.Cuts.Application.VideoProcessing.Helpers;

public static class PromptLanguageHelper
{
    /// <summary>
    /// Método genérico que busca um recurso e aplica uma lista de substituições.
    /// </summary>
    /// <param name="resourceKey">Nome da chave no arquivo .resx</param>
    /// <param name="languageCode">Código do idioma (ex: "pt", "en")</param>
    /// <param name="replacements">Lista de Tuplas contendo (Tag_no_Resx, Valor_Real)</param>
    public static string GetProcessedResource(
        string resourceKey,
        string languageCode,
        List<(string Tag, string Value)> replacements)
    {
        var cultureInfo = GetCultureInfo(languageCode);
        var rawText = Prompts.ResourceManager.GetString(resourceKey, cultureInfo);

        if (string.IsNullOrEmpty(rawText))
        {
            throw new InvalidOperationException($"A chave de recurso '{resourceKey}' não foi encontrada para o idioma '{languageCode}'.");
        }

        if (replacements == null || replacements.Count == 0)
            return rawText;

        foreach (var (tag, value) in replacements)
        {
            rawText = rawText.Replace(tag, value ?? string.Empty);
        }

        return rawText;
    }

    private static CultureInfo GetCultureInfo(string languageCode)
    {
        var lang = languageCode?.ToLower() ?? "en";

        if (lang.Contains("pt")) return new CultureInfo("pt-BR");
        if (lang.Contains("es")) return new CultureInfo("es-ES");

        return new CultureInfo("en-US");
    }
}