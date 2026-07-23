# Eigenverft.Routed.RequestFilters

Composable request filtering, traffic control, diagnostics, and hosting utilities for ASP.NET Core applications.

> **Preview:** This package is pre-1.0. APIs, option names, defaults, and configuration behavior may change between releases.

## Install

```shell
dotnet add package Eigenverft.Routed.RequestFilters
```

## Target frameworks

The package provides assets for `net6.0`, `net7.0`, `net8.0`, and `net10.0` and uses the ASP.NET Core shared framework.

## Quick start

Register a component with `Add...`, configure its policy, and add the corresponding `Use...` middleware before the endpoints it protects.

```csharp
using Eigenverft.Routed.RequestFilters.Middleware.HostNameFiltering;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostNameFiltering(options =>
{
    options.Whitelist = new[]
    {
        "api.example.com",
        "*.example.com",
    };

    options.AllowBlacklistedRequests = false;
    options.AllowUnmatchedRequests = false;
    options.BlockStatusCode = StatusCodes.Status403Forbidden;
});

var app = builder.Build();

app.UseHostNameFiltering();

app.MapGet("/", () => Results.Ok(new { status = "ready" }));

app.Run();
```

Most parameterless registrations bind a configuration section named after the corresponding options type, such as `HostNameFilteringOptions`.

> Registration alone is not necessarily an enforcement policy. Configure the relevant `Allow...`, logging, recording, and block-status options explicitly.

## Included middleware

- host name, URL, URI segment, path depth, and file-extension filtering;
- HTTP method, HTTP protocol, TLS protocol, language, and user-agent filtering;
- remote-IP, CIDR, and request-signature filtering;
- request logging, delay throttling, and rate smoothing;
- canonical-host redirects, browser-bootstrap checks, and favicon-aware health probes;
- filtering-event evaluation with null, in-memory, and SQLite-backed storage.

The package also includes optional helpers for Kestrel SNI, HTTPS redirection, static files, warm-up requests, encoded settings, certificates, and application directory layouts.

## Filtering model

Most filters classify an observed request value as whitelisted, blacklisted, or unmatched. Options control whether each result is allowed, recorded, logged, or blocked. String-based filters support anchored wildcard patterns:

| Token | Meaning |
| --- | --- |
| `*` | Zero or more characters |
| `?` | Zero or one character |
| `#` | Exactly one character |

## Evaluation and storage

The package can record filter events and evaluate them later through `FilteringEvaluationGate`.

Storage selections:

- `NullStorage`
- `InMemoryStorage`
- `SqliteStorage`

Evaluator selections:

- `NullFiltering`
- `SimpleFilteringScore`
- `SourceAndMatchKindWeighted`

Evaluator and storage selection use a last-call-wins model.

## Documentation

See the [GitHub repository](https://github.com/eigenverft/Eigenverft.Routed.RequestFilters) for the full middleware catalog, configuration recipes, pipeline-ordering guidance, build instructions, and security notes.

## License

MIT
