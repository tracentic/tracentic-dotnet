using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Tracentic.Sdk.Internal;

/// <summary>
/// A no-op processor that hooks <see cref="AppDomain.ProcessExit"/>
/// to flush the parent <see cref="TracerProvider"/> before the process exits.
/// This ensures short-lived apps (console tools, CLI commands, batch
/// jobs) don't silently lose buffered spans — no hosted service or
/// explicit <c>ForceFlush</c> call required.
///
/// If the provider is shut down through normal disposal (e.g. host
/// shutdown), <see cref="OnShutdown"/> unhooks the event first so
/// the handler never fires redundantly.
/// </summary>
internal sealed class FlushOnExitProcessor : BaseProcessor<Activity>
{
    public FlushOnExitProcessor()
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        (ParentProvider as TracerProvider)?.ForceFlush(timeoutMilliseconds: 5000);
    }

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        return true;
    }
}
