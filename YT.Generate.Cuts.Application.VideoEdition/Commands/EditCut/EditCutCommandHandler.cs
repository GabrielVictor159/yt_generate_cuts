using System.Text;
using CliWrap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Exceptions;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoEdition.Helpers;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Application.VideoEdition.Commands.EditCut;

/// <summary>
/// Edita o arquivo do corte conforme a configuração de edição do canal:
/// resolução de saída e, opcionalmente, legenda embutida.
/// </summary>
/// <remarks>
/// A configuração vem do canal monitorado do vídeo de origem
/// (<c>MonitoringChannel.EditionConfiguration</c>). Sem configuração no canal, os
/// valores da seção <c>Edition</c> valem como padrão — a etapa nunca fica sem
/// resposta, porque um corte parado aqui nunca chegaria à publicação.
/// </remarks>
public class EditCutCommandHandler : ICommandHandler<EditCutCommand, EditCutCommandResponse>
{
    private const int DefaultWidth = 1080;
    private const int DefaultHeight = 1920;
    private const string DefaultPreset = "veryfast";
    private const int DefaultCrf = 23;

    private readonly ILogger<EditCutCommandHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWork _uow;

    public EditCutCommandHandler(
        ILogger<EditCutCommandHandler> logger,
        IConfiguration configuration,
        IUnitOfWork uow)
    {
        _logger = logger;
        _configuration = configuration;
        _uow = uow;
    }

    public async Task<EditCutCommandResponse> Handle(EditCutCommand command, CancellationToken ct)
    {
        var cut = command.Cut;

        _logger.LogInformation("[INÍCIO] Editando corte: {CutName} (ID {CutId})", cut.Name, cut.Id);

        if (string.IsNullOrWhiteSpace(cut.CutPath) || !File.Exists(cut.CutPath))
            throw new CommandOperationException(
                $"Arquivo do corte não encontrado para '{cut.Name}' (Path: {cut.CutPath}).");

        var settings = ResolveSettings(cut);

        // Etapa desligada: o corte apenas avança, sem tocar no arquivo. É o
        // escape para quem não quer edição nenhuma — sem isso o corte ficaria
        // parado em Process e nunca seria publicado.
        if (!ReadBool("Edition:Enabled", true))
        {
            _logger.LogInformation("[EDIÇÃO] Desativada por configuração (Edition:Enabled). O corte avança sem alteração.");
            await AdvanceAsync(cut);
            return new EditCutCommandResponse(cut.CutPath!, settings.Width, settings.Height, false);
        }

        var subtitlePath = ResolveSubtitle(cut, settings);

        var directory = Path.GetDirectoryName(cut.CutPath!) ?? ".";
        var editedPath = Path.Combine(directory,
            $"edit_{cut.VideoId ?? 0}_{cut.Id}_{Guid.NewGuid():N}.mp4");

        _logger.LogInformation(
            "[FFMPEG] Editando para {Width}x{Height}{Subtitle}.",
            settings.Width, settings.Height,
            subtitlePath is null ? " sem legenda embutida" : " com legenda embutida");

        var ffmpegPath = _configuration["FFmpegSettings:BinaryPath"];
        if (string.IsNullOrWhiteSpace(ffmpegPath)) ffmpegPath = "ffmpeg";

        var stdErr = new StringBuilder();

        var result = await Cli.Wrap(ffmpegPath)
            .WithArguments(EditionArguments.Build(cut.CutPath!, editedPath, subtitlePath, settings))
            .WithValidation(CommandResultValidation.None)
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErr))
            .ExecuteAsync(ct);

        if (result.ExitCode != 0)
        {
            TryDelete(editedPath);
            var detail = Tail(stdErr.ToString());
            _logger.LogError("[FFMPEG ERRO] Código {Code}. {Detail}", result.ExitCode, detail);

            throw new CommandOperationException(
                $"Falha ao editar o corte '{cut.Name}' (código {result.ExitCode}): {detail}");
        }

        if (!File.Exists(editedPath) || new FileInfo(editedPath).Length == 0)
        {
            TryDelete(editedPath);
            throw new CommandOperationException($"A edição do corte '{cut.Name}' não produziu arquivo utilizável.");
        }

        var original = cut.CutPath!;

        // O arquivo editado passa a SER o arquivo do corte: é ele que será
        // publicado. Guardar os dois dobraria o disco por corte, e foi o acúmulo
        // de arquivos que motivou os tetos deste fluxo.
        cut.CutPath = editedPath;
        cut.Status = CutStatusEnum.Edit;
        _uow.Repository<Domain.Entities.Cut>().Update(cut);
        await _uow.CommitAsync();

        if (ReadBool("Edition:KeepOriginal", false))
            _logger.LogInformation("[EDIÇÃO] O arquivo pré-edição foi mantido: {Path}", original);
        else
            TryDelete(original);

        _logger.LogInformation("[FIM] Corte '{Name}' editado: {Path} ({Size} bytes).",
            cut.Name, editedPath, new FileInfo(editedPath).Length);

        return new EditCutCommandResponse(editedPath, settings.Width, settings.Height, subtitlePath is not null);
    }

    // ------------------------------------------------------------------

    /// <summary>
    /// Configuração do canal quando existe; padrão global quando não.
    /// </summary>
    private EditionSettings ResolveSettings(Domain.Entities.Cut cut)
    {
        var config = cut.Video?.Channel?.EditionConfiguration;

        var width = config?.Width ?? ReadInt("Edition:Width", DefaultWidth);
        var height = config?.Height ?? ReadInt("Edition:Height", DefaultHeight);
        var burn = config?.BurnSubtitles ?? ReadBool("Edition:BurnSubtitles", true);

        // O libx264 exige dimensões pares no yuv420p; um valor ímpar faria o
        // ffmpeg falhar com "width not divisible by 2" depois de todo o trabalho.
        width = MakeEven(width);
        height = MakeEven(height);

        if (config is null)
        {
            _logger.LogInformation(
                "[EDIÇÃO] O canal '{Channel}' não tem perfil de edição; usando o padrão global {Width}x{Height}.",
                cut.Video?.Channel?.Name ?? "(desconhecido)", width, height);
        }
        else
        {
            _logger.LogInformation("[EDIÇÃO] Perfil '{Profile}' do canal '{Channel}': {Width}x{Height}, legenda {Burn}.",
                config.Name, cut.Video?.Channel?.Name, width, height, burn ? "embutida" : "não embutida");
        }

        return new EditionSettings(
            width, height, burn,
            ReadString("Edition:Preset", DefaultPreset),
            ReadInt("Edition:Crf", DefaultCrf));
    }

    /// <summary>
    /// Caminho da legenda a queimar, ou <c>null</c> quando não há o que queimar.
    /// </summary>
    private string? ResolveSubtitle(Domain.Entities.Cut cut, EditionSettings settings)
    {
        if (!settings.BurnSubtitles) return null;

        if (string.IsNullOrWhiteSpace(cut.SubtitlePath))
        {
            // Acontece quando o vídeo original não tinha legenda, ou quando
            // nenhuma fala caía na janela do corte. Entregar o corte sem texto é
            // melhor do que não entregar.
            _logger.LogInformation(
                "[LEGENDA] O corte '{Name}' não tem legenda; será editado sem texto embutido.", cut.Name);
            return null;
        }

        if (!File.Exists(cut.SubtitlePath))
        {
            _logger.LogWarning(
                "[LEGENDA] A legenda do corte '{Name}' não está em disco ({Path}). Editando sem texto embutido.",
                cut.Name, cut.SubtitlePath);
            return null;
        }

        return cut.SubtitlePath;
    }

    private async Task AdvanceAsync(Domain.Entities.Cut cut)
    {
        cut.Status = CutStatusEnum.Edit;
        _uow.Repository<Domain.Entities.Cut>().Update(cut);
        await _uow.CommitAsync();
    }

    /// <summary>Arredonda para o par mais próximo, para baixo, com piso em 2.</summary>
    internal static int MakeEven(int value) => value <= 2 ? 2 : value - (value % 2);

    private static string Tail(string stdErr)
    {
        if (string.IsNullOrWhiteSpace(stdErr)) return "(sem saída do ffmpeg)";

        var text = string.Join(" | ", stdErr.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .TakeLast(3));

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
            _logger.LogWarning("Não foi possível remover {Path}: {Message}", path, ex.Message);
        }
    }

    private string ReadString(string key, string fallback) =>
        string.IsNullOrWhiteSpace(_configuration[key]) ? fallback : _configuration[key]!;

    private int ReadInt(string key, int fallback) =>
        int.TryParse(_configuration[key], out var value) && value > 0 ? value : fallback;

    private bool ReadBool(string key, bool fallback) =>
        bool.TryParse(_configuration[key], out var value) ? value : fallback;
}
