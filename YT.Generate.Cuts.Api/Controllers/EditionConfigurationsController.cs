using Microsoft.AspNetCore.Mvc;
using YT.Generate.Cuts.Domain.Entities;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Api.Controllers;

/// <summary>
/// Perfis de edição: resolução de saída e legenda embutida.
/// </summary>
/// <remarks>
/// Um perfil pode ser usado por vários canais monitorados. Apagar um perfil em
/// uso não apaga os canais: eles voltam ao padrão global (a chave estrangeira é
/// SetNull).
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class EditionConfigurationsController : ControllerBase
{
    /// <summary>Limites de sanidade. Acima disso o ffmpeg trabalha muito para nada.</summary>
    private const int MinDimension = 128;
    private const int MaxDimension = 3840;

    private readonly IUnitOfWork _uow;

    public EditionConfigurationsController(IUnitOfWork uow) => _uow = uow;

    [HttpGet]
    public async Task<ActionResult<List<EditionConfiguration>>> GetAll()
    {
        var items = await _uow.Repository<EditionConfiguration>().FindAsync(_ => true);
        return Ok(items.OrderBy(e => e.Name).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<EditionConfiguration>> GetById(long id)
    {
        var item = await _uow.Repository<EditionConfiguration>().GetByIdAsync(id);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<EditionConfiguration>> Create([FromBody] EditionConfigurationRequest request)
    {
        Validate(request);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var config = new EditionConfiguration
        {
            Name = request.Name.Trim(),
            Width = request.Width,
            Height = request.Height,
            BurnSubtitles = request.BurnSubtitles,
        };

        await _uow.Repository<EditionConfiguration>().AddAsync(config);
        await _uow.CommitAsync();

        return CreatedAtAction(nameof(GetById), new { id = config.Id }, config);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> Update(long id, [FromBody] EditionConfigurationRequest request)
    {
        var config = await _uow.Repository<EditionConfiguration>().GetByIdAsync(id);
        if (config is null) return NotFound();

        Validate(request);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        config.Name = request.Name.Trim();
        config.Width = request.Width;
        config.Height = request.Height;
        config.BurnSubtitles = request.BurnSubtitles;

        _uow.Repository<EditionConfiguration>().Update(config);
        await _uow.CommitAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(long id)
    {
        var config = await _uow.Repository<EditionConfiguration>().GetByIdAsync(id);
        if (config is null) return NotFound();

        _uow.Repository<EditionConfiguration>().Delete(config);
        await _uow.CommitAsync();

        return NoContent();
    }

    private void Validate(EditionConfigurationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            ModelState.AddModelError(nameof(request.Name), "O nome do perfil é obrigatório.");

        foreach (var (value, field) in new[]
                 {
                     (request.Width, nameof(request.Width)),
                     (request.Height, nameof(request.Height)),
                 })
        {
            if (value < MinDimension || value > MaxDimension)
                ModelState.AddModelError(field,
                    $"O valor deve estar entre {MinDimension} e {MaxDimension} pixels.");
            else if (value % 2 != 0)
                // O libx264 em yuv420p exige dimensões pares; recusar aqui é
                // melhor do que descobrir na primeira edição.
                ModelState.AddModelError(field, "O valor deve ser par.");
        }
    }
}

public record EditionConfigurationRequest(string Name, int Width, int Height, bool BurnSubtitles);
