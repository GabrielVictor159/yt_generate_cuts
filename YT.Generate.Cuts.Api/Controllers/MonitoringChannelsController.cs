using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoExtraction.Commands.MonitoringChannel;
using YT.Generate.Cuts.Application.VideoExtraction.Commands.QuerysVideoExtraction;
using YT.Generate.Cuts.Domain.Entities;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MonitoringChannelsController : ControllerBase
{
    private readonly IAppDispatcher _dispatcher;
    private readonly IUnitOfWork _uow;

    public MonitoringChannelsController(IAppDispatcher dispatcher, IUnitOfWork uow)
    {
        _dispatcher = dispatcher;
        _uow = uow;
    }

    [HttpGet]
    public async Task<ActionResult<List<MonitoringChannel>>> GetAll()
    {
        var channels = await _dispatcher.Send(new GetAllMonitoringChannels(null));
        return Ok(channels);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<MonitoringChannel>> GetById(long id)
    {
        var channels = await _dispatcher.Send(
            new GetAllMonitoringChannels(c => c.Id == id, v => v.Videos));
        var channel = channels.FirstOrDefault();
        if (channel == null) return NotFound();
        return Ok(channel);
    }

    [HttpPost]
    public async Task<ActionResult<MonitoringChannel>> Create([FromBody] CreateMonitoringChannelRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var url = request.Url?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
            ModelState.AddModelError(nameof(request.Name), "O nome do canal é obrigatório.");
        else if (name.Length > 200)
            ModelState.AddModelError(nameof(request.Name), "O nome do canal deve ter no máximo 200 caracteres.");

        if (string.IsNullOrWhiteSpace(url))
            ModelState.AddModelError(nameof(request.Url), "A URL do canal é obrigatória.");
        else if (!ChannelReferenceValidator.IsValid(url))
            ModelState.AddModelError(nameof(request.Url), ChannelReferenceValidator.ErrorMessage);

        ValidateCutDuration(request.MinCutSeconds, request.MaxCutSeconds);
        ValidateMaxPendingCuts(request.MaxPendingCuts);

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var duplicated = await _uow.Repository<MonitoringChannel>()
            .FindAsync(c => c.Url.ToLower() == url.ToLower());

        if (duplicated.Any())
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Canal já cadastrado",
                Detail = $"Já existe um canal monitorado com a URL '{url}'."
            });

        var channel = new MonitoringChannel
        {
            Name = name,
            Url = url,
            MinCutSeconds = request.MinCutSeconds,
            MaxCutSeconds = request.MaxCutSeconds,
            MaxPendingCuts = request.MaxPendingCuts,
            EditionConfigurationId = request.EditionConfigurationId,
            DefaultTags = NormalizeTags(request.DefaultTags)
        };

        await _uow.Repository<MonitoringChannel>().AddAsync(channel);
        await _uow.CommitAsync();

        return CreatedAtAction(nameof(GetById), new { id = channel.Id }, channel);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> Update(long id, [FromBody] UpdateMonitoringChannelRequest request)
    {
        var channel = await _uow.Repository<MonitoringChannel>().GetByIdAsync(id);
        if (channel == null) return NotFound();

        var name = request.Name?.Trim();
        var url = request.Url?.Trim();

        if (name is not null)
        {
            if (name.Length == 0)
                ModelState.AddModelError(nameof(request.Name), "O nome do canal não pode ficar vazio.");
            else if (name.Length > 200)
                ModelState.AddModelError(nameof(request.Name), "O nome do canal deve ter no máximo 200 caracteres.");
        }

        if (url is not null)
        {
            if (url.Length == 0)
                ModelState.AddModelError(nameof(request.Url), "A URL do canal não pode ficar vazia.");
            else if (!ChannelReferenceValidator.IsValid(url))
                ModelState.AddModelError(nameof(request.Url), ChannelReferenceValidator.ErrorMessage);
        }

        // Valida a combinação final, não só o que veio no corpo: mudar apenas o
        // mínimo não pode deixá-lo maior que o máximo já gravado.
        ValidateCutDuration(
            request.MinCutSeconds ?? channel.MinCutSeconds,
            request.MaxCutSeconds ?? channel.MaxCutSeconds);

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (url is { Length: > 0 } && !string.Equals(url, channel.Url, StringComparison.OrdinalIgnoreCase))
        {
            var duplicated = await _uow.Repository<MonitoringChannel>()
                .FindAsync(c => c.Url.ToLower() == url.ToLower() && c.Id != id);

            if (duplicated.Any())
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Canal já cadastrado",
                    Detail = $"Já existe outro canal monitorado com a URL '{url}'."
                });
        }

        channel.Name = string.IsNullOrEmpty(name) ? channel.Name : name;
        channel.Url = string.IsNullOrEmpty(url) ? channel.Url : url;

        // ClearCutDuration permite voltar ao padrão global, coisa que um
        // "null significa não mexer" sozinho não conseguiria expressar.
        if (request.ClearCutDuration == true)
        {
            channel.MinCutSeconds = null;
            channel.MaxCutSeconds = null;
        }
        else
        {
            channel.MinCutSeconds = request.MinCutSeconds ?? channel.MinCutSeconds;
            channel.MaxCutSeconds = request.MaxCutSeconds ?? channel.MaxCutSeconds;
        }

        if (request.ClearMaxPendingCuts == true)
            channel.MaxPendingCuts = null;
        else
            channel.MaxPendingCuts = request.MaxPendingCuts ?? channel.MaxPendingCuts;

        if (request.ClearEditionConfiguration == true)
            channel.EditionConfigurationId = null;
        else
            channel.EditionConfigurationId = request.EditionConfigurationId ?? channel.EditionConfigurationId;

        // String vazia é intencional aqui: é como a interface diz "apaguei as
        // tags". Null continua significando "não mexi".
        if (request.DefaultTags is not null)
            channel.DefaultTags = NormalizeTags(request.DefaultTags);

        _uow.Repository<MonitoringChannel>().Update(channel);
        await _uow.CommitAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(long id)
    {
        var channel = await _uow.Repository<MonitoringChannel>().GetByIdAsync(id);
        if (channel == null) return NotFound();

        _uow.Repository<MonitoringChannel>().Delete(channel);
        await _uow.CommitAsync();

        return NoContent();
    }

    /// <summary>
    /// Os dois são opcionais (null = usar o padrão global), mas quando
    /// informados precisam formar uma faixa coerente.
    /// </summary>
    private void ValidateCutDuration(int? min, int? max)
    {
        const int absoluteMax = 600;

        if (min is not null && min < 1)
            ModelState.AddModelError(nameof(CreateMonitoringChannelRequest.MinCutSeconds),
                "A duração mínima do corte deve ser de pelo menos 1 segundo.");

        if (max is not null && max > absoluteMax)
            ModelState.AddModelError(nameof(CreateMonitoringChannelRequest.MaxCutSeconds),
                $"A duração máxima do corte deve ser de no máximo {absoluteMax} segundos.");

        if (min is not null && max is not null && max <= min)
            ModelState.AddModelError(nameof(CreateMonitoringChannelRequest.MaxCutSeconds),
                $"A duração máxima ({max}s) deve ser maior que a mínima ({min}s).");
    }

    /// <summary>
    /// O teto de cortes pendentes aceita zero, que é a forma explícita de dizer
    /// "sem limite" — diferente de null, que significa "usar o padrão global".
    /// </summary>
    private void ValidateMaxPendingCuts(int? maxPendingCuts)
    {
        if (maxPendingCuts is not null && maxPendingCuts < 0)
            ModelState.AddModelError(nameof(CreateMonitoringChannelRequest.MaxPendingCuts),
                "O teto de cortes pendentes não pode ser negativo. Use 0 para sem limite.");
    }

    /// <summary>
    /// Normaliza a lista de tags digitada, reusando a mesma regra que o fluxo
    /// aplica às tags do modelo — assim o que o usuário vê salvo é o que vai para
    /// o corte.
    /// </summary>
    private static string? NormalizeTags(string? raw) =>
        YT.Generate.Cuts.Application.VideoProcessing.Helpers.TagList.Join(
            YT.Generate.Cuts.Application.VideoProcessing.Helpers.TagList.Merge(
                YT.Generate.Cuts.Application.VideoProcessing.Helpers.TagList.Split(raw)));

    [HttpPost("{id}/monitor")]
    public async Task<ActionResult> TriggerMonitoring(long id)
    {
        var channels = await _dispatcher.Send(new GetAllMonitoringChannels(c => c.Id == id));
        var channel = channels.FirstOrDefault();
        if (channel == null) return NotFound();

        await _dispatcher.Send(new MonitoringChannelCommand(channel, false));
        return Accepted();
    }
}

/// <summary>
/// O MonitoringChannelCommandHandler resolve o canal no YouTube por handle
/// (quando a string contém "@") ou por ID/URL de canal. Validar isso na entrada
/// evita cadastrar um canal que nunca vai conseguir ser monitorado.
/// </summary>
internal static class ChannelReferenceValidator
{
    public const string ErrorMessage =
        "Informe o handle do canal (ex.: @nomedocanal), a URL do canal " +
        "(ex.: https://www.youtube.com/@nomedocanal) ou o ID do canal (ex.: UC...).";

    private static readonly Regex ChannelIdPattern =
        new(@"^UC[A-Za-z0-9_-]{22}$", RegexOptions.Compiled);

    public static bool IsValid(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return false;

        var value = reference.Trim();

        // Handle: "@canal" ou qualquer URL que contenha o handle.
        if (value.Contains('@')) return true;

        // ID de canal puro.
        if (ChannelIdPattern.IsMatch(value)) return true;

        // URL de canal do YouTube.
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var host = uri.Host.ToLowerInvariant();
            return host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtu.be";
        }

        return false;
    }
}

public record CreateMonitoringChannelRequest(
    string Name,
    string Url,
    int? MinCutSeconds = null,
    int? MaxCutSeconds = null,
    /// <summary>Perfil de edição. null = padrão global.</summary>
    long? EditionConfigurationId = null,
    /// <summary>Tags padrão, separadas por vírgula.</summary>
    string? DefaultTags = null,
    /// <summary>Teto de cortes não publicados. null = padrão global; 0 = sem limite.</summary>
    int? MaxPendingCuts = null);

public record UpdateMonitoringChannelRequest(
    string? Name,
    string? Url,
    int? MinCutSeconds = null,
    int? MaxCutSeconds = null,
    /// <summary>Quando true, zera a duração do canal e volta ao padrão global.</summary>
    bool? ClearCutDuration = null,
    int? MaxPendingCuts = null,
    /// <summary>Quando true, volta o teto de cortes ao padrão global.</summary>
    bool? ClearMaxPendingCuts = null,
    long? EditionConfigurationId = null,
    /// <summary>Quando true, volta a edição ao padrão global.</summary>
    bool? ClearEditionConfiguration = null,
    /// <summary>Tags padrão. Vazio limpa; null não altera.</summary>
    string? DefaultTags = null);
