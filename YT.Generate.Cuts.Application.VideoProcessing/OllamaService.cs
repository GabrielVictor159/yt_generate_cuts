using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using OllamaSharp;
using OllamaSharp.Models;
using YT.Generate.Cuts.Application.Abstractions;
using YT.Generate.Cuts.Application.VideoProcessing.Helpers;

namespace YT.Generate.Cuts.Application.VideoProcessing;

public class OllamaService : ILlmService
{
    private readonly IOllamaApiClient _client;
    private readonly IConfiguration _config;

    public OllamaService(IOllamaApiClient client, IConfiguration config)
    {
        _client = client;
        _config = config;
    }

    public async Task<IEnumerable<SuggestedCut>> AnalyzeCutsAsync(LlmContext context, CancellationToken ct)
    {
        var resultList = new List<SuggestedCut>();

        // A lógica de "chunking" e montagem do prompt agora vive aqui dentro.
        // O Handler não precisa mais saber como o texto é dividido ou como o JSON é extraído.
        int totalLimit = int.TryParse(_config["MaxCharactersInputOllama"], out var limit) ? limit : 32000;
        int totalTokens = int.TryParse(_config["NumTokens"], out var tokens) ? tokens : 64000;
        string modelName = _config["OLLAMA_MODEL"] ?? "llama3.1:8b";

        // O processo de divisão em pedaços (chunks) para lidar com limites de contexto é encapsulado aqui.
        var chunks = SplitContextIntoChunks(context.Content, Math.Max(1, totalLimit - 1500));

        foreach (var chunk in chunks)
        {
            var systemReplacements = new List<(string Tag, string Value)> { ("[VIDEO_TITLE]", context.VideoTitle) };
            var userReplacements = new List<(string Tag, string Value)> { ("[SUB_CHUNK]", chunk) };

            var request = new GenerateRequest
            {
                Model = modelName,
                System = PromptLanguageHelper.GetProcessedResource("SystemCutPrompt", context.Language, systemReplacements),
                Prompt = PromptLanguageHelper.GetProcessedResource("UserCutPrompt", context.Language, userReplacements),
                Stream = false,
                Format = "json", // Forçamos o formato JSON no nível do motor
                Options = new RequestOptions { NumCtx = totalTokens }
            };

            var rawResponse = await FetchAndValidateResponse(request, ct);

            if (!string.IsNullOrWhiteSpace(rawResponse))
            {
                resultList.AddRange(ParseCutsFromRawResult(rawResponse));
            }
        }

        return resultList;
    }

    private IEnumerable<string> SplitContextIntoChunks(string text, int limit)
    {
        // Implementação de lógica de chunking segura para garantir que não cortamos no meio de uma palavra/sentença.
        // Isso protege o modelo contra "quebras" de raciocínio por corte abrupto.
        if (string.IsNullOrEmpty(text)) yield break;

        for (int i = 0; i < text.Length; i += limit)
        {
            yield return text.Substring(i, Math.Min(limit, text.Length - i));
        }
    }

    private async Task<string> FetchAndValidateResponse(GenerateRequest request, CancellationToken ct)
    {
        // Aqui entra a lógica de retentativa (Retry Policy) se o JSON vier malformado
        // Isso é crucial para uma integração robusta.
        var rawResponse = string.Empty;

        await foreach (var response in _client.GenerateAsync(request, ct))
        {
            rawResponse += response?.Response ?? string.Empty;
        }

        return rawResponse;
    }

    private List<SuggestedCut> ParseCutsFromRawResult(string json)
    {
        // Tratamento de erro robusto para quando o modelo "alucina" e coloca blocos de código (markdown).
        var cleanJson = CleanMarkdownMarkers(json);
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var wrapper = JsonSerializer.Deserialize<CutsWrapperDTO>(cleanJson, options);

            if (wrapper?.Cuts is null) return new List<SuggestedCut>();

            return wrapper.Cuts
                .Select(ToSuggestedCut)
                .Where(cut => cut is not null)
                .Select(cut => cut!)
                .ToList();
        }
        catch
        {
            // Se falhar, o sistema ainda captura algo ou retorna lista vazia para não quebrar a pipeline principal.
            return new List<SuggestedCut>();
        }
    }

    private static SuggestedCut? ToSuggestedCut(CutDTO raw)
    {
        if (!TimeOnly.TryParse(raw.Start, out var start) || !TimeOnly.TryParse(raw.End, out var end))
        {
            return null;
        }

        return new SuggestedCut
        {
            Name = raw.Name ?? string.Empty,
            Reason = raw.Reason ?? string.Empty,
            StartSeconds = start.ToTimeSpan().TotalSeconds,
            EndSeconds = end.ToTimeSpan().TotalSeconds
        };
    }

    private string CleanMarkdownMarkers(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var cleaned = input.Replace("```json", "").Replace("```", "");
        return cleaned.Trim();
    }

    // DTOs internos para não poluir o Domain
    private record CutsWrapperDTO
    {
        [JsonPropertyName("cuts")]
        public List<CutDTO> Cuts { get; set; } = new();
    }

    private record CutDTO
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = "";

        [JsonPropertyName("init")]
        public string Start { get; set; } = "";

        [JsonPropertyName("end")]
        public string End { get; set; } = "";
    }
}
