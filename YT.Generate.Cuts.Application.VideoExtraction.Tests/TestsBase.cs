using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YT.Generate.Cuts.Application.Abstractions;

namespace YT.Generate.Cuts.Application.VideoExtraction.Tests;
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

        services.AddApplication();

        ServiceProvider = services.BuildServiceProvider();

        using var scope = ServiceProvider.CreateScope();
    }

}
