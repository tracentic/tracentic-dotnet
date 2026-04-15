namespace Tracentic;

/// <summary>
/// Well-known constants used by the Tracentic SDK.
/// </summary>
public static class TracenticHeaders
{
    /// <summary>
    /// Header / message-property name for propagating a parent scope ID
    /// across services. Use this constant rather than hard-coding the
    /// string so a typo on either end can't silently break cross-service
    /// linking.
    /// </summary>
    public const string ScopeId = "x-tracentic-scope-id";
}

/// <summary>
/// The primary Tracentic interface. Inject this into your services
/// to record LLM spans and group them into named operation scopes.
/// </summary>
public interface ITracentic
{
    /// <summary>
    /// Creates a new root operation scope. Pass the returned
    /// TracenticScope into RecordSpan to associate spans with
    /// this operation. No disposal required — fire and forget.
    /// </summary>
    TracenticScope Begin(
        string name,
        Dictionary<string, object>? attributes = null,
        string? correlationId = null
    );

    /// <summary>
    /// Creates a child scope whose parent is in a different service.
    /// Service A passes scope.Id via a message or header.
    /// Service B calls this overload to link its scope as a child.
    /// </summary>
    TracenticScope Begin(
        string name,
        string parentScopeId,
        Dictionary<string, object>? attributes = null,
        string? correlationId = null
    );

    /// <summary>
    /// Records a completed LLM span associated with a scope.
    /// Attributes merged: span &gt; scope &gt; global (span wins).
    /// </summary>
    void RecordSpan(TracenticScope scope, TracenticSpan span);

    /// <summary>
    /// Records a completed LLM span with no scope association.
    /// Appears as a top-level span. Global attributes applied.
    /// </summary>
    void RecordSpan(TracenticSpan span);

    /// <summary>
    /// Records an LLM span that resulted in an exception,
    /// associated with a scope.
    /// </summary>
    void RecordError(TracenticScope scope, TracenticSpan span,
        Exception exception);

    /// <summary>
    /// Records an LLM span that resulted in an exception,
    /// with no scope association.
    /// </summary>
    void RecordError(TracenticSpan span, Exception exception);
}
