using System.Linq.Expressions;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;

namespace YT.Generate.Cuts.Worker.VideoProcessing.Tests.Fakes;

/// <summary>
/// Unidade de trabalho de teste: só registra o que foi chamado. O que se quer
/// observar nos testes de limpeza é o efeito no disco e no estado da entidade,
/// não a persistência.
/// </summary>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Commits { get; private set; }
    public List<object> Updated { get; } = new();

    public bool HasActiveTransaction => false;

    public IGenericRepository<T> Repository<T>() where T : class => new FakeRepository<T>(this);

    public Task<int> CommitAsync()
    {
        Commits++;
        return Task.FromResult(1);
    }

    public Task BeginTransactionAsync() => Task.CompletedTask;
    public Task CommitTransactionAsync() => Task.CompletedTask;
    public Task RollbackTransactionAsync() => Task.CompletedTask;
    public void Dispose() { }

    private sealed class FakeRepository<T> : IGenericRepository<T> where T : class
    {
        private readonly FakeUnitOfWork _uow;
        public FakeRepository(FakeUnitOfWork uow) => _uow = uow;

        public void Update(T entity) => _uow.Updated.Add(entity);

        public Task<IEnumerable<T>> FindAsync(
            Expression<Func<T, bool>> predicate, params Expression<Func<T, object>>[] includes) =>
            Task.FromResult(Enumerable.Empty<T>());

        public Task<(IEnumerable<T> Items, int TotalCount)> FindPagedAsync(
            int page, int pageSize, Expression<Func<T, bool>> predicate,
            params Expression<Func<T, object>>[] includes) =>
            Task.FromResult((Enumerable.Empty<T>(), 0));

        public Task<T?> GetByIdAsync(long id) => Task.FromResult<T?>(null);
        public Task AddAsync(T entity) => Task.CompletedTask;
        public void Delete(T entity) { }
        public Task AddRangeAsync(IEnumerable<T> entities) => Task.CompletedTask;
        public void UpdateRange(IEnumerable<T> entities) { }
        public void DeleteRange(IEnumerable<T> entities) { }
    }
}
