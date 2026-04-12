using Microsoft.AspNetCore.Http;

namespace Tracentic.Sdk.Internal;

/// <summary>
/// Injects per-request attributes into the global context for the
/// duration of a single HTTP request, then restores the previous
/// values. This uses a snapshot-and-restore pattern rather than
/// scoped state because the global context is a singleton shared
/// across all requests.
///
/// Null values in RequestAttributes temporarily remove a key,
/// letting callers suppress a global attribute for specific requests.
/// </summary>
internal sealed class TracenticRequestMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TracenticOptions _options;
    private readonly TracenticGlobalContext _global;

    public TracenticRequestMiddleware(
        RequestDelegate next,
        TracenticOptions options,
        TracenticGlobalContext global)
    {
        _next    = next;
        _options = options;
        _global  = global;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var attributes = _options.RequestAttributes?.Invoke(context);

        if (attributes is null || attributes.Count == 0)
        {
            await _next(context);
            return;
        }

        // Snapshot current global values for keys we are about to set
        var snapshot = new Dictionary<string, object?>(attributes.Count);
        foreach (var key in attributes.Keys)
        {
            _global.GetAll().TryGetValue(key, out var existing);
            snapshot[key] = existing;
        }

        // Apply per-request attributes
        foreach (var (key, value) in attributes)
        {
            if (value is not null) _global.Set(key, value);
            else                   _global.Remove(key);
        }

        try
        {
            await _next(context);
        }
        finally
        {
            // Always restore — even if the request threw
            foreach (var (key, prev) in snapshot)
            {
                if (prev is not null) _global.Set(key, prev);
                else                  _global.Remove(key);
            }
        }
    }
}
