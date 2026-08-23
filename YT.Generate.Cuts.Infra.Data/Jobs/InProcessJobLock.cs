using System.Collections.Concurrent;

namespace YT.Generate.Cuts.Infra.Data.Jobs;

/// <summary>
/// Lock válido apenas dentro do processo.
/// </summary>
/// <remarks>
/// Serve para cenários de uma instância só — testes e o McpServer, que usa o
/// Hangfire em memória. <b>Não</b> deve ser registrado nos workers: é justamente
/// a limitação desta abordagem (um semáforo por processo, cada processo achando
/// que é o único) que causava as execuções paralelas.
/// </remarks>
public sealed class InProcessJobLock : IJobLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public async Task<bool> TryRunAsync(string resource, TimeSpan wait, Func<Task> action)
    {
        var gate = _gates.GetOrAdd(resource, _ => new SemaphoreSlim(1, 1));

        if (!await gate.WaitAsync(wait))
            return false;

        try
        {
            await action();
        }
        finally
        {
            gate.Release();
        }

        return true;
    }
}
