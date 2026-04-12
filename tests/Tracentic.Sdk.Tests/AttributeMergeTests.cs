using Tracentic;
using Tracentic.Sdk.Internal;
using Xunit;

namespace Tracentic.Sdk.Tests;

/// <summary>
/// Tests for <see cref="AttributeMerger"/>, which combines three layers
/// of attributes into a single dictionary before they are written to a span.
///
/// Merge priority (lowest → highest):
///   1. Global attributes (set at startup or runtime via TracenticGlobalContext)
///   2. Scope attributes  (passed when calling Begin / CreateChild)
///   3. Span attributes   (set directly on TracenticSpan)
///
/// On key collision the higher-priority layer wins. The original
/// dictionaries must never be mutated.
/// </summary>
public class AttributeMergeTests
{
    private TracenticGlobalContext CreateGlobal(Dictionary<string, object>? attrs = null)
    {
        var ctx = new TracenticGlobalContext();
        if (attrs is not null)
            foreach (var (k, v) in attrs)
                ctx.Set(k, v);
        return ctx;
    }

    /// <summary>
    /// Global attributes should appear on spans that have no scope.
    /// </summary>
    [Fact]
    public void GlobalAttributes_AppearedOnUnscopedSpan()
    {
        var global = CreateGlobal(new() { ["region"] = "us-east-1" });
        var merger = new AttributeMerger(global, new AttributeLimits());

        var result = merger.Merge(null, null);

        Assert.Equal("us-east-1", result["region"]);
    }

    /// <summary>
    /// Scope-level attributes should be present in the merged output.
    /// </summary>
    [Fact]
    public void ScopeAttributes_AppearOnScopedSpan()
    {
        var global = CreateGlobal();
        var merger = new AttributeMerger(global, new AttributeLimits());
        var scope = new TracenticScope("test", new Dictionary<string, object>
        {
            ["user_id"] = "user-42"
        }, null, null);

        var result = merger.Merge(scope, null);

        Assert.Equal("user-42", result["user_id"]);
    }

    /// <summary>
    /// Span attributes (layer 3) override scope attributes (layer 2)
    /// when both define the same key.
    /// </summary>
    [Fact]
    public void SpanAttributes_OverrideScopeAttributes()
    {
        var global = CreateGlobal();
        var merger = new AttributeMerger(global, new AttributeLimits());
        var scope = new TracenticScope("test", new Dictionary<string, object>
        {
            ["key"] = "scope-value"
        }, null, null);
        var spanAttrs = new Dictionary<string, object> { ["key"] = "span-value" };

        var result = merger.Merge(scope, spanAttrs);

        Assert.Equal("span-value", result["key"]);
    }

    /// <summary>
    /// Scope attributes (layer 2) override global attributes (layer 1)
    /// when both define the same key.
    /// </summary>
    [Fact]
    public void ScopeAttributes_OverrideGlobalAttributes()
    {
        var global = CreateGlobal(new() { ["key"] = "global-value" });
        var merger = new AttributeMerger(global, new AttributeLimits());
        var scope = new TracenticScope("test", new Dictionary<string, object>
        {
            ["key"] = "scope-value"
        }, null, null);

        var result = merger.Merge(scope, null);

        Assert.Equal("scope-value", result["key"]);
    }

    /// <summary>
    /// Merging must not mutate the global context. After a span overrides
    /// a global key, the global value should remain unchanged.
    /// </summary>
    [Fact]
    public void GlobalAttributes_UnchangedAfterRecordSpanWithOverride()
    {
        var global = CreateGlobal(new() { ["key"] = "global-value" });
        var merger = new AttributeMerger(global, new AttributeLimits());
        var spanAttrs = new Dictionary<string, object> { ["key"] = "span-value" };

        merger.Merge(null, spanAttrs);

        var globals = global.GetAll();
        Assert.Equal("global-value", globals["key"]);
    }

    /// <summary>
    /// When both scope and span attributes are null, only global
    /// attributes appear — and no exception is thrown.
    /// </summary>
    [Fact]
    public void NullSpanAttributes_OnlyGlobalApplied_NoException()
    {
        var global = CreateGlobal(new() { ["region"] = "eu-west-1" });
        var merger = new AttributeMerger(global, new AttributeLimits());

        var result = merger.Merge(null, null);

        Assert.Single(result);
        Assert.Equal("eu-west-1", result["region"]);
    }

    /// <summary>
    /// A null scope means layer 2 is skipped — only global attributes apply.
    /// </summary>
    [Fact]
    public void NullScope_OnlyGlobalApplied()
    {
        var global = CreateGlobal(new() { ["env"] = "prod" });
        var merger = new AttributeMerger(global, new AttributeLimits());

        var result = merger.Merge(null, null);

        Assert.Equal("prod", result["env"]);
    }

    /// <summary>
    /// Full three-layer merge with a collision on "shared". The span
    /// layer should win. Keys unique to each layer should all be present.
    /// </summary>
    [Fact]
    public void AllThreeLayers_SpanWinsOnCollision()
    {
        var global = CreateGlobal(new()
        {
            ["shared"] = "global",
            ["global_only"] = "g"
        });
        var merger = new AttributeMerger(global, new AttributeLimits());
        var scope = new TracenticScope("test", new Dictionary<string, object>
        {
            ["shared"] = "scope",
            ["scope_only"] = "s"
        }, null, null);
        var spanAttrs = new Dictionary<string, object>
        {
            ["shared"] = "span",
            ["span_only"] = "sp"
        };

        var result = merger.Merge(scope, spanAttrs);

        Assert.Equal("span", result["shared"]);
        Assert.Equal("g", result["global_only"]);
        Assert.Equal("s", result["scope_only"]);
        Assert.Equal("sp", result["span_only"]);
    }
}
