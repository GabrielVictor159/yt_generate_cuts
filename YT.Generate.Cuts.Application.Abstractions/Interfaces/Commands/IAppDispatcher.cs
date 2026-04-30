
namespace YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
public interface IAppDispatcher
{
    Task Send(ICommand command, CancellationToken ct = default);
    Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default);
}
