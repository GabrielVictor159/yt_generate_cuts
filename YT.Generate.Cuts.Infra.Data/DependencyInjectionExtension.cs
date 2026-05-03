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
                SchemaName = schema 
            }));

        services.AddHangfireServer(options =>
        {
            options.Queues = filas;
            options.WorkerCount = 10;
        });

        return services;
    }

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
