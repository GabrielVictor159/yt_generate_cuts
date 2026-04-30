namespace YT.Generate.Cuts.Application.Abstractions.Exceptions;
public class CommandOperationException : Exception
{
    public CommandOperationException() : base("Ocorreu um erro na execução do comando.") { }

    public CommandOperationException(string message) : base(message) { }

    public CommandOperationException(string message, Exception innerException)
        : base(message, innerException) { }
}
