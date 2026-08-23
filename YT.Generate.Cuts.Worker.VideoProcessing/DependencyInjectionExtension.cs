using Hangfire;
using YT.Generate.Cuts.Worker.VideoProcessing.Services;

namespace YT.Generate.Cuts.Worker.VideoProcessing;

public static class DependencyInjectionExtension
{
    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<ProcessInterestingTimesService>();
        services.AddScoped<ProcessingCutsService>();
        services.AddScoped<RemoveVideoCutCreatedService>();
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

            Infra.Data.DependencyInjectionExtension.ClearSpecificHangfireJobs(services, "processing_cuts");

            manager.AddOrUpdate<ProcessingCutsService>(
                "processing_cuts",
                "processing",
                s => s.ProcessCutsAsync(CancellationToken.None),
                Cron.Minutely);

            Infra.Data.DependencyInjectionExtension.ClearSpecificHangfireJobs(services, "processing_remove_videos");

            manager.AddOrUpdate<RemoveVideoCutCreatedService>(
                "processing_remove_videos",
                "processing",
                s => s.RemoveVideosAsync(CancellationToken.None),
                Cron.Minutely);
        }
        catch { }

        return services;
    }
}