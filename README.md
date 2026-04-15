# Tracentic .NET SDK

LLM observability with scoped tracing and OTLP export for .NET applications.

## Installation

Add a project reference or, once published, install via NuGet:

```bash
dotnet add package Tracentic.Sdk
```

The SDK targets **.NET 6.0**, **8.0**, and **10.0**.

## Quick start

Register Tracentic in your DI container at startup:

```csharp
builder.Services.AddTracentic(opts =>
{
    opts.ApiKey = "your-api-key";
    opts.ServiceName = "my-service";
    opts.Environment = "production";
});
```

Then inject `ITracentic` and start tracing:

```csharp
public class MyService(ITracentic tracentic)
{
    public async Task<string> Summarize(string text)
    {
        var scope = tracentic.Begin("summarize", attributes: new()
        {
            ["user_id"] = "user-123",
        });

        var startedAt = DateTimeOffset.UtcNow;
        var result = await CallLlm(text);
        var endedAt = DateTimeOffset.UtcNow;

        tracentic.RecordSpan(scope, new TracenticSpan
        {
            StartedAt = startedAt,
            EndedAt = endedAt,
            Provider = "anthropic",
            Model = "claude-sonnet-4-20250514",
            InputTokens = result.Usage.InputTokens,
            OutputTokens = result.Usage.OutputTokens,
            OperationType = "chat",
        });

        return result.Text;
    }
}
```

## Features

### Scoped tracing

Group related LLM calls under a logical scope. Nest scopes for multi-step pipelines:

```csharp
var pipeline = tracentic.Begin("rag-pipeline", correlationId: "order-42");

// Child scope inherits the parent link automatically
var synthesis = pipeline.CreateChild("synthesis", attributes: new()
{
    ["strategy"] = "hybrid",
});
```

### Error recording

```csharp
tracentic.RecordError(scope, span, exception);
```

### Scopeless spans

For standalone LLM calls that don't belong to a larger operation:

```csharp
tracentic.RecordSpan(new TracenticSpan
{
    StartedAt = startedAt,
    EndedAt = endedAt,
    Provider = "openai",
    Model = "gpt-4o-mini",
    InputTokens = 200,
    OutputTokens = 50,
    OperationType = "chat",
});
```

### Custom pricing

```csharp
opts.CustomPricing = new()
{
    ["claude-sonnet-4-20250514"] = (3.00, 15.00),
    ["gpt-4o"] = (2.50, 10.00),
};
```

Cost is calculated automatically when a matching pricing entry exists and both token counts are present.

### Global attributes

Static attributes applied to every span:

```csharp
opts.GlobalAttributes = new()
{
    ["region"] = "us-east-1",
    ["version"] = "2.1.0",
};
```

Dynamic attributes can be set/removed at runtime:

```csharp
TracenticGlobalContext.Current.Set("deploy_id", "deploy-abc");
TracenticGlobalContext.Current.Remove("deploy_id");
```

### Per-request attributes (ASP.NET Core)

The SDK automatically registers middleware via a startup filter when using `AddTracentic()`. Configure per-request attributes:

```csharp
opts.RequestAttributes = (context) => new Dictionary<string, object?>
{
    ["http.method"] = context.Request.Method,
    ["user_id"] = context.User.FindFirst("sub")?.Value,
};
```

If you need to control where the middleware runs in the pipeline (e.g. after authentication so `context.User` is populated), call `UseTracentic()` explicitly instead:

```csharp
app.UseAuthentication();
app.UseTracentic();   // must come after auth if RequestAttributes reads context.User
```

When `UseTracentic()` is called explicitly, the automatic startup filter registration is skipped.

### Cross-service linking

Tracentic does not propagate scope IDs automatically — you pass them explicitly through whatever transport connects your services (HTTP headers, message properties, etc.).

For cross-service linking to work, both services must integrate the Tracentic SDK (or implement the OTLP JSON ingest API directly) and their API keys must belong to the **same tenant**. Spans from different tenants are isolated and cannot be linked.

**Via HTTP header:**

```csharp
// Service A — outgoing request
var scope = tracentic.Begin("gateway-handler");
httpClient.DefaultRequestHeaders.Add("x-tracentic-scope-id", scope.Id);

// Service B — incoming request
var parentScopeId = context.Request.Headers["x-tracentic-scope-id"].FirstOrDefault();
var linked = tracentic.Begin("worker", parentScopeId: parentScopeId);
```

**Via service bus message:**

```csharp
// Producer
var scope = tracentic.Begin("order-processor");
var message = new ServiceBusMessage(payload);
message.ApplicationProperties["tracentic-scope-id"] = scope.Id;
await sender.SendMessageAsync(message);

// Consumer
var parentScopeId = message.ApplicationProperties["tracentic-scope-id"] as string;
var linked = tracentic.Begin("fulfillment", parentScopeId: parentScopeId);
```

### HTTP transport

The SDK owns a single long-lived `HttpClient` dedicated to the ingest endpoint. Connections are pooled and recycled every 5 minutes so long-running processes pick up DNS changes. To customize TLS, proxy, or outbound HTTP middleware (e.g. Polly retry), supply your own `HttpMessageHandler`:

```csharp
opts.HttpMessageHandlerFactory = () => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    Proxy = new WebProxy("http://corp-proxy:8080"),
};

opts.ExportTimeout = TimeSpan.FromSeconds(10);
```

The SDK owns the returned handler's lifetime and disposes it on shutdown. Do not share the handler across other `HttpClient` instances.

## Configuration reference

| Option | Default | Description |
|--------|---------|-------------|
| `ApiKey` | `null` | API key. If null, spans are created locally but not exported |
| `ServiceName` | `"unknown-service"` | Service identifier in the dashboard |
| `Endpoint` | `"https://ingest.tracentic.dev"` | OTLP ingestion endpoint |
| `Environment` | `"production"` | Deployment environment tag |
| `Collector` | remote (cloud) | Where spans are sent. See `TracenticCollector.Remote(...)` |
| `CustomPricing` | `null` | Model pricing for cost calculation |
| `GlobalAttributes` | `null` | Static attributes on every span |
| `RequestAttributes` | `null` | Per-request attribute callback (ASP.NET Core) |
| `AttributeLimits` | platform defaults | Limits on attribute count, key/value length |
| `HttpMessageHandlerFactory` | `SocketsHttpHandler` w/ 5-min pooled lifetime | Custom HTTP transport for the OTLP exporter |
| `ExportTimeout` | `30s` | Per-request timeout for OTLP exports |

## Running tests

```bash
cd tests/Tracentic.Sdk.Tests

# All tests
dotnet test

# A single test class
dotnet test --filter "FullyQualifiedName~ScopeTests"

# A single test
dotnet test --filter "FullyQualifiedName~CreateChild_SetsParentId"
```

### Test files

| File | What it covers |
|------|----------------|
| `ScopeTests.cs` | Scope creation, nesting, cross-service linking, correlation IDs |
| `GlobalContextTests.cs` | Global context set/get/remove, per-request lifecycle, thread safety |
| `AttributeMergeTests.cs` | Three-layer merge priority (global < scope < span), collision resolution |
| `AttributeLimitsTests.cs` | Attribute count caps, key/value length truncation, platform maximums |
| `CostCalculationTests.cs` | Pricing lookup, known/unknown models, case sensitivity |
| `RequestMiddlewareTests.cs` | Middleware attribute injection, cleanup after request completion |
| `CollectorTests.cs` | Collector configuration, null API key handling |
