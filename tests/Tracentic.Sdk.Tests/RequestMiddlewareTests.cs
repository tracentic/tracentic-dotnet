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
/// Tests for <see cref="TracenticRequestMiddleware"/>, which injects
/// per-request attributes into the global context for the duration of
/// each HTTP request, then restores the previous values.
///
/// Each test spins up a real in-memory TestServer so the middleware
/// runs in a genuine ASP.NET Core pipeline.
/// </summary>
public class RequestMiddlewareTests : IDisposable
{
    public void Dispose()
    {
        TracenticGlobalContext.ResetCurrent();
    }

    /// <summary>
    /// When RequestAttributes returns an empty dictionary, the middleware
    /// should still pass the request through to the terminal handler.
    /// </summary>
    [Fact]
    public async Task EmptyRequestAttributes_PassesThrough()
    {
        var requestReached = false;

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddTracentic(options =>
                    {
                        options.ServiceName = "test";
                        options.RequestAttributes = ctx =>
                            new Dictionary<string, object?>();
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<TracenticRequestMiddleware>();
                    app.Run(async ctx =>
                    {
                        requestReached = true;
                        await ctx.Response.WriteAsync("ok");
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        await client.GetAsync("/");

        Assert.True(requestReached);
    }

    /// <summary>
    /// When RequestAttributes is not configured (null), the middleware
    /// should be a no-op pass-through.
    /// </summary>
    [Fact]
    public async Task NullRequestAttributes_PassesThrough()
    {
        var requestReached = false;

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddTracentic(options =>
                    {
                        options.ServiceName = "test";
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<TracenticRequestMiddleware>();
                    app.Run(async ctx =>
                    {
                        requestReached = true;
                        await ctx.Response.WriteAsync("ok");
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        await client.GetAsync("/");

        Assert.True(requestReached);
    }

    /// <summary>
    /// Per-request attributes should be visible in the global context
    /// while the request is being processed.
    /// </summary>
    [Fact]
    public async Task RequestAttributes_AvailableDuringRequest()
    {
        string? tenantDuringRequest = null;

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
                            ["tenant_id"] = "acme"
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
                        tenantDuringRequest = global.GetAll()
                            .GetValueOrDefault("tenant_id") as string;
                        await ctx.Response.WriteAsync("ok");
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        await client.GetAsync("/");

        Assert.Equal("acme", tenantDuringRequest);
    }

    /// <summary>
    /// After the request completes, per-request attributes must be
    /// removed from the global context so they don't leak into
    /// subsequent requests or background work.
    /// </summary>
    [Fact]
    public async Task RequestAttributes_CleanedUpAfterRequest()
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
                            ["tenant_id"] = "acme"
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

        var client = host.GetTestClient();
        await client.GetAsync("/");

        var global = host.Services.GetRequiredService<TracenticGlobalContext>();
        Assert.False(global.GetAll().ContainsKey("tenant_id"));
    }

    /// <summary>
    /// When RequestAttributes returns multiple key-value pairs, all of
    /// them should be present in the global context during the request.
    /// Also verifies that dynamic values like TraceIdentifier are resolved.
    /// </summary>
    [Fact]
    public async Task MultipleRequestAttributes_AllApplied()
    {
        var capturedAttrs = new Dictionary<string, object?>();

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
                            ["tenant_id"] = "acme",
                            ["user_id"] = "user-42",
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
                        var all = global.GetAll();
                        capturedAttrs["tenant_id"] = all.GetValueOrDefault("tenant_id");
                        capturedAttrs["user_id"] = all.GetValueOrDefault("user_id");
                        capturedAttrs["request_id"] = all.GetValueOrDefault("request_id");
                        await ctx.Response.WriteAsync("ok");
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        await client.GetAsync("/");

        Assert.Equal("acme", capturedAttrs["tenant_id"]);
        Assert.Equal("user-42", capturedAttrs["user_id"]);
        Assert.NotNull(capturedAttrs["request_id"]);
    }
}
