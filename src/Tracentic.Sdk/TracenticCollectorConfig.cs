namespace Tracentic;

/// <summary>Base class for collector configurations.</summary>
public abstract class TracenticCollectorConfig { }

/// <summary>
/// Configuration for remote OTLP export to Tracentic cloud
/// or any compatible endpoint.
/// </summary>
public sealed class RemoteCollectorConfig
    : TracenticCollectorConfig
{
    /// <summary>The OTLP endpoint URL.</summary>
    public string? Endpoint { get; }

    /// <summary>The API key for authentication.</summary>
    public string? ApiKey { get; }

    internal RemoteCollectorConfig(
        string? endpoint, string? apiKey)
    {
        Endpoint = endpoint;
        ApiKey   = apiKey;
    }
}
