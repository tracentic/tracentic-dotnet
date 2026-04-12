using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Tracentic;
using Tracentic.Sdk.Internal;
using Xunit;

namespace Tracentic.Sdk.Tests;

/// <summary>
/// Tests for remote collector configuration and DI wiring.
/// Verifies that the collector factory methods produce the correct
/// config types and that the SDK tolerates missing API keys gracefully.
/// </summary>
public class CollectorTests : IDisposable
{
    private readonly List<Activity> _activities = new();
    private readonly ActivityListener _listener;

    public CollectorTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Tracentic",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        TracenticGlobalContext.ResetCurrent();
    }

    /// <summary>
    /// When no Collector is set, the default should be null (resolved
    /// at startup to Remote using ApiKey/Endpoint from options).
    /// </summary>
    [Fact]
    public void DefaultCollector_RemoteUsedWhenCollectorIsNull()
    {
        var options = new TracenticOptions
        {
            ApiKey = "test-key",
            Endpoint = "https://example.com"
        };

        Assert.Null(options.Collector);
    }

    /// <summary>
    /// TracenticCollector.Remote() should return a RemoteCollectorConfig
    /// that carries the endpoint and API key through unchanged.
    /// </summary>
    [Fact]
    public void ExplicitRemoteCollector_OverridesOptionsValues()
    {
        var remote = TracenticCollector.Remote(
            endpoint: "https://custom.endpoint",
            apiKey: "custom-key");

        Assert.IsType<RemoteCollectorConfig>(remote);
        var config = (RemoteCollectorConfig)remote;
        Assert.Equal("https://custom.endpoint", config.Endpoint);
        Assert.Equal("custom-key", config.ApiKey);
    }

    /// <summary>
    /// A null API key should not throw during DI setup. The exporter
    /// simply becomes a no-op (spans are created but not exported).
    /// This supports local development without a Tracentic account.
    /// </summary>
    [Fact]
    public void RemoteWithNullApiKey_NoException()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTracentic(options =>
        {
            options.ServiceName = "test";
            options.Collector = TracenticCollector.Remote(apiKey: null);
        });

        using var provider = services.BuildServiceProvider();
        var tracentic = provider.GetRequiredService<ITracentic>();
        Assert.NotNull(tracentic);
    }

    private TracenticClient CreateClientWithListener()
    {
        var global = new TracenticGlobalContext();
        var merger = new AttributeMerger(global, new AttributeLimits());
        return new TracenticClient(global, merger, new TracenticOptions());
    }
}
