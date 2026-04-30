using System.Linq.Expressions;

namespace YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;
public interface IGenericRepository<T>
{
    Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate, params Expression<Func<T, object>>[] includes);
    Task<(IEnumerable<T> Items, int TotalCount)> FindPagedAsync(int page, int pageSize, Expression<Func<T, bool>> predicate, params Expression<Func<T, object>>[] includes);
    Task<T?> GetByIdAsync(long id);

    Task AddAsync(T entity);
    void Update(T entity);
    void Delete(T entity);

    Task AddRangeAsync(IEnumerable<T> entities);
    void UpdateRange(IEnumerable<T> entities);
    void DeleteRange(IEnumerable<T> entities);
}
