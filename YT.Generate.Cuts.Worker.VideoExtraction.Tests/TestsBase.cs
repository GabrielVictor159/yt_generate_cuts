using Hangfire;
using Hangfire.InMemory;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;
using YT.Generate.Cuts.Infra.Data;
using YT.Generate.Cuts.Application.VideoExtraction;
using YT.Generate.Cuts.Application.VideoProcessing;
using YT.Generate.Cuts.Application.VideoPublish;

namespace YT.Generate.Cuts.Worker.VideoExtraction.Tests;

public class TestsBase
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly SqliteConnection Connection;

    protected TestsBase()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        services.AddApplicationVideoExtraction();
        services.AddApplicationVideoProcessing();
        services.AddApplicationVideoPublish();
        services.AddInfrastructureData(configuration);

        // Armazenamento em memória apenas para os testes: o MonitoringChannelsService
        // depende de IBackgroundJobClient e do JobStorage (locks distribuídos).
        services.AddHangfire(config => config.UseInMemoryStorage());

        ServiceProvider = services.BuildServiceProvider();

        using var scope = ServiceProvider.CreateScope();
    }
}