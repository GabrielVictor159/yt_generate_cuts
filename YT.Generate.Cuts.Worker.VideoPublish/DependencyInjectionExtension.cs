using Hangfire;
using YT.Generate.Cuts.Publish.Abstractions;
using YT.Generate.Cuts.Publish.TikTok;
using YT.Generate.Cuts.Worker.VideoPublish.Services;

namespace YT.Generate.Cuts.Worker.VideoPublish;

public static class DependencyInjectionExtension
{
    public static IServiceCollection AddPublishServices(this IServiceCollection services)
    {
        services.AddScoped<PublishCutsService>();

        services.AddScoped<IPublishPlugin, TikTokPublishPlugin>();

        return services;
    }

    public static IServiceProvider AddPublishServicesHangfire(this IServiceProvider services)
    {
        var manager = services.GetRequiredService<IRecurringJobManager>();

        try
        {
            Infra.Data.DependencyInjectionExtension.ClearSpecificHangfireJobs(services, "publish-cuts");

            manager.AddOrUpdate<PublishCutsService>(
                "publish-cuts",
                "publish",
                s => s.PublishPendingCutsAsync(CancellationToken.None),
                Cron.Minutely);
        }
        catch { }

        return services;
    }
}