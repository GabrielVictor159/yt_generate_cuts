using AngleSharp.Dom;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YoutubeExplode;
using YT.Generate.Cuts.Application.Abstractions.Common;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoExtraction.Commands.Download;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Application.VideoExtraction.Commands.MonitoringChannel;
public class MonitoringChannelCommandHandler : ICommandHandler<MonitoringChannelCommand>
{
    private readonly ILogger<MonitoringChannelCommandHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWork _uow;
    private readonly IAppDispatcher _appDispatcher;

    public MonitoringChannelCommandHandler(ILogger<MonitoringChannelCommandHandler> logger, 
        IConfiguration configuration, IUnitOfWork uow, IAppDispatcher appDispatcher)
    {
        _logger = logger;
        _configuration = configuration;
        _uow = uow;
        _appDispatcher = appDispatcher;
    }

    public async Task Handle(MonitoringChannelCommand command, CancellationToken ct)
    {
        _logger.LogInformation("[INÍCIO] Iniciando monitoramento para o canal: {ChannelName}", command.Channel.Name);

        try
        {
            _logger.LogDebug("Buscando vídeos existentes no banco para o ChannelId: {Id}", command.Channel.Id);
            command.Channel.Videos = (await _uow.Repository<Domain.Entities.Video>()
                .FindAsync(e => e.ChannelId == command.Channel.Id)).ToList();

            _logger.LogInformation("Total de vídeos carregados do banco local: {Count}", command.Channel.Videos.Count);

            var youtube = new YoutubeClient();

            _logger.LogDebug("Conectando ao YoutubeExplode para o canal: {Url}", command.Channel.Url);
            var channel = command.Channel.Url.Contains("@")
                ? await youtube.Channels.GetByHandleAsync(command.Channel.Url)
                : await youtube.Channels.GetAsync(command.Channel.Url);

            _logger.LogInformation("Canal identificado no YouTube: {Title} (ID: {Id})", channel.Title, channel.Id);

            var uploads = youtube.Channels.GetUploadsAsync(channel.Id);

            await foreach (var video in uploads)
            {
                _logger.LogDebug("Verificando vídeo da lista de uploads: {VideoTitle} ({VideoUrl})", video.Title, video.Url);

                var existingVideo = command.Channel.Videos.FirstOrDefault(e => e.Url.Equals(video.Url));

                if(existingVideo==null)
                {
                    var videoDetails = await youtube.Videos.GetAsync(video.Id, ct);
                    bool isShort = videoDetails.Duration.HasValue && videoDetails.Duration.Value.TotalSeconds <= 300;

                    if (!command.IncludeShorts && isShort)
                    {
                        _logger.LogInformation("[SKIP] Shorts ignorado por configuração: {Title}", video.Title);
                        continue; 
                    }

                    await _uow.BeginTransactionAsync();

                    var newVideo = new Domain.Entities.Video()
                    {
                        Title = video.Title,
                        Url = video.Url,
                        ChannelId = command.Channel.Id,
                        Status = Domain.Enums.VideoStatusEnum.Created
                    };

                    await _uow.Repository<Domain.Entities.Video>().AddAsync(newVideo);

                    await _uow.CommitAsync();
                    await _uow.CommitTransactionAsync();
                    _logger.LogInformation("Novo registro de vídeo inserido com sucesso para a URL: {Url}", video.Url);

                    var downloadPath = _configuration["DownloadPath"] ?? "/app/downloads";
                    _logger.LogInformation("Disparando DownloadVideoCommand para o diretório: {Path}", downloadPath);

                    var downloadCommand = new DownloadVideoCommand(video.Url, downloadPath);
                    var responseDownload = await _appDispatcher.Send(downloadCommand);

                    if (responseDownload != null)
                    {
                        _logger.LogInformation("[DOWNLOAD OK] Arquivos baixados. Vídeo: {VPath}, Legenda: {SPath}",
                            responseDownload.videoPath, responseDownload.subtitlePath);

                        _logger.LogDebug("Abrindo segunda transação (Atualização de paths)");
                        await _uow.BeginTransactionAsync();

                        newVideo.VideoPath = responseDownload.videoPath;
                        newVideo.SubtitlePath = responseDownload.subtitlePath;
                        newVideo.Status = Domain.Enums.VideoStatusEnum.Download;
                        newVideo.Language = responseDownload.language;
                        newVideo.Duration = responseDownload.duration;

                        await _uow.CommitAsync();
                        await _uow.CommitTransactionAsync();
                        _logger.LogInformation("Paths de arquivo atualizados no banco de dados.");
                    }
                    else
                    {
                        _logger.LogWarning("[DOWNLOAD FAIL] O comando de download retornou nulo para o vídeo: {VideoTitle}", video.Title);
                    }

                    _logger.LogInformation("[FIM DO LOOP] Encerrando monitoramento após o primeiro match conforme lógica do break.");
                    break;
                }
            }

            _logger.LogInformation("[CONCLUÍDO] Método Handle finalizado para o canal {ChannelName}.", command.Channel.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ERRO CRÍTICO] Falha no monitoramento do canal: {channel}. Mensagem: {msg}",
                command.Channel.Name, ex.Message);

            await _uow.RollbackTransactionAsync();
            throw;
        }
    }
}
