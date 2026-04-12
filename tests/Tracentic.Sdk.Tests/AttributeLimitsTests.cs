using Tracentic;
using Tracentic.Sdk.Internal;
using Xunit;

namespace Tracentic.Sdk.Tests;

/// <summary>
/// Tests for <see cref="AttributeLimits"/> enforcement inside
/// <see cref="AttributeMerger"/>. Verifies that oversized keys,
/// values, and attribute counts are capped before export.
/// </summary>
public class AttributeLimitsTests
{
    private TracenticGlobalContext CreateGlobal(Dictionary<string, object>? attrs = null)
    {
        var ctx = new TracenticGlobalContext();
        if (attrs is not null)
            foreach (var (k, v) in attrs)
                ctx.Set(k, v);
        return ctx;
    }

    // ── String value truncation ───────────────────────────────────────────

    [Fact]
    public void LongStringValue_IsTruncated()
    {
        var limits = new AttributeLimits { MaxStringValueLength = 10 };
        var merger = new AttributeMerger(CreateGlobal(), limits);
        var spanAttrs = new Dictionary<string, object>
        {
            ["msg"] = "hello world, this is way too long"
        };

        var result = merger.Merge(null, spanAttrs);

        Assert.Equal("hello worl", result["msg"]);
    }

    [Fact]
    public void StringValueAtExactLimit_IsNotTruncated()
    {
        var limits = new AttributeLimits { MaxStringValueLength = 5 };
        var merger = new AttributeMerger(CreateGlobal(), limits);
        var spanAttrs = new Dictionary<string, object>
        {
            ["msg"] = "hello"
        };

        var result = merger.Merge(null, spanAttrs);

        Assert.Equal("hello", result["msg"]);
    }

    [Fact]
    public void NonStringValue_IsNotAffectedByStringLimit()
    {
        var limits = new AttributeLimits { MaxStringValueLength = 1 };
        var merger = new AttributeMerger(CreateGlobal(), limits);
        var spanAttrs = new Dictionary<string, object>
        {
            ["count"] = 999999
        };

        var result = merger.Merge(null, spanAttrs);

        Assert.Equal(999999, result["count"]);
    }

    // ── Key truncation ────────────────────────────────────────────────────

    [Fact]
    public void LongKey_IsTruncated()
    {
        var limits = new AttributeLimits { MaxKeyLength = 5 };
        var merger = new AttributeMerger(CreateGlobal(), limits);
        var spanAttrs = new Dictionary<string, object>
        {
            ["very_long_key_name"] = "value"
        };

        var result = merger.Merge(null, spanAttrs);

        Assert.True(result.ContainsKey("very_"));
        Assert.Equal("value", result["very_"]);
    }

    [Fact]
    public void KeyAtExactLimit_IsNotTruncated()
    {
        var limits = new AttributeLimits { MaxKeyLength = 3 };
        var merger = new AttributeMerger(CreateGlobal(), limits);
        var spanAttrs = new Dictionary<string, object>
        {
            ["abc"] = "value"
        };

        var result = merger.Merge(null, spanAttrs);

        Assert.True(result.ContainsKey("abc"));
    }

    // ── Attribute count cap ───────────────────────────────────────────────

    [Fact]
    public void ExcessAttributes_AreDropped()
    {
        var limits = new AttributeLimits { MaxAttributeCount = 3 };
        var merger = new AttributeMerger(CreateGlobal(), limits);
        var spanAttrs = new Dictionary<string, object>
        {
            ["a"] = 1,
            ["b"] = 2,
            ["c"] = 3,
            ["d"] = 4,
            ["e"] = 5,
        };

        var result = merger.Merge(null, spanAttrs);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void AttributeCountAtExactLimit_AllPreserved()
    {
        var limits = new AttributeLimits { MaxAttributeCount = 3 };
        var merger = new AttributeMerger(CreateGlobal(), limits);
        var spanAttrs = new Dictionary<string, object>
        {
            ["a"] = 1,
            ["b"] = 2,
            ["c"] = 3,
        };

        var result = merger.Merge(null, spanAttrs);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void CountLimit_AppliesAcrossAllLayers()
    {
        var limits = new AttributeLimits { MaxAttributeCount = 2 };
        var global = CreateGlobal(new() { ["g1"] = "a", ["g2"] = "b", ["g3"] = "c" });
        var merger = new AttributeMerger(global, limits);

        var result = merger.Merge(null, null);

        Assert.Equal(2, result.Count);
    }

    // ── Default limits are permissive ─────────────────────────────────────

    [Fact]
    public void DefaultLimits_DoNotTruncateReasonableData()
    {
        var merger = new AttributeMerger(CreateGlobal(), new AttributeLimits());
        var spanAttrs = new Dictionary<string, object>
        {
            ["region"] = "us-east-1",
            ["user_id"] = "user-42",
            ["request_id"] = "req-abc-123"
        };

        var result = merger.Merge(null, spanAttrs);

        Assert.Equal(3, result.Count);
        Assert.Equal("us-east-1", result["region"]);
        Assert.Equal("user-42", result["user_id"]);
        Assert.Equal("req-abc-123", result["request_id"]);
    }

    // ── Platform ceiling clamping ─────────────────────────────────────────

    [Fact]
    public void MaxAttributeCount_ClampedToPlatformCeiling()
    {
        var limits = new AttributeLimits { MaxAttributeCount = 99999 };

        Assert.Equal(AttributeLimits.PlatformMaxAttributeCount, limits.MaxAttributeCount);
    }

    [Fact]
    public void MaxStringValueLength_ClampedToPlatformCeiling()
    {
        var limits = new AttributeLimits { MaxStringValueLength = 99999 };

        Assert.Equal(AttributeLimits.PlatformMaxStringValueLength, limits.MaxStringValueLength);
    }

    [Fact]
    public void MaxKeyLength_ClampedToPlatformCeiling()
    {
        var limits = new AttributeLimits { MaxKeyLength = 99999 };

        Assert.Equal(AttributeLimits.PlatformMaxKeyLength, limits.MaxKeyLength);
    }

    [Fact]
    public void Limits_CanBeLoweredBelowDefaults()
    {
        var limits = new AttributeLimits
        {
            MaxAttributeCount = 16,
            MaxStringValueLength = 512,
            MaxKeyLength = 64,
        };

        Assert.Equal(16, limits.MaxAttributeCount);
        Assert.Equal(512, limits.MaxStringValueLength);
        Assert.Equal(64, limits.MaxKeyLength);
    }

    [Fact]
    public void Limits_ClampedToMinimumOfOne()
    {
        var limits = new AttributeLimits
        {
            MaxAttributeCount = 0,
            MaxStringValueLength = -5,
            MaxKeyLength = -100,
        };

        Assert.Equal(1, limits.MaxAttributeCount);
        Assert.Equal(1, limits.MaxStringValueLength);
        Assert.Equal(1, limits.MaxKeyLength);
    }
}
