using System;

using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.BrowserBootstrapFiltering
{
    /// <summary>
    /// Provides configuration options for <see cref="BrowserBootstrapFiltering"/>.
    /// </summary>
    /// <remarks>
    /// Defaults are defined via property initializers.
    /// When configuration supplies a value (for example <see cref="EntryPaths"/>), the binder replaces the list entirely.
    /// To intentionally clear a default list from configuration, set it to an empty array (<c>[]</c>).
    /// <para>
    /// Example configuration snippet:
    /// </para>
    /// <code>
    /// "BrowserBootstrapFilteringOptions": {
    ///   "EntryPaths": [ "/", "/index.html" ],
    ///   "CookieName": "ev_boot",
    ///   "CookieValue": "1",
    ///   "CookieMaxAge": "30.00:00:00",
    ///   "BootstrapQueryParameterName": "boot",
    ///   "BlockStatusCode": 400,
    ///   "AllowBlacklistedRequests": false,
    ///   "RecordBlacklistedRequests": true,
    ///   "RecordUnmatchedRequests": false,
    ///   "LogLevelWhitelist": "None",
    ///   "LogLevelBlacklist": "Warning",
    ///   "LogLevelUnmatched": "Information"
    /// }
    /// </code>
    /// </remarks>
    public sealed class BrowserBootstrapFilteringOptions
    {
        /// <summary>
        /// Gets or sets the list of entry paths that are protected by the browser bootstrap challenge.
        /// </summary>
        /// <remarks>
        /// Only these paths are intercepted. All other requests pass through unchanged.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> EntryPaths { get; set; } = new[]
        {
            "/",
            "/index.html",
        };

        /// <summary>
        /// Gets or sets the cookie name that indicates a client has completed the bootstrap.
        /// </summary>
        public string CookieName { get; set; } = "ev_boot";

        /// <summary>
        /// Gets or sets the cookie value that indicates a client has completed the bootstrap.
        /// </summary>
        public string CookieValue { get; set; } = "1";

        /// <summary>
        /// Gets or sets the desired lifetime of the bootstrap cookie.
        /// </summary>
        /// <remarks>
        /// A longer lifetime reduces repeated challenges for returning users.
        /// </remarks>
        public TimeSpan CookieMaxAge { get; set; } = TimeSpan.FromDays(30);

        /// <summary>
        /// Gets or sets the query parameter name used for loop detection after the bootstrap response.
        /// </summary>
        /// <remarks>
        /// The bootstrap HTML triggers a reload that includes this parameter.
        /// If the parameter is present but the cookie is still missing, the request is treated as suspicious.
        /// </remarks>
        public string BootstrapQueryParameterName { get; set; } = "boot";

        /// <summary>
        /// Gets or sets the http status code used when the middleware actively blocks a request
        /// after a failed bootstrap attempt.
        /// </summary>
        public int BlockStatusCode { get; set; } = StatusCodes.Status400BadRequest;

        /// <summary>
        /// Gets or sets a value indicating whether requests classified as <c>Blacklist</c> are still allowed to pass through.
        /// </summary>
        /// <remarks>
        /// This is mainly useful for testing. In production this is typically <see langword="false"/>.
        /// </remarks>
        public bool AllowBlacklistedRequests { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether blacklist hits are recorded to <see cref="Services.FilteringEvent.IFilteringEventStorage"/>.
        /// </summary>
        public bool RecordBlacklistedRequests { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether unmatched hits are recorded to <see cref="Services.FilteringEvent.IFilteringEventStorage"/>.
        /// </summary>
        /// <remarks>
        /// Default is <see langword="false"/> to avoid counting normal first-time visits as suspicious.
        /// </remarks>
        public bool RecordUnmatchedRequests { get; set; } = false;

        /// <summary>
        /// Gets or sets the log level used when the request is already bootstrapped (cookie present).
        /// </summary>
        public LogLevel LogLevelWhitelist { get; set; } = LogLevel.None;

        /// <summary>
        /// Gets or sets the log level used when the bootstrap is considered failed or suspicious (cookie missing after bootstrap marker).
        /// </summary>
        public LogLevel LogLevelBlacklist { get; set; } = LogLevel.Warning;

        /// <summary>
        /// Gets or sets the log level used when the request requires the bootstrap challenge (cookie missing on first entry).
        /// </summary>
        public LogLevel LogLevelUnmatched { get; set; } = LogLevel.Information;
    }
}
