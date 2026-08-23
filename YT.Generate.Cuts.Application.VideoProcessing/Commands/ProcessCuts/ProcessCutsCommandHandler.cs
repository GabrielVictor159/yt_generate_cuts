using System.Globalization;
using System.Text;
using CliWrap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Exceptions;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoProcessing.Helpers;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Application.VideoProcessing.Commands.ProcessCuts;

/// <summary>
/// Gera o arquivo de um corte com o ffmpeg.
/// </summary>
/// <remarks>
/// A montagem dos argumentos tinha um defeito que passava despercebido porque o
/// corte era gerado com sucesso — só com a duração errada. Era
/// <c>-ss {início} -i {arquivo} -to {fim}</c>, e o <c>-to</c>, como opção de
/// saída, conta a partir do início do arquivo <b>gerado</b>, não do original.
/// Depois do <c>-ss</c> a saída começa em zero, então <c>-to 00:05:30</c> não
/// terminava em 5m30s do vídeo: produzia 5m30s de corte. Medido com o ffmpeg:
/// um corte pedido de 20s a 30s (10 segundos) saía com 50 segundos.
/// <para>
/// A correção é <c>-t {duração}</c>. E o padrão passou a reencodar: com
/// <c>-c copy</c> o corte só pode começar e terminar em quadros-chave, o que na
/// mesma medição transformou os 10 segundos pedidos em 30. Para cortes de 15 a 60
/// segundos esse desvio é o conteúdo inteiro. Quem preferir velocidade pode voltar
/// à cópia com <c>Cuts:StreamCopy=true</c>.
/// </para>
/// </remarks>
public class ProcessCutsCommandHandler : ICommandHandler<ProcessCutsCommand, ProcessCutsCommandResponse>
{
    private const string DefaultPreset = "veryfast";
    private const int DefaultCrf = 23;

    private readonly ILogger<ProcessCutsCommandHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWork _uow;

    public ProcessCutsCommandHandler(
        ILogger<ProcessCutsCommandHandler> logger,
        IConfiguration configuration,
        IUnitOfWork uow)
    {
        _logger = logger;
        _configuration = configuration;
        _uow = uow;
    }

    public async Task<ProcessCutsCommandResponse> Handle(ProcessCutsCommand command, CancellationToken ct)
    {
        var cut = command.Cut;

        _logger.LogInformation("[INÍCIO] Processando corte: {CutName} (Vídeo ID: {VideoId})", cut.Name, cut.VideoId);

        var video = await _uow.Repository<Domain.Entities.Video>().GetByIdAsync(cut.VideoId ?? 0)
            ?? throw new CommandOperationException($"Vídeo ID {cut.VideoId} não encontrado.");

        if (string.IsNullOrEmpty(video.VideoPath) || !File.Exists(video.VideoPath))
            throw new CommandOperationException(
                $"Arquivo de vídeo não encontrado para o vídeo: {video.Title} (Path: {video.VideoPath})");

        var duration = cut.FinallyTime - cut.InitialTime;

        if (duration <= TimeSpan.Zero)
            throw new CommandOperationException(
                $"O corte '{cut.Name}' tem duração inválida: início {cut.InitialTime:HH:mm:ss}, " +
                $"fim {cut.FinallyTime:HH:mm:ss}.");

        // Um corte que começa depois do fim do vídeo produz um mp4 válido e
        // vazio: o ffmpeg sai com zero, o arquivo tem cabeçalho mas nenhum
        // quadro. Medido aqui: 1434 bytes e duração "N/A", aceito como sucesso.
        // Com a duração conhecida, isso é detectável antes de gastar o processo.
        if (video.Duration is { } videoDuration && videoDuration > TimeSpan.Zero &&
            cut.InitialTime.ToTimeSpan() >= videoDuration)
        {
            throw new CommandOperationException(
                $"O corte '{cut.Name}' começa em {cut.InitialTime:HH:mm:ss}, depois do fim do vídeo " +
                $"'{video.Title}' ({videoDuration:hh\\:mm\\:ss}).");
        }

        var ffmpegPath = _configuration["FFmpegSettings:BinaryPath"];
        if (string.IsNullOrWhiteSpace(ffmpegPath)) ffmpegPath = "ffmpeg";

        var cutsDirectory = _configuration["CutsPath"] ?? "/app/downloads/cuts";
        Directory.CreateDirectory(cutsDirectory);

        var cutFullPath = Path.Combine(cutsDirectory,
            $"cut_{video.Id}_{cut.Id}_{Guid.NewGuid():N}.mp4");

        var streamCopy = ReadBool("Cuts:StreamCopy", false);

        _logger.LogInformation("[FFMPEG] Gerando corte de {Start} por {Duration} ({Mode}).",
            cut.InitialTime.ToString("HH:mm:ss"), duration, streamCopy ? "cópia de fluxo" : "reencode");

        var arguments = BuildArguments(
            video.VideoPath!, cutFullPath, cut.InitialTime.ToTimeSpan(), duration,
            streamCopy, ReadString("Cuts:Preset", DefaultPreset), ReadInt("Cuts:Crf", DefaultCrf));

        var stdErr = new StringBuilder();

        var result = await Cli.Wrap(ffmpegPath)
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None)
            // Sem isto a saída do ffmpeg era descartada: uma falha aparecia
            // apenas como um número de código de saída, sem o motivo.
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErr))
            .ExecuteAsync(ct);

        if (result.ExitCode != 0)
        {
            // O código anterior aceitava também o código 1 como sucesso. Um
            // arquivo truncado passava e só falhava muito depois, na publicação.
            _logger.LogError("[FFMPEG ERRO] Código {ExitCode}. {Detail}", result.ExitCode, Tail(stdErr.ToString()));
            TryDelete(cutFullPath);

            throw new CommandOperationException(
                $"Falha ao gerar o corte '{cut.Name}' com o ffmpeg (código {result.ExitCode}): {Tail(stdErr.ToString())}");
        }

        if (!File.Exists(cutFullPath))
            throw new CommandOperationException($"Arquivo de corte não foi gerado: {cutFullPath}");

        var fileInfo = new FileInfo(cutFullPath);

        if (fileInfo.Length == 0)
        {
            TryDelete(cutFullPath);
            throw new CommandOperationException($"O corte '{cut.Name}' foi gerado vazio.");
        }

        _logger.LogInformation("[SUCESSO] Corte gerado: {Path} ({Size} bytes, {Duration})",
            cutFullPath, fileInfo.Length, duration);

        // A legenda do corte é recortada AQUI, e não depois, porque a etapa de
        // limpeza apaga a legenda do vídeo assim que todos os cortes dele foram
        // gerados. Depois disso a origem não existe mais.
        var subtitlePath = SliceSubtitle(video, cut, cutFullPath, duration);

        // Um SaveChanges já é atômico; a transação explícita envolvia um único
        // update e é o padrão que gerava o ObjectDisposedException no rollback.
        cut.CutPath = cutFullPath;
        cut.SubtitlePath = subtitlePath;
        cut.SubtitleLanguage = subtitlePath is null ? null : video.Language;
        cut.Status = CutStatusEnum.Process;
        _uow.Repository<Domain.Entities.Cut>().Update(cut);
        await _uow.CommitAsync();

        _logger.LogInformation("[FIM] Corte {CutName} processado com sucesso.", cut.Name);

        return new ProcessCutsCommandResponse(cutFullPath, duration);
    }

    /// <summary>
    /// Recorta a legenda do vídeo na janela do corte e grava ao lado do mp4.
    /// </summary>
    /// <remarks>
    /// Melhor esforço: vídeo sem legenda, ou janela sem nenhuma fala, é resultado
    /// legítimo. Perder o corte por causa da legenda não seria.
    /// </remarks>
    private string? SliceSubtitle(
        Domain.Entities.Video video, Domain.Entities.Cut cut, string cutFullPath, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(video.SubtitlePath))
        {
            _logger.LogInformation("[LEGENDA] O vídeo '{Title}' não tem legenda; o corte fica sem.", video.Title);
            return null;
        }

        if (!File.Exists(video.SubtitlePath))
        {
            _logger.LogWarning(
                "[LEGENDA] A legenda do vídeo '{Title}' não está mais em disco ({Path}). O corte fica sem legenda.",
                video.Title, video.SubtitlePath);
            return null;
        }

        try
        {
            var start = cut.InitialTime.ToTimeSpan();

            var target = Path.ChangeExtension(cutFullPath, ".srt");
            var written = SubtitleSlicer.SliceFile(video.SubtitlePath, start, start + duration, target);

            if (written is null)
            {
                _logger.LogInformation(
                    "[LEGENDA] Nenhuma fala da legenda cai entre {Start} e {End}; o corte fica sem legenda.",
                    start, start + duration);
                return null;
            }

            _logger.LogInformation("[LEGENDA] Legenda do corte gravada: {Path}", written);
            return written;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[LEGENDA] Falha ao recortar a legenda do corte '{Name}': {Message}", cut.Name, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Monta os argumentos do ffmpeg. Público para ser verificável: é aqui que
    /// morava o erro de duração.
    /// </summary>
    /// <remarks>
    /// <c>-ss</c> vem <b>antes</b> do <c>-i</c> de propósito: assim o ffmpeg
    /// posiciona a leitura em vez de decodificar desde o começo, o que é a
    /// diferença entre segundos e minutos num vídeo longo. E o recorte usa
    /// <c>-t</c> (duração), não <c>-to</c> (instante), porque depois do <c>-ss</c>
    /// a linha de tempo da saída começa em zero.
    /// </remarks>
    public static IReadOnlyList<string> BuildArguments(
        string inputPath,
        string outputPath,
        TimeSpan start,
        TimeSpan duration,
        bool streamCopy,
        string preset,
        int crf)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-nostdin",
            "-loglevel", "error",
            "-y",
            "-ss", Seconds(start),
            "-i", inputPath,
            "-t", Seconds(duration),
        };

        if (streamCopy)
        {
            args.Add("-c");
            args.Add("copy");
            args.Add("-avoid_negative_ts");
            args.Add("make_zero");
        }
        else
        {
            args.AddRange(new[]
            {
                "-c:v", "libx264",
                "-preset", preset,
                "-crf", crf.ToString(CultureInfo.InvariantCulture),
                "-pix_fmt", "yuv420p",
                "-c:a", "aac",
                "-b:a", "128k",
                // Alinha o começo da saída em zero: sem isto o corte pode
                // carregar o deslocamento do -ss nos timestamps.
                "-reset_timestamps", "1",
            });
        }

        // Deixa o índice no início do arquivo, para o corte poder ser lido em
        // streaming pelas plataformas de publicação.
        args.Add("-movflags");
        args.Add("+faststart");
        args.Add(outputPath);

        return args;
    }

    private static string Seconds(TimeSpan value) =>
        value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Últimas linhas do stderr, para a mensagem não virar um despejo.</summary>
    private static string Tail(string stdErr)
    {
        if (string.IsNullOrWhiteSpace(stdErr)) return "(sem saída do ffmpeg)";

        var lines = stdErr.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .TakeLast(3);

        var text = string.Join(" | ", lines);
        return text.Length > 500 ? text[..500] + "…" : text;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Não foi possível remover o corte parcial {Path}: {Message}", path, ex.Message);
        }
    }

    private string ReadString(string key, string fallback) =>
        string.IsNullOrWhiteSpace(_configuration[key]) ? fallback : _configuration[key]!;

    private int ReadInt(string key, int fallback) =>
        int.TryParse(_configuration[key], out var value) && value >= 0 ? value : fallback;

    private bool ReadBool(string key, bool fallback) =>
        bool.TryParse(_configuration[key], out var value) ? value : fallback;
}
