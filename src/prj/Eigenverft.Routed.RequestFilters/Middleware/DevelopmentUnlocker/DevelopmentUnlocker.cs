using System;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.GenericExtensions.HttpResponseExtensions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.DevelopmentUnlocker
{
    /// <summary>
    /// Middleware that removes stored filtering events for the current remote ip address when a configured unlock endpoint is reached.
    /// </summary>
    public sealed class DevelopmentUnlocker
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<DevelopmentUnlocker> _logger;
        private readonly IOptionsMonitor<DevelopmentUnlockerOptions> _optionsMonitor;
        private readonly IFilteringEventStorage _filteringEventStorage;

        /// <summary>
        /// Initializes a new instance of the <see cref="DevelopmentUnlocker"/> class.
        /// </summary>
        /// <param name="nextMiddleware">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="DevelopmentUnlockerOptions"/>.</param>
        /// <param name="filteringEventStorage">The central filtering event storage.</param>
        public DevelopmentUnlocker(
            RequestDelegate nextMiddleware,
            IDeferredLogger<DevelopmentUnlocker> logger,
            IOptionsMonitor<DevelopmentUnlockerOptions> optionsMonitor,
            IFilteringEventStorage filteringEventStorage)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _filteringEventStorage = filteringEventStorage ?? throw new ArgumentNullException(nameof(filteringEventStorage));

            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(DevelopmentUnlocker)));
        }

        /// <summary>
        /// Processes the current request and triggers an unlock when the configured endpoint is reached.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            DevelopmentUnlockerOptions options = _optionsMonitor.CurrentValue;

            if (!options.Enabled)
            {
                await _next(context);
                return;
            }

            if (!IsUnlockRequest(context, options))
            {
                await _next(context);
                return;
            }

            string remoteIpAddress = context.GetRemoteIpAddress();

            if (options.LogLevelUnlock != LogLevel.None && _logger.IsEnabled(options.LogLevelUnlock))
            {
                _logger.Log(
                    options.LogLevelUnlock,
                    "Development unlock triggered via {Method} {Path} for {RemoteIpAddress}.",
                    () => context.Request.Method,
                    () => context.Request.Path.Value ?? string.Empty,
                    () => remoteIpAddress);
            }

            await _filteringEventStorage.RemoveByRemoteIpAddressAsync(remoteIpAddress);

            await context.Response.WriteDefaultStatusCodeAnswerEx(StatusCodes.Status200OK); ;
            return;
        }

        private static bool IsUnlockRequest(HttpContext context, DevelopmentUnlockerOptions options)
        {
            if (context == null) return false;
            if (options == null) return false;

            string endpointPathRaw = options.EndpointPath ?? string.Empty;
            endpointPathRaw = endpointPathRaw.Trim();

            if (endpointPathRaw.Length == 0) return false;

            if (!endpointPathRaw.StartsWith("/", StringComparison.Ordinal))
            {
                endpointPathRaw = "/" + endpointPathRaw;
            }

            endpointPathRaw = endpointPathRaw.TrimEnd('/');

            PathString configuredPath = new PathString(endpointPathRaw);
            PathString requestPath = context.Request.Path;

            // Always case-insensitive by design.
            return configuredPath.Equals(requestPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
