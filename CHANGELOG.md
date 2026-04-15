# Changelog

All notable changes to the Tracentic .NET SDK are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/tracentic/tracentic-dotnet/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/tracentic/tracentic-dotnet/releases/tag/v0.1.0
