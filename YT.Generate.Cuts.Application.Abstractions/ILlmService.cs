namespace YT.Generate.Cuts.Application.Abstractions;

/// <summary>
/// Define a estrutura de dados necessária para que o LLM processe um vídeo.
/// </summary>
public record LlmContext
{
    public string VideoTitle { get; set; } = "";
    public string Content { get; set; } = ""; // Texto da legenda processado
    public string Language { get; set; } = "en";
}

/// <summary>
/// Representa um segmento de vídeo identificado pela IA como interessante.
/// </summary>
public record SuggestedCut
{
    public string Name { get; set; } = "";
    public string Reason { get; set; } = "";
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
}

/// <summary>
/// Abstrai a comunicação com o provedor de LLM.
/// Implementações podem usar Ollama, local via ONNX (DirectML), ou APIs externas.
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// Analisa o contexto fornecido e retorna uma lista de sugestões de corte estruturadas.
    /// </summary>
    /// <param name="context">Os dados do vídeo e transcrição para análise.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma coleção de cortes validados para o sistema de produção.</returns>
    Task<IEnumerable<SuggestedCut>> AnalyzeCutsAsync(LlmContext context, CancellationToken ct);
}
