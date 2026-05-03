using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;
using System.Reflection;
using YT.Generate.Cuts.Application.Abstractions.Common;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;

namespace YT.Generate.Cuts.Application.VideoProcessing;
public static class DependencyInjectionExtension
{
    public static IServiceCollection AddApplicationVideoProcessing (this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddScoped<ValidationBehavior>();
        services.AddScoped<IAppDispatcher, AppDispatcher>();

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

    public static IServiceCollection AddOllama(this IServiceCollection services, IConfiguration config)
    {
        var uri = new Uri(config["OllamaUrl"] ?? "http://localhost:11434");
        var httpClient = new HttpClient
        {
            BaseAddress = uri,
            Timeout = TimeSpan.FromMinutes(10)
        };

        services.AddSingleton<IOllamaApiClient>(new OllamaApiClient(httpClient));
        return services;
    }
}
