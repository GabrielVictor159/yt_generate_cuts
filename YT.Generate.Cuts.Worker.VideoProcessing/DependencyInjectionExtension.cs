using Hangfire;
using YT.Generate.Cuts.Worker.VideoProcessing.Services;

namespace YT.Generate.Cuts.Worker.VideoProcessing;

public static class DependencyInjectionExtension
{
    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<ProcessInterestingTimesService>();
        return services;
    }

    public static IServiceProvider AddServicesHangfire(this IServiceProvider services)
    {
        var manager = services.GetRequiredService<IRecurringJobManager>();

        try
        {
            Infra.Data.DependencyInjectionExtension.ClearSpecificHangfireJobs(services, "extraction_interesting_times");

            manager.AddOrUpdate<ProcessInterestingTimesService>(
                "extraction_interesting_times",
                "processing",
                s => s.ProcessInterestingTimesAsync(CancellationToken.None),
                Cron.Minutely);
        }
        catch { }

        return services;
    }
}