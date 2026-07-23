# Eigenverft.Routed.RequestFilters

Composable request filtering, traffic control, diagnostics, and hosting utilities for ASP.NET Core applications.

> [!IMPORTANT]
> This project is currently **pre-1.0**. Public APIs, option names, defaults, and configuration behavior may change between preview releases.

## Overview

`Eigenverft.Routed.RequestFilters` provides small, independently configurable ASP.NET Core middleware components. Applications can combine only the filters and supporting services they need instead of adopting one monolithic request-processing pipeline.

The package includes:

- request classification by host name, URL, URI segment, path depth, file extension, HTTP method, HTTP protocol, TLS protocol, language, user agent, remote IP address, CIDR range, and request signature;
- request logging, delay throttling, rate smoothing, canonical-host redirects, browser-bootstrap checks, and favicon-aware health probes;
- whitelist, blacklist, unmatched-request, logging, recording, and block-response controls;
- filtering-event evaluation with null, in-memory, and SQLite-backed storage options;
- supporting helpers for Kestrel SNI, HTTPS redirection, static files, warm-up requests, encoded settings, certificates, and application directory layouts.

## Installation

```shell
dotnet add package Eigenverft.Routed.RequestFilters
```

The NuGet package contains assets for:

- .NET 6
- .NET 7
- .NET 8
- .NET 10

It uses the ASP.NET Core shared framework.

## Quick start

Register each component with `Add...` before building the application, then place its corresponding `Use...` middleware in the request pipeline.

```csharp
using Eigenverft.Routed.RequestFilters.Middleware.HostNameFiltering;
using Eigenverft.Routed.RequestFilters.Middleware.UserAgentFiltering;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostNameFiltering();
builder.Services.AddUserAgentFiltering();

var app = builder.Build();

app.UseHostNameFiltering();
app.UseUserAgentFiltering();

app.MapGet("/", () => Results.Ok(new { status = "ready" }));

app.Run();
```

The parameterless registrations bind configuration from sections named after their option types. For example, host-name filtering reads `HostNameFilteringOptions`.

```json
{
  "HostNameFilteringOptions": {
    "Whitelist": [
      "api.example.com",
      "*.example.com"
    ],
    "Blacklist": [],
    "AllowBlacklistedRequests": false,
    "AllowUnmatchedRequests": false,
    "BlockStatusCode": 400
  }
}
```

This example permits the listed host patterns and rejects unmatched hosts. Registration alone should not be treated as an implicit deny policy; configure the allow/block flags explicitly for your application.

### Pattern syntax

String-based filters use simple wildcard patterns:

| Token | Meaning |
| --- | --- |
| `*` | zero or more characters |
| `?` | zero or one character |
| `#` | exactly one character |

Matching is anchored to the complete value. For example, `*.example.com` matches `api.example.com`, while `api.example.com` matches only that full host name.

## Configuration model

Most middleware components expose three service-registration forms:

```csharp
builder.Services.AddHostNameFiltering();

builder.Services.AddHostNameFiltering(options =>
{
    options.Whitelist = new[] { "api.example.com" };
    options.AllowUnmatchedRequests = false;
});

builder.Services.AddHostNameFiltering(
    builder.Configuration,
    options => options.BlockStatusCode = StatusCodes.Status403Forbidden);
```

The conventions are:

- `AddX()` binds section `XOptions` and uses property initializers as defaults.
- `AddX(Action<XOptions>)` binds configuration and then applies code-based overrides.
- `AddX(IConfiguration, Action<XOptions>?)` binds from an explicitly supplied configuration root.
- Several `UseX(Action<XOptions>)` overloads can apply an additional pipeline-local configuration layer.
- Configuration-bound middleware uses options monitoring, so reloadable configuration sources can update values without rebuilding the pipeline.

Filtering components commonly classify a request as **whitelisted**, **blacklisted**, or **unmatched**. Their options then control whether each result is allowed, recorded, logged, or immediately blocked.

## Middleware catalog

| Component | Service registration | Pipeline registration | Purpose |
| --- | --- | --- | --- |
| Accept language filtering | `AddAcceptLanguageFiltering` | `UseAcceptLanguageFiltering` | Classify requests by `Accept-Language`. |
| Browser bootstrap filtering | `AddBrowserBootstrapFiltering` | `UseBrowserBootstrapFiltering` | Detect and control browser bootstrap requests. |
| Canonical host redirect | `AddCanonicalHostRedirect` | `UseCanonicalHostRedirect` | Redirect requests to a configured canonical host. |
| CIDR filtering | `AddCidrFiltering` | `UseCidrFiltering` | Match remote addresses against CIDR networks. |
| Development unlocker | `AddDevelopmentUnlocker` | `UseDevelopmentUnlocker` | Provide explicitly configured development unlock behavior. |
| File-extension blocking | `AddFileExtensionBlocking` | `UseFileExtensionBlocking` | Block or record requests by file extension. |
| Filtering evaluation gate | `AddFilteringEvaluationGate` | `UseFilteringEvaluationGate` | Enforce the decision produced by a filtering evaluator. |
| Favicon-aware health probe | `AddHealthProbeFaviconAware` | `UseHealthProbeFaviconAware` | Handle health probes while accounting for favicon requests. |
| Host-name filtering | `AddHostNameFiltering` | `UseHostNameFiltering` | Filter by the request host name. |
| HTTP method filtering | `AddHttpMethodFiltering` | `UseHttpMethodFiltering` | Filter `GET`, `POST`, and other HTTP methods. |
| HTTP protocol filtering | `AddHttpProtocolFiltering` | `UseHttpProtocolFiltering` | Filter by HTTP protocol/version. |
| Path-depth filtering | `AddPathDepthFiltering` | `UsePathDepthFiltering` | Limit or classify request path depth. |
| Remote-IP filtering | `AddRemoteIpAddressFiltering` | `UseRemoteIpAddressFiltering` | Filter individual remote IP addresses. |
| Request delay throttling | `AddRequestDelayThrottling` | `UseRequestDelayThrottling` | Add configurable delay-based throttling. |
| Request logging | `AddRequestLogging` | `UseRequestLogging` | Produce structured request logs. |
| Request rate smoothing | `AddRequestRateSmoothing` | `UseRequestRateSmoothing` | Smooth request bursts over time. |
| Request-signature filtering | `AddRequestSignatureFiltering` | `UseRequestSignatureFiltering` | Build and filter a signature from request properties. |
| Request-URL filtering | `AddRequestUrlFiltering` | `UseRequestUrlFiltering` | Filter the complete request URL. |
| TLS protocol filtering | `AddTlsProtocolFiltering` | `UseTlsProtocolFiltering` | Filter by the negotiated TLS protocol. |
| URI-segment filtering | `AddUriSegmentFiltering` | `UseUriSegmentFiltering` | Inspect individual URI path segments. |
| User-agent filtering | `AddUserAgentFiltering` | `UseUserAgentFiltering` | Filter by the `User-Agent` header. |

Each component has its own namespace below:

```text
Eigenverft.Routed.RequestFilters.Middleware.<ComponentName>
```

This keeps imports and registrations explicit.

## Evaluation and event storage

Individual filters can emit filtering events. The package includes evaluator and storage abstractions that allow applications to aggregate those events and make a later allow/block decision.

Available evaluator strategies include:

- null evaluation;
- simple score evaluation;
- source- and match-kind-weighted evaluation.

Available event-storage implementations include:

- null storage;
- bounded in-memory storage;
- SQLite-backed storage.

`FilteringEvaluationGate` consumes the selected evaluator and can either block the request or run in an allow-through/log-only mode. This is useful when introducing a policy gradually before enforcement.

## Pipeline ordering

Middleware order is part of the security policy. A typical application should consider the following sequence:

1. Configure trusted forwarded headers when the app runs behind a reverse proxy.
2. Apply canonical redirects or connection-context middleware.
3. Apply inexpensive request filters before expensive application work.
4. Apply request logging at the point that matches the desired logging scope.
5. Apply the evaluation gate after the data it depends on is available.
6. Map endpoints last.

When using remote-IP or CIDR filtering behind a proxy, configure ASP.NET Core forwarded-header trust correctly before the filters. Never trust arbitrary forwarded headers from untrusted networks.

The package inserts its remote-IP context middleware once when required by dependent components, but application-level proxy configuration remains the host application's responsibility.

## Supporting utilities

The package also contains reusable infrastructure outside the core filters:

- Kestrel SNI configuration and certificate selection;
- permanent HTTPS-redirection registration;
- PWA and Blazor static-file content-type mappings;
- dynamic non-asset file serving;
- application warm-up requests;
- deferred logging and Microsoft logging adapters;
- encoded/decoded JSON configuration layers;
- JSON settings write-back storage;
- certificate and application-directory helpers.

These utilities are optional. Consumers can use the request filters without adopting the hosting helpers.

## Build from source

```shell
git clone https://github.com/eigenverft/Eigenverft.Routed.RequestFilters.git
cd Eigenverft.Routed.RequestFilters

dotnet restore src/Eigenverft.Routed.RequestFilters.slnx
dotnet build src/Eigenverft.Routed.RequestFilters.slnx -c Release --no-restore
```

Create a local package with the repository's pack metadata enabled:

```shell
dotnet pack \
  src/prj/Eigenverft.Routed.RequestFilters/Eigenverft.Routed.RequestFilters.csproj \
  -c Release \
  -p:Stage=pack
```

Main source layout:

```text
src/
├── Eigenverft.Routed.RequestFilters.slnx
└── prj/
    └── Eigenverft.Routed.RequestFilters/
        ├── Middleware/
        ├── Services/
        ├── Hosting/
        ├── Options/
        ├── Utilities/
        └── GenericExtensions/
```

## Security notes

This library provides application middleware, not a complete web application firewall. Review every enabled filter, default, bypass, proxy, and logging setting for the deployment environment.

In particular:

- start new enforcement policies in an observable allow-through mode where practical;
- verify trusted proxies before relying on client IP information;
- avoid exposing development unlock behavior in production;
- keep middleware before the endpoints or resources it is intended to protect;
- treat preview upgrades as potentially breaking until the project reaches 1.0.

## Project status

The first public releases are preview packages produced by the repository's CI/CD workflow. Source, releases, and issue tracking are available in the [GitHub repository](https://github.com/eigenverft/Eigenverft.Routed.RequestFilters).

## License

Licensed under the [MIT License](LICENSE).
