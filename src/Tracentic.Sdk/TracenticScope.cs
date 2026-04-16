using System.Collections.Immutable;

namespace Tracentic;

/// <summary>
/// Represents a logical operation scope. Pass to RecordSpan to
/// associate spans with this operation. Fire and forget - no
/// disposal or End call required.
///
/// Create a root scope: _tracentic.Begin("name")
/// Create a nested scope: scope.CreateChild("name")
/// </summary>
public sealed class TracenticScope
{
    /// <summary>
    /// Auto-generated UUID. Always unique. Never set by the developer.
    /// Used internally to reconstruct the scope tree.
    /// Pass this to another service as parentScopeId for cross-service
    /// linking.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Optional business identifier for cross-service correlation
    /// and searching. Set by the developer. Multiple scopes may
    /// intentionally share the same CorrelationId.
    /// Use a meaningful value: order ID, session ID, job ID.
    /// </summary>
    public string? CorrelationId { get; }

    /// <summary>The name of this operation scope.</summary>
    public string Name { get; }

    /// <summary>
    /// The Id of the parent scope, or null for root scopes.
    /// Set automatically by CreateChild or the parentScopeId
    /// overload of Begin.
    /// </summary>
    public string? ParentId { get; }

    /// <summary>UTC timestamp when Begin() was called.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Attributes associated with this scope. Merged into every
    /// span that references this scope - overrides global attributes
    /// on key collision. Overridden by span-level attributes.
    /// </summary>
    public IReadOnlyDictionary<string, object> Attributes { get; }

    internal TracenticScope(
        string name,
        IReadOnlyDictionary<string, object>? attributes,
        string? correlationId,
        string? parentId)
    {
        Id = Guid.NewGuid().ToString("N");
        Name = name;
        CorrelationId = correlationId;
        ParentId = parentId;
        StartedAt = DateTimeOffset.UtcNow;
        Attributes = attributes
            ?? ImmutableDictionary<string, object>.Empty;
    }

    /// <summary>
    /// Creates a child scope nested under this scope.
    /// Child's ParentId is set to this scope's Id automatically.
    /// </summary>
    public TracenticScope CreateChild(
        string name,
        Dictionary<string, object>? attributes = null,
        string? correlationId = null)
        => new TracenticScope(
            name,
            attributes is not null
                ? new Dictionary<string, object>(attributes)
                : null,
            correlationId,
            parentId: this.Id
        );
}
