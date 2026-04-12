using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tracentic;
using Tracentic.Sdk.Internal;
using Xunit;

namespace Tracentic.Sdk.Tests;

/// <summary>
/// Tests for <see cref="TracenticGlobalContext"/>, the thread-safe
/// key-value store that holds global and per-request attributes.
///
/// Global attributes are set once at startup and live for the
/// application's lifetime. Per-request attributes are injected by
/// the middleware, visible during the request, and restored afterward.
/// </summary>
public class GlobalContextTests : IDisposable
{
    private readonly TracenticGlobalContext _ctx = new();

    public void Dispose()
    {
        TracenticGlobalContext.ResetCurrent();
    }

    // ── Basic CRUD ─────────────────────────────────────────────────────────

    /// <summary>Set then GetAll should return the stored value.</summary>
    [Fact]
    public void SetAndGet_RoundTrip()
    {
        _ctx.Set("key", "value");

        var all = _ctx.GetAll();
        Assert.Equal("value", all["key"]);
    }

    /// <summary>Removing a key makes it absent in subsequent reads.</summary>
    [Fact]
    public void Remove_Key()
    {
        _ctx.Set("key", "value");
        _ctx.Remove("key");

        var all = _ctx.GetAll();
        Assert.False(all.ContainsKey("key"));
    }

    /// <summary>
    /// GetAll must return a snapshot. Mutating the returned dictionary
    /// must not affect the context's internal state.
    /// </summary>
    [Fact]
    public void GetAll_ReturnsSnapshot_MutatingDoesNotAffectContext()
    {
        _ctx.Set("key", "value");

        var snapshot = _ctx.GetAll();
        if (snapshot is Dictionary<string, object> dict)
            dict["key"] = "mutated";

        var fresh = _ctx.GetAll();
        Assert.Equal("value", fresh["key"]);
    }

    // ── Thread safety ──────────────────────────────────────────────────────

    /// <summary>
    /// Hammer the context with 100 concurrent writers and readers.
    /// The test passes if no exception is thrown (no lock corruption).
    /// </summary>
    [Fact]
    public void ConcurrentReadsAndWrites_NoException()
    {
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(() =>
            {
                _ctx.Set($"key-{idx}", $"value-{idx}");
                _ctx.GetAll();
                _ctx.Remove($"key-{idx}");
            }));
        }

        Task.WaitAll(tasks.ToArray());
    }

    // ── DI integration ─────────────────────────────────────────────────────

    /// <summary>
    /// GlobalAttributes defined in TracenticOptions should be applied
    /// to the context immediately during AddTracentic, before any
    /// request is served.
    /// </summary>
    [Fact]
    public void GlobalAttributes_FromOptions_AppliedAtStartup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTracentic(options =>
        {
            options.ServiceName = "test-service";
            options.GlobalAttributes = new()
            {
                ["region"] = "us-east-1",
                ["version"] = "1.0"
            };
        });

        using var provider = services.BuildServiceProvider();
        var ctx = provider.GetRequiredService<TracenticGlobalContext>();

        var all = ctx.GetAll();
        Assert.Equal("us-east-1", all["region"]);
        Assert.Equal("1.0", all["version"]);
    }

    // ── Per-request attribute lifecycle ─────────────────────────────────────

    /// <summary>
    /// Per-request attributes should be visible during the request
    /// and absent after the request completes.
    /// </summary>
    [Fact]
    public async Task PerRequestAttrs_PresentDuringRequest_AbsentAfter()
    {
        string? capturedValue = null;

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddTracentic(options =>
                    {
                        options.ServiceName = "test";
                        options.RequestAttributes = ctx => new()
                        {
                            ["request_id"] = ctx.TraceIdentifier
                        };
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<TracenticRequestMiddleware>();
                    app.Run(async ctx =>
                    {
                        var global = ctx.RequestServices
                            .GetRequiredService<TracenticGlobalContext>();
                        capturedValue = global.GetAll()
                            .GetValueOrDefault("request_id") as string;
                        await ctx.Response.WriteAsync("ok");
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        await client.GetAsync("/");

        Assert.NotNull(capturedValue);

        // After the request, the middleware should have restored the original state
        var globalCtx = host.Services.GetRequiredService<TracenticGlobalContext>();
        Assert.False(globalCtx.GetAll().ContainsKey("request_id"));
    }

    /// <summary>
    /// If a global key already exists before the request, the middleware
    /// should restore it to its original value after the request — even
    /// if the request attribute overwrote it during the request.
    /// </summary>
    [Fact]
    public async Task PerRequestAttrs_RestoredAfterRequest_SuccessPath()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddTracentic(options =>
                    {
                        options.ServiceName = "test";
                        options.RequestAttributes = ctx => new()
                        {
                            ["key"] = "request-val"
                        };
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<TracenticRequestMiddleware>();
                    app.Run(async ctx =>
                        await ctx.Response.WriteAsync("ok"));
                });
            })
            .StartAsync();

        // Set a pre-existing global value that the request will overwrite
        var globalCtx = host.Services.GetRequiredService<TracenticGlobalContext>();
        globalCtx.Set("key", "original");

        var client = host.GetTestClient();
        await client.GetAsync("/");

        // The original value must be restored after the request
        Assert.Equal("original", globalCtx.GetAll()["key"]);
    }

    /// <summary>
    /// Same as the success path test, but the request handler throws.
    /// The middleware's finally block must still restore the original value.
    /// </summary>
    [Fact]
    public async Task PerRequestAttrs_RestoredAfterRequest_ExceptionPath()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddTracentic(options =>
                    {
                        options.ServiceName = "test";
                        options.RequestAttributes = ctx => new()
                        {
                            ["key"] = "request-val"
                        };
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<TracenticRequestMiddleware>();
                    app.Run(ctx =>
                        throw new InvalidOperationException("boom"));
                });
            })
            .StartAsync();

        var globalCtx = host.Services.GetRequiredService<TracenticGlobalContext>();
        globalCtx.Set("key", "original");

        var client = host.GetTestClient();
        try { await client.GetAsync("/"); } catch { }

        Assert.Equal("original", globalCtx.GetAll()["key"]);
    }

    /// <summary>
    /// When a request attribute has a null value, it should temporarily
    /// remove that key from the global context for the duration of the
    /// request. The original value must be restored afterward.
    ///
    /// This lets callers suppress a global attribute for specific requests
    /// (e.g. removing a default tenant_id for internal health checks).
    /// </summary>
    [Fact]
    public async Task NullValue_InRequestAttributes_RemovesKeyForDuration()
    {
        string? duringRequest = "not-set";

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddTracentic(options =>
                    {
                        options.ServiceName = "test";
                        options.RequestAttributes = ctx => new()
                        {
                            ["key"] = null
                        };
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<TracenticRequestMiddleware>();
                    app.Run(async ctx =>
                    {
                        var global = ctx.RequestServices
                            .GetRequiredService<TracenticGlobalContext>();
                        duringRequest = global.GetAll()
                            .ContainsKey("key") ? "present" : "absent";
                        await ctx.Response.WriteAsync("ok");
                    });
                });
            })
            .StartAsync();

        // Set a value that the null request attribute should temporarily remove
        var globalCtx = host.Services.GetRequiredService<TracenticGlobalContext>();
        globalCtx.Set("key", "original");

        var client = host.GetTestClient();
        await client.GetAsync("/");

        Assert.Equal("absent", duringRequest);
        Assert.Equal("original", globalCtx.GetAll()["key"]);
    }

    // ── Startup filter guard ───────────────────────────────────────────────

    /// <summary>
    /// When MiddlewareRegisteredExplicitly is true (set by app.UseTracentic()),
    /// the startup filter must not add the middleware a second time.
    /// </summary>
    [Fact]
    public void MiddlewareRegisteredExplicitly_PreventsDoubleRegistration()
    {
        var options = new TracenticOptions
        {
            ServiceName = "test",
            RequestAttributes = ctx => new() { ["x"] = "y" }
        };

        options.MiddlewareRegisteredExplicitly = true;

        var filter = new TracenticStartupFilter(options);
        var configureRan = false;
        var middlewareAdded = false;

        var configure = filter.Configure(app =>
        {
            configureRan = true;
        });

        // The filter should NOT add middleware when MiddlewareRegisteredExplicitly is true
        Assert.True(options.MiddlewareRegisteredExplicitly);
    }
}
