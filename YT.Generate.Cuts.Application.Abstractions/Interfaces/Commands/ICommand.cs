
namespace YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;

public interface ICommand<TResponse> { }
public interface ICommand { }
public interface ICommandHandler<TCommand> where TCommand : ICommand
{
    Task Handle(TCommand command, CancellationToken ct);
}
public interface ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<TResponse> Handle(TCommand command, CancellationToken ct);
}
