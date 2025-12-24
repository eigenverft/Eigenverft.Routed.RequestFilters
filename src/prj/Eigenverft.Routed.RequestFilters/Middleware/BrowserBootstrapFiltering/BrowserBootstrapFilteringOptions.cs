using System;
using System.Text;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.GenericExtensions.HttpResponseExtensions;
using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.BrowserBootstrapFiltering
{
    /// <summary>
    /// Provides configuration options for <see cref="BrowserBootstrapFiltering"/>.
    /// </summary>
    /// <remarks>
    /// Bindable from configuration section <c>BrowserBootstrapFilteringOptions</c>. If the section is missing,
    /// property initializers act as defaults.
    /// <para>Example configuration snippet:</para>
    /// <code>
    /// "BrowserBootstrapFilteringOptions": {
    ///   "ProtectedPaths": [ "/", "/index.html" ],
    ///   "CookieName": "Eigenverft.BrowserBootstrap",
    ///   "CookieMaxAge": "1.00:00:00",
    ///   "AllowRequestsWithoutBootstrapCookie": false,
    ///   "RecordRequestsWithoutBootstrapCookie": false,
    ///   "BlockStatusCode": 400,
    ///   "LogLevelAllowedRequests": "None",
    ///   "LogLevelBootstrapAttempt": "Information",
    ///   "LogLevelBootstrapOutcome": "Warning",
    ///   "CaseSensitivePaths": true
    /// }
    /// </code>
    /// </remarks>
    public sealed class BrowserBootstrapFilteringOptions
    {
        /// <summary>
        /// Gets or sets the list of request paths that are protected by the bootstrap check.
        /// </summary>
        /// <remarks>
        /// Default: <c>/</c> and <c>/index.html</c>.
        /// </remarks>
        public string[] ProtectedPaths { get; set; } = new[] { "/", "/index.html" };

        /// <summary>
        /// Gets or sets a value indicating whether path comparisons are case sensitive.
        /// </summary>
        public bool CaseSensitivePaths { get; set; } = true;

        /// <summary>
        /// Gets or sets the cookie name used as the bootstrap signal.
        /// </summary>
        public string CookieName { get; set; } = "Eigenverft.BrowserBootstrap";

        /// <summary>
        /// Gets or sets the cookie max-age.
        /// </summary>
        public TimeSpan CookieMaxAge { get; set; } = TimeSpan.FromDays(1);

        /// <summary>
        /// Gets or sets the http status code that is used when the middleware actively blocks a request
        /// after a failed bootstrap outcome.
        /// </summary>
        public int BlockStatusCode { get; set; } = StatusCodes.Status400BadRequest;

        /// <summary>
        /// Gets or sets a value indicating whether requests that fail the bootstrap outcome are allowed to pass through.
        /// </summary>
        /// <remarks>
        /// Set to <see langword="true"/> for rollout or accessibility scenarios where JavaScript or cookies might be disabled.
        /// </remarks>
        public bool AllowRequestsWithoutBootstrapCookie { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether bootstrap failures are recorded to the central event storage.
        /// </summary>
        public bool RecordRequestsWithoutBootstrapCookie { get; set; } = false;

        /// <summary>
        /// Gets or sets the log level used when the request is allowed to proceed normally (cookie already present).
        /// </summary>
        /// <remarks>
        /// Use <see cref="LogLevel.None"/> to disable these "allowed" log lines.
        /// </remarks>
        public LogLevel LogLevelAllowedRequests { get; set; } = LogLevel.None;

        /// <summary>
        /// Gets or sets the log level used when the middleware serves the bootstrap attempt HTML.
        /// </summary>
        /// <remarks>
        /// This log does not represent a whitelist/blacklist/unmatched classifier result; it indicates that
        /// the middleware returned the bootstrap HTML (and therefore did not continue the pipeline on that request).
        /// Use <see cref="LogLevel.None"/> to disable these log lines.
        /// </remarks>
        public LogLevel LogLevelBootstrapAttempt { get; set; } = LogLevel.Information;

        /// <summary>
        /// Gets or sets the log level used for bootstrap outcomes (continue/fail), including allow/block decisions.
        /// </summary>
        /// <remarks>
        /// These logs use the standard filter log shape via <see cref="FilterDecisionLogBuilder"/>.
        /// Use <see cref="LogLevel.None"/> to disable these log lines.
        /// </remarks>
        public LogLevel LogLevelBootstrapOutcome { get; set; } = LogLevel.Warning;
    }
}
