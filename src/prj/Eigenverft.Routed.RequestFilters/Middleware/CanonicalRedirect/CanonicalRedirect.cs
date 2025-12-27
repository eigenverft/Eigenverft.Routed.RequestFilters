using System;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.CanonicalRedirect
{
    /// <summary>
    /// Middleware that permanently redirects requests to a canonical host and (optionally) enforces HTTPS.
    /// </summary>
    /// <remarks>
    /// Designed to avoid multi-step redirect chains by emitting a single permanent redirect to the desired canonical host.
    /// If the app is behind a reverse proxy that terminates TLS, ensure forwarded headers are applied before this middleware
    /// so <see cref="HttpRequest.IsHttps"/> reflects the external scheme.
    /// </remarks>
    public sealed class CanonicalRedirect
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<CanonicalRedirect> _logger;
        private readonly IOptionsMonitor<CanonicalRedirectOptions> _optionsMonitor;

        /// <summary>
        /// Initializes a new instance of the <see cref="CanonicalRedirect"/> class.
        /// </summary>
        /// <param name="nextMiddleware">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="CanonicalRedirectOptions"/>.</param>
        public CanonicalRedirect(
            RequestDelegate nextMiddleware,
            IDeferredLogger<CanonicalRedirect> logger,
            IOptionsMonitor<CanonicalRedirectOptions> optionsMonitor)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));

            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(CanonicalRedirect)));
        }

        /// <summary>
        /// Processes the current request and issues a permanent redirect when the canonical host or scheme is not satisfied.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            CanonicalRedirectOptions options = _optionsMonitor.CurrentValue;

            if (!options.Enabled)
            {
                await _next(context);
                return;
            }

            string requestHost = context.Request.Host.Host ?? string.Empty;
            if (requestHost.Length == 0)
            {
                await _next(context);
                return;
            }

            if (!TryResolveCanonicalHost(requestHost, options, out string canonicalHost))
            {
                await _next(context);
                return;
            }

            bool needsHostRedirect = !string.Equals(requestHost, canonicalHost, StringComparison.OrdinalIgnoreCase);
            bool needsHttpsRedirect = options.EnforceHttps && !context.Request.IsHttps;

            if (!needsHostRedirect && !needsHttpsRedirect)
            {
                await _next(context);
                return;
            }

            string targetScheme = options.EnforceHttps ? "https" : (context.Request.Scheme ?? "http");
            string pathAndQuery = $"{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
            string location = $"{targetScheme}://{canonicalHost}{pathAndQuery}";

            if (options.LogLevelRedirect != LogLevel.None && _logger.IsEnabled(options.LogLevelRedirect))
            {
                _logger.Log(
                    options.LogLevelRedirect,
                    "Permanent canonical redirect. From {FromScheme}://{FromHost}{FromPath} to {ToLocation}.",
                    () => context.Request.Scheme ?? string.Empty,
                    () => requestHost,
                    () => pathAndQuery,
                    () => location);
            }

            context.Response.StatusCode = options.RedirectStatusCode;
            context.Response.Headers.Location = location;
        }

        private static bool TryResolveCanonicalHost(string requestHost, CanonicalRedirectOptions options, out string canonicalHost)
        {
            canonicalHost = string.Empty;

            string primaryApex = (options.PrimaryApexHost ?? string.Empty).Trim();
            if (primaryApex.Length == 0)
            {
                return false;
            }

            string primaryWww = "www." + primaryApex;

            // Primary group: apex <-> www canonicalization
            if (IsHostInGroup(requestHost, primaryApex, primaryWww))
            {
                canonicalHost = options.Canonicalization switch
                {
                    CanonicalHostMode.ToWww => primaryWww,
                    CanonicalHostMode.ToApex => primaryApex,
                    _ => requestHost
                };

                return true;
            }

            // Inbound aliases: always redirect to the canonical host derived from the primary host + mode
            string[] redirectFrom = options.RedirectFromHosts ?? Array.Empty<string>();

            for (int i = 0; i < redirectFrom.Length; i++)
            {
                string alias = (redirectFrom[i] ?? string.Empty).Trim();
                if (alias.Length == 0) continue;

                if (string.Equals(requestHost, alias, StringComparison.OrdinalIgnoreCase))
                {
                    canonicalHost = options.Canonicalization switch
                    {
                        CanonicalHostMode.ToWww => primaryWww,
                        CanonicalHostMode.ToApex => primaryApex,
                        _ => primaryApex
                    };

                    return true;
                }
            }

            return false;
        }

        private static bool IsHostInGroup(string requestHost, string apex, string www)
            => string.Equals(requestHost, apex, StringComparison.OrdinalIgnoreCase)
               || string.Equals(requestHost, www, StringComparison.OrdinalIgnoreCase);
    }
}
