using Microsoft.EntityFrameworkCore.Storage;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;
using YT.Generate.Cuts.Infra.Data.Context;

namespace YT.Generate.Cuts.Infra.Data.Repositories;
public class UnitOfWork : IUnitOfWork
{
    private readonly CutContext _context;
    private IDbContextTransaction? _transaction;
    private Dictionary<string, object>? _repositories;

    public UnitOfWork(CutContext context)
    {
        _context = context;
    }

    /// <summary>Indica se existe uma transação explícita aberta neste UnitOfWork.</summary>
    public bool HasActiveTransaction => _transaction is not null;

    public IGenericRepository<T> Repository<T>() where T : class
    {
        _repositories ??= new Dictionary<string, object>();
        var type = typeof(T).Name;

        if (!_repositories.ContainsKey(type))
        {
            var repositoryInstance = new GenericRepository<T>(_context);
            _repositories.Add(type, repositoryInstance);
        }

        return (IGenericRepository<T>)_repositories[type];
    }

    public async Task BeginTransactionAsync()
    {
        // Reaproveita a transação corrente em vez de deixar o EF lançar
        // "The connection is already in a transaction".
        if (_transaction is not null) return;

        _transaction = await _context.Database.BeginTransactionAsync();
    }

    public async Task CommitTransactionAsync()
    {
        // Sem transação aberta, ainda assim persistimos o que estiver pendente:
        // é o que o chamador espera de um "commit".
        if (_transaction is null)
        {
            await CommitAsync();
            return;
        }

        try
        {
            if (_context.ChangeTracker.HasChanges())
            {
                await _context.SaveChangesAsync();
            }

            await _transaction.CommitAsync();
        }
        finally
        {
            // Zerar o campo é essencial: antes ele continuava apontando para a
            // transação já descartada, e o rollback do catch estourava
            // ObjectDisposedException, escondendo a exceção real.
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync()
    {
        // Idempotente: chamar rollback sem transação aberta (ou depois de um
        // commit) não deve gerar exceção nem mascarar o erro que levou até aqui.
        // Esta guarda é o que resolve o problema na raiz — não é preciso engolir
        // exceção para o caso do rollback pós-commit.
        if (_transaction is null) return;

        try
        {
            await _transaction.RollbackAsync();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // A transação já foi encerrada por outro caminho. É seguro ignorar,
            // e o catch é estreito de propósito: uma falha real de rollback
            // (conexão caída, por exemplo) continua visível em vez de silenciosa.
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task<int> CommitAsync() => await _context.SaveChangesAsync();

    public void Dispose()
    {
        _transaction?.Dispose();
        _transaction = null;
        _context.Dispose();
    }
}
