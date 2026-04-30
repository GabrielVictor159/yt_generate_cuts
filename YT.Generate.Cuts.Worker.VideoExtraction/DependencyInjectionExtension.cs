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

        services.ClearSpecificHangfireJobs<MonitoringChannelsService>("monitoring-channels");

        manager.AddOrUpdate<MonitoringChannelsService>(
            "monitoring-channels",
            s => s.MonitoringNewVideosAsync(CancellationToken.None),
            Cron.Minutely);

        return services;
    }

    public static void ClearSpecificHangfireJobs<T>(this IServiceProvider serviceProvider, string recurringJobId)
    {
        var monitor = JobStorage.Current.GetMonitoringApi();
        var backgroundJobClient = serviceProvider.GetRequiredService<IBackgroundJobClient>();

        var targetType = typeof(T);

        var processing = monitor.ProcessingJobs(0, int.MaxValue)
            .Where(x => x.Value?.Job?.Type == targetType);

        var enqueued = monitor.EnqueuedJobs("default", 0, int.MaxValue)
            .Where(x => x.Value?.Job?.Type == targetType);

        var scheduled = monitor.ScheduledJobs(0, int.MaxValue)
            .Where(x => x.Value?.Job?.Type == targetType);

        var failed = monitor.FailedJobs(0, int.MaxValue)
            .Where(x => x.Value?.Job?.Type == targetType);

        foreach (var job in processing)
            backgroundJobClient.Delete(job.Key);
        foreach (var job in enqueued)
            backgroundJobClient.Delete(job.Key);
        foreach (var job in scheduled)
            backgroundJobClient.Delete(job.Key);
        foreach (var job in failed)
            backgroundJobClient.Delete(job.Key);

        var manager = serviceProvider.GetRequiredService<IRecurringJobManager>();
        manager.RemoveIfExists(recurringJobId);
    }
}