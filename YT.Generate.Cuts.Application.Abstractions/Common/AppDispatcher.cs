using Microsoft.Extensions.DependencyInjection;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;

namespace YT.Generate.Cuts.Application.Abstractions.Common;
public class AppDispatcher : IAppDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ValidationBehavior _validationBehavior;

    public AppDispatcher(IServiceProvider serviceProvider, ValidationBehavior validationBehavior)
    {
        _serviceProvider = serviceProvider;
        _validationBehavior = validationBehavior;
    }

    public async Task Send(ICommand command, CancellationToken ct = default)
    {
        await _validationBehavior.ValidateAsync((dynamic)command, ct);

        var handlerType = typeof(ICommandHandler<>).MakeGenericType(command.GetType());
        var handler = _serviceProvider.GetRequiredService(handlerType);
        var method = handlerType.GetMethod("Handle");

        await (Task)method!.Invoke(handler, new object[] { command, ct })!;
    }

    public async Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default)
    {
        await _validationBehavior.ValidateAsync((dynamic)command, ct);

        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResponse));
        var handler = _serviceProvider.GetRequiredService(handlerType);
        var method = handlerType.GetMethod("Handle");

        return await (Task<TResponse>)method!.Invoke(handler, new object[] { command, ct })!;
    }
}
