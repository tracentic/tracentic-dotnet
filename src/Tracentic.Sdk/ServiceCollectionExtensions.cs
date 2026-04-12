using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Tracentic.Sdk.Internal;

namespace Tracentic;

/// <summary>
/// Extension methods for configuring Tracentic in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Tracentic services to the dependency injection container.
    /// </summary>
    public static IServiceCollection AddTracentic(
        this IServiceCollection services,
        Action<TracenticOptions> configure)
    {
        var options = new TracenticOptions();
        configure(options);

        // Copy CustomPricing so post-startup mutations are ignored
        if (options.CustomPricing is not null)
            options.CustomPricing = new Dictionary<string, (double, double)>(
                options.CustomPricing);

        // 1. Register TracenticOptions as singleton
        services.AddSingleton(options);

        // 2. Register TracenticGlobalContext as singleton
        var globalContext = new TracenticGlobalContext();
        TracenticGlobalContext.SetCurrent(globalContext);
        services.AddSingleton(globalContext);

        // 3. Apply options.GlobalAttributes immediately
        if (options.GlobalAttributes is not null)
        {
            foreach (var (key, value) in options.GlobalAttributes)
                globalContext.Set(key, value);
        }

        // 4–9. Configure OTel TracerProvider
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                // 4. Listen to "Tracentic" ActivitySource
                builder.AddSource("Tracentic");

                // 8. Set resource attributes
                builder.SetResourceBuilder(
                    ResourceBuilder.CreateDefault()
                        .AddService(
                            serviceName: options.ServiceName,
                            serviceVersion: typeof(ServiceCollectionExtensions)
                                .Assembly.GetName().Version?.ToString())
                        .AddAttributes(new Dictionary<string, object>
                        {
                            ["deployment.environment"] = options.Environment
                        }));

                // 6. Ensure buffered spans are flushed on process exit
                builder.AddProcessor(new FlushOnExitProcessor());

                // 7–8. Configure collector/exporter
                ConfigureCollector(builder, options);
            });

        // 10. Register ITracentic as scoped
        services.AddSingleton(options.AttributeLimits);
        services.AddSingleton<AttributeMerger>();
        services.AddScoped<ITracentic, TracenticClient>();

        // 12. Register middleware if RequestAttributes is configured
        if (options.RequestAttributes is not null)
        {
            services.AddTransient<TracenticRequestMiddleware>();
            services.AddTransient<IStartupFilter, TracenticStartupFilter>();
        }

        return services;
    }

    private static void ConfigureCollector(
        TracerProviderBuilder builder,
        TracenticOptions options)
    {
        var collector = options.Collector
            ?? TracenticCollector.Remote(
                   options.Endpoint,
                   options.ApiKey);

        if (collector is not RemoteCollectorConfig remote)
            return;

        var endpoint = remote.Endpoint ?? options.Endpoint;
        var apiKey   = remote.ApiKey   ?? options.ApiKey;

        if (string.IsNullOrEmpty(apiKey))
        {
            // No API key — no-op, spans not exported
            return;
        }

        var exporter = new OtlpJsonExporter(endpoint, apiKey);
        builder.AddProcessor(
            new BatchActivityExportProcessor(
                exporter,
                scheduledDelayMilliseconds: 5000,
                maxQueueSize: 512,
                maxExportBatchSize: 128));
    }
}
