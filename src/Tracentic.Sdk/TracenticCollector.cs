namespace Tracentic;

/// <summary>
/// Factory for configuring where Tracentic sends span data.
/// Exactly one collector must be registered.
/// </summary>
public static class TracenticCollector
{
    /// <summary>
    /// Sends spans to the Tracentic cloud endpoint or any
    /// OTLP HTTP endpoint. This is the default if no collector
    /// is configured.
    /// </summary>
    public static TracenticCollectorConfig Remote(
        string? endpoint = null,
        string? apiKey   = null)
        => new RemoteCollectorConfig(endpoint, apiKey);
}
