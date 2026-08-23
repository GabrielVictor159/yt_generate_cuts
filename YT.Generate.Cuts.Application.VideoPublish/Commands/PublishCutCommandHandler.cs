using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Exceptions;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Domain.Entities;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;
using YT.Generate.Cuts.Publish.Abstractions;

namespace YT.Generate.Cuts.Application.VideoPublish.Commands;

public class PublishCutCommandHandler : ICommandHandler<PublishCutCommand, PublishCutCommandResponse>
{
    private readonly ILogger<PublishCutCommandHandler> _logger;
    private readonly IUnitOfWork _uow;
    private readonly IEnumerable<IPublishPlugin> _publishPlugins;

    public PublishCutCommandHandler(
        ILogger<PublishCutCommandHandler> logger,
        IUnitOfWork uow,
        IEnumerable<IPublishPlugin> publishPlugins)
    {
        _logger = logger;
        _uow = uow;
        _publishPlugins = publishPlugins;
    }

    public async Task<PublishCutCommandResponse> Handle(PublishCutCommand command, CancellationToken ct)
    {
        _logger.LogInformation("[INÍCIO] Publicando corte: {CutName} (ID: {CutId})",
            command.Cut.Name, command.Cut.Id);

        try
        {
            // Validar se o corte tem caminho de arquivo
            if (string.IsNullOrEmpty(command.Cut.CutPath) || !File.Exists(command.Cut.CutPath))
            {
                throw new CommandOperationException(
                    $"Arquivo de corte não encontrado para publicação: {command.Cut.CutPath}");
            }

            // Buscar o canal de publicação associado ao corte (via PublishChannelId)
            if (command.Cut.PublishChannelId == null)
            {
                _logger.LogWarning("[PUBLISH] Corte {CutName} não possui canal de publicação associado.", command.Cut.Name);
                return new PublishCutCommandResponse(false, null, null,
                    "Corte não possui canal de publicação associado.", command.Cut);
            }

            var publishChannel = await _uow.Repository<PublishChannel>()
                .GetByIdAsync(command.Cut.PublishChannelId.Value);

            if (publishChannel == null)
            {
                throw new CommandOperationException(
                    $"Canal de publicação ID {command.Cut.PublishChannelId} não encontrado.");
            }

            // Selecionar o plugin de publicação baseado no tipo
            var plugin = SelectPublishPlugin(publishChannel.TypePublish);
            if (plugin == null)
            {
                _logger.LogError("[PUBLISH] Nenhum plugin encontrado para o tipo: {Type}", publishChannel.TypePublish);
                return new PublishCutCommandResponse(false, null, null,
                    $"Nenhum plugin de publicação configurado para o tipo: {publishChannel.TypePublish}",
                    command.Cut);
            }

            _logger.LogInformation("[PUBLISH] Plugin selecionado: {Plugin} para plataforma: {Platform}",
                plugin.GetType().Name, plugin.PlatformName);

            // Buscar o vídeo associado para obter título e descrição
            var video = command.Cut.VideoId.HasValue
                ? await _uow.Repository<Domain.Entities.Video>().GetByIdAsync(command.Cut.VideoId.Value)
                : null;

            var title = command.Cut.Name;
            var description = command.Cut.Description
                ?? $"Corte do vídeo: {video?.Title ?? "Vídeo"} - {command.Cut.InitialTime} até {command.Cut.FinallyTime}";

            // Executar a publicação
            var credentials = new PublishCredentials(
                Login: publishChannel.Login,
                Password: publishChannel.Password);

            var publishResult = await plugin.PublishAsync(
                command.Cut.CutPath,
                title,
                description,
                credentials,
                ct);

            // Atualizar o registro do corte no banco
            await _uow.BeginTransactionAsync();

            if (publishResult.Success)
            {
                command.Cut.Status = CutStatusEnum.Publish;
                _logger.LogInformation("[PUBLISH OK] Corte {CutName} publicado com sucesso! ID: {PostId}",
                    command.Cut.Name, publishResult.PlatformPostId);
            }
            else
            {
                _logger.LogError("[PUBLISH FAIL] Falha ao publicar corte {CutName}: {Error}",
                    command.Cut.Name, publishResult.ErrorMessage);
            }

            _uow.Repository<Domain.Entities.Cut>().Update(command.Cut);
            await _uow.CommitAsync();
            await _uow.CommitTransactionAsync();

            _logger.LogInformation("[FIM] Publicação do corte {CutName} concluída.", command.Cut.Name);

            return new PublishCutCommandResponse(
                publishResult.Success,
                publishResult.PlatformPostId,
                publishResult.PostUrl,
                publishResult.ErrorMessage,
                command.Cut);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[CANCELADO] Publicação do corte foi cancelada: {CutName}", command.Cut.Name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ERRO CRÍTICO] Falha ao publicar corte: {CutName}", command.Cut.Name);
            return new PublishCutCommandResponse(false, null, null, ex.Message, command.Cut);
        }
    }

    private IPublishPlugin? SelectPublishPlugin(TypePublishEnum typePublish)
    {
        return typePublish switch
        {
            TypePublishEnum.TIKTOK => _publishPlugins.FirstOrDefault(p =>
                p.PlatformName.Equals("TikTok", StringComparison.OrdinalIgnoreCase)),
            _ => _publishPlugins.FirstOrDefault()
        };
    }
}