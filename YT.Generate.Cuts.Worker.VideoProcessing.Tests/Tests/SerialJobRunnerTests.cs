using Hangfire;
using Hangfire.InMemory;
using Microsoft.Extensions.DependencyInjection;
using YT.Generate.Cuts.Infra.Data.Jobs;

namespace YT.Generate.Cuts.Worker.VideoProcessing.Tests.Tests;

/// <summary>
/// O <see cref="SerialJobRunner"/> é o mecanismo de "um por vez" dos quatro jobs
/// recorrentes, então vale testá-lo direto — o que falhar aqui falha nos quatro.
/// </summary>
public class SerialJobRunnerTests
{
    private const string Resource = "teste:serial";
    private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task ExecutaUmPorVez_NuncaEmParalelo()
    {
        var provider = BuildProvider();
        var runner = provider.GetRequiredService<SerialJobRunner>();

        var dentro = 0;
        var maximo = 0;
        var gate = new object();

        async Task<bool> Work(IServiceProvider _, long __, CancellationToken ___)
        {
            var agora = Interlocked.Increment(ref dentro);
            lock (gate) maximo = Math.Max(maximo, agora);
            await Task.Delay(30);
            Interlocked.Decrement(ref dentro);
            return true;
        }

        var ids = Enumerable.Range(1, 8).Select(i => (long)i).ToList();

        var resultados = await Task.WhenAll(
            runner.RunAsync(Resource, Wait, ids, Work, CancellationToken.None),
            runner.RunAsync(Resource, Wait, ids, Work, CancellationToken.None));

        Assert.Equal(1, maximo);
        Assert.All(resultados, r => Assert.True(r.Processed >= 0));
    }

    [Fact]
    public async Task ComOLockTomadoPorFora_DevolveBusySemProcessarNada()
    {
        var provider = BuildProvider();
        var runner = provider.GetRequiredService<SerialJobRunner>();

        var chamadas = 0;

        var liberar = new TaskCompletionSource();

        var segurando = provider.GetRequiredService<IJobLock>()
            .TryRunAsync(Resource, Wait, () => liberar.Task);

        var result = await runner.RunAsync(
            Resource, Wait, new long[] { 1, 2, 3 },
            (_, __, ___) => { chamadas++; return Task.FromResult(true); },
            CancellationToken.None);

        liberar.SetResult();
        Assert.True(await segurando);

        Assert.True(result.Busy);
        Assert.Equal(0, result.Processed);
        Assert.Equal(0, chamadas);
    }

    [Fact]
    public async Task CadaItemRecebeUmEscopoNovo()
    {
        var provider = BuildProvider();
        var runner = provider.GetRequiredService<SerialJobRunner>();

        var vistos = new List<Guid>();

        var result = await runner.RunAsync(
            Resource, Wait, new long[] { 1, 2, 3 },
            (scope, _, __) =>
            {
                vistos.Add(scope.GetRequiredService<ScopeMarker>().Id);
                return Task.FromResult(true);
            },
            CancellationToken.None);

        Assert.False(result.Busy);
        Assert.Equal(3, result.Processed);

        // Escopo novo por item é o que permite reler o estado do banco de verdade.
        Assert.Equal(3, vistos.Distinct().Count());
    }

    [Fact]
    public async Task ItemIgnoradoNaoContaComoProcessado()
    {
        var runner = BuildProvider().GetRequiredService<SerialJobRunner>();

        var result = await runner.RunAsync(
            Resource, Wait, new long[] { 1, 2, 3, 4 },
            (_, id, __) => Task.FromResult(id % 2 == 0),
            CancellationToken.None);

        Assert.False(result.Busy);
        Assert.Equal(2, result.Processed);
    }

    // ==================================================================

    private sealed class ScopeMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ScopeMarker>();
        services.AddHangfire(config => config.UseInMemoryStorage());
        services.AddSingleton<IJobLock, InProcessJobLock>();
        services.AddSingleton<SerialJobRunner>();
        return services.BuildServiceProvider();
    }
}
