using Microsoft.AspNetCore.Mvc;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoEdition.Commands.EditCut;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.ProcessCuts;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.QuerysVideoProcessing;
using YT.Generate.Cuts.Application.VideoPublish.Commands;
using YT.Generate.Cuts.Domain.Entities;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CutsController : ControllerBase
{
    private readonly IAppDispatcher _dispatcher;
    private readonly IUnitOfWork _uow;

    public CutsController(IAppDispatcher dispatcher, IUnitOfWork uow)
    {
        _dispatcher = dispatcher;
        _uow = uow;
    }

    /// <summary>
    /// Lista os cortes. O vídeo e o canal de publicação vêm no mesmo retorno
    /// porque a tela mostra o título do vídeo e o nome do canal — sem o include
    /// ela só teria os ids e precisaria de uma requisição por linha.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<Cut>>> GetAll([FromQuery] CutStatusEnum? status = null)
    {
        var cuts = status.HasValue
            ? await _dispatcher.Send(new GetAllCuts(c => c.Status == status.Value, c => c.Video!, c => c.PublishChannel!))
            : await _dispatcher.Send(new GetAllCuts(null, c => c.Video!, c => c.PublishChannel!));

        // Mais recentes primeiro: é a ordem útil numa tela de acompanhamento.
        return Ok(cuts.OrderByDescending(c => c.Id).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Cut>> GetById(long id)
    {
        var cuts = await _dispatcher.Send(new GetAllCuts(c => c.Id == id, c => c.Video!, c => c.PublishChannel!));
        var cut = cuts.FirstOrDefault();
        if (cut == null) return NotFound();
        return Ok(cut);
    }

    [HttpPost]
    public async Task<ActionResult<Cut>> Create([FromBody] CreateCutRequest request)
    {
        var video = await _uow.Repository<Video>().GetByIdAsync(request.VideoId);
        if (video == null) return BadRequest("Vídeo não encontrado");

        var cut = new Cut
        {
            Name = request.Name,
            Description = request.Description,
            InitialTime = TimeOnly.Parse(request.InitialTime),
            FinallyTime = TimeOnly.Parse(request.FinallyTime),
            CutPath = string.Empty,
            Status = CutStatusEnum.Created,
            VideoId = request.VideoId,
            PublishChannelId = request.PublishChannelId
        };

        await _uow.Repository<Cut>().AddAsync(cut);
        await _uow.CommitAsync();

        return CreatedAtAction(nameof(GetById), new { id = cut.Id }, cut);
    }

    [HttpPut("{id}/publish-channel")]
    public async Task<ActionResult> AssignPublishChannel(long id, [FromBody] AssignPublishChannelRequest request)
    {
        var cut = await _uow.Repository<Cut>().GetByIdAsync(id);
        if (cut == null) return NotFound();

        var channel = await _uow.Repository<PublishChannel>().GetByIdAsync(request.PublishChannelId);
        if (channel == null) return BadRequest("Canal de publicação não encontrado");

        cut.PublishChannelId = request.PublishChannelId;
        _uow.Repository<Cut>().Update(cut);
        await _uow.CommitAsync();

        return NoContent();
    }

    [HttpPost("{id}/process")]
    public async Task<ActionResult> ProcessCut(long id)
    {
        var cut = await _uow.Repository<Cut>().GetByIdAsync(id);
        if (cut == null) return NotFound();

        await _dispatcher.Send(new ProcessCutsCommand(cut));
        return Accepted();
    }

    /// <summary>
    /// Edita o corte conforme o perfil do canal (resolução e legenda embutida).
    /// Etapa entre a geração do arquivo e a publicação.
    /// </summary>
    [HttpPost("{id}/edit")]
    public async Task<ActionResult> EditCut(long id)
    {
        // Com os includes: a resolução vem do perfil de edição do canal do vídeo.
        var cuts = await _dispatcher.Send(new GetAllCuts(
            c => c.Id == id, c => c.Video!, c => c.Video!.Channel!, c => c.Video!.Channel!.EditionConfiguration!));

        var cut = cuts.FirstOrDefault();
        if (cut == null) return NotFound();

        var result = await _dispatcher.Send(new EditCutCommand(cut));

        return Ok(new
        {
            path = result.EditedPath,
            width = result.Width,
            height = result.Height,
            subtitlesBurned = result.SubtitlesBurned
        });
    }

    [HttpPost("{id}/publish")]
    public async Task<ActionResult> PublishCut(long id)
    {
        var cut = await _uow.Repository<Cut>().GetByIdAsync(id);
        if (cut == null) return NotFound();

        var result = await _dispatcher.Send(new PublishCutCommand(cut));
        if (result.Success)
            return Ok(new { postId = result.PlatformPostId, url = result.PostUrl });

        return StatusCode(500, new { error = result.ErrorMessage });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(long id)
    {
        var cut = await _uow.Repository<Cut>().GetByIdAsync(id);
        if (cut == null) return NotFound();

        _uow.Repository<Cut>().Delete(cut);
        await _uow.CommitAsync();

        return NoContent();
    }
}

public record CreateCutRequest(string Name, string? Description, string InitialTime, string FinallyTime, long VideoId, long? PublishChannelId);
public record AssignPublishChannelRequest(long PublishChannelId);