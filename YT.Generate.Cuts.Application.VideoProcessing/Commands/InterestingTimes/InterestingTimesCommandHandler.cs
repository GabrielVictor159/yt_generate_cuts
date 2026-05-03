using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoProcessing.Helpers;
using YT.Generate.Cuts.Domain.Entities;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Application.VideoProcessing.Commands.InterestingTimes;

public class InterestingTimesCommandHandler : ICommandHandler<InterestingTimesCommand, InterestingTimesCommandResponse>
{
    private readonly ILogger<InterestingTimesCommandHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly IOllamaApiClient _ollamaClient;
    private readonly IUnitOfWork _uow;

    public InterestingTimesCommandHandler(ILogger<InterestingTimesCommandHandler> logger, IConfiguration configuration, IOllamaApiClient ollamaClient, IUnitOfWork uow)
    {
        _logger = logger;
        _configuration = configuration;
        _ollamaClient = ollamaClient;
        _uow = uow;
    }

    public async Task<InterestingTimesCommandResponse> Handle(InterestingTimesCommand command, CancellationToken ct)
    {
        _logger.LogInformation("[INÍCIO] Analisando momentos interessantes. Vídeo: {Title}", command.Video.Title);

        try
        {
            if (string.IsNullOrEmpty(command.Video.SubtitlePath) || !File.Exists(command.Video.SubtitlePath))
            {
                _logger.LogWarning("[AVISO] Arquivo de legenda não encontrado: {Path}", command.Video.SubtitlePath);
                return new InterestingTimesCommandResponse(new List<Domain.Entities.Cut>());
            }

            string videoTitle = command.Video.Title ?? "Assunto Geral";
            string rawSubtitles = await File.ReadAllTextAsync(command.Video.SubtitlePath, ct);
            string cleanedSubtitles = SubtitleOptimizer.CleanSrt(rawSubtitles);

            _logger.LogInformation("[PROCESSO] Legenda limpa. Tamanho: {Size} caracteres.", cleanedSubtitles.Length);

            int totalLimit = int.TryParse(_configuration["MaxCharactersInputOllama"], out var limit) ? limit : 32000;
            int totalTokens = int.TryParse(_configuration["NumTokens"], out var limitTokens) ? limitTokens: 64000;
            string modelName = _configuration["OLLAMA_MODEL"] ?? "llama3.1:8b";

            int reservedSpace = 1500;
            int availableSpaceForSubtitles = totalLimit - reservedSpace;

            List<Domain.Entities.Cut> allCuts = new();
            int currentIndex = 0;
            int chunkCount = 0;

            while (currentIndex < cleanedSubtitles.Length)
            {
                chunkCount++;
                int length = Math.Min(availableSpaceForSubtitles, cleanedSubtitles.Length - currentIndex);
                string subChunk = cleanedSubtitles.Substring(currentIndex, length);

                var prompts = new List<string>();

                var systemReplacements = new List<(string, string)> { ("[VIDEO_TITLE]", command.Video.Title ?? "") };
                var userReplacements = new List<(string, string)> { ("[SUB_CHUNK]", subChunk) };

                prompts.Add(PromptLanguageHelper.GetProcessedResource("SystemCutPrompt", command.Video.Language ?? "en", systemReplacements));
                prompts.Add(PromptLanguageHelper.GetProcessedResource("UserCutPrompt", command.Video.Language ?? "en", userReplacements));

                _logger.LogInformation("[Ollama] Bloco #{Chunk}: Processando {Chars} caracteres...", chunkCount, length);


                var request = new GenerateRequest
                {
                    Model = modelName,
                    System = prompts[0],
                    Prompt = prompts[1],
                    Stream = false,
                    Format = "json",
                    Options = new RequestOptions
                    {
                        NumCtx = totalTokens
                    }
                };

                string rawResponse = "";
                try
                {
                    await foreach (var response in _ollamaClient.GenerateAsync(request, ct))
                    {
                        rawResponse += response?.Response;
                    }

                    if (!string.IsNullOrWhiteSpace(rawResponse))
                    {
                        var chunkCuts = ParseAiResponse(rawResponse);

                        foreach (var c in chunkCuts)
                        {
                            if (TryParseCut(c, command.Video.Id, out var validCut, command.Video.Duration))
                            {
                                allCuts.Add(validCut);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("[Ollama] Falha no Bloco #{Chunk}: {Error}", chunkCount, ex.Message);
                }

                currentIndex += length;
            }

            if (allCuts.Any())
            {
                _logger.LogInformation("[DB] Salvando {Count} cortes validados...", allCuts.Count);
                await _uow.BeginTransactionAsync();
                foreach (var cut in allCuts)
                {
                    await _uow.Repository<Domain.Entities.Cut>().AddAsync(cut);
                }
                await _uow.CommitAsync();
                await _uow.CommitTransactionAsync();
            }
            else
            {
                _logger.LogWarning("[AVISO] Nenhum corte foi identificado pela IA em nenhum dos blocos.");
            }

            await _uow.BeginTransactionAsync();
            command.Video.Status = VideoStatusEnum.Process;
            _uow.Repository<Domain.Entities.Video>().Update(command.Video);
            await _uow.CommitAsync();
            await _uow.CommitTransactionAsync();

            return new InterestingTimesCommandResponse(allCuts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ERRO CRÍTICO] Falha no processamento: {Url}", command.Video.Url);
            throw;
        }
    }

    private bool TryParseCut(VideoCut raw, long videoId, out Domain.Entities.Cut cut, TimeSpan? videoDuration)
    {
        cut = null;
        try
        {
            var initialTime = TimeOnly.Parse(raw.Start);
            var finallyTime = TimeOnly.Parse(raw.End);

            if (videoDuration.HasValue && finallyTime.ToTimeSpan() > videoDuration.Value)
            {
                _logger.LogWarning("[FILTRO] Corte '{Name}' ignorado. Tempo final ({End}) ultrapassa a duração total do vídeo ({Duration}).",
                    raw.Name, raw.End, videoDuration.Value);
                return false;
            }

            var duration = finallyTime - initialTime;

            if (duration.TotalSeconds < 10 || duration.TotalSeconds > 90)
            {
                _logger.LogWarning("[FILTRO] Corte '{Name}' ignorado. Duração inválida: {Seconds} segundos ({Start} até {End}).",
                    raw.Name, duration.TotalSeconds, raw.Start, raw.End);
                return false;
            }

            cut = new Domain.Entities.Cut
            {
                VideoId = videoId,
                Name = raw.Name ?? "Corte Sem Nome",
                Description = raw.Reason,
                InitialTime = initialTime,
                FinallyTime = finallyTime,
                Status = CutStatusEnum.Created,
                CutPath = string.Empty
            };
            return true;
        }
        catch
        {
            _logger.LogWarning("[PARSE] Erro de formatação. Tempo inválido ignorado: {Start} - {End}", raw.Start, raw.End);
            return false;
        }
    }

    private List<VideoCut> ParseAiResponse(string json)
    {
        try
        {
            string cleanedJson = json.Trim();
            if (cleanedJson.Contains("```json"))
            {
                cleanedJson = cleanedJson.Split("```json")[1].Split("```")[0];
            }
            else if (cleanedJson.Contains("```"))
            {
                cleanedJson = cleanedJson.Split("```")[1].Split("```")[0];
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<OllamaCutsWrapper>(cleanedJson.Trim(), options);

            return result?.Cuts ?? new List<VideoCut>();
        }
        catch (Exception ex)
        {
            _logger.LogError("[ERRO JSON] A IA ignorou o formato e enviou: {RawJson}", json);
            return new List<VideoCut>();
        }
    }

    public record OllamaCutsWrapper([property: JsonPropertyName("cuts")] List<VideoCut> Cuts);
    public record VideoCut(
        [property: JsonPropertyName("init")] string Start,
        [property: JsonPropertyName("end")] string End,
        [property: JsonPropertyName("reason")] string Reason,
        [property: JsonPropertyName("name")] string Name);
}