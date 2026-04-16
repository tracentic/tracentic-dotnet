using System.Diagnostics;
using Tracentic;
using Tracentic.Sdk.Internal;
using Xunit;

namespace Tracentic.Sdk.Tests;

/// <summary>
/// Tests for <see cref="TracenticScope"/> lifecycle:
/// creation, nesting, cross-service linking, correlation IDs,
/// and how scope metadata is attached to recorded spans.
///
/// Scopes are lightweight value objects - they are not disposable,
/// have no finalizer, and carry no mutable state after creation.
/// Their ID is auto-generated and never set by the developer.
/// </summary>
public class ScopeTests : IDisposable
{
    private readonly List<Activity> _activities = new();
    private readonly ActivityListener _listener;

    public ScopeTests()
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

    private TracenticClient CreateClient()
    {
        var global = new TracenticGlobalContext();
        var merger = new AttributeMerger(global, new AttributeLimits());
        return new TracenticClient(global, merger, new TracenticOptions());
    }

    // ── Scope creation ─────────────────────────────────────────────────────

    /// <summary>Begin returns a scope with the given name, a non-empty auto-generated ID, and no parent.</summary>
    [Fact]
    public void Begin_ReturnsScope_WithCorrectName_NullParentId_NonNullId()
    {
        var client = CreateClient();
        var scope = client.Begin("my-operation");

        Assert.Equal("my-operation", scope.Name);
        Assert.Null(scope.ParentId);
        Assert.NotNull(scope.Id);
        Assert.NotEmpty(scope.Id);
    }

    /// <summary>Every call to Begin generates a unique scope ID.</summary>
    [Fact]
    public void Begin_AlwaysGeneratesUniqueId()
    {
        var client = CreateClient();
        var ids = Enumerable.Range(0, 10)
            .Select(_ => client.Begin("test").Id)
            .ToList();

        Assert.Equal(10, ids.Distinct().Count());
    }

    /// <summary>The optional correlationId is preserved on the scope.</summary>
    [Fact]
    public void Begin_WithCorrelationId_SetCorrectly()
    {
        var client = CreateClient();
        var scope = client.Begin("test", correlationId: "order-123");

        Assert.Equal("order-123", scope.CorrelationId);
    }

    /// <summary>When no correlationId is provided, it defaults to null.</summary>
    [Fact]
    public void Begin_WithoutCorrelationId_IsNull()
    {
        var client = CreateClient();
        var scope = client.Begin("test");

        Assert.Null(scope.CorrelationId);
    }

    // ── Nesting ────────────────────────────────────────────────────────────

    /// <summary>
    /// CreateChild sets the child's ParentId to the parent's Id,
    /// and generates a distinct Id for the child.
    /// </summary>
    [Fact]
    public void CreateChild_ParentIdEqualsParentId_ChildIdDiffers()
    {
        var client = CreateClient();
        var parent = client.Begin("parent");
        var child = parent.CreateChild("child");

        Assert.Equal(parent.Id, child.ParentId);
        Assert.NotEqual(parent.Id, child.Id);
    }

    /// <summary>Child scopes can carry their own correlation ID.</summary>
    [Fact]
    public void CreateChild_WithCorrelationId_Set()
    {
        var client = CreateClient();
        var parent = client.Begin("parent");
        var child = parent.CreateChild("child", correlationId: "corr-1");

        Assert.Equal("corr-1", child.CorrelationId);
    }

    /// <summary>
    /// Three levels of nesting: root → child → grandchild.
    /// Each level's ParentId should point to its immediate parent.
    /// </summary>
    [Fact]
    public void DeeplyNested_ParentChainCorrect()
    {
        var client = CreateClient();
        var root = client.Begin("root");
        var child = root.CreateChild("child");
        var grandchild = child.CreateChild("grandchild");

        Assert.Null(root.ParentId);
        Assert.Equal(root.Id, child.ParentId);
        Assert.Equal(child.Id, grandchild.ParentId);
    }

    // ── Cross-service linking ──────────────────────────────────────────────

    /// <summary>
    /// Begin with a parentScopeId (received from another service) sets
    /// ParentId to that external ID while still auto-generating a new Id.
    /// </summary>
    [Fact]
    public void Begin_WithParentScopeId_SetsParentId_AutoGeneratesId()
    {
        var client = CreateClient();
        var scope = client.Begin("remote-child",
            parentScopeId: "ext-id");

        Assert.Equal("ext-id", scope.ParentId);
        Assert.NotEqual("ext-id", scope.Id);
        Assert.NotEmpty(scope.Id);
    }

    // ── Span recording with scopes ─────────────────────────────────────────

    /// <summary>
    /// RecordSpan with a scope should tag the Activity with scope ID,
    /// name, and started_at.
    /// </summary>
    [Fact]
    public void RecordSpan_WithScope_HasScopeAttributes()
    {
        var client = CreateClient();
        var scope = client.Begin("my-op");

        client.RecordSpan(scope, new TracenticSpan
        {
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            EndedAt = DateTimeOffset.UtcNow,
            Provider = "anthropic"
        });

        var activity = _activities.Last();
        Assert.Equal(scope.Id, activity.GetTagItem("tracentic.scope.id"));
        Assert.Equal("my-op", activity.GetTagItem("tracentic.scope.name"));
        Assert.NotNull(activity.GetTagItem("tracentic.scope.started_at"));
    }

    /// <summary>
    /// When a span is recorded under a child scope, the Activity should
    /// carry the child's parent scope ID.
    /// </summary>
    [Fact]
    public void RecordSpan_WithScope_ParentId_Set()
    {
        var client = CreateClient();
        var parent = client.Begin("parent");
        var child = parent.CreateChild("child");

        client.RecordSpan(child, new TracenticSpan
        {
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            EndedAt = DateTimeOffset.UtcNow
        });

        var activity = _activities.Last();
        Assert.Equal(parent.Id, activity.GetTagItem("tracentic.scope.parent_id"));
    }

    /// <summary>
    /// Correlation ID on the scope flows through to the Activity tag.
    /// </summary>
    [Fact]
    public void RecordSpan_WithScope_CorrelationId_Set()
    {
        var client = CreateClient();
        var scope = client.Begin("test", correlationId: "order-42");

        client.RecordSpan(scope, new TracenticSpan
        {
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            EndedAt = DateTimeOffset.UtcNow
        });

        var activity = _activities.Last();
        Assert.Equal("order-42",
            activity.GetTagItem("tracentic.scope.correlation_id"));
    }

    /// <summary>
    /// When the scope has no correlation ID, the tag should be absent
    /// (not an empty string).
    /// </summary>
    [Fact]
    public void RecordSpan_WithScope_NullCorrelationId_AttributeAbsent()
    {
        var client = CreateClient();
        var scope = client.Begin("test");

        client.RecordSpan(scope, new TracenticSpan
        {
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            EndedAt = DateTimeOffset.UtcNow
        });

        var activity = _activities.Last();
        Assert.Null(activity.GetTagItem("tracentic.scope.correlation_id"));
    }

    // ── Scopeless spans ────────────────────────────────────────────────────

    /// <summary>
    /// RecordSpan without a scope should produce an Activity with no
    /// scope-related tags at all.
    /// </summary>
    [Fact]
    public void RecordSpan_NoScope_ScopeAttributesAbsent()
    {
        var client = CreateClient();

        client.RecordSpan(new TracenticSpan
        {
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            EndedAt = DateTimeOffset.UtcNow,
            Provider = "openai"
        });

        var activity = _activities.Last();
        Assert.Null(activity.GetTagItem("tracentic.scope.id"));
        Assert.Null(activity.GetTagItem("tracentic.scope.name"));
        Assert.Null(activity.GetTagItem("tracentic.scope.started_at"));
    }

    // ── Concurrency ────────────────────────────────────────────────────────

    /// <summary>
    /// Two scopes recorded concurrently should not contaminate each
    /// other's attributes.
    /// </summary>
    [Fact]
    public void ConcurrentScopes_NoCrossContamination()
    {
        var client = CreateClient();
        var scope1 = client.Begin("scope-1", attributes: new() { ["tag"] = "one" });
        var scope2 = client.Begin("scope-2", attributes: new() { ["tag"] = "two" });

        var tasks = new[]
        {
            Task.Run(() => client.RecordSpan(scope1, new TracenticSpan
            {
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
                EndedAt = DateTimeOffset.UtcNow
            })),
            Task.Run(() => client.RecordSpan(scope2, new TracenticSpan
            {
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
                EndedAt = DateTimeOffset.UtcNow
            }))
        };
        Task.WaitAll(tasks);

        Assert.Equal(2, _activities.Count);
        var names = _activities.Select(a =>
            a.GetTagItem("tracentic.scope.name") as string).ToHashSet();
        Assert.Contains("scope-1", names);
        Assert.Contains("scope-2", names);
    }

    // ── Design contract ────────────────────────────────────────────────────

    /// <summary>
    /// TracenticScope is intentionally not IDisposable. It is a
    /// lightweight value object with no resources to release.
    /// </summary>
    [Fact]
    public void Scope_RequiresNoDisposal()
    {
        var client = CreateClient();
        var scope = client.Begin("ephemeral");

        Assert.False(typeof(TracenticScope)
            .GetInterfaces().Any(i => i == typeof(IDisposable)));

        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
