using Hangfire;
using YT.Generate.Cuts.Worker.VideoEdition.Services;

namespace YT.Generate.Cuts.Worker.VideoEdition;

public static class DependencyInjectionExtension
{
    public static IServiceCollection AddEditionServices(this IServiceCollection services)
    {
        services.AddScoped<EditingCutsService>();
        return services;
    }

    public static IServiceProvider AddEditionServicesHangfire(this IServiceProvider services)
    {
        var manager = services.GetRequiredService<IRecurringJobManager>();

        try
        {
            Infra.Data.DependencyInjectionExtension.ClearSpecificHangfireJobs(services, "edition_cuts");

            manager.AddOrUpdate<EditingCutsService>(
                "edition_cuts",
                "edition",
                s => s.EditCutsAsync(CancellationToken.None),
                Cron.Minutely);
        }
        catch { }

        return services;
    }
}
