using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Application.VideoProcessing.Commands.RemoveVideos;

/// <summary>
/// Apaga os arquivos do vídeo depois que todos os cortes dele já foram gerados.
/// </summary>
/// <remarks>
/// A versão anterior chamava <c>Directory.Delete(command.Video.VideoPath)</c>.
/// <c>VideoPath</c> é <b>arquivo</b>, não diretório, então isso lançava
/// <c>DirectoryNotFoundException</c> em toda execução — e a exceção era relançada,
/// abortando a limpeza dos vídeos seguintes. O log mostrava o mesmo erro no mesmo
/// vídeo a cada minuto, indefinidamente.
/// <para>
/// Além do <c>Delete</c> certo, três mudanças fazem a etapa realmente concluir:
/// arquivo ausente passa a ser sucesso (é o estado desejado, não um erro); a
/// legenda também é apagada e o campo limpo; e o vídeo avança para
/// <see cref="VideoStatusEnum.Removed"/> — status que já existia no enum e que
/// ninguém no fluxo definia, motivo pelo qual os mesmos vídeos voltavam à
/// consulta em todo ciclo.
/// </para>
/// </remarks>
public class RemoveVideosCommandHandler : ICommandHandler<RemoveVideosCommand>
{
    private readonly ILogger<RemoveVideosCommandHandler> _logger;
    private readonly IUnitOfWork _uow;

    public RemoveVideosCommandHandler(ILogger<RemoveVideosCommandHandler> logger, IUnitOfWork uow)
    {
        _logger = logger;
        _uow = uow;
    }

    public async Task Handle(RemoveVideosCommand command, CancellationToken ct)
    {
        var video = command.Video;

        _logger.LogInformation("[INÍCIO] Limpando arquivos do vídeo: {VideoName} (Vídeo ID: {VideoId})",
            video.Title, video.Id);

        var pending = new List<string>();

        if (!TryDeleteFile(video.VideoPath, "vídeo")) pending.Add(video.VideoPath!);
        if (!TryDeleteFile(video.SubtitlePath, "legenda")) pending.Add(video.SubtitlePath!);

        // Sobras do download: quando o merge do yt-dlp não conclui, ficam os
        // arquivos por formato (`<handle>.f398.mp4`, `<handle>.f251.webm`). Eles
        // não estão no banco, então só saem daqui.
        RemoveLeftovers(video.VideoPath);

        if (pending.Count > 0)
        {
            // Não avança o status: o arquivo continua no disco e a próxima rodada
            // tenta de novo. Se fosse um bloqueio momentâneo do ffmpeg, resolve
            // sozinho; se for permissão, o aviso fica visível sem virar exceção.
            _logger.LogWarning(
                "[PENDENTE] {Count} arquivo(s) do vídeo '{Title}' não puderam ser apagados agora. " +
                "O vídeo permanece na fila de limpeza para nova tentativa.", pending.Count, video.Title);
            return;
        }

        video.VideoPath = null;
        video.SubtitlePath = null;
        video.Status = VideoStatusEnum.Removed;

        // Um SaveChanges já é atômico. A transação explícita que existia aqui
        // envolvia um único update — é o padrão que gerava o
        // ObjectDisposedException no rollback.
        _uow.Repository<Domain.Entities.Video>().Update(video);
        await _uow.CommitAsync();

        _logger.LogInformation("[FIM] Arquivos do vídeo '{Title}' removidos. Status: {Status}.",
            video.Title, VideoStatusEnum.Removed);
    }

    /// <summary>
    /// Apaga um arquivo. Caminho vazio ou arquivo inexistente contam como
    /// sucesso: o resultado desejado é "não existe mais".
    /// </summary>
    private bool TryDeleteFile(string? path, string what)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        try
        {
            if (!File.Exists(path))
            {
                _logger.LogDebug("[LIMPEZA] O arquivo de {What} já não existe: {Path}", what, path);
                return true;
            }

            File.Delete(path);
            _logger.LogInformation("[LIMPEZA] Arquivo de {What} removido: {Path}", what, path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[LIMPEZA] Não foi possível remover o arquivo de {What} ({Path}): {Message}",
                what, path, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Remove os irmãos do mesmo download. Só age quando o nome base é um GUID de
    /// 32 hexadecimais — o formato que o downloader gera — para nunca varrer
    /// arquivos de outra origem que estejam na mesma pasta.
    /// </summary>
    private void RemoveLeftovers(string? videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath)) return;

        try
        {
            var directory = Path.GetDirectoryName(videoPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

            var fileName = Path.GetFileName(videoPath);
            var handle = fileName.Split('.')[0];

            if (!IsDownloadHandle(handle)) return;

            foreach (var leftover in Directory.EnumerateFiles(directory, handle + ".*"))
            {
                try
                {
                    File.Delete(leftover);
                    _logger.LogInformation("[LIMPEZA] Sobra do download removida: {Path}", leftover);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[LIMPEZA] Não foi possível remover a sobra {Path}: {Message}", leftover, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[LIMPEZA] Falha ao varrer sobras de {Path}: {Message}", videoPath, ex.Message);
        }
    }

    private static bool IsDownloadHandle(string value) =>
        value.Length == 32 && value.All(Uri.IsHexDigit);
}
