using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;
using YT.Generate.Cuts.Infra.Data.Context;
using YT.Generate.Cuts.Infra.Data.Repositories;

namespace YT.Generate.Cuts.Infra.Data;
public static class DependencyInjectionExtension
{
    public static IServiceCollection AddInfrastructureData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CutContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

        return services;
    }

    public static IServiceCollection AddHangfire(this IServiceCollection services, IConfiguration configuration, string[] filas, string schema)
    {
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options =>
            {
                options.UseNpgsqlConnection(configuration.GetConnectionString("DefaultConnection"));
            }, new PostgreSqlStorageOptions
            {
                PrepareSchemaIfNecessary = true,
                SchemaName = schema,

                // Tempo de vida dos locks internos do Hangfire. A exclusão
                // mútua dos nossos jobs NÃO usa mais este mecanismo — ver
                // PostgresAdvisoryJobLock e o motivo lá descrito.
                DistributedLockTimeout = ReadTimeout(configuration, "Hangfire:DistributedLockTimeoutMinutes", 60),

                // Enquanto o worker está vivo, ele renova a invisibilidade do
                // job que está executando. Sem isto (o padrão é false), um job
                // que passa de InvisibilityTimeout volta para a fila e é
                // buscado por OUTRO worker enquanto o primeiro ainda trabalha:
                // mais uma fonte de execução duplicada, independente de lock.
                UseSlidingInvisibilityTimeout = true,

                // Com a renovação ligada, este prazo passa a significar "quanto
                // tempo depois de o worker morrer o job volta para a fila".
                // Trinta minutos (o padrão) é o que fazia o sistema parecer
                // travado depois de um Stop no Visual Studio; cinco é suficiente
                // para não competir com um desligamento normal.
                InvisibilityTimeout = ReadTimeout(configuration, "Hangfire:InvisibilityTimeoutMinutes", 5),
            }));

        services.AddHangfireServer(options =>
        {
            options.Queues = filas;
            options.WorkerCount = ReadInt(configuration, "Hangfire:WorkerCount", 10);

            // Quanto tempo o watchdog espera antes de considerar um servidor
            // morto e devolver os jobs dele. O padrão são 5 minutos; encurtar
            // faz a recuperação depois de um encerramento abrupto ser rápida,
            // que é o caso do ciclo de depuração no Visual Studio.
            options.ServerTimeout = ReadTimeout(configuration, "Hangfire:ServerTimeoutMinutes", 2);
            options.ServerCheckInterval = TimeSpan.FromMinutes(1);
        });

        // Exclusão mútua entre execuções, válida entre processos e imune a
        // encerramento abrupto: o advisory lock morre com a conexão.
        services.AddSingleton<Jobs.IJobLock, Jobs.PostgresAdvisoryJobLock>();
        services.AddSingleton<Jobs.SerialJobRunner>();

        return services;
    }

    private static TimeSpan ReadTimeout(IConfiguration configuration, string key, int defaultMinutes)
    {
        var minutes = int.TryParse(configuration[key], out var value) && value > 0 ? value : defaultMinutes;
        return TimeSpan.FromMinutes(minutes);
    }

    private static int ReadInt(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], out var value) && value > 0 ? value : fallback;

    public static void ApplyMigrations(this IApplicationBuilder app)
    {
        using IServiceScope scope = app.ApplicationServices.CreateScope();
        using CutContext context = scope.ServiceProvider.GetRequiredService<CutContext>();

        int retries = 10;
        int delayPerRetryInSeconds = 2;

        for (int i = 1; i <= retries; i++)
        {
            try
            {
                Console.WriteLine($"[Tentativa {i}/{retries}] Iniciando migration...");
                context.Database.Migrate();
                Console.WriteLine("Migrations aplicadas com sucesso!");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro na tentativa {i}: O banco ainda não está pronto ({ex.Message})");

                if (i == retries)
                {
                    Console.WriteLine("Número máximo de tentativas atingido. Encerrando aplicação.");
                    throw;
                }

                Console.WriteLine($"Aguardando {delayPerRetryInSeconds} segundos para tentar novamente...");
                Thread.Sleep(delayPerRetryInSeconds * 1000);
            }
        }
    }

    public static void ClearSpecificHangfireJobs(this IServiceProvider serviceProvider, string recurringJobId)
    {
        try
        {
            var manager = serviceProvider.GetRequiredService<IRecurringJobManager>();
            manager.RemoveIfExists(recurringJobId);
        }
        catch { }
    }
}
