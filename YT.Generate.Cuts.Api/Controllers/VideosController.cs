using Microsoft.AspNetCore.Mvc;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.QuerysVideoProcessing;
using YT.Generate.Cuts.Domain.Entities;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VideosController : ControllerBase
{
    private readonly IAppDispatcher _dispatcher;
    private readonly IUnitOfWork _uow;

    public VideosController(IAppDispatcher dispatcher, IUnitOfWork uow)
    {
        _dispatcher = dispatcher;
        _uow = uow;
    }

    [HttpGet]
    public async Task<ActionResult<List<Video>>> GetAll([FromQuery] VideoStatusEnum? status = null)
    {
        var videos = status.HasValue
            ? await _dispatcher.Send(new GetAllVideo(v => v.Status == status.Value))
            : await _dispatcher.Send(new GetAllVideo(null));
        return Ok(videos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Video>> GetById(long id)
    {
        var videos = await _dispatcher.Send(
            new GetAllVideo(v => v.Id == id, v => v.Cuts, v => v.Channel));
        var video = videos.FirstOrDefault()!;
        if (video == null) return NotFound();
        return Ok(video);
    }

    [HttpPut("{id}/status")]
    public async Task<ActionResult> UpdateStatus(long id, [FromBody] UpdateVideoStatusRequest request)
    {
        var video = await _uow.Repository<Video>().GetByIdAsync(id);
        if (video == null) return NotFound();

        video.Status = Enum.Parse<VideoStatusEnum>(request.Status, true);
        _uow.Repository<Video>().Update(video);
        await _uow.CommitAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(long id)
    {
        var video = await _uow.Repository<Video>().GetByIdAsync(id);
        if (video == null) return NotFound();

        video.Status = VideoStatusEnum.Removed;
        _uow.Repository<Video>().Update(video);
        await _uow.CommitAsync();

        return NoContent();
    }
}

public record UpdateVideoStatusRequest(string Status);