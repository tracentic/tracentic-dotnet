# Changelog

All notable changes to the Tracentic .NET SDK are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-04-15

### Added
- `TracenticHeaders.ScopeId` constant for cross-service scope-ID propagation. Use this in place of the literal `"x-tracentic-scope-id"` string so a typo on either end can't silently break linking.
- One-time warning (via `System.Diagnostics.Trace.TraceWarning`) when a span has token data but no matching `CustomPricing` entry — surfaces missing cost configuration that previously failed silently. Emitted at most once per unique model.
- One-time warning when `AddTracentic` is called without an `ApiKey` — clarifies that spans are created locally but not exported.
- README guidance for serverless runtimes (AWS Lambda, Azure Functions) explaining why `AppDomain.ProcessExit` may not fire and how to call `TracerProvider.ForceFlush` from `finally`.

### Changed
- Default `Endpoint` is now `https://tracentic.dev` (previously `https://ingest.tracentic.dev`). Any caller passing an explicit `Endpoint` is unaffected.
- README clarifies that `CustomPricing` is required for cost tracking — there are no built-in pricing defaults — and that the SDK warns when it's missing.
- README quick start now includes `CustomPricing` so the expected configuration shape is visible by default.

## [0.1.0] - 2026-04-15

Initial public release.

### Added
- Scoped tracing with `ITracentic.Begin`, `TracenticScope.CreateChild`, and cross-service linking via `parentScopeId`.
- Span recording (`RecordSpan`, `RecordError`) with and without a scope.
- Three-layer attribute merge (global < scope < span) with platform-enforced limits.
- Global attribute context (`TracenticGlobalContext`) with static and dynamic attributes.
- ASP.NET Core middleware and `IStartupFilter` for per-request attribute injection.
- `UseTracentic()` extension for explicit pipeline placement (e.g. after authentication).
- LLM cost calculation from user-supplied `CustomPricing`.
- OTLP/HTTP JSON exporter with batched delivery, long-lived pooled `HttpClient`, configurable timeout, and `User-Agent` header.
- `HttpMessageHandlerFactory` option for custom TLS, proxy, or Polly middleware.
- Multi-targeting: `net6.0`, `net8.0`, `net10.0`.
- Source Link and symbol package (`.snupkg`) for debuggable consumption.

[Unreleased]: https://github.com/tracentic/tracentic-dotnet/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/tracentic/tracentic-dotnet/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/tracentic/tracentic-dotnet/releases/tag/v0.1.0
