# Tracentic.Sdk.Tests

Unit and integration tests for the Tracentic .NET SDK. Uses **xUnit** on .NET 8.0.

## Running tests

```bash
# All tests
dotnet test

# A single test class
dotnet test --filter "FullyQualifiedName~CollectorTests"

# A single test
dotnet test --filter "FullyQualifiedName~RemoteWithNullApiKey_NoException"
```

## Test files

| File | Tests | What it covers |
|------|------:|----------------|
| `ScopeTests.cs` | 15 | Scope creation, nesting, cross-service linking via `parentScopeId`, correlation IDs, concurrent scopes, scopeless spans |
| `GlobalContextTests.cs` | 10 | `TracenticGlobalContext` set/get/remove, per-request attribute lifecycle, thread safety, middleware registration guard |
| `AttributeMergeTests.cs` | 8 | Three-layer merge priority (global < scope < span), collision resolution, null-safety, mutation isolation |
| `CostCalculationTests.cs` | 6 | Pricing lookup, known/unknown models, zero-cost models, case sensitivity, `CustomPricing` mutation safety |
| `RequestMiddlewareTests.cs` | 5 | Middleware pass-through, attribute injection during requests, cleanup after request completion |
| `CollectorTests.cs` | 3 | Remote collector config, default collector behaviour, null API key handling |

## Test patterns

All test classes that create OpenTelemetry activities follow the same setup:

- An `ActivityListener` subscribed to the `"Tracentic"` source captures stopped activities into a `List<Activity>`.
- A `TracenticClient` is created directly (bypassing DI) using `TracenticGlobalContext` and `AttributeMerger`.
- `Dispose()` tears down the listener and resets the global context to prevent cross-test contamination.

## Dependencies

- `Tracentic.Sdk` (project reference)
- `Microsoft.AspNetCore.TestHost` (for middleware tests)
- `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`
