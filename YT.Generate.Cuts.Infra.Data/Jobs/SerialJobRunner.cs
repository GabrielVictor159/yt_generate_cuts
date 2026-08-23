using Microsoft.Extensions.DependencyInjection;

namespace YT.Generate.Cuts.Infra.Data.Jobs;

/// <summary>Resultado de uma passagem do <see cref="SerialJobRunner"/>.</summary>
/// <param name="Processed">Itens efetivamente trabalhados.</param>
/// <param name="Busy">
/// Verdadeiro quando a rodada terminou porque outra execução estava com a vez.
/// </param>
public readonly record struct SerialJobResult(int Processed, bool Busy);

/// <summary>
/// Percorre itens executando <b>um por vez, globalmente</b>, cada um com escopo
/// de DI novo.
/// </summary>
/// <remarks>
/// Concentra o padrão que os quatro jobs recorrentes precisam, e que cada um
/// resolvia (mal) por conta própria com um <c>static SemaphoreSlim</c>:
/// <list type="bullet">
/// <item>
/// <b>Exclusão global.</b> Um único nome de recurso no lock do Hangfire, então
/// vale entre processos — dois containers, ou a instância antiga ainda viva
/// durante um restart, ou o worker rodando na IDE contra o mesmo banco.
/// </item>
/// <item>
/// <b>Lock por item, não pelo lote.</b> Cada posse do lock cobre um item, então
/// uma rodada longa não fica presa a uma única aquisição. Isso mantém o
/// comportamento previsível qualquer que seja a implementação de
/// <see cref="IJobLock"/> e permite que outra instância assuma a fila quando esta
/// termina.
/// </item>
/// <item>
/// <b>Escopo novo por item.</b> Um <c>DbContext</c> novo é o que permite reler o
/// estado do item de verdade. Reaproveitando o contexto do lote, a releitura
/// devolveria a entidade já rastreada, com o estado de quando o lote começou — e
/// o item seria trabalhado duas vezes se outra execução o tivesse concluído
/// nesse meio-tempo. Em publicação, isso significaria postar o mesmo corte duas
/// vezes.
/// </item>
/// <item>
/// <b>Falha de um item não derruba a rodada.</b> Quem chama trata o próprio erro
/// e devolve <c>false</c>; o laço segue para o próximo.
/// </item>
/// </list>
/// </remarks>
public sealed class SerialJobRunner
{
    private readonly IJobLock _jobLock;
    private readonly IServiceScopeFactory _scopeFactory;

    public SerialJobRunner(IJobLock jobLock, IServiceScopeFactory scopeFactory)
    {
        _jobLock = jobLock;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Executa <paramref name="process"/> para cada id, um por vez.
    /// </summary>
    /// <param name="process">
    /// Recebe um <see cref="IServiceProvider"/> de escopo próprio e o id. Deve
    /// devolver <c>false</c> quando o item foi ignorado (já concluído por outra
    /// execução, por exemplo), e nunca deixar escapar erro de um item só.
    /// </param>
    /// <returns>
    /// Quantos itens foram trabalhados e se a rodada parou por já haver outra
    /// execução em andamento.
    /// </returns>
    public async Task<SerialJobResult> RunAsync(
        string resource,
        TimeSpan lockWait,
        IReadOnlyList<long> ids,
        Func<IServiceProvider, long, CancellationToken, Task<bool>> process,
        CancellationToken ct)
    {
        var processed = 0;

        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested)
                break;

            var ran = await _jobLock.TryRunAsync(resource, lockWait, async () =>
            {
                using var scope = _scopeFactory.CreateScope();

                if (await process(scope.ServiceProvider, id, ct))
                    processed++;
            });

            // Outra execução está com a vez. Insistir item por item só geraria
            // log: ela vai percorrer a mesma fila.
            if (!ran)
                return new SerialJobResult(processed, Busy: true);
        }

        return new SerialJobResult(processed, Busy: false);
    }

    /// <summary>
    /// Executa um trabalho pontual em escopo próprio — a consulta que monta a
    /// fila, tipicamente, para o contexto do lote não acumular entidades
    /// rastreadas.
    /// </summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = _scopeFactory.CreateScope();
        return await work(scope.ServiceProvider);
    }
}
