using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Tracentic.Sdk.Internal;

/// <summary>
/// Automatically registers <see cref="TracenticRequestMiddleware"/>
/// at the start of the pipeline — unless the user has already called
/// app.UseTracentic() explicitly (which sets MiddlewareRegisteredExplicitly).
/// This prevents double-registration while still providing zero-config
/// middleware for the common case.
/// </summary>
internal sealed class TracenticStartupFilter : IStartupFilter
{
    private readonly TracenticOptions _options;

    public TracenticStartupFilter(TracenticOptions options)
        => _options = options;

    public Action<IApplicationBuilder> Configure(
        Action<IApplicationBuilder> next)
        => app =>
        {
            if (!_options.MiddlewareRegisteredExplicitly)
                app.UseMiddleware<TracenticRequestMiddleware>();
            next(app);
        };
}
