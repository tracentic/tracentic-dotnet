using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Tracentic.Sdk.Internal;

/// <summary>
/// Custom exporter that serializes Activities to OTLP/HTTP JSON format
/// and POSTs them to the Tracentic ingest endpoint.
/// The built-in OtlpExporter only supports gRPC and HTTP/protobuf,
/// but the Tracentic backend expects application/json.
/// </summary>
internal sealed class OtlpJsonExporter : BaseExporter<Activity>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly HttpMessageHandler _handler;
    private readonly Uri _endpoint;

    public OtlpJsonExporter(
        string endpoint,
        string apiKey,
        Func<HttpMessageHandler>? handlerFactory = null,
        TimeSpan? timeout = null)
    {
        _endpoint = new Uri($"{endpoint.TrimEnd('/')}/v1/ingest");
        _handler = handlerFactory?.Invoke() ?? new SocketsHttpHandler
        {
            // Recycle pooled connections periodically so long-lived
            // processes pick up DNS changes for the ingest endpoint.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        _http = new HttpClient(_handler, disposeHandler: false)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.Add("x-tracentic-api-key", apiKey);
        var version = typeof(OtlpJsonExporter).Assembly
            .GetName().Version?.ToString() ?? "0.0.0";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Tracentic.Sdk/{version}");
    }

    public override ExportResult Export(in Batch<Activity> batch)
    {
        // Build resource attributes from the TracerProvider's Resource
        var resourceAttrs = new List<OtlpJsonAttribute>();
        if (ParentProvider is TracerProvider tp)
        {
            var resource = tp.GetResource();
            foreach (var attr in resource.Attributes)
            {
                resourceAttrs.Add(new OtlpJsonAttribute
                {
                    Key = attr.Key,
                    Value = ConvertValue(attr.Value),
                });
            }
        }

        var spans = new List<OtlpJsonSpan>();
        foreach (var activity in batch)
            spans.Add(ConvertActivity(activity));

        if (spans.Count == 0)
            return ExportResult.Success;

        var request = new OtlpJsonRequest
        {
            ResourceSpans = new List<OtlpJsonResourceSpans>
            {
                new()
                {
                    Resource = new OtlpJsonResource { Attributes = resourceAttrs },
                    ScopeSpans = new List<OtlpJsonScopeSpans>
                    {
                        new()
                        {
                            Scope = new OtlpJsonScope
                            {
                                Name = "Tracentic",
                                Version = typeof(OtlpJsonExporter).Assembly
                                    .GetName().Version?.ToString(),
                            },
                            Spans = spans,
                        }
                    }
                }
            }
        };

        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Sync-over-async is required here because OpenTelemetry's
            // BaseExporter.Export is a synchronous API. ConfigureAwait(false)
            // avoids deadlocks by not capturing the synchronization context.
            using var response = _http.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = content },
                    HttpCompletionOption.ResponseContentRead)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            // Drain the response body so the HTTP connection is released
            // back to the pool and not left dangling.
            _ = response.Content.ReadAsStringAsync()
                .ConfigureAwait(false).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                // Diagnostics flow through an EventSource so operators can enable
                // them with `dotnet-trace --providers Tracentic-Sdk` without us
                // taking an ILogger dependency in the SDK.
                TracenticEventSource.Log.ExportFailed(
                    (int)response.StatusCode, response.ReasonPhrase ?? "");
            }

            return response.IsSuccessStatusCode
                ? ExportResult.Success
                : ExportResult.Failure;
        }
        catch (Exception ex)
        {
            TracenticEventSource.Log.ExportException(ex.GetType().FullName ?? "Exception", ex.Message);
            return ExportResult.Failure;
        }
    }

    [EventSource(Name = "Tracentic-Sdk")]
    private sealed class TracenticEventSource : EventSource
    {
        public static readonly TracenticEventSource Log = new();

        [Event(1, Level = EventLevel.Warning, Message = "OTLP export failed: HTTP {0} {1}")]
        public void ExportFailed(int statusCode, string reasonPhrase) =>
            WriteEvent(1, statusCode, reasonPhrase);

        [Event(2, Level = EventLevel.Error, Message = "OTLP export threw: {0}: {1}")]
        public void ExportException(string exceptionType, string message) =>
            WriteEvent(2, exceptionType, message);
    }

    private static OtlpJsonSpan ConvertActivity(Activity activity)
    {
        var attributes = new List<OtlpJsonAttribute>();
        foreach (var tag in activity.TagObjects)
        {
            attributes.Add(new OtlpJsonAttribute
            {
                Key = tag.Key,
                Value = ConvertValue(tag.Value),
            });
        }

        // OTLP requires timestamps as nanoseconds since Unix epoch.
        // .NET Ticks are 100-nanosecond intervals, so multiply by 100.
        var startNano = activity.StartTimeUtc.Ticks
            - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        startNano *= 100;

        var endNano = startNano + (activity.Duration.Ticks * 100);

        var span = new OtlpJsonSpan
        {
            TraceId = HexToBase64(activity.TraceId.ToString()),
            SpanId = HexToBase64(activity.SpanId.ToString()),
            ParentSpanId = activity.ParentSpanId == default
                ? null
                : HexToBase64(activity.ParentSpanId.ToString()),
            Name = activity.DisplayName,
            Kind = (int)activity.Kind,
            StartTimeUnixNano = startNano.ToString(),
            EndTimeUnixNano = endNano.ToString(),
            Attributes = attributes.Count > 0 ? attributes : null,
            Status = new OtlpJsonStatus
            {
                Code = activity.Status == ActivityStatusCode.Error ? 2
                     : activity.Status == ActivityStatusCode.Ok ? 1
                     : 0,
                Message = activity.StatusDescription,
            },
        };

        return span;
    }

    // OTLP JSON encodes trace/span IDs as base64, but Activity stores
    // them as hex strings. Convert hex → bytes → base64.
    private static string HexToBase64(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        return Convert.ToBase64String(bytes);
    }

    private static OtlpJsonValue ConvertValue(object? value) => value switch
    {
        string s => new OtlpJsonValue { StringValue = s },
        int i => new OtlpJsonValue { IntValue = i.ToString() },
        long l => new OtlpJsonValue { IntValue = l.ToString() },
        double d => new OtlpJsonValue { DoubleValue = d },
        float f => new OtlpJsonValue { DoubleValue = f },
        decimal m => new OtlpJsonValue { DoubleValue = (double)m },
        bool b => new OtlpJsonValue { BoolValue = b },
        _ => new OtlpJsonValue { StringValue = value?.ToString() },
    };

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        _http.Dispose();
        _handler.Dispose();
        return true;
    }

    // ── JSON DTOs matching OTLP/HTTP JSON spec ─────────────────────────────

    private sealed class OtlpJsonRequest
    {
        [JsonPropertyName("resourceSpans")]
        public List<OtlpJsonResourceSpans>? ResourceSpans { get; set; }
    }

    private sealed class OtlpJsonResourceSpans
    {
        [JsonPropertyName("resource")]
        public OtlpJsonResource? Resource { get; set; }

        [JsonPropertyName("scopeSpans")]
        public List<OtlpJsonScopeSpans>? ScopeSpans { get; set; }
    }

    private sealed class OtlpJsonResource
    {
        [JsonPropertyName("attributes")]
        public List<OtlpJsonAttribute>? Attributes { get; set; }
    }

    private sealed class OtlpJsonScopeSpans
    {
        [JsonPropertyName("scope")]
        public OtlpJsonScope? Scope { get; set; }

        [JsonPropertyName("spans")]
        public List<OtlpJsonSpan>? Spans { get; set; }
    }

    private sealed class OtlpJsonScope
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }

    private sealed class OtlpJsonSpan
    {
        [JsonPropertyName("traceId")]
        public string? TraceId { get; set; }

        [JsonPropertyName("spanId")]
        public string? SpanId { get; set; }

        [JsonPropertyName("parentSpanId")]
        public string? ParentSpanId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("kind")]
        public int Kind { get; set; }

        [JsonPropertyName("startTimeUnixNano")]
        public string? StartTimeUnixNano { get; set; }

        [JsonPropertyName("endTimeUnixNano")]
        public string? EndTimeUnixNano { get; set; }

        [JsonPropertyName("attributes")]
        public List<OtlpJsonAttribute>? Attributes { get; set; }

        [JsonPropertyName("status")]
        public OtlpJsonStatus? Status { get; set; }
    }

    private sealed class OtlpJsonAttribute
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public OtlpJsonValue? Value { get; set; }
    }

    private sealed class OtlpJsonValue
    {
        [JsonPropertyName("stringValue")]
        public string? StringValue { get; set; }

        [JsonPropertyName("intValue")]
        public string? IntValue { get; set; }

        [JsonPropertyName("doubleValue")]
        public double? DoubleValue { get; set; }

        [JsonPropertyName("boolValue")]
        public bool? BoolValue { get; set; }
    }

    private sealed class OtlpJsonStatus
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
