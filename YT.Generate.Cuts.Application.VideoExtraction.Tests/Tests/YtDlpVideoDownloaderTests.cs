using Microsoft.Extensions.Logging.Abstractions;
using YT.Generate.Cuts.Application.Abstractions.Exceptions;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.VideoSource;
using YT.Generate.Cuts.Application.VideoExtraction.Youtube;

namespace YT.Generate.Cuts.Application.VideoExtraction.Tests.Tests;

/// <summary>
/// Exercita o download em duas etapas com um yt-dlp falso: um script que responde
/// como o executável real, inclusive falhando com o mesmo <c>HTTP Error 429</c> na
/// legenda que derrubava o download inteiro na versão anterior.
/// </summary>
public class YtDlpVideoDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ytdlp-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _scripts = new();

    public YtDlpVideoDownloaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temporário */ }
    }

    // ==================================================================
    // Comportamento
    // ==================================================================

    [Fact]
    public async Task DownloadAsync_ShouldKeepTheVideo_WhenTheSubtitleIsRateLimited()
    {
        if (!CanRunFakeExecutable) return;

        var downloader = Build(FakeYtDlp(subtitleBehaviour: SubtitleBehaviour.RateLimited));
        var target = NewTargetDirectory();

        var result = await downloader.DownloadAsync(Request(target), CancellationToken.None);

        // O ponto da correção: a legenda falhou, o vídeo ficou.
        Assert.True(File.Exists(result.VideoPath));
        Assert.Null(result.SubtitlePath);
        Assert.Equal(TimeSpan.FromSeconds(1234.5), result.Duration);

        // Nada de sobra no diretório: nem legenda parcial, nem o info.json.
        Assert.Empty(Directory.EnumerateFiles(target, "*.srt"));
        Assert.Empty(Directory.EnumerateFiles(target, "*.info.json"));
    }

    [Fact]
    public async Task DownloadAsync_ShouldReturnTheSubtitle_WhenTheSecondPhaseSucceeds()
    {
        if (!CanRunFakeExecutable) return;

        var downloader = Build(FakeYtDlp(subtitleBehaviour: SubtitleBehaviour.Succeeds));
        var target = NewTargetDirectory();

        var result = await downloader.DownloadAsync(Request(target), CancellationToken.None);

        Assert.True(File.Exists(result.VideoPath));
        Assert.NotNull(result.SubtitlePath);
        Assert.EndsWith(".srt", result.SubtitlePath);

        // pt-PT: variante regional humana, escolhida por nós a partir do
        // info.json — não pelo regex do yt-dlp.
        Assert.Contains("pt-PT", Path.GetFileName(result.SubtitlePath!));
        Assert.Equal("pt-PT", result.SubtitleLanguage);
        Assert.Empty(Directory.EnumerateFiles(target, "*.info.json"));
    }

    [Fact]
    public async Task DownloadAsync_ShouldMakeExactlyOneSubtitleRequest()
    {
        if (!CanRunFakeExecutable) return;

        var script = FakeYtDlp(subtitleBehaviour: SubtitleBehaviour.Succeeds);
        var downloader = Build(script);

        await downloader.DownloadAsync(Request(NewTargetDirectory()), CancellationToken.None);

        var invocations = ReadInvocations(script);

        // Duas invocações: vídeo e legenda. E a de legenda pede UMA tag exata.
        Assert.Equal(2, invocations.Count);
        Assert.DoesNotContain("--skip-download", invocations[0]);
        Assert.Contains("--skip-download", invocations[1]);
        Assert.Contains("--sub-langs pt-PT ", invocations[1] + " ");
        Assert.DoesNotContain(".*", invocations[1]);
    }

    [Fact]
    public async Task DownloadAsync_ShouldNotRequestSubtitles_WhenDisabled()
    {
        if (!CanRunFakeExecutable) return;

        var script = FakeYtDlp(subtitleBehaviour: SubtitleBehaviour.Succeeds);
        var downloader = Build(script, downloadSubtitles: false);

        var result = await downloader.DownloadAsync(Request(NewTargetDirectory()), CancellationToken.None);

        Assert.Null(result.SubtitlePath);
        Assert.Single(ReadInvocations(script));
    }

    [Fact]
    public async Task DownloadAsync_ShouldThrowAndCleanUp_WhenTheVideoPhaseFails()
    {
        if (!CanRunFakeExecutable) return;

        var downloader = Build(FakeYtDlp(videoFails: true));
        var target = NewTargetDirectory();

        await Assert.ThrowsAsync<VideoUnavailableException>(
            () => downloader.DownloadAsync(Request(target), CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(target));
    }

    [Fact]
    public async Task DownloadAsync_ShouldSkipTheSubtitlePhase_WhenNoRequestedLanguageIsAvailable()
    {
        if (!CanRunFakeExecutable) return;

        var script = FakeYtDlp(
            subtitleBehaviour: SubtitleBehaviour.Succeeds,
            manualTags: new[] { "ko" },
            automaticTags: new[] { "ja", "live_chat" });

        var downloader = Build(script);

        var result = await downloader.DownloadAsync(Request(NewTargetDirectory()), CancellationToken.None);

        Assert.True(File.Exists(result.VideoPath));
        Assert.Null(result.SubtitlePath);

        // Sem faixa compatível, nem chegamos a bater no endpoint de legendas.
        Assert.Single(ReadInvocations(script));
    }

    [Fact]
    public async Task IsAvailableAsync_ShouldBeFalse_WhenTheExecutableDoesNotExist()
    {
        var downloader = Build(Path.Combine(_root, "nao-existe"));

        Assert.False(await downloader.IsAvailableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_ShouldFail_WhenOnlyMergeFragmentsRemain()
    {
        if (!CanRunFakeExecutable) return;

        var downloader = Build(FakeYtDlp(mergeFails: true));
        var target = NewTargetDirectory();

        var error = await Assert.ThrowsAsync<VideoUnavailableException>(
            () => downloader.DownloadAsync(Request(target), CancellationToken.None));

        // Um fragmento é vídeo SEM áudio. Aceitá-lo como resultado gravaria no
        // banco um caminho que só revela o defeito no corte publicado.
        Assert.Contains("merge", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("f398", error.Message);
        Assert.Empty(Directory.EnumerateFiles(target));
    }

    [Fact]
    public void FindVideo_ShouldPreferTheMergedFileOverFragments()
    {
        var directory = NewTargetDirectory();
        const string handle = "0123456789abcdef0123456789abcdef";

        // O fragmento é de propósito MAIOR que o arquivo final: só o tamanho não
        // basta para distinguir, e era esse o critério anterior.
        File.WriteAllBytes(Path.Combine(directory, handle + ".f398.mp4"), new byte[4096]);
        File.WriteAllBytes(Path.Combine(directory, handle + ".mp4"), new byte[64]);

        var found = YtDlpVideoDownloader.FindVideo(directory, handle);

        Assert.Equal(handle + ".mp4", Path.GetFileName(found));
    }

    [Fact]
    public void FindVideo_ShouldIgnoreSubtitlesAndInfoJson()
    {
        var directory = NewTargetDirectory();
        const string handle = "abcdefabcdefabcdefabcdefabcdef12";

        File.WriteAllBytes(Path.Combine(directory, handle + ".info.json"), new byte[8192]);
        File.WriteAllBytes(Path.Combine(directory, handle + ".pt.srt"), new byte[8192]);
        File.WriteAllBytes(Path.Combine(directory, handle + ".mkv"), new byte[16]);

        Assert.Equal(handle + ".mkv", Path.GetFileName(YtDlpVideoDownloader.FindVideo(directory, handle)));
    }

    // ==================================================================
    // Argumentos
    // ==================================================================

    [Fact]
    public void BuildVideoArguments_ShouldNotAskForAnySubtitle()
    {
        var downloader = Build(FakeYtDlp());
        var args = downloader.BuildVideoArguments(Request("/tmp/x"), "/tmp/x/handle.%(ext)s");

        Assert.DoesNotContain("--write-subs", args);
        Assert.DoesNotContain("--write-auto-subs", args);
        Assert.DoesNotContain("--sub-langs", args);

        // Defensivo: um yt-dlp.conf no ambiente não pode reintroduzir o problema.
        Assert.Contains("--no-write-subs", args);
        Assert.Contains("--no-write-auto-subs", args);

        // É daqui que sai a lista de faixas disponíveis, sem requisição extra.
        Assert.Contains("--write-info-json", args);
    }

    [Fact]
    public void BuildSubtitleArguments_ShouldUseTheExactTagAndSkipTheDownload()
    {
        var downloader = Build(FakeYtDlp());
        var track = new SubtitleTrack("pt-PT", IsAutomatic: false, Language: "pt");

        var args = downloader.BuildSubtitleArguments(Request("/tmp/x"), "/tmp/x/handle.%(ext)s", track);

        Assert.Contains("--skip-download", args);
        Assert.Contains("--write-subs", args);
        Assert.Contains("--no-write-auto-subs", args);
        Assert.Equal("pt-PT", args[args.ToList().IndexOf("--sub-langs") + 1]);
        Assert.Contains("--convert-subs", args);
    }

    [Fact]
    public void BuildSubtitleArguments_ShouldSwitchTheFlag_ForAutomaticTracks()
    {
        var downloader = Build(FakeYtDlp());
        var track = new SubtitleTrack("pt-orig", IsAutomatic: true, Language: "pt");

        var args = downloader.BuildSubtitleArguments(Request("/tmp/x"), "/tmp/x/handle.%(ext)s", track);

        // --write-subs não baixa transcrição automática, e vice-versa.
        Assert.Contains("--write-auto-subs", args);
        Assert.Contains("--no-write-subs", args);
        Assert.DoesNotContain("--write-subs", args);
    }

    // ==================================================================
    // Leitura de metadados
    // ==================================================================

    [Fact]
    public void ReadAvailableTracks_ShouldReadOnlyTheKeys()
    {
        var directory = NewTargetDirectory();
        File.WriteAllText(Path.Combine(directory, "abc.info.json"), """
            {
              "id": "abc",
              "subtitles": { "pt-PT": [ { "ext": "vtt", "url": "http://x/1" } ] },
              "automatic_captions": { "pt": [], "live_chat": [] }
            }
            """);

        var (manual, automatic) = YtDlpVideoDownloader.ReadAvailableTracks(directory, "abc");

        Assert.Equal(new[] { "pt-PT" }, manual);
        Assert.Equal(new[] { "pt", "live_chat" }, automatic);
    }

    [Fact]
    public void ReadAvailableTracks_ShouldReturnEmpty_WhenThereIsNoInfoJson()
    {
        var (manual, automatic) = YtDlpVideoDownloader.ReadAvailableTracks(NewTargetDirectory(), "abc");

        Assert.Empty(manual);
        Assert.Empty(automatic);
    }

    [Theory]
    [InlineData("DURATION=1234.5", 1234.5)]
    [InlineData("algo antes\nDURATION=60\nalgo depois", 60)]
    [InlineData("DURATION=NA", 0)]
    [InlineData("nada aqui", 0)]
    public void ParseDuration_ShouldReadTheDedicatedLine(string stdOut, double expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), YtDlpVideoDownloader.ParseDuration(stdOut));
    }

    // ==================================================================
    // Infraestrutura do teste
    // ==================================================================

    private enum SubtitleBehaviour { RateLimited, Succeeds }

    private static YtDlpVideoDownloader Build(string executable, bool downloadSubtitles = true) =>
        new(new YtDlpOptions
            {
                ExecutablePath = executable,
                FfmpegPath = "/bin/true",
                SleepRequestsSeconds = 0,
                SleepSubtitlesSeconds = 0,
                SubtitleTimeoutMinutes = 1,
                ProcessTimeoutMinutes = 1,
                DownloadSubtitles = downloadSubtitles,
            },
            NullLogger<YtDlpVideoDownloader>.Instance);

    /// <summary>
    /// O yt-dlp falso é um script de shell, então os testes que sobem processo só
    /// rodam onde há bash. O ambiente de execução do projeto é Linux (container),
    /// e a lógica que realmente decide o comportamento — escolha da faixa, montagem
    /// dos argumentos, leitura do info.json — está coberta por testes puros, que
    /// rodam em qualquer plataforma.
    /// </summary>
    private static bool CanRunFakeExecutable => File.Exists("/bin/bash");

    private static VideoDownloadRequest Request(string targetDirectory) =>
        new("https://www.youtube.com/watch?v=abcdefghijk", targetDirectory, 1080, new[] { "pt", "en" });

    private string NewTargetDirectory()
    {
        var path = Path.Combine(_root, "d" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }

    private List<string> ReadInvocations(string script) =>
        File.Exists(script + ".log")
            ? File.ReadAllLines(script + ".log").Where(l => l.Length > 0).ToList()
            : new List<string>();

    /// <summary>
    /// Escreve um yt-dlp falso. Reproduz o que importa do real: o template de
    /// saída <c>%(ext)s</c>, o <c>--write-info-json</c>, a linha de DURATION e o
    /// código de saída 1 com a mensagem de erro no stderr.
    /// </summary>
    private string FakeYtDlp(
        SubtitleBehaviour subtitleBehaviour = SubtitleBehaviour.Succeeds,
        bool videoFails = false,
        bool mergeFails = false,
        IEnumerable<string>? manualTags = null,
        IEnumerable<string>? automaticTags = null)
    {
        var path = Path.Combine(_root, "yt-dlp-fake-" + Guid.NewGuid().ToString("N")[..8]);

        string Keys(IEnumerable<string> tags) =>
            string.Join(", ", tags.Select(t => $"\"{t}\": [{{\"ext\":\"vtt\",\"url\":\"http://x/{t}\"}}]"));

        var manual = Keys(manualTags ?? new[] { "pt-PT", "en" });
        var automatic = Keys(automaticTags ?? new[] { "pt", "en-orig", "live_chat" });

        var videoPhase = mergeFails
            ? $$"""
              printf 'fragmento-de-video' > "$BASE.f398.mp4"
              printf 'fragmento-de-audio' > "$BASE.f251.webm"
              cat > "$BASE.info.json" <<'JSON'
              { "id": "abcdefghijk", "subtitles": { {{manual}} }, "automatic_captions": { {{automatic}} } }
              JSON
              echo "DURATION=1234.5"
              exit 0
              """
            : videoFails
            ? """
              echo "ERROR: [youtube] abcdefghijk: Video unavailable" >&2
              exit 1
              """
            : $$"""
              printf 'fake-video-bytes' > "$BASE.mp4"
              cat > "$BASE.info.json" <<'JSON'
              { "id": "abcdefghijk", "subtitles": { {{manual}} }, "automatic_captions": { {{automatic}} } }
              JSON
              echo "DURATION=1234.5"
              exit 0
              """;

        var subtitlePhase = subtitleBehaviour == SubtitleBehaviour.RateLimited
            ? """
              echo "ERROR: Unable to download video subtitles for 'pt-PT': HTTP Error 429: Too Many Requests" >&2
              exit 1
              """
            : """
              printf '1\n00:00:01,000 --> 00:00:03,000\nola\n\n' > "$BASE.$SUBLANGS.srt"
              exit 0
              """;

        var script = $$"""
            #!/bin/bash
            if [ "$1" = "--version" ]; then echo "2026.08.19"; exit 0; fi

            printf '%s\n' "$*" >> "{{path}}.log"

            OUT=""
            SUBLANGS=""
            SKIP=0
            PREV=""
            for ARG in "$@"; do
              if [ "$PREV" = "-o" ]; then OUT="$ARG"; fi
              if [ "$PREV" = "--sub-langs" ]; then SUBLANGS="$ARG"; fi
              if [ "$ARG" = "--skip-download" ]; then SKIP=1; fi
              PREV="$ARG"
            done

            BASE=$(printf '%s' "$OUT" | sed 's/\.%(ext)s$//')

            if [ "$SKIP" = "1" ]; then
            {{subtitlePhase}}
            else
            {{videoPhase}}
            fi
            """;

        File.WriteAllText(path, script.ReplaceLineEndings("\n"));

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        _scripts.Add(path);
        return path;
    }
}
