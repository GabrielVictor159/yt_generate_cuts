namespace YT.Generate.Cuts.Infra.Data.Jobs;

/// <summary>
/// Exclusão mútua entre execuções de job, valendo para todas as instâncias.
/// </summary>
public interface IJobLock
{
    /// <summary>
    /// Executa <paramref name="action"/> apenas se conseguir o lock de
    /// <paramref name="resource"/>.
    /// </summary>
    /// <param name="wait">Quanto tempo insistir antes de desistir.</param>
    /// <returns>
    /// <c>true</c> se a ação rodou; <c>false</c> se outra execução detinha o
    /// recurso — quem chama decide se descarta a rodada.
    /// </returns>
    Task<bool> TryRunAsync(string resource, TimeSpan wait, Func<Task> action);
}
