using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Tracentic;
using Tracentic.Sdk.Internal;
using Xunit;

namespace Tracentic.Sdk.Tests;

/// <summary>
/// Tests for the cost calculation logic in <see cref="TracenticClient"/>.
///
/// Cost is computed as:
///   (inputTokens / 1,000,000 * inputCostPerMillion)
///   + (outputTokens / 1,000,000 * outputCostPerMillion)
///
/// Cost is only set when all of Model, InputTokens, OutputTokens,
/// and a matching CustomPricing entry are present. Lookup is exact
/// and case-sensitive.
/// </summary>
public class CostCalculationTests : IDisposable
{
    private readonly List<Activity> _exportedActivities = new();
    private readonly ActivityListener _listener;

    public CostCalculationTests()
    {
        // Subscribe to the "Tracentic" ActivitySource so we can inspect
        // the tags that RecordSpan writes to each Activity.
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Tracentic",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _exportedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        TracenticGlobalContext.ResetCurrent();
    }

    private TracenticClient CreateClient(
        Dictionary<string, (double, double)>? pricing)
    {
        var options = new TracenticOptions
        {
            // Defensive copy mirrors what AddTracentic does at startup
            CustomPricing = pricing is not null
                ? new Dictionary<string, (double, double)>(pricing)
                : null
        };
        var global = new TracenticGlobalContext();
        var merger = new AttributeMerger(global, new AttributeLimits());
        return new TracenticClient(global, merger, options);
    }

    /// <summary>
    /// With a matching model in CustomPricing, the cost tag should be
    /// calculated and present on the Activity.
    /// </summary>
    [Fact]
    public void KnownModel_CorrectCalculation()
    {
        var client = CreateClient(new()
        {
            ["claude-sonnet-4-20250514"] = (3.00, 15.00)
        });

        client.RecordSpan(new TracenticSpan
        {
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            EndedAt = DateTimeOffset.UtcNow,
            Model = "claude-sonnet-4-20250514",
            InputTokens = 1000,
            OutputTokens = 500
        });

        var activity = _exportedActivities.Last();
        var cost = activity.GetTagItem("llm.cost.total_usd");
        Assert.NotNull(cost);
        // (1000 / 1M * 3.00) + (500 / 1M * 15.00) = 0.003 + 0.0075 = 0.0105
        Assert.Equal(0.0105, (double)cost!, precision: 10);
    }

    /// <summary>
    /// When the model is not in the pricing dictionary, the cost tag
    /// should be absent (no fallback pricing).
    /// </summary>
    [Fact]
    public void UnknownModel_CostAbsent()
    {
        var client = CreateClient(new()
        {
            ["gpt-4o"] = (2.50, 10.00)
        });

        client.RecordSpan(new TracenticSpan
        {
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            EndedAt = DateTimeOffset.UtcNow,
            Model = "unknown-model",
            InputTokens = 1000,
            OutputTokens = 500
        });

        var activity = _exportedActivities.Last();
        Assert.Null(activity.GetTagItem("llm.cost.total_usd"));
    }

    /// <summary>
    /// When CustomPricing is null (not configured), cost should never
    /// be computed regardless of the model.
    /// </summary>
    [Fact]
    public void CustomPricingNull_CostAbsentForAnyModel()
    {
        var client = CreateClient(null);

        client.RecordSpan(new TracenticSpan
        {
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            EndedAt = DateTimeOffset.UtcNow,
            Model = "gpt-4o",
            InputTokens = 1000,
            OutputTokens = 500
        });

        var activity = _exportedActivities.Last();
        Assert.Null(activity.GetTagItem("llm.cost.total_usd"));
    }

    /// <summary>
    /// A model priced at (0, 0) should produce a cost tag of $0.00,
    /// not a missing tag. This distinguishes "free" from "unknown".
    /// </summary>
    [Fact]
    public void ZeroCostModel_CostPresentAsZero()
    {
        var client = CreateClient(new()
        {
            ["free-model"] = (0.00, 0.00)
        });

        client.RecordSpan(new TracenticSpan
        {
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            EndedAt = DateTimeOffset.UtcNow,
            Model = "free-model",
            InputTokens = 1000,
            OutputTokens = 500
        });

        var activity = _exportedActivities.Last();
        var cost = activity.GetTagItem("llm.cost.total_usd");
        Assert.NotNull(cost);
        Assert.Equal(0.0, (double)cost!);
    }

    /// <summary>
    /// Model lookup is case-sensitive. "GPT-4O" should not match "gpt-4o".
    /// </summary>
    [Fact]
    public void CaseSensitiveMismatch_CostAbsent()
    {
        var client = CreateClient(new()
        {
            ["gpt-4o"] = (2.50, 10.00)
        });

        client.RecordSpan(new TracenticSpan
        {
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            EndedAt = DateTimeOffset.UtcNow,
            Model = "GPT-4O",
            InputTokens = 1000,
            OutputTokens = 500
        });

        var activity = _exportedActivities.Last();
        Assert.Null(activity.GetTagItem("llm.cost.total_usd"));
    }

    /// <summary>
    /// AddTracentic defensively copies the pricing dictionary. Mutations
    /// to the original dictionary after setup should not affect the SDK.
    /// </summary>
    [Fact]
    public void MutationSafety_PostStartupChangesIgnored()
    {
        var pricing = new Dictionary<string, (double, double)>
        {
            ["gpt-4o"] = (2.50, 10.00)
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTracentic(options =>
        {
            options.ServiceName = "test";
            options.CustomPricing = pricing;
        });

        // Mutate original dictionary after AddTracentic
        pricing["new-model"] = (1.00, 2.00);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<TracenticOptions>();

        Assert.False(options.CustomPricing!.ContainsKey("new-model"));
    }
}
