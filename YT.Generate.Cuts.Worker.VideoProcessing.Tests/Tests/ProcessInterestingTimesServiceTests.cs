using Hangfire;
using Hangfire.InMemory;
using Hangfire.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Infra.Data.Jobs;
using YT.Generate.Cuts.Worker.VideoProcessing.Services;
using YT.Generate.Cuts.Worker.VideoProcessing.Tests.Fakes;

namespace YT.Generate.Cuts.Worker.VideoProcessing.Tests.Tests;

/// <summary>
/// O que se quer garantir aqui é a queixa concreta: o ciclo de InterestingTimes
/// rodando mais de uma vez ao mesmo tempo. O teste sobe duas execuções em
/// paralelo — como dois jobs do Hangfire, ou dois containers — e mede quantas
/// inferências estiveram ativas simultaneamente.
/// </summary>
public class ProcessInterestingTimesServiceTests
{
    [Fact]
    public async Task DuasExecucoesEmParalelo_NuncaProcessamAoMesmoTempo()
    {
        var store = NewStore(videos: 6, work: TimeSpan.FromMilliseconds(80));
        var provider = BuildProvider(store);

        var primeira = NewService(provider);
        var segunda = NewService(provider);

        await Task.WhenAll(
            primeira.ProcessInterestingTimesAsync(CancellationToken.None),
            segunda.ProcessInterestingTimesAsync(CancellationToken.None));

        // O ponto do teste. Com o SemaphoreSlim estático isto valia dentro de um
        // processo; com o lock no Postgres/Hangfire vale entre execuções.
        Assert.Equal(1, store.MaxActive);

        // Nenhum vídeo analisado duas vezes: a releitura de status dentro do lock
        // é o que impede a inferência (e os cortes) em duplicidade.
        Assert.Equal(store.Processed.Count, store.Processed.Distinct().Count());

        // E nada ficou para trás.
        Assert.Equal(6, store.Processed.Count);
        Assert.All(store.Snapshot(), v => Assert.Equal(VideoStatusEnum.Process, v.Status));
    }

    /// <summary>
    /// O teste decisivo: alguém já está com a vez (o lock global tomado por
    /// fora, como uma inferência em andamento em outro processo). Nada pode ser
    /// processado — nem um vídeo diferente, porque a exclusão é global e não por
    /// vídeo nem por canal.
    /// </summary>
    [Fact]
    public async Task ComOLockGlobalTomado_NadaEProcessado()
    {
        var store = NewStore(videos: 4, work: TimeSpan.FromMilliseconds(10));
        var provider = BuildProvider(store);
        var service = NewService(provider);

        var jobLock = provider.GetRequiredService<IJobLock>();
        var liberar = new TaskCompletionSource();

        // Segura o lock global por fora, como uma inferência em andamento em
        // outro processo.
        var segurando = jobLock.TryRunAsync(
            ProcessInterestingTimesService.LockResource, TimeSpan.FromSeconds(1), () => liberar.Task);

        await service.ProcessInterestingTimesAsync(CancellationToken.None);

        liberar.SetResult();
        Assert.True(await segurando);

        Assert.Empty(store.Processed);
        Assert.All(store.Snapshot(), v => Assert.Equal(VideoStatusEnum.Download, v.Status));

        // Liberado o lock, a rodada seguinte trabalha normalmente.
        await service.ProcessInterestingTimesAsync(CancellationToken.None);
        Assert.Equal(4, store.Processed.Count);
    }

    [Fact]
    public async Task UmaExecucao_ProcessaTodosOsVideosPendentes()
    {
        var store = NewStore(videos: 3, work: TimeSpan.FromMilliseconds(10));
        var service = NewService(BuildProvider(store));

        await service.ProcessInterestingTimesAsync(CancellationToken.None);

        // Mais antigos primeiro, para nenhum vídeo ficar preso atrás dos outros.
        Assert.Equal(new long[] { 1, 2, 3 }, store.Processed);
    }

    [Fact]
    public async Task TetoPorCiclo_LimitaQuantosVideosEntram()
    {
        var store = NewStore(videos: 5, work: TimeSpan.FromMilliseconds(10));
        var service = NewService(BuildProvider(store, maxPerCycle: 2));

        await service.ProcessInterestingTimesAsync(CancellationToken.None);

        Assert.Equal(new long[] { 1, 2 }, store.Processed);

        // O que sobrou é retomado na rodada seguinte.
        await service.ProcessInterestingTimesAsync(CancellationToken.None);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, store.Processed);
    }

    [Fact]
    public async Task SemVideosPendentes_NaoFazNada()
    {
        var store = NewStore(videos: 0, work: TimeSpan.Zero);
        var service = NewService(BuildProvider(store));

        await service.ProcessInterestingTimesAsync(CancellationToken.None);

        Assert.Empty(store.Processed);
    }

    [Fact]
    public void RecursoDoLock_NaoTemSufixoPorCanalOuVideo()
    {
        // A exclusão é global de propósito: o gargalo é o Ollama, não o canal.
        // Um sufixo aqui reintroduziria execuções paralelas.
        Assert.Equal("processing:interesting-times", ProcessInterestingTimesService.LockResource);
    }

    // ==================================================================

    private static RecordingDispatcher.Store NewStore(int videos, TimeSpan work)
    {
        var store = new RecordingDispatcher.Store { Work = work };

        for (var i = 1; i <= videos; i++)
        {
            store.Videos.Add(new Domain.Entities.Video
            {
                Id = i,
                ChannelId = 1,
                Title = $"Vídeo {i}",
                Url = $"https://www.youtube.com/watch?v=video{i}",
                Status = VideoStatusEnum.Download,
            });
        }

        return store;
    }

    private static IServiceProvider BuildProvider(RecordingDispatcher.Store store, int? maxPerCycle = null)
    {
        var settings = new Dictionary<string, string?>();

        if (maxPerCycle is not null)
            settings["InterestingTimes:MaxVideosPerCycle"] = maxPerCycle.Value.ToString();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        // Um escopo novo por vídeo é o que dá a leitura fresca; o store é
        // compartilhado, como o banco seria.
        services.AddScoped<IAppDispatcher>(_ => new RecordingDispatcher(store));

        // Armazenamento em memória: o lock distribuído do Hangfire funciona aqui
        // dentro do processo, o que basta para observar a exclusão mútua.
        services.AddHangfire(config => config.UseInMemoryStorage());
        services.AddSingleton<IJobLock, InProcessJobLock>();
        services.AddSingleton<SerialJobRunner>();

        return services.BuildServiceProvider();
    }

    private static ProcessInterestingTimesService NewService(IServiceProvider provider) =>
        new(provider.GetRequiredService<SerialJobRunner>(),
            provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<ILogger<ProcessInterestingTimesService>>());
}
