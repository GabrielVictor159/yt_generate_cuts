using Hangfire;
using YT.Generate.Cuts.Worker.VideoExtraction.Services;

namespace YT.Generate.Cuts.Worker.VideoExtraction;

public static class DependencyInjectionExtension
{
    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<MonitoringChannelsService>();
        return services;
    }

    public static IServiceProvider AddServicesHangfire(this IServiceProvider services)
    {
        var manager = services.GetRequiredService<IRecurringJobManager>();

        try
        {
            Infra.Data.DependencyInjectionExtension.ClearSpecificHangfireJobs(services, "monitoring-channels");

            manager.AddOrUpdate<MonitoringChannelsService>(
            "monitoring-channels",
            "extraction",
            s => s.MonitoringNewVideosAsync(CancellationToken.None),
            Cron.Minutely);
        }
        catch { }

        return services;
    }
}