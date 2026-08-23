using Microsoft.AspNetCore.Mvc;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Domain.Entities;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PublishChannelsController : ControllerBase
{
    private readonly IAppDispatcher _dispatcher;
    private readonly IUnitOfWork _uow;

    public PublishChannelsController(IAppDispatcher dispatcher, IUnitOfWork uow)
    {
        _dispatcher = dispatcher;
        _uow = uow;
    }

    [HttpGet]
    public async Task<ActionResult<List<PublishChannelResponse>>> GetAll()
    {
        var channels = await _uow.Repository<PublishChannel>().FindAsync(c => true);
        return Ok(channels.Select(PublishChannelResponse.From).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PublishChannelResponse>> GetById(long id)
    {
        var channel = await _uow.Repository<PublishChannel>().GetByIdAsync(id);
        if (channel == null) return NotFound();
        return Ok(PublishChannelResponse.From(channel));
    }

    [HttpGet("types")]
    public ActionResult<List<string>> GetTypes()
        => Ok(Enum.GetNames<TypePublishEnum>().ToList());

    [HttpPost]
    public async Task<ActionResult<PublishChannelResponse>> Create([FromBody] CreatePublishChannelRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var login = request.Login?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
            ModelState.AddModelError(nameof(request.Name), "O nome do canal é obrigatório.");
        else if (name.Length > 200)
            ModelState.AddModelError(nameof(request.Name), "O nome do canal deve ter no máximo 200 caracteres.");

        if (string.IsNullOrWhiteSpace(login))
            ModelState.AddModelError(nameof(request.Login), "O login é obrigatório.");

        if (string.IsNullOrWhiteSpace(password))
            ModelState.AddModelError(nameof(request.Password), "A senha é obrigatória.");

        if (!TryParseType(request.TypePublish, out var typePublish, out var typeError))
            ModelState.AddModelError(nameof(request.TypePublish), typeError);

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var duplicated = await _uow.Repository<PublishChannel>()
            .FindAsync(c => c.Login.ToLower() == login.ToLower() && c.TypePublish == typePublish);

        if (duplicated.Any())
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Canal já cadastrado",
                Detail = $"Já existe um canal {typePublish} com o login '{login}'."
            });

        var channel = new PublishChannel
        {
            Name = name,
            Login = login,
            Password = password,
            TypePublish = typePublish
        };

        await _uow.Repository<PublishChannel>().AddAsync(channel);
        await _uow.CommitAsync();

        return CreatedAtAction(nameof(GetById), new { id = channel.Id }, PublishChannelResponse.From(channel));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> Update(long id, [FromBody] UpdatePublishChannelRequest request)
    {
        var channel = await _uow.Repository<PublishChannel>().GetByIdAsync(id);
        if (channel == null) return NotFound();

        var name = request.Name?.Trim();
        var login = request.Login?.Trim();

        if (name is { Length: 0 })
            ModelState.AddModelError(nameof(request.Name), "O nome do canal não pode ficar vazio.");
        else if (name is { Length: > 200 })
            ModelState.AddModelError(nameof(request.Name), "O nome do canal deve ter no máximo 200 caracteres.");

        if (login is { Length: 0 })
            ModelState.AddModelError(nameof(request.Login), "O login não pode ficar vazio.");

        if (request.Password is { Length: 0 })
            ModelState.AddModelError(nameof(request.Password), "A senha não pode ficar vazia.");

        TypePublishEnum? typePublish = null;
        if (request.TypePublish is not null)
        {
            if (TryParseType(request.TypePublish, out var parsed, out var typeError))
                typePublish = parsed;
            else
                ModelState.AddModelError(nameof(request.TypePublish), typeError);
        }

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        channel.Name = string.IsNullOrEmpty(name) ? channel.Name : name;
        channel.Login = string.IsNullOrEmpty(login) ? channel.Login : login;
        channel.Password = string.IsNullOrEmpty(request.Password) ? channel.Password : request.Password;
        channel.TypePublish = typePublish ?? channel.TypePublish;

        _uow.Repository<PublishChannel>().Update(channel);
        await _uow.CommitAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(long id)
    {
        var channel = await _uow.Repository<PublishChannel>().GetByIdAsync(id);
        if (channel == null) return NotFound();

        _uow.Repository<PublishChannel>().Delete(channel);
        await _uow.CommitAsync();

        return NoContent();
    }

    private static bool TryParseType(string? value, out TypePublishEnum typePublish, out string error)
    {
        typePublish = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"O tipo de publicação é obrigatório. Valores aceitos: {ValidTypes}.";
            return false;
        }

        // Enum.Parse levantaria 500; aqui a entrada inválida vira 400 com a lista de opções.
        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out typePublish)
            || !Enum.IsDefined(typePublish))
        {
            error = $"Tipo de publicação '{value}' inválido. Valores aceitos: {ValidTypes}.";
            return false;
        }

        return true;
    }

    private static string ValidTypes => string.Join(", ", Enum.GetNames<TypePublishEnum>());
}

/// <summary>
/// Resposta sem a senha: a credencial fica no servidor e nunca vai para o cliente.
/// A propriedade continua no JSON (como string vazia) porque
/// <see cref="PublishChannel.Password"/> é <c>required</c> e o desserializador
/// do front-end exige que ela esteja presente.
/// </summary>
public record PublishChannelResponse(
    long Id,
    string Name,
    string Login,
    string Password,
    DateTime CreationDate,
    TypePublishEnum TypePublish)
{
    public static PublishChannelResponse From(PublishChannel channel) => new(
        channel.Id,
        channel.Name,
        channel.Login,
        string.Empty,
        channel.CreationDate,
        channel.TypePublish);
}

public record CreatePublishChannelRequest(string Name, string Login, string Password, string TypePublish);
public record UpdatePublishChannelRequest(string? Name, string? Login, string? Password, string? TypePublish);
