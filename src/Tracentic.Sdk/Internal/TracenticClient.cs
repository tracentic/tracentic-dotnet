using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Tracentic.Sdk.Internal;

/// <summary>
/// Core SDK implementation. Creates OpenTelemetry Activities for each
/// recorded LLM span, attaching merged attributes, scope metadata,
/// and optional cost calculations.
///
/// Registered as scoped in DI so that per-request state (if any)
/// is isolated between concurrent HTTP requests.
/// </summary>
internal sealed class TracenticClient : ITracentic
{
    private readonly TracenticGlobalContext _global;
    private readonly AttributeMerger _merger;
    private readonly TracenticOptions _options;
    private readonly HashSet<string> _pricingWarned = new();
    private readonly object _pricingWarnedLock = new();

    public TracenticClient(
        TracenticGlobalContext global,
        AttributeMerger merger,
        TracenticOptions options)
    {
        _global  = global;
        _merger  = merger;
        _options = options;
    }

    // Defensive-copy the caller's attributes so mutations after Begin()
    // don't silently change what gets attached to future spans.
    public TracenticScope Begin(
        string name,
        Dictionary<string, object>? attributes = null,
        string? correlationId = null)
        => new TracenticScope(
            name,
            attributes is not null
                ? new Dictionary<string, object>(attributes)
                : null,
            correlationId,
            parentId: null
        );

    public TracenticScope Begin(
        string name,
        string parentScopeId,
        Dictionary<string, object>? attributes = null,
        string? correlationId = null)
        => new TracenticScope(
            name,
            attributes is not null
                ? new Dictionary<string, object>(attributes)
                : null,
            correlationId,
            parentId: parentScopeId
        );

    public void RecordSpan(TracenticScope scope, TracenticSpan span)
    {
        var merged = _merger.Merge(scope, span.Attributes);
        RecordInternal(span, merged, scope);
    }

    public void RecordSpan(TracenticSpan span)
    {
        var merged = _merger.Merge(null, span.Attributes);
        RecordInternal(span, merged, null);
    }

    public void RecordError(TracenticScope scope, TracenticSpan span,
        Exception exception)
    {
        var merged = _merger.Merge(scope, span.Attributes);
        RecordErrorInternal(span, merged, exception, scope);
    }

    public void RecordError(TracenticSpan span, Exception exception)
    {
        var merged = _merger.Merge(null, span.Attributes);
        RecordErrorInternal(span, merged, exception, null);
    }

    private void RecordInternal(
        TracenticSpan span,
        IReadOnlyDictionary<string, object> merged,
        TracenticScope? scope)
    {
        using var activity = TracenticSdk.ActivitySource.StartActivity(
            BuildSpanName(span.Provider, span.OperationType),
            ActivityKind.Client
        );
        if (activity is null) return;

        SetTimestamps(activity, span);
        SetLlmAttributes(activity, span);
        SetCustomAttributes(activity, merged);
        SetScopeAttributes(activity, scope);
        SetCost(activity, span);
        activity.SetStatus(ActivityStatusCode.Ok);
    }

    private void RecordErrorInternal(
        TracenticSpan span,
        IReadOnlyDictionary<string, object> merged,
        Exception exception,
        TracenticScope? scope)
    {
        using var activity = TracenticSdk.ActivitySource.StartActivity(
            BuildSpanName(span.Provider, span.OperationType),
            ActivityKind.Client
        );
        if (activity is null) return;

        SetTimestamps(activity, span);
        SetLlmAttributes(activity, span);
        SetCustomAttributes(activity, merged);
        SetScopeAttributes(activity, scope);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.RecordException(exception);
        activity.SetTag("llm.error.type", exception.GetType().Name);
    }

    // Build a human-readable Activity name like "llm.anthropic.chat".
    // Falls back gracefully when provider or operation type is missing.
    private static string BuildSpanName(string? provider,
        string? operationType)
        => (provider, operationType) switch
        {
            (not null, not null) => $"llm.{provider}.{operationType}",
            (not null, null)     => $"llm.{provider}",
            _                    => "llm.call"
        };

    private static void SetTimestamps(Activity a, TracenticSpan s)
    {
        a.SetStartTime(s.StartedAt.UtcDateTime);
        a.SetEndTime(s.EndedAt.UtcDateTime);
        a.SetTag("llm.duration_ms",
            (int)(s.EndedAt - s.StartedAt).TotalMilliseconds);
    }

    private static void SetLlmAttributes(Activity a, TracenticSpan s)
    {
        if (s.Provider      is not null)
            a.SetTag("llm.provider",            s.Provider);
        if (s.Model         is not null)
            a.SetTag("llm.request.model",        s.Model);
        if (s.OperationType is not null)
            a.SetTag("llm.request.type",         s.OperationType);
        if (s.InputTokens   is not null)
            a.SetTag("llm.usage.input_tokens",   s.InputTokens);
        if (s.OutputTokens  is not null)
            a.SetTag("llm.usage.output_tokens",  s.OutputTokens);
        if (s.InputTokens is not null && s.OutputTokens is not null)
            a.SetTag("llm.usage.total_tokens",
                s.InputTokens.Value + s.OutputTokens.Value);
    }

    private static void SetCustomAttributes(
        Activity a,
        IReadOnlyDictionary<string, object> attrs)
    {
        foreach (var (k, v) in attrs)
            a.SetTag(k, v);
    }

    private static void SetScopeAttributes(Activity a,
        TracenticScope? scope)
    {
        if (scope is null) return;
        a.SetTag("tracentic.scope.id",         scope.Id);
        a.SetTag("tracentic.scope.name",        scope.Name);
        a.SetTag("tracentic.scope.started_at",
            scope.StartedAt.ToString("O"));
        if (scope.ParentId is not null)
            a.SetTag("tracentic.scope.parent_id",       scope.ParentId);
        if (scope.CorrelationId is not null)
            a.SetTag("tracentic.scope.correlation_id",  scope.CorrelationId);
    }

    // Cost is only computed when all four prerequisites are met:
    // Model, InputTokens, OutputTokens, and a matching CustomPricing entry.
    // This is intentional — partial data produces no cost rather than a
    // misleading estimate. Typed as double end-to-end (SDK → OTLP JSON → DB)
    // so no conversion happens at any hop: OTLP JSON numbers are IEEE-754 and
    // double's ~15 significant digits are more than enough for per-span USD.
    private void SetCost(Activity a, TracenticSpan s)
    {
        if (s.Model is null || s.InputTokens is null
            || s.OutputTokens is null)
            return;

        if (_options.CustomPricing is null
            || !_options.CustomPricing.TryGetValue(s.Model, out var p))
        {
            WarnMissingPricing(s.Model);
            return;
        }

        var cost =
            (s.InputTokens.Value  / 1_000_000.0) * p.InputCostPerMillion +
            (s.OutputTokens.Value / 1_000_000.0) * p.OutputCostPerMillion;
        a.SetTag("llm.cost.total_usd", cost);
    }

    private void WarnMissingPricing(string model)
    {
        lock (_pricingWarnedLock)
        {
            if (!_pricingWarned.Add(model)) return;
        }
        System.Diagnostics.Trace.TraceWarning(
            "[tracentic] No CustomPricing entry for model \"{0}\" — " +
            "llm.cost.total_usd will be omitted. Set " +
            "TracenticOptions.CustomPricing to enable cost tracking.",
            model);
    }
}
