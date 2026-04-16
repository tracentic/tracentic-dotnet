using System.Net.Http;
using Microsoft.AspNetCore.Http;

namespace Tracentic;

/// <summary>
/// Configuration options for the Tracentic SDK.
/// </summary>
public class TracenticOptions
{
    /// <summary>
    /// Your Tracentic API key. If null or empty, spans are created
    /// locally but not exported. Enables local dev without an account.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Identifies your service in the dashboard.</summary>
    public string ServiceName { get; set; } = "unknown-service";

    /// <summary>
    /// OTLP ingestion endpoint. Defaults to Tracentic cloud.
    /// Override for self-hosted or local testing.
    /// </summary>
    public string Endpoint { get; set; } = "https://tracentic.dev";

    /// <summary>Deployment environment tag. Default: "production".</summary>
    public string Environment { get; set; } = "production";

    /// <summary>
    /// Pricing for LLM cost calculation. Keys are model identifiers
    /// (exact, case-sensitive match). Values are tuples of
    /// (costPerMillionInputTokens, costPerMillionOutputTokens) in USD.
    ///
    /// If a model is not present, llm.cost.total_usd is omitted.
    /// There is no built-in fallback pricing.
    ///
    /// Example:
    ///   options.CustomPricing = new()
    ///   {
    ///       ["claude-sonnet-4-20250514"] = (3.00, 15.00),
    ///       ["gpt-4o"]                   = (2.50, 10.00),
    ///   };
    /// </summary>
    public Dictionary<string, (double InputCostPerMillion,
                                double OutputCostPerMillion)>? CustomPricing
    { get; set; }

    /// <summary>
    /// Static attributes applied to every span for the lifetime of
    /// the application. Use for values known at startup:
    /// environment, region, version, deployment ID, etc.
    ///
    /// Example:
    ///   options.GlobalAttributes = new()
    ///   {
    ///       ["region"]  = "us-east-1",
    ///       ["version"] = "2.1.0",
    ///   };
    /// </summary>
    public Dictionary<string, object>? GlobalAttributes { get; set; }

    /// <summary>
    /// Delegate invoked once per HTTP request to resolve per-request
    /// attributes. Use for values only available at request time:
    /// user identity, tenant ID, request ID, etc.
    ///
    /// When set, the SDK automatically registers middleware.
    /// You do not need to call app.Use() or app.UseTracentic().
    ///
    /// Note: if you read context.User here, authentication middleware
    /// must run first. Use app.UseTracentic() explicitly after
    /// app.UseAuthentication() to control ordering.
    ///
    /// Example:
    ///   options.RequestAttributes = context => new()
    ///   {
    ///       ["user_id"]   = context.User.FindFirst("sub")?.Value,
    ///       ["tenant_id"] = context.User.FindFirst("tenant")?.Value,
    ///   };
    /// </summary>
    public Func<HttpContext, Dictionary<string, object?>>?
        RequestAttributes
    { get; set; }

    /// <summary>
    /// Configures where spans are sent. Exactly one collector can
    /// be registered. Defaults to remote cloud export using ApiKey
    /// and Endpoint if not set.
    ///
    /// Examples:
    ///
    ///   // Cloud (default - no property needed if ApiKey is set)
    ///   options.Collector = TracenticCollector.Remote();
    ///
    ///   // Custom endpoint
    ///   options.Collector = TracenticCollector.Remote("https://custom.endpoint", "key");
    /// </summary>
    public TracenticCollectorConfig? Collector { get; set; }

    /// <summary>
    /// Limits applied to user-supplied attributes to prevent oversized
    /// payloads. Defaults are aligned with the OpenTelemetry specification.
    /// </summary>
    public AttributeLimits AttributeLimits { get; set; } = new();

    /// <summary>
    /// Factory for the <see cref="HttpMessageHandler"/> used by the OTLP
    /// exporter. Override to supply custom TLS, proxy, or outbound HTTP
    /// middleware (e.g. Polly retry). The SDK owns the lifetime of the
    /// returned handler and disposes it on shutdown.
    ///
    /// Defaults to a <see cref="SocketsHttpHandler"/> with a 5-minute
    /// <c>PooledConnectionLifetime</c> so long-lived processes pick up
    /// DNS changes.
    /// </summary>
    public Func<HttpMessageHandler>? HttpMessageHandlerFactory { get; set; }

    /// <summary>
    /// Per-request timeout for OTLP exports. Default: 30 seconds.
    /// </summary>
    public TimeSpan ExportTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Internal - not part of the public API.</summary>
    internal bool MiddlewareRegisteredExplicitly { get; set; }
}

/// <summary>
/// Guards against unbounded attribute data. Applied during the merge
/// step so limits are enforced uniformly across global, scope, and
/// span attributes.
///
/// Users may lower these limits to catch accidental bloat during
/// development, but values are clamped to platform maximums that
/// cannot be exceeded.
/// </summary>
public class AttributeLimits
{
    internal const int PlatformMaxAttributeCount = 128;
    internal const int PlatformMaxStringValueLength = 4096;
    internal const int PlatformMaxKeyLength = 256;

    private int _maxAttributeCount = PlatformMaxAttributeCount;
    private int _maxStringValueLength = PlatformMaxStringValueLength;
    private int _maxKeyLength = PlatformMaxKeyLength;

    /// <summary>
    /// Maximum number of attributes per span. Attributes beyond this
    /// limit are silently dropped. Default and platform maximum: 128.
    /// </summary>
    public int MaxAttributeCount
    {
        get => _maxAttributeCount;
        set => _maxAttributeCount = Math.Clamp(value, 1, PlatformMaxAttributeCount);
    }

    /// <summary>
    /// Maximum length for string attribute values. Strings exceeding
    /// this limit are truncated. Default and platform maximum: 4096.
    /// </summary>
    public int MaxStringValueLength
    {
        get => _maxStringValueLength;
        set => _maxStringValueLength = Math.Clamp(value, 1, PlatformMaxStringValueLength);
    }

    /// <summary>
    /// Maximum length for attribute keys. Keys exceeding this limit
    /// are truncated. Default and platform maximum: 256.
    /// </summary>
    public int MaxKeyLength
    {
        get => _maxKeyLength;
        set => _maxKeyLength = Math.Clamp(value, 1, PlatformMaxKeyLength);
    }
}
