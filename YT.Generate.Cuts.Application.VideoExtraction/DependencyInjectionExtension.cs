using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YT.Generate.Cuts.Application.Abstractions.Common;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.VideoSource;
using YT.Generate.Cuts.Application.VideoExtraction.Youtube;

namespace YT.Generate.Cuts.Application.VideoExtraction;
public static class DependencyInjectionExtension
{
    public static IServiceCollection AddApplicationVideoExtraction(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddScoped<ValidationBehavior>();
        services.AddScoped<IAppDispatcher, AppDispatcher>();

        AddVideoSource(services);

        var handlerInterfaces = new[] { typeof(ICommandHandler<>), typeof(ICommandHandler<,>) };

        var handlers = assembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && handlerInterfaces.Contains(i.GetGenericTypeDefinition())));

        foreach (var handler in handlers)
        {
            var interfaces = handler.GetInterfaces()
                .Where(i => i.IsGenericType && handlerInterfaces.Contains(i.GetGenericTypeDefinition()));

            foreach (var @interface in interfaces)
            {
                services.AddScoped(@interface, handler);
            }
        }

        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
    /// <summary>
    /// Registra os provedores de catálogo e download.
    /// </summary>
    /// <remarks>
    /// A seleção é por configuração para que a troca de provedor não exija
    /// recompilar: <c>VideoSource__Catalog</c> escolhe um catálogo e
    /// <c>VideoSource__Downloader</c> aceita uma lista ordenada que se torna a
    /// cadeia de fallback.
    /// <para>
    /// Padrões: catálogo no YoutubeExplode (em processo, rápido) e download em
    /// <c>ytdlp,youtubeexplode</c> — o yt-dlp primeiro porque é o mantido contra
    /// as mudanças do endpoint de player, com o YoutubeExplode como reserva.
    /// </para>
    /// </remarks>
    private static void AddVideoSource(IServiceCollection services)
    {
        // Singleton: um único YoutubeClient (e portanto um único HttpClient)
        // para toda a aplicação, com os cookies carregados uma só vez.
        services.AddSingleton<YoutubeClientProvider>();

        services.AddSingleton(sp => YtDlpOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()));

        services.AddSingleton<YtDlpVideoDownloader>();
        services.AddSingleton<YoutubeExplodeVideoDownloader>();
        services.AddSingleton<YtDlpVideoCatalog>();
        services.AddSingleton<YoutubeExplodeVideoCatalog>();

        services.AddSingleton<IVideoCatalog>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var choice = (configuration["VideoSource:Catalog"] ?? "youtubeexplode").Trim().ToLowerInvariant();

            return choice switch
            {
                "ytdlp" or "yt-dlp" => sp.GetRequiredService<YtDlpVideoCatalog>(),
                _ => sp.GetRequiredService<YoutubeExplodeVideoCatalog>(),
            };
        });

        services.AddSingleton<IVideoDownloader>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var order = configuration["VideoSource:Downloader"] ?? "ytdlp,youtubeexplode";

            var chain = order
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => Resolve(sp, name))
                .Where(provider => provider is not null)
                .Select(provider => provider!)
                .ToList();

            if (chain.Count == 0)
                chain.Add(sp.GetRequiredService<YtDlpVideoDownloader>());

            return chain.Count == 1
                ? chain[0]
                : new FallbackVideoDownloader(chain, sp.GetRequiredService<ILogger<FallbackVideoDownloader>>());
        });
    }

    private static IVideoDownloader? Resolve(IServiceProvider sp, string name) =>
        name.ToLowerInvariant() switch
        {
            "ytdlp" or "yt-dlp" => sp.GetRequiredService<YtDlpVideoDownloader>(),
            "youtubeexplode" or "explode" => sp.GetRequiredService<YoutubeExplodeVideoDownloader>(),
            _ => null,
        };

}
