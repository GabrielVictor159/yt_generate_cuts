namespace YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;
public interface IUnitOfWork : IDisposable
{
    IGenericRepository<T> Repository<T>() where T : class;

    /// <summary>Indica se existe uma transação explícita aberta.</summary>
    bool HasActiveTransaction { get; }

    Task<int> CommitAsync();
    Task BeginTransactionAsync();
    Task CommitTransactionAsync();
    Task RollbackTransactionAsync();
}
