# 🛡️ Eigenverft.Routed.RequestFilters

<!-- Maintenance note: The NuGet package README is maintained at src/prj/Eigenverft.Routed.RequestFilters/Properties/NugetMetadata/Readme.md. Keep shared content aligned when changing either document. -->

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.Routed.RequestFilters?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.Routed.RequestFilters) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.Routed.RequestFilters?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.Routed.RequestFilters) [![Build Status](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.Routed.RequestFilters/cicd.yml?branch=main&label=build)](https://github.com/eigenverft/Eigenverft.Routed.RequestFilters/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-.NET%208%20%7C%2010-512BD4?logo=dotnet&logoColor=white)](#-installation) [![License](https://img.shields.io/github/license/eigenverft/Eigenverft.Routed.RequestFilters?logo=mit)](LICENSE)

Composable request filtering, filter evaluation, enforcement, and filter-event storage for ASP.NET Core applications.

> [!IMPORTANT]
> This project is currently **pre-1.0**. Public APIs, option names, defaults, and configuration behavior may change between preview releases.

## ✨ At a glance

| | |
| --- | --- |
| Package | `Eigenverft.Routed.RequestFilters` |
| Application model | ASP.NET Core middleware and dependency-injection extensions |
| Target frameworks | .NET 8 and .NET 10 |
| Configuration | `IOptionsMonitor<T>`, `IConfiguration`, or code-based delegates |
| Event storage | Null, bounded in-memory, or SQLite |
| License | MIT |

The library is designed for applications that want to compose focused request policies instead of adopting one monolithic request-processing pipeline. Each component is registered explicitly and added to the pipeline explicitly.

## 🧭 Contents

- [Installation](#-installation)
- [Quick start](#-quick-start)
- [How filtering works](#-how-filtering-works)
- [Common recipes](#-common-recipes)
- [Middleware catalog](#-middleware-catalog)
- [Evaluation and event storage](#-evaluation-and-event-storage)
- [Pipeline ordering](#-pipeline-ordering)
- [Build from source](#-build-from-source)
- [Security notes](#-security-notes)

## 📦 Installation

```shell
dotnet add package Eigenverft.Routed.RequestFilters
```

The package provides assets for:

- `net8.0`
- `net10.0`

It uses the ASP.NET Core shared framework.

## 🚀 Quick start

Every middleware follows the same basic pattern:

1. Register it with `Add...`.
2. Configure its matching and enforcement policy.
3. Add it to the request pipeline with `Use...`.

The following example allows only the configured host names.

### `Program.cs`

```csharp
using Eigenverft.Routed.RequestFilters.Middleware.HostNameFiltering;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostNameFiltering();

var app = builder.Build();

app.UseHostNameFiltering();

app.MapGet("/", () => Results.Ok(new { status = "ready" }));

app.Run();
```

### `appsettings.json`

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
    "BlockStatusCode": 403
  }
}
```

The parameterless `AddHostNameFiltering()` registration binds the section named `HostNameFilteringOptions`. The example allows requests matching the whitelist and rejects unmatched or blacklisted hosts.

> [!CAUTION]
> Registration alone is not an implicit deny policy. Many filters are intentionally observable by default. Set the relevant `Allow...` flags explicitly before relying on a filter for enforcement.

## 🔍 How filtering works

Most filtering middleware classifies an observed request value as one of three results:

| Result | Meaning |
| --- | --- |
| Whitelist | The value matched an allowed pattern. |
| Blacklist | The value matched a blocked pattern. |
| Unmatched | The value matched neither list. |

The corresponding options then determine whether the request is allowed, recorded, logged, or short-circuited with `BlockStatusCode`. When a value appears in both lists, `FilterPriority` controls which result wins.

Option names differ slightly between components, but the recurring controls are:

- `Whitelist` and `Blacklist`
- `FilterPriority`
- `AllowBlacklistedRequests`
- `AllowUnmatchedRequests`
- `RecordBlacklistedRequests`
- `RecordUnmatchedRequests`
- per-result log levels
- `BlockStatusCode`

### Configuration forms

Most middleware components expose these registration forms:

```csharp
// Bind the conventional XOptions configuration section.
builder.Services.AddHostNameFiltering();

// Bind configuration, then apply code-based overrides.
builder.Services.AddHostNameFiltering(options =>
{
    options.Whitelist = new() { "api.example.com" };
    options.AllowUnmatchedRequests = false;
});

// Bind from an explicitly supplied configuration root, then override values.
builder.Services.AddHostNameFiltering(
    builder.Configuration,
    options => options.BlockStatusCode = StatusCodes.Status403Forbidden);
```

Several middleware components also provide `UseX(Action<XOptions>)`. That overload adds a pipeline-local configuration layer on top of the DI-registered options.

Configuration-bound middleware uses options monitoring. Reloadable configuration sources can therefore update values without rebuilding the middleware pipeline.

### Pattern syntax

String-based filters use anchored wildcard patterns rather than raw regular expressions:

| Token | Meaning |
| --- | --- |
| `*` | Zero or more characters |
| `?` | Zero or one character |
| `#` | Exactly one character |

Examples:

| Pattern | Example match |
| --- | --- |
| `*.example.com` | `api.example.com` |
| `api-##.example.com` | `api-01.example.com` |
| `/v?/health` | `/v1/health` and `/v/health` |

Matching is anchored to the complete observed value. Matching is case-insensitive by default where the corresponding options expose a case-sensitivity switch.

## 🧩 Common recipes

### Configure a filter entirely in code

```csharp
using Eigenverft.Routed.RequestFilters.Middleware.HttpMethodFiltering;

builder.Services.AddHttpMethodFiltering(options =>
{
    options.Whitelist = new() { "GET", "HEAD" };
    options.AllowBlacklistedRequests = false;
    options.AllowUnmatchedRequests = false;
    options.BlockStatusCode = StatusCodes.Status405MethodNotAllowed;
});
```

Add the matching middleware before the endpoints it protects:

```csharp
app.UseHttpMethodFiltering();
app.MapControllers();
```

### Filter client IP addresses behind a reverse proxy

Configure trusted forwarded headers before remote-IP or CIDR filters. The host application remains responsible for defining trusted proxies and networks.

```csharp
app.UseForwardedHeaders();
app.UseRemoteIpAddressFiltering();
app.UseCidrFiltering();
```

Never accept arbitrary forwarded headers from untrusted networks. Incorrect proxy trust configuration can make client-IP policies ineffective.

### Record filter events and evaluate them later

```csharp
using Eigenverft.Routed.RequestFilters.Middleware.FilteringEvaluationGate;
using Eigenverft.Routed.RequestFilters.Middleware.HostNameFiltering;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage;

builder.Services.AddFilteringEventStorage<InMemoryStorage>();
builder.Services.AddFilteringEvaluator(FilteringEvaluatorKind.SimpleFilteringScore);
builder.Services.AddHostNameFiltering();
builder.Services.AddFilteringEvaluationGate();

var app = builder.Build();

app.UseHostNameFiltering();
app.UseFilteringEvaluationGate();
```

Use `NullStorage` when events should be discarded, `InMemoryStorage` for process-local bounded storage, or `SqliteStorage` when events must survive process restarts. Evaluator and storage selection use a **last-call-wins** model.

## 🧰 Middleware catalog

### Request classification and filtering

| Component | Registration | Pipeline | Observed value |
| --- | --- | --- | --- |
| Accept language | `AddAcceptLanguageFiltering` | `UseAcceptLanguageFiltering` | `Accept-Language` header |
| CIDR | `AddCidrFiltering` | `UseCidrFiltering` | Remote address and CIDR networks |
| File extension | `AddFileExtensionBlocking` | `UseFileExtensionBlocking` | Requested file extension |
| Host name | `AddHostNameFiltering` | `UseHostNameFiltering` | Request host name |
| HTTP method | `AddHttpMethodFiltering` | `UseHttpMethodFiltering` | `GET`, `POST`, and other methods |
| HTTP protocol | `AddHttpProtocolFiltering` | `UseHttpProtocolFiltering` | HTTP protocol/version |
| Path depth | `AddPathDepthFiltering` | `UsePathDepthFiltering` | Number of path segments |
| Remote IP | `AddRemoteIpAddressFiltering` | `UseRemoteIpAddressFiltering` | Individual remote IP address |
| Request signature | `AddRequestSignatureFiltering` | `UseRequestSignatureFiltering` | Composite request signature |
| Request URL | `AddRequestUrlFiltering` | `UseRequestUrlFiltering` | Complete request URL |
| TLS protocol | `AddTlsProtocolFiltering` | `UseTlsProtocolFiltering` | Negotiated TLS protocol |
| URI segment | `AddUriSegmentFiltering` | `UseUriSegmentFiltering` | Individual path segments |
| User agent | `AddUserAgentFiltering` | `UseUserAgentFiltering` | `User-Agent` header |

### Filter orchestration

| Component | Registration | Pipeline | Purpose |
| --- | --- | --- | --- |
| Browser bootstrap filtering | `AddBrowserBootstrapFiltering` | `UseBrowserBootstrapFiltering` | Detect and control browser bootstrap requests. |
| Development unlocker | `AddDevelopmentUnlocker` | `UseDevelopmentUnlocker` | Apply explicitly configured development unlock behavior. |
| Evaluation gate | `AddFilteringEvaluationGate` | `UseFilteringEvaluationGate` | Enforce a filtering evaluator decision. |

Each component has a dedicated namespace below:

```text
Eigenverft.Routed.RequestFilters.Middleware.<ComponentName>
```

This keeps imports and registrations explicit and lets consumers include only the components they use.

## 📊 Evaluation and event storage

Individual filters can emit `FilteringEvent` records. An evaluator can aggregate the events associated with an observed remote address and produce an allow/block decision for `FilteringEvaluationGate`.

### Evaluators

| Kind | Behavior |
| --- | --- |
| `NullFiltering` | Always uses the no-op evaluator. |
| `SimpleFilteringScore` | Evaluates the accumulated filter score. |
| `SourceAndMatchKindWeighted` | Applies configurable weights by event source and match kind. |

Register exactly one active evaluator:

```csharp
builder.Services.AddFilteringEvaluator(
    FilteringEvaluatorKind.SourceAndMatchKindWeighted);
```

### Event storage

| Selection | Behavior |
| --- | --- |
| `NullStorage` | Discards events. |
| `InMemoryStorage` | Keeps a bounded process-local event history. |
| `SqliteStorage` | Persists events in SQLite. |

Register exactly one active storage:

```csharp
builder.Services.AddFilteringEventStorage<SqliteStorage>();
```

Configurable backends bind their conventional sections:

- `InMemoryFilteringEventStorageOptions`
- `InSqliteDbFilteringEventStorageOptions`
- `SourceAndMatchKindWeightedFilteringEvaluatorOptions`

`FilteringEvaluationGate` can enforce the evaluator result or run in allow-through mode. Allow-through mode is useful for observing a new policy before enabling blocking.

## 🔀 Pipeline ordering

Middleware order is part of the policy. A typical application should consider this sequence:

1. Configure trusted forwarded headers when running behind a reverse proxy.
2. Apply inexpensive request classifiers and filters.
3. Apply `FilteringEvaluationGate` after the filters whose events it evaluates.
4. Map application endpoints and resources after the filters that should protect them.

A minimal composed pipeline might look like this:

```csharp
app.UseForwardedHeaders();
app.UseHostNameFiltering();
app.UseUserAgentFiltering();
app.UseFilteringEvaluationGate();

app.MapControllers();
```

Only include middleware that has been registered and configured for the application. Filters that need the peer address
insert the shared ClientNetwork feature once; proxy trust and forwarded-header ordering remain the host application's
responsibility.

## 🧭 Scope boundary

Application hosting, Kestrel/SNI, certificates, configuration-source composition, startup logging, static-file serving,
warm-up, health probes, canonical redirects, general request logging, and traffic shaping are intentionally outside this
package. Applications that need those capabilities reference their dedicated WebLib or NetLib packages directly; they
are not forwarded or transitively exposed by RequestFilters.

## 🧪 Build from source

```shell
git clone https://github.com/eigenverft/Eigenverft.Routed.RequestFilters.git
cd Eigenverft.Routed.RequestFilters

dotnet restore src/Eigenverft.Routed.RequestFilters.slnx
dotnet build src/Eigenverft.Routed.RequestFilters.slnx -c Release --no-restore
```

Create a local NuGet package with repository pack metadata enabled:

```shell
dotnet pack src/prj/Eigenverft.Routed.RequestFilters/Eigenverft.Routed.RequestFilters.csproj -c Release -p:Stage=pack
```

Main source layout:

```text
src/
├── Eigenverft.Routed.RequestFilters.slnx
└── prj/
    ├── Eigenverft.Routed.RequestFilters/
    │   ├── Middleware/
    │   └── Services/
    └── Eigenverft.Routed.RequestFilters.Tests/
```

## 🔐 Security notes

This library provides application middleware, not a complete web application firewall. Review every enabled filter, default, bypass, proxy, and logging setting for the deployment environment.

In particular:

- configure enforcement flags explicitly;
- verify trusted proxies before relying on client-IP information;
- place filters before the endpoints or resources they protect;
- start new evaluator policies in allow-through mode where practical;
- do not expose development unlock behavior in production;
- avoid logging secrets, credentials, or sensitive request data;
- treat preview upgrades as potentially breaking until the project reaches 1.0.

## 🚢 Project status

Preview packages are produced by the repository's CI/CD workflow. Source, releases, and issue tracking are available in the [GitHub repository](https://github.com/eigenverft/Eigenverft.Routed.RequestFilters).

## 📄 License

Licensed under the [MIT License](LICENSE) by Eigenverft.

---

<div align="center">
Made with ❤️ by Eigenverft
</div>
