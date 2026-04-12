using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Tracentic.Sdk.Internal;

namespace Tracentic;

/// <summary>
/// Extension methods for adding Tracentic middleware to the
/// ASP.NET Core request pipeline.
/// </summary>
public static class IApplicationBuilderExtensions
{
    /// <summary>
    /// Adds Tracentic request middleware at this position in the pipeline.
    /// Use when RequestAttributes reads context.User and authentication
    /// middleware must run first.
    ///
    ///   app.UseAuthentication();
    ///   app.UseTracentic();
    /// </summary>
    public static IApplicationBuilder UseTracentic(
        this IApplicationBuilder app)
    {
        var options = app.ApplicationServices
            .GetRequiredService<TracenticOptions>();
        options.MiddlewareRegisteredExplicitly = true;
        return app.UseMiddleware<TracenticRequestMiddleware>();
    }
}
