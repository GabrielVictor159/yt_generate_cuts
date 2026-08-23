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
    // Padrões usados quando nem o canal nem a configuração definem o valor.
    private const int DefaultMinCutSeconds = 15;
    private const int DefaultMaxCutSeconds = 60;
    private const int DefaultWindowMinutes = 12;
    private const int DefaultOverlapSeconds = 60;
    private const int DefaultMaxCutsPerWindow = 3;
    private const int DefaultMaxCutsPerVideo = 10;

    /// <summary>
    /// Teto padrão de cortes não publicados por canal monitorado. Zero seria "sem
    /// limite", e é justamente o que deixava o fluxo crescer sem fim.
    /// </summary>
    private const int DefaultMaxPendingCutsPerChannel = 20;
    private const int DefaultNumCtx = 16384;
    private const int DefaultNumPredict = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Esquema entregue ao Ollama no campo <c>format</c>. Restringir a geração à
    /// forma exata é mais confiável do que pedir JSON no texto do prompt.
    /// </summary>
    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            cuts = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        start_id = new { type = "integer" },
                        end_id = new { type = "integer" },
                        score = new { type = "number" },
                        reason = new { type = "string" },
                        name = new { type = "string" },

                        // Extraídas na MESMA chamada que escolhe o corte: o
                        // modelo já leu o trecho, então as tags não custam uma
                        // inferência a mais.
                        tags = new
                        {
                            type = "array",
                            items = new { type = "string" }
                        }
                    },
                    required = new[] { "start_id", "end_id", "score", "reason", "name", "tags" }
                }
            }
        },
        required = new[] { "cuts" }
    };

    private readonly ILogger<InterestingTimesCommandHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly IOllamaApiClient _ollamaClient;
    private readonly IUnitOfWork _uow;

    public InterestingTimesCommandHandler(
        ILogger<InterestingTimesCommandHandler> logger,
        IConfiguration configuration,
        IOllamaApiClient ollamaClient,
        IUnitOfWork uow)
    {
        _logger = logger;
        _configuration = configuration;
        _ollamaClient = ollamaClient;
        _uow = uow;
    }

    public async Task<InterestingTimesCommandResponse> Handle(InterestingTimesCommand command, CancellationToken ct)
    {
        var video = command.Video;
        _logger.LogInformation("[INÍCIO] Analisando momentos interessantes. Vídeo: {Title}", video.Title);

        // Os três casos abaixo são definitivos para ESTE vídeo, então ele precisa
        // sair da etapa de Download. Antes eles apenas retornavam, e o vídeo
        // ficava sendo reavaliado a cada minuto para sempre — ocupando também uma
        // vaga do teto por etapa, que nunca era liberada. Vídeo sem legenda ficou
        // comum depois que a busca de legenda passou a ser melhor esforço.
        if (string.IsNullOrEmpty(video.SubtitlePath) || !File.Exists(video.SubtitlePath))
        {
            _logger.LogWarning(
                "[AVISO] Arquivo de legenda não encontrado ({Path}). Sem legenda não há como identificar cortes; " +
                "o vídeo avança de etapa para não travar a fila.", video.SubtitlePath);

            await PersistAsync(video, new List<Cut>(), ct);
            return new InterestingTimesCommandResponse(new List<Cut>());
        }

        // TimeOnly (usado por Cut) dá a volta em 24h; acima disso os tempos
        // gravados sairiam errados, então é melhor não processar.
        if (video.Duration.HasValue && video.Duration.Value >= TimeSpan.FromHours(24))
        {
            _logger.LogWarning("[AVISO] Vídeo com {Duration} não é suportado (limite de 24h). Avançando de etapa.", video.Duration);
            await PersistAsync(video, new List<Cut>(), ct);
            return new InterestingTimesCommandResponse(new List<Cut>());
        }

        // Teto por canal, verificado ANTES de qualquer inferência: se não há vaga,
        // rodar o modelo seria gastar GPU para produzir cortes que não caberiam.
        // O vídeo fica em Download e é reavaliado quando abrir vaga.
        var room = await ResolveRoomForNewCutsAsync(video, ct);

        if (room <= 0)
        {
            // Aqui, ao contrário dos casos acima, o vídeo NÃO avança: a espera é
            // temporária e ele deve ser reavaliado quando abrir vaga no canal.
            return new InterestingTimesCommandResponse(new List<Cut>());
        }

        var rawSubtitles = await File.ReadAllTextAsync(video.SubtitlePath, ct);
        var cues = SubtitleOptimizer.ParseCues(rawSubtitles);

        if (cues.Count == 0)
        {
            _logger.LogWarning("[AVISO] A legenda de '{Title}' não produziu nenhuma fala utilizável. Avançando de etapa.", video.Title);
            await PersistAsync(video, new List<Cut>(), ct);
            return new InterestingTimesCommandResponse(new List<Cut>());
        }

        var byId = cues.ToDictionary(c => c.Id);
        var (minSeconds, maxSeconds) = ResolveCutDuration(video);
        var language = video.Language ?? "en";

        var windowSize = TimeSpan.FromMinutes(ReadInt("InterestingTimes:WindowMinutes", DefaultWindowMinutes));
        var overlap = TimeSpan.FromSeconds(ReadInt("InterestingTimes:OverlapSeconds", DefaultOverlapSeconds));
        var maxCutsPerWindow = ReadInt("InterestingTimes:MaxCutsPerWindow", DefaultMaxCutsPerWindow);
        var maxCutsPerVideo = ReadInt("InterestingTimes:MaxCutsPerVideo", DefaultMaxCutsPerVideo);

        var windows = SubtitleOptimizer.BuildWindows(cues, windowSize, overlap);

        _logger.LogInformation(
            "[PROCESSO] {Cues} falas em {Windows} janela(s) de {Window} com {Overlap} de sobreposição. " +
            "Duração do corte: {Min}-{Max}s (canal '{Channel}').",
            cues.Count, windows.Count, windowSize, overlap, minSeconds, maxSeconds,
            video.Channel?.Name ?? video.ChannelId.ToString());

        var candidates = new List<CutCandidate>();

        for (int i = 0; i < windows.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var window = windows[i];
            _logger.LogInformation("[Ollama] Janela {Index}/{Total}: {Count} falas ({From} → {To}).",
                i + 1, windows.Count, window.Count, window[0].Start, window[^1].End);

            try
            {
                var raw = await AskModelAsync(window, video, language, minSeconds, maxSeconds, maxCutsPerWindow, ct);
                candidates.AddRange(ResolveCandidates(raw, byId, video, minSeconds, maxSeconds));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Uma janela ruim não invalida as outras.
                _logger.LogError(ex, "[Ollama] Falha na janela {Index}/{Total}. Seguindo para a próxima.", i + 1, windows.Count);
            }
        }

        // O menor dos dois tetos manda: o do vídeo e a vaga que sobrou no canal.
        var limit = Math.Min(maxCutsPerVideo, room);
        var selected = SelectBest(candidates, limit);

        _logger.LogInformation(
            "[SELEÇÃO] {Candidates} candidato(s) → {Selected} corte(s) após remover sobreposições. " +
            "Limite aplicado: {Limit} (por vídeo {PerVideo}, vaga no canal {Room}).",
            candidates.Count, selected.Count, limit, maxCutsPerVideo, room);

        // As tags do canal vêm primeiro de propósito: são as que o dono do canal
        // quer em tudo, e o teto de tags corta o excesso a partir do fim.
        var channelTags = TagList.Split(video.Channel?.DefaultTags).ToList();

        var cuts = selected.Select(c => new Cut
        {
            VideoId = video.Id,
            Name = c.Name,
            Description = c.Reason,
            Tags = TagList.Join(TagList.Merge(channelTags, c.Tags)),
            InitialTime = TimeOnly.FromTimeSpan(c.Start),
            FinallyTime = TimeOnly.FromTimeSpan(c.End),
            Status = CutStatusEnum.Created,
            CutPath = string.Empty
        }).ToList();

        await PersistAsync(video, cuts, ct);

        return new InterestingTimesCommandResponse(cuts);
    }

    // ------------------------------------------------------------------
    // Teto de cortes por canal
    // ------------------------------------------------------------------

    /// <summary>
    /// Quantos cortes novos ainda cabem neste canal monitorado.
    /// </summary>
    /// <remarks>
    /// A conta considera os cortes <b>não publicados</b> (Created e Process) de
    /// todos os vídeos do canal. Publicados não contam: são estado terminal, o
    /// trabalho neles terminou.
    /// <para>
    /// Sem este teto o fluxo não tinha freio. A publicação é o único ponto que
    /// consome cortes e só age em cortes com canal de publicação definido; sem
    /// canal, os cortes ficam parados em Process indefinidamente. Enquanto isso o
    /// monitoramento continua: os vídeos avançam até Removed, liberam as vagas do
    /// teto por etapa, novos vídeos entram e geram mais cortes. Resultado: cortes
    /// e arquivos acumulando sem parar. Com o teto, o canal simplesmente para
    /// quando enche, e volta a andar quando alguém publica ou remove cortes.
    /// </para>
    /// </remarks>
    private async Task<int> ResolveRoomForNewCutsAsync(Domain.Entities.Video video, CancellationToken ct)
    {
        var limit = video.Channel?.MaxPendingCuts
                    ?? ReadInt("Cuts:MaxPendingPerChannel", DefaultMaxPendingCutsPerChannel);

        // Zero (ou negativo) é a forma explícita de dizer "sem limite".
        if (limit <= 0) return int.MaxValue;

        var channelId = video.ChannelId;

        // CutStatus.Pending, e não "!= Publish" escrito à mão: quando um status
        // novo entra no fluxo (foi o caso de Edit), ele passa a contar aqui sem
        // ninguém precisar lembrar deste trecho. Um status esquecido abriria um
        // furo silencioso justamente no freio que impede o acúmulo sem fim.
        var pending = (await _uow.Repository<Cut>().FindAsync(
                c => CutStatus.Pending.Contains(c.Status) &&
                     c.Video != null &&
                     c.Video.ChannelId == channelId))
            .Count();

        var room = limit - pending;

        if (room <= 0)
        {
            _logger.LogWarning(
                "[TETO] O canal '{Channel}' já tem {Pending} corte(s) não publicado(s) (teto {Limit}). " +
                "Nenhum corte novo será criado até que os existentes sejam publicados ou removidos. " +
                "Se os cortes não têm canal de publicação definido, eles nunca saem deste estado.",
                video.Channel?.Name ?? channelId.ToString(), pending, limit);

            return 0;
        }

        _logger.LogInformation("[TETO] Canal '{Channel}': {Pending}/{Limit} corte(s) não publicado(s); cabem {Room}.",
            video.Channel?.Name ?? channelId.ToString(), pending, limit, room);

        return room;
    }

    // ------------------------------------------------------------------
    // Chamada ao modelo
    // ------------------------------------------------------------------
    private async Task<string> AskModelAsync(
        List<SubtitleCue> window, Domain.Entities.Video video, string language,
        int minSeconds, int maxSeconds, int maxCutsPerWindow, CancellationToken ct)
    {
        var systemReplacements = new List<(string Tag, string Value)>
        {
            ("[VIDEO_TITLE]", video.Title ?? string.Empty),
            ("[MIN_SECONDS]", minSeconds.ToString()),
            ("[MAX_SECONDS]", maxSeconds.ToString()),
        };

        var userReplacements = new List<(string Tag, string Value)>
        {
            ("[CUE_LIST]", SubtitleOptimizer.RenderForPrompt(window)),
            ("[MAX_CUTS]", maxCutsPerWindow.ToString()),
        };

        var request = new GenerateRequest
        {
            Model = _configuration["OLLAMA_MODEL"] ?? "qwen3:14b",
            System = PromptLanguageHelper.GetProcessedResource("SystemCutPrompt", language, systemReplacements),
            Prompt = PromptLanguageHelper.GetProcessedResource("UserCutPrompt", language, userReplacements),
            Stream = false,

            // Esquema em vez de "json": a saída já nasce na forma certa.
            Format = ResponseSchema,

            // Esta etapa é extração com julgamento leve, não raciocínio de
            // múltiplos passos. Em modelos com thinking, o bloco de raciocínio
            // atrapalha a saída estruturada e custa latência sem ganho aqui.
            // Para deliberar, o lugar é uma etapa de ranqueamento posterior.
            Think = ReadBool("Ollama:Think", false),

            Options = new RequestOptions
            {
                NumCtx = ReadInt("NumTokens", DefaultNumCtx),
                NumPredict = ReadInt("Ollama:NumPredict", DefaultNumPredict),

                // Sem isto o modelo roda no default (~0.8): criativo demais para
                // uma tarefa de extração, e a causa de boa parte da inconsistência.
                Temperature = ReadFloat("Ollama:Temperature", 0.2f),
                TopP = ReadFloat("Ollama:TopP", 0.9f),
                Seed = ReadInt("Ollama:Seed", 42),
            }
        };

        // O Ollama separa o raciocínio do conteúdo: 'Thinking' e 'Response' são
        // campos distintos. Acumulando apenas Response, o bloco de raciocínio de
        // um modelo com thinking não entra no JSON que será desserializado.
        var response = new System.Text.StringBuilder();
        var thinking = new System.Text.StringBuilder();

        await foreach (var part in _ollamaClient.GenerateAsync(request, ct))
        {
            response.Append(part?.Response ?? string.Empty);
            thinking.Append(part?.Thinking ?? string.Empty);
        }

        if (thinking.Length > 0)
            _logger.LogDebug("[Ollama] O modelo raciocinou {Chars} caracteres no campo 'thinking' (fora do conteúdo).",
                thinking.Length);

        if (response.Length == 0 && thinking.Length > 0)
            _logger.LogWarning(
                "[Ollama] O modelo raciocinou mas não devolveu conteúdo. Se estiver usando um modelo com " +
                "thinking, verifique se ele aceita saída restrita por esquema — ou desligue com Ollama__Think=false.");

        return response.ToString();
    }

    // ------------------------------------------------------------------
    // Resolução dos ids em tempo + validação
    // ------------------------------------------------------------------
    private IEnumerable<CutCandidate> ResolveCandidates(
        string rawJson,
        Dictionary<int, SubtitleCue> byId,
        Domain.Entities.Video video,
        int minSeconds,
        int maxSeconds)
    {
        var parsed = ParseResponse(rawJson);

        foreach (var raw in parsed)
        {
            // Um id inexistente é simplesmente descartado — é isto que torna
            // impossível o modelo "inventar" um tempo.
            if (!byId.TryGetValue(raw.StartId, out var startCue))
            {
                _logger.LogWarning("[FILTRO] start_id {Id} não existe na legenda. Descartado.", raw.StartId);
                continue;
            }

            if (!byId.TryGetValue(raw.EndId, out var endCue))
            {
                _logger.LogWarning("[FILTRO] end_id {Id} não existe na legenda. Descartado.", raw.EndId);
                continue;
            }

            if (endCue.End <= startCue.Start)
            {
                _logger.LogWarning("[FILTRO] Corte '{Name}' ignorado: fim ({End}) não é depois do início ({Start}).",
                    raw.Name, endCue.End, startCue.Start);
                continue;
            }

            var duration = endCue.End - startCue.Start;

            if (duration.TotalSeconds < minSeconds || duration.TotalSeconds > maxSeconds)
            {
                _logger.LogWarning("[FILTRO] Corte '{Name}' ignorado: {Seconds:n0}s fora da faixa {Min}-{Max}s.",
                    raw.Name, duration.TotalSeconds, minSeconds, maxSeconds);
                continue;
            }

            if (video.Duration.HasValue && endCue.End > video.Duration.Value)
            {
                _logger.LogWarning("[FILTRO] Corte '{Name}' ignorado: fim ({End}) ultrapassa a duração do vídeo ({Duration}).",
                    raw.Name, endCue.End, video.Duration.Value);
                continue;
            }

            yield return new CutCandidate(
                Start: startCue.Start,
                End: endCue.End,
                Score: raw.Score,
                Name: string.IsNullOrWhiteSpace(raw.Name) ? $"Corte {startCue.Start:hh\\:mm\\:ss}" : raw.Name.Trim(),
                Reason: raw.Reason?.Trim() ?? string.Empty,
                Tags: TagList.Merge(raw.Tags));
        }
    }

    private List<RawCut> ParseResponse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<RawCut>();

        // Com o esquema no format isto raramente é necessário, mas continua como
        // rede de segurança: cerca markdown, texto introdutório e — no caso de um
        // modelo com thinking cujo template o Ollama não reconheça — um bloco de
        // raciocínio vazado para dentro do conteúdo.
        var cleaned = LlmResponseSanitizer.ExtractJsonObject(json);

        if (cleaned.Length == 0)
        {
            _logger.LogWarning("[Ollama] Nenhum objeto JSON encontrado na resposta: {RawJson}", Truncate(json, 500));
            return new List<RawCut>();
        }

        try
        {
            return JsonSerializer.Deserialize<CutsWrapper>(cleaned, JsonOptions)?.Cuts ?? new List<RawCut>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[ERRO JSON] Resposta não pôde ser interpretada: {RawJson}", Truncate(json, 500));
            return new List<RawCut>();
        }
    }

    /// <summary>
    /// Ordena por score e descarta candidatos que se sobrepõem a um corte já
    /// escolhido. Sem isso as janelas com sobreposição gerariam clipes quase
    /// idênticos, e um vídeo longo devolveria dezenas de cortes medianos.
    /// </summary>
    private static List<CutCandidate> SelectBest(List<CutCandidate> candidates, int maxCuts)
    {
        var selected = new List<CutCandidate>();

        foreach (var candidate in candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Start))
        {
            if (selected.Count >= maxCuts) break;

            var overlaps = selected.Any(s => candidate.Start < s.End && s.Start < candidate.End);
            if (overlaps) continue;

            selected.Add(candidate);
        }

        return selected.OrderBy(c => c.Start).ToList();
    }

    private async Task PersistAsync(Domain.Entities.Video video, List<Cut> cuts, CancellationToken ct)
    {
        if (cuts.Count > 0)
        {
            _logger.LogInformation("[DB] Salvando {Count} corte(s) validado(s)...", cuts.Count);
            await _uow.Repository<Cut>().AddRangeAsync(cuts);
        }
        else
        {
            _logger.LogWarning("[AVISO] Nenhum corte foi identificado pela IA para '{Title}'.", video.Title);
        }

        // O vídeo avança de etapa mesmo sem cortes: senão ele voltaria a ser
        // analisado em todo ciclo, gastando inferência para o mesmo nada.
        video.Status = VideoStatusEnum.Process;
        _uow.Repository<Domain.Entities.Video>().Update(video);

        await _uow.CommitAsync();
    }

    // ------------------------------------------------------------------
    // Configuração
    // ------------------------------------------------------------------

    /// <summary>
    /// Duração do corte: o canal manda; sem valor no canal, cai na configuração
    /// global; sem ela, nos padrões da classe.
    /// </summary>
    private (int Min, int Max) ResolveCutDuration(Domain.Entities.Video video)
    {
        var min = video.Channel?.MinCutSeconds ?? ReadInt("CutDuration:MinSeconds", DefaultMinCutSeconds);
        var max = video.Channel?.MaxCutSeconds ?? ReadInt("CutDuration:MaxSeconds", DefaultMaxCutSeconds);

        if (min < 1) min = 1;

        if (max <= min)
        {
            _logger.LogWarning("[CONFIG] Duração máxima ({Max}s) não é maior que a mínima ({Min}s). Usando {Min}-{Fallback}s.",
                max, min, min, min + DefaultMaxCutSeconds);
            max = min + DefaultMaxCutSeconds;
        }

        return (min, max);
    }

    private int ReadInt(string key, int fallback) =>
        int.TryParse(_configuration[key], out var value) && value > 0 ? value : fallback;

    private float ReadFloat(string key, float fallback) =>
        float.TryParse(_configuration[key], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : fallback;

    private bool ReadBool(string key, bool fallback) =>
        bool.TryParse(_configuration[key], out var value) ? value : fallback;

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    // ------------------------------------------------------------------
    // Tipos internos
    // ------------------------------------------------------------------
    private sealed record CutCandidate(
        TimeSpan Start, TimeSpan End, double Score, string Name, string Reason, List<string> Tags);

    private sealed record CutsWrapper([property: JsonPropertyName("cuts")] List<RawCut> Cuts);

    private sealed record RawCut(
        [property: JsonPropertyName("start_id")] int StartId,
        [property: JsonPropertyName("end_id")] int EndId,
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("tags")] List<string>? Tags);
}
