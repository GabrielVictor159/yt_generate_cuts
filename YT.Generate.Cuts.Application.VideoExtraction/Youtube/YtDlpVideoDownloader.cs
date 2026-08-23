using System.Globalization;
using System.Text;
using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Exceptions;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.VideoSource;

namespace YT.Generate.Cuts.Application.VideoExtraction.Youtube;

/// <summary>
/// Download via yt-dlp, em duas etapas: primeiro o vídeo, depois — e só se der
/// certo — a legenda.
/// </summary>
/// <remarks>
/// A separação corrige dois defeitos da versão anterior, que rodava tudo numa
/// única invocação:
/// <list type="number">
/// <item>
/// O yt-dlp devolve código de saída diferente de zero quando QUALQUER legenda
/// pedida falha. Com uma invocação só, um <c>429</c> em
/// <c>Unable to download video subtitles for 'pt-PT'</c> descartava também o
/// vídeo — que muitas vezes já tinha baixado por inteiro.
/// </item>
/// <item>
/// Pedir <c>--write-subs --write-auto-subs --sub-langs "pt.*,en.*"</c> baixava
/// todas as variantes de todos os idiomas casados, nas duas modalidades: uma
/// rajada de requisições ao endpoint de legendas que provocava o próprio
/// <c>429</c>. Agora a escolha é nossa (<see cref="SubtitleTrackSelector"/>) e
/// vira uma única requisição, com pausa entre chamadas.
/// </item>
/// </list>
/// <para>
/// A etapa 2 é explicitamente "melhor esforço": qualquer falha nela virá como
/// aviso e <c>SubtitlePath = null</c>. Vídeo sem legenda ainda é um vídeo; vídeo
/// perdido por causa da legenda é trabalho jogado fora.
/// </para>
/// <para>
/// O nome de saída é um GUID fixado por nós (<c>-o "{dir}/{guid}.%(ext)s"</c>),
/// então localizamos os arquivos por padrão de nome em vez de tentar interpretar
/// o que o yt-dlp imprimiu. Isso sobrevive a mudanças de formato de log entre
/// versões, que é justamente o tipo de acoplamento que queremos evitar aqui.
/// </para>
/// </remarks>
public sealed class YtDlpVideoDownloader : IVideoDownloader
{
    private const string DurationPrefix = "DURATION=";
    private const string InfoJsonSuffix = ".info.json";

    private readonly YtDlpOptions _options;
    private readonly ILogger<YtDlpVideoDownloader> _logger;

    public YtDlpVideoDownloader(YtDlpOptions options, ILogger<YtDlpVideoDownloader> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string ProviderName => "yt-dlp";

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try
        {
            var result = await Cli.Wrap(_options.ExecutablePath)
                .WithArguments("--version")
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);

            if (result.ExitCode == 0)
            {
                _logger.LogInformation("[yt-dlp] Disponível, versão {Version}.", result.StandardOutput.Trim());
                return true;
            }

            _logger.LogWarning("[yt-dlp] '{Path} --version' terminou com código {Code}.", _options.ExecutablePath, result.ExitCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[yt-dlp] Executável não encontrado em '{Path}': {Message}", _options.ExecutablePath, ex.Message);
            return false;
        }
    }

    public async Task<VideoDownloadResult> DownloadAsync(VideoDownloadRequest request, CancellationToken ct)
    {
        Directory.CreateDirectory(request.TargetDirectory);

        var handle = Guid.NewGuid().ToString("N");
        var outputTemplate = Path.Combine(request.TargetDirectory, handle + ".%(ext)s");

        // ------------------------------------------------------------------
        // Etapa 1 — vídeo. Obrigatória: falhar aqui é falhar o download.
        // ------------------------------------------------------------------
        _logger.LogInformation("[yt-dlp] Baixando {Url} (até {Height}p) em {Dir}.",
            request.VideoUrl, request.MaxHeight, request.TargetDirectory);

        var stdOut = await RunRequiredAsync(
            BuildVideoArguments(request, outputTemplate),
            request,
            handle,
            TimeSpan.FromMinutes(_options.ProcessTimeoutMinutes),
            ct);

        var videoPath = FindVideo(request.TargetDirectory, handle);

        if (videoPath is null)
        {
            // Distingue "nada foi baixado" de "baixou mas o merge não fechou":
            // são causas diferentes e a segunda aponta para o ffmpeg.
            var fragments = Directory.EnumerateFiles(request.TargetDirectory, handle + ".*")
                .Where(f => !IsSubtitle(f) && !IsInfoJson(f))
                .Select(Path.GetFileName)
                .ToList();

            CleanUp(request.TargetDirectory, handle);

            throw new VideoUnavailableException(fragments.Count > 0
                ? $"yt-dlp baixou os fluxos de {request.VideoUrl} mas não produziu o arquivo final — o merge não " +
                  $"concluiu, quase sempre por ffmpeg ausente ou não encontrado (YtDlp:FfmpegPath = " +
                  $"'{_options.FfmpegPath}'). Intermediários encontrados: " + string.Join(", ", fragments)
                : $"yt-dlp terminou com sucesso mas nenhum arquivo de vídeo foi produzido para {request.VideoUrl}.");
        }

        var duration = ParseDuration(stdOut);

        // ------------------------------------------------------------------
        // Etapa 2 — legenda. Melhor esforço.
        // ------------------------------------------------------------------
        var subtitle = await TryDownloadSubtitleAsync(request, outputTemplate, handle, ct);

        // O info.json só existia para descobrir as faixas disponíveis.
        DeleteInfoJson(request.TargetDirectory, handle);

        _logger.LogInformation("[yt-dlp] Concluído. Vídeo: {Video} | Legenda: {Subtitle} | Idioma: {Language}",
            videoPath, subtitle.Path ?? "(nenhuma)", subtitle.Language ?? "-");

        return new VideoDownloadResult(videoPath, subtitle.Path, subtitle.Language, duration);
    }

    // ==================================================================
    // Etapa 1
    // ==================================================================

    /// <summary>
    /// Argumentos do download do vídeo. Nenhuma flag de legenda aqui — as
    /// <c>--no-write-*</c> são defensivas, para um <c>yt-dlp.conf</c> no ambiente
    /// não reintroduzir o comportamento que causou o 429.
    /// </summary>
    public IReadOnlyList<string> BuildVideoArguments(VideoDownloadRequest request, string outputTemplate)
    {
        var args = new List<string>
        {
            "--no-playlist",
            "--newline",
            "--no-progress",
            "--retries", _options.Retries.ToString(),
            "--socket-timeout", _options.SocketTimeoutSeconds.ToString(),

            // Vídeo e áudio separados no melhor par até a altura pedida, com
            // fallback para stream único caso não exista a combinação.
            "-f", FormatSelector(request.MaxHeight),
            "--merge-output-format", "mp4",

            "--no-write-subs",
            "--no-write-auto-subs",

            // Grava os metadados já obtidos para escolhermos a faixa de legenda
            // sem gastar uma requisição a mais.
            "--write-info-json",

            "-o", outputTemplate,

            // Duração numa linha própria e previsível, em vez de garimpar o log.
            // Sem prefixo de etapa: "after_move" não imprime quando não há
            // movimentação de arquivo, e aqui só queremos o metadado.
            "--print", $"{DurationPrefix}%(duration)s",
            "--no-simulate",
        };

        AddSleep(args, "--sleep-requests", _options.SleepRequestsSeconds);
        AddCommonArguments(args);

        args.Add(request.VideoUrl);
        return args;
    }

    // ==================================================================
    // Etapa 2
    // ==================================================================

    private async Task<(string? Path, string? Language)> TryDownloadSubtitleAsync(
        VideoDownloadRequest request, string outputTemplate, string handle, CancellationToken ct)
    {
        if (!_options.DownloadSubtitles)
        {
            _logger.LogInformation("[yt-dlp] Busca de legendas desativada por configuração (YtDlp:DownloadSubtitles).");
            return (null, null);
        }

        try
        {
            var available = ReadAvailableTracks(request.TargetDirectory, handle);

            var track = SubtitleTrackSelector.Choose(
                available.Manual, available.Automatic, request.PreferredSubtitleLanguages);

            if (track is null)
            {
                _logger.LogWarning(
                    "[yt-dlp] Nenhuma legenda nos idiomas pedidos ({Wanted}). Disponíveis: humanas [{Manual}], " +
                    "automáticas [{Auto}]. O vídeo segue sem legenda.",
                    string.Join(", ", request.PreferredSubtitleLanguages),
                    string.Join(", ", available.Manual.Take(12)),
                    string.Join(", ", available.Automatic.Take(12)));

                return (null, null);
            }

            _logger.LogInformation("[yt-dlp] Legenda escolhida: {Tag} ({Kind}).",
                track.Tag, track.IsAutomatic ? "automática" : "humana");

            var result = await RunAsync(
                BuildSubtitleArguments(request, outputTemplate, track),
                TimeSpan.FromMinutes(_options.SubtitleTimeoutMinutes),
                ct);

            if (result.ExitCode != 0)
            {
                var error = YtDlpErrorTranslator.Translate(result.StandardError, result.ExitCode, request.VideoUrl);

                // Ponto central da correção: a legenda falhou, o vídeo não.
                _logger.LogWarning(
                    "[yt-dlp] Não foi possível baixar a legenda {Tag} ({Reason}). O vídeo foi mantido e segue sem " +
                    "legenda — a etapa de InterestingTimes vai ignorá-lo até que exista legenda.",
                    track.Tag, error.Message);

                DeleteSubtitles(request.TargetDirectory, handle);
                return (null, null);
            }

            var (path, language) = FindSubtitle(request.TargetDirectory, handle, request.PreferredSubtitleLanguages);

            if (path is null)
                _logger.LogWarning("[yt-dlp] O yt-dlp aceitou a legenda {Tag} mas nenhum arquivo apareceu em {Dir}.",
                    track.Tag, request.TargetDirectory);

            return (path, language ?? track.Language);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Inclui timeout da própria etapa e falha ao ler o info.json.
            _logger.LogWarning("[yt-dlp] A etapa de legendas falhou ({Message}). O vídeo foi mantido.", ex.Message);
            DeleteSubtitles(request.TargetDirectory, handle);
            return (null, null);
        }
    }

    /// <summary>
    /// Argumentos da legenda: <c>--skip-download</c> e UMA tag exata, sem regex.
    /// A flag muda conforme a faixa: <c>--write-auto-subs</c> não serve para
    /// legenda humana e vice-versa.
    /// </summary>
    public IReadOnlyList<string> BuildSubtitleArguments(
        VideoDownloadRequest request, string outputTemplate, SubtitleTrack track)
    {
        var args = new List<string>
        {
            "--no-playlist",
            "--newline",
            "--no-progress",
            "--skip-download",
            "--retries", _options.Retries.ToString(),
            "--socket-timeout", _options.SocketTimeoutSeconds.ToString(),

            // Mantém o mesmo seletor: com --skip-download nada é baixado, mas o
            // yt-dlp ainda resolve formato e aborta se o seletor não casar.
            "-f", FormatSelector(request.MaxHeight),

            track.IsAutomatic ? "--write-auto-subs" : "--write-subs",
            track.IsAutomatic ? "--no-write-subs" : "--no-write-auto-subs",

            // Sem ".*": exatamente a faixa escolhida, uma requisição.
            "--sub-langs", track.Tag,
            "--sub-format", "srt/vtt/best",
            "--convert-subs", "srt",

            "-o", outputTemplate,
        };

        AddSleep(args, "--sleep-requests", _options.SleepRequestsSeconds);

        if (_options.SleepSubtitlesSeconds > 0)
        {
            args.Add("--sleep-subtitles");
            args.Add(_options.SleepSubtitlesSeconds.ToString(CultureInfo.InvariantCulture));
        }

        AddCommonArguments(args);

        args.Add(request.VideoUrl);
        return args;
    }

    // ==================================================================
    // Metadados
    // ==================================================================

    /// <summary>
    /// Lê as tags de legenda do <c>info.json</c> gravado na etapa 1. Só os nomes
    /// das chaves interessam; as URLs são longas e não são usadas.
    /// </summary>
    public static (List<string> Manual, List<string> Automatic) ReadAvailableTracks(string directory, string handle)
    {
        var path = Path.Combine(directory, handle + InfoJsonSuffix);

        if (!File.Exists(path)) return (new List<string>(), new List<string>());

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);

        return (Keys(document.RootElement, "subtitles"), Keys(document.RootElement, "automatic_captions"));
    }

    private static List<string> Keys(JsonElement root, string property)
    {
        var keys = new List<string>();

        if (root.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.Object)
            foreach (var item in node.EnumerateObject())
                keys.Add(item.Name);

        return keys;
    }

    // ==================================================================
    // Execução
    // ==================================================================

    private async Task<string> RunRequiredAsync(
        IReadOnlyList<string> arguments,
        VideoDownloadRequest request,
        string handle,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var result = await RunAsync(arguments, timeout, ct);

        // Avisos do yt-dlp em execução bem-sucedida importam. Foi um deles —
        // "ffmpeg-location ffmpeg does not exist! Continuing without ffmpeg" —
        // que explicava o merge incompleto, e o `--no-warnings` que este código
        // passava o mantinha invisível.
        LogWarnings(result.StandardError);

        if (result.ExitCode != 0)
        {
            CleanUp(request.TargetDirectory, handle);
            var error = YtDlpErrorTranslator.Translate(result.StandardError, result.ExitCode, request.VideoUrl);
            _logger.LogWarning("[yt-dlp] {Type}: {Message}", error.GetType().Name, error.Message);
            throw error;
        }

        return result.StandardOutput;
    }

    /// <summary>Repassa para o log os avisos que o yt-dlp escreveu no stderr.</summary>
    private void LogWarnings(string? stdErr)
    {
        if (string.IsNullOrWhiteSpace(stdErr)) return;

        foreach (var line in stdErr.Split('\n')
                     .Select(l => l.Trim())
                     .Where(l => l.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
                     .Take(10))
        {
            _logger.LogWarning("[yt-dlp] {Warning}", line);
        }
    }

    private async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(
        IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        _logger.LogDebug("[yt-dlp] {Executable} {Arguments}", _options.ExecutablePath, string.Join(' ', arguments));

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);

        try
        {
            var result = await Cli.Wrap(_options.ExecutablePath)
                .WithArguments(arguments)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOut))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErr))
                .ExecuteAsync(linked.Token);

            return (result.ExitCode, stdOut.ToString(), stdErr.ToString());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new VideoSourceNetworkException(
                $"yt-dlp excedeu o limite de {timeout.TotalMinutes:0.#} min.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not VideoSourceException)
        {
            // Falha ao iniciar o processo: executável ausente ou sem permissão.
            throw new VideoSourceUnavailableException(
                $"Não foi possível executar '{_options.ExecutablePath}': {ex.Message}", ex);
        }
    }

    // ==================================================================
    // Argumentos comuns e utilitários
    // ==================================================================

    /// <summary>
    /// Seletor de formato. A primeira opção pede vídeo em mp4 com áudio em m4a
    /// (H.264 + AAC).
    /// </summary>
    /// <remarks>
    /// A ordem não é estética. O seletor anterior era só
    /// <c>bv*[height&lt;=H]+ba</c>, que no YouTube costuma escolher AV1 (formato
    /// 398) com áudio Opus em webm (formato 251) — e juntar Opus dentro de um mp4
    /// é justamente o que mais falha no merge. Pedindo mp4+m4a primeiro, o merge
    /// é uma remuxagem trivial, e o arquivo resultante é o que o ffmpeg do corte
    /// consome sem reencodar. As opções seguintes existem para o vídeo que não
    /// publica esse par.
    /// </remarks>
    private string FormatSelector(int maxHeight)
    {
        if (!string.IsNullOrWhiteSpace(_options.FormatSelector))
            return _options.FormatSelector!.Replace("{height}", maxHeight.ToString());

        return $"bv*[height<={maxHeight}][ext=mp4]+ba[ext=m4a]/" +
               $"bv*[height<={maxHeight}]+ba[ext=m4a]/" +
               $"bv*[height<={maxHeight}]+ba/" +
               $"b[height<={maxHeight}]/" +
               "bv*+ba/b";
    }

    /// <summary>
    /// Passa <c>--ffmpeg-location</c> só quando aponta para algo que existe.
    /// </summary>
    /// <remarks>
    /// Este era o defeito que quebrava o fluxo inteiro. A configuração trazia
    /// <c>YtDlp__FfmpegPath=ffmpeg</c> — o nome do executável, não um caminho — e
    /// o yt-dlp responde a isso com
    /// <c>WARNING: ffmpeg-location ffmpeg does not exist! Continuing without ffmpeg</c>:
    /// ele baixa os dois fluxos, não consegue juntá-los e <b>termina com código
    /// zero</b>, deixando apenas os intermediários <c>.f398.mp4</c> e
    /// <c>.f251.webm</c>. Sem o argumento, o yt-dlp procura o ffmpeg no PATH, que
    /// é onde ele está no container.
    /// </remarks>
    private void AddFfmpegLocation(List<string> args)
    {
        var path = _options.FfmpegPath;

        if (string.IsNullOrWhiteSpace(path)) return;

        // Nome simples ("ffmpeg"): deixa o yt-dlp resolver pelo PATH.
        var looksLikePath = path.Contains(Path.DirectorySeparatorChar) ||
                            path.Contains(Path.AltDirectorySeparatorChar);

        if (!looksLikePath) return;

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            _logger.LogWarning(
                "[yt-dlp] O caminho configurado para o ffmpeg não existe ({Path}). O yt-dlp vai procurar no PATH.",
                path);
            return;
        }

        args.Add("--ffmpeg-location");
        args.Add(path);
    }

    private void AddCommonArguments(List<string> args)
    {
        AddFfmpegLocation(args);

        if (!string.IsNullOrWhiteSpace(_options.CookiesFile) && File.Exists(_options.CookiesFile))
        {
            args.Add("--cookies");
            args.Add(_options.CookiesFile);
        }

        if (!string.IsNullOrWhiteSpace(_options.PlayerClients))
        {
            args.Add("--extractor-args");
            args.Add($"youtube:player_client={_options.PlayerClients}");
        }
    }

    private static void AddSleep(List<string> args, string flag, double seconds)
    {
        if (seconds <= 0) return;

        args.Add(flag);
        args.Add(seconds.ToString("0.###", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Localiza o vídeo final. O arquivo resultante tem exatamente um segmento
    /// depois do handle (<c>handle.mp4</c>); os intermediários por formato têm
    /// dois (<c>handle.f398.mp4</c>, <c>handle.f251.webm</c>) e só sobram quando o
    /// merge não conclui.
    /// </summary>
    /// <remarks>
    /// A distinção importa: sem ela, um merge incompleto faz o maior fragmento —
    /// que é vídeo <b>sem áudio</b> — ser gravado no banco como o vídeo do canal,
    /// e o defeito só aparece muito depois, no corte publicado.
    /// </remarks>
    public static string? FindVideo(string directory, string handle)
    {
        var candidates = Directory.EnumerateFiles(directory, handle + ".*")
            .Where(f => !IsSubtitle(f) && !IsInfoJson(f))
            .ToList();

        if (candidates.Count == 0) return null;

        return candidates
            .Where(f => SegmentsAfterHandle(f, handle) == 1)
            .OrderByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();
    }

    /// <summary>Quantos segmentos separados por ponto vêm depois do handle.</summary>
    private static int SegmentsAfterHandle(string path, string handle)
    {
        var name = Path.GetFileName(path);

        if (!name.StartsWith(handle + ".", StringComparison.Ordinal))
            return 0;

        return name[(handle.Length + 1)..].Split('.').Length;
    }

    /// <summary>
    /// Escolhe a legenda seguindo a ordem de preferência dos idiomas. O yt-dlp
    /// nomeia como <c>{handle}.{idioma}.srt</c>.
    /// </summary>
    public static (string? Path, string? Language) FindSubtitle(
        string directory, string handle, IReadOnlyList<string> preferredLanguages)
    {
        var candidates = Directory.EnumerateFiles(directory, handle + ".*")
            .Where(IsSubtitle)
            .ToList();

        if (candidates.Count == 0) return (null, null);

        foreach (var language in preferredLanguages)
        {
            var match = candidates.FirstOrDefault(f =>
                LanguageOf(f, handle).StartsWith(language, StringComparison.OrdinalIgnoreCase));

            if (match is not null) return (match, LanguageOf(match, handle));
        }

        var fallback = candidates[0];
        return (fallback, LanguageOf(fallback, handle));
    }

    private static bool IsSubtitle(string path) =>
        path.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase);

    private static bool IsInfoJson(string path) =>
        path.EndsWith(InfoJsonSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Extrai o idioma de <c>{handle}.{idioma}.srt</c>.</summary>
    private static string LanguageOf(string path, string handle)
    {
        var name = Path.GetFileName(path);
        var withoutHandle = name.StartsWith(handle + ".", StringComparison.Ordinal)
            ? name[(handle.Length + 1)..]
            : name;

        var parts = withoutHandle.Split('.');
        return parts.Length >= 2 ? parts[0] : string.Empty;
    }

    public static TimeSpan ParseDuration(string stdOut)
    {
        foreach (var line in stdOut.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(DurationPrefix, StringComparison.Ordinal)) continue;

            var value = trimmed[DurationPrefix.Length..].Trim();

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);
        }

        // "NA" para live ou vídeo sem duração declarada: quem chama usa a
        // duração que já veio do catálogo.
        return TimeSpan.Zero;
    }

    private void DeleteInfoJson(string directory, string handle) =>
        Delete(Directory.EnumerateFiles(directory, handle + "*").Where(IsInfoJson));

    private void DeleteSubtitles(string directory, string handle) =>
        Delete(Directory.EnumerateFiles(directory, handle + "*").Where(IsSubtitle));

    private void CleanUp(string directory, string handle) =>
        Delete(Directory.EnumerateFiles(directory, handle + "*"));

    private void Delete(IEnumerable<string> files)
    {
        foreach (var file in files.ToList())
        {
            try { File.Delete(file); }
            catch (Exception ex) { _logger.LogWarning("[yt-dlp] Não foi possível remover {File}: {Message}", file, ex.Message); }
        }
    }
}
