using System;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.TlsProtocolFiltering
{
    /// <summary>
    /// Provides configuration options for TLS protocol filtering middleware.
    /// </summary>
    /// <remarks>
    /// Defaults are defined via property initializers.
    /// When configuration supplies a value (for example <c>Whitelist</c>), the binder replaces the array entirely.
    /// To intentionally clear a default list from configuration, set it to an empty array (<c>[]</c>).
    /// <para>
    /// The observed TLS protocol comes from the negotiated handshake (when available) and is typically formatted like <c>Tls12</c> or <c>Tls13</c>.
    /// If TLS is terminated upstream (reverse proxy) the handshake feature might be unavailable; the middleware then normalizes the observed value to <c>string.Empty</c>.
    /// </para>
    /// <para>
    /// Example configuration snippet:
    /// </para>
    /// <code>
    /// "TlsProtocolFilteringOptions": {
    ///   "FilterPriority": "Whitelist",
    ///
    ///   // Observed values typically look like: "Tls12", "Tls13".
    ///   // Origin: negotiated TLS protocol as exposed by the server runtime.
    ///   "Whitelist": [ "Tls12", "Tls13" ],
    ///
    ///   "Blacklist": [
    ///     // Empty indicates "unknown / not available" (for example TLS terminated upstream).
    ///     "",
    ///
    ///     // Typical legacy/obsolete protocol tokens (often disabled at the platform level anyway).
    ///     "None",
    ///     "Ssl2",
    ///     "Ssl3",
    ///     "Tls",
    ///     "Tls10",
    ///     "Tls11"
    ///   ],
    ///
    ///   "CaseSensitive": true,
    ///   "BlockStatusCode": 400,
    ///   "AllowBlacklistedRequests": true,
    ///   "AllowUnmatchedRequests": true,
    ///   "RecordBlacklistedRequests": true,
    ///   "RecordUnmatchedRequests": true,
    ///   "LogLevelWhitelist": "None",
    ///   "LogLevelBlacklist": "Information",
    ///   "LogLevelUnmatched": "Warning"
    /// }
    /// </code>
    /// </remarks>
    public sealed class TlsProtocolFilteringOptions
    {
        /// <summary>
        /// Gets or sets the resolution strategy when a protocol pattern matches both the whitelist and the blacklist.
        /// </summary>
        /// <remarks>
        /// When the value is <see cref="FilterPriority.Whitelist"/> the protocol is treated as allowed.
        /// When the value is <see cref="FilterPriority.Blacklist"/> the protocol is treated as forbidden.
        /// </remarks>
        public FilterPriority FilterPriority { get; set; } = FilterPriority.Whitelist;

        /// <summary>
        /// Gets or sets the list of explicitly allowed TLS protocol patterns.
        /// </summary>
        /// <remarks>
        /// Default: modern TLS versions.
        /// If configuration specifies <c>Whitelist</c>, it fully replaces this value.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> Whitelist { get; set; } = new[]
        {
            "Tls12",
            "Tls13",
        };

        /// <summary>
        /// Gets or sets the list of explicitly forbidden TLS protocol patterns.
        /// </summary>
        /// <remarks>
        /// Default: includes empty (unknown/unavailable) and common legacy tokens.
        /// If configuration specifies <c>Blacklist</c>, it fully replaces this value.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> Blacklist { get; set; } = new[]
        {
            "",
            "None",
            "Ssl2",
            "Ssl3",
            "Tls",
            "Tls10",
            "Tls11",
        };

        /// <summary>
        /// Gets or sets a value indicating whether protocol pattern matching is case sensitive.
        /// </summary>
        public bool CaseSensitive { get; set; } = false;

        /// <summary>
        /// Gets or sets the http status code that is used when the middleware actively blocks a request.
        /// </summary>
        /// <remarks>
        /// The status code is applied when <see cref="AllowBlacklistedRequests"/> or <see cref="AllowUnmatchedRequests"/> are set to <see langword="false"/>
        /// and the corresponding case occurs.
        /// </remarks>
        public int BlockStatusCode { get; set; } = StatusCodes.Status400BadRequest;

        /// <summary>
        /// Gets or sets a value indicating whether requests classified as <see cref="FilterMatchKind.Blacklist"/> are still allowed to pass through.
        /// </summary>
        public bool AllowBlacklistedRequests { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether requests classified as <see cref="FilterMatchKind.Unmatched"/> are allowed to pass through.
        /// </summary>
        public bool AllowUnmatchedRequests { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether blacklist hits are recorded.
        /// </summary>
        public bool RecordBlacklistedRequests { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether unmatched hits are recorded.
        /// </summary>
        public bool RecordUnmatchedRequests { get; set; } = true;

        /// <summary>
        /// Gets or sets the log level used when the request matches the whitelist.
        /// </summary>
        /// <remarks>
        /// Use <see cref="LogLevel.None"/> to disable logging for whitelist matches.
        /// </remarks>
        public LogLevel LogLevelWhitelist { get; set; } = LogLevel.None;

        /// <summary>
        /// Gets or sets the log level used when the request matches the blacklist.
        /// </summary>
        /// <remarks>
        /// Use <see cref="LogLevel.None"/> to disable logging for blacklist matches.
        /// </remarks>
        public LogLevel LogLevelBlacklist { get; set; } = LogLevel.Information;

        /// <summary>
        /// Gets or sets the log level used when the request is unmatched.
        /// </summary>
        /// <remarks>
        /// Use <see cref="LogLevel.None"/> to disable logging for unmatched results.
        /// </remarks>
        public LogLevel LogLevelUnmatched { get; set; } = LogLevel.Warning;
    }
}
