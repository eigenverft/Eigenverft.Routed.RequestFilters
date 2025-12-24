using System;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.HttpMethodFiltering
{
    /// <summary>
    /// Provides configuration options for http method filtering middleware.
    /// </summary>
    /// <remarks>
    /// Defaults are defined via property initializers.
    /// When configuration supplies a value (for example <c>Whitelist</c>), the binder replaces the array entirely.
    /// To intentionally clear a default list from configuration, set it to an empty array (<c>[]</c>).
    /// <para>
    /// Example configuration snippet:
    /// </para>
    /// <code>
    /// "HttpMethodFilteringOptions": {
    ///   "FilterPriority": "Whitelist",
    ///
    ///   // Core HTTP request methods used by typical APIs / browsers.
    ///   // Origin: HTTP Semantics (RFC 7231; updated/superseded by RFC 9110).
    ///   "Whitelist": [ "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" ],
    ///
    ///   "Blacklist": [
    ///     // Empty / missing method token:
    ///     // Usually malformed requests, parsing artifacts, or broken clients.
    ///     "",
    ///
    ///     // Standard HTTP methods often disabled to reduce attack surface:
    ///     // - TRACE: historically related to cross-site tracing style issues and diagnostic reflection.
    ///     // - CONNECT: proxy tunneling (mostly relevant for forward proxies).
    ///     // Origin: HTTP Semantics (RFC 7231; updated by RFC 9110).
    ///     "TRACE", "CONNECT",
    ///
    ///     // WebDAV authoring methods (remote file/collection operations over HTTP).
    ///     // If you do NOT intentionally support WebDAV, these are typically safe to block and are often probed by scanners.
    ///     // Origin: RFC 4918 (WebDAV core), RFC 5323 (DASL SEARCH), RFC 3253 (DeltaV), RFC 3744 (ACL), RFC 5842 (Bindings), RFC 4437 (Redirect References).
    ///     "PROPFIND", "PROPPATCH", "MKCOL", "COPY", "MOVE", "LOCK", "UNLOCK", "SEARCH", "REPORT", "MKACTIVITY", "MKWORKSPACE", "VERSION-CONTROL", "CHECKIN",
    ///     "CHECKOUT", "UNCHECKOUT", "MERGE", "UPDATE", "LABEL", "BASELINE-CONTROL", "MKREDIRECTREF", "MKRESOURCE", "BIND", "REBIND", "UNBIND", "ACL"
    ///   ],
    ///
    ///   "CaseSensitive": true,
    ///   "BlockStatusCode": 400,
    ///   "AllowBlacklistedRequests": false,
    ///   "AllowUnmatchedRequests": true,
    ///   "RecordBlacklistedRequests": true,
    ///   "RecordUnmatchedRequests": true,
    ///   "LogLevelWhitelist": "None",
    ///   "LogLevelBlacklist": "Information",
    ///   "LogLevelUnmatched": "Warning"
    /// }
    /// </code>
    /// </remarks>
    public sealed class HttpMethodFilteringOptions
    {
        /// <summary>
        /// Gets or sets the resolution strategy when a method pattern matches both the whitelist and the blacklist.
        /// </summary>
        /// <remarks>
        /// When the value is <see cref="FilterPriority.Whitelist"/> the method is treated as allowed.
        /// When the value is <see cref="FilterPriority.Blacklist"/> the method is treated as forbidden.
        /// </remarks>
        public FilterPriority FilterPriority { get; set; } = FilterPriority.Whitelist;

        /// <summary>
        /// Gets or sets the list of explicitly allowed HTTP method patterns.
        /// </summary>
        /// <remarks>
        /// Default: common standard methods.
        /// If configuration specifies <c>Whitelist</c>, it fully replaces this value.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> Whitelist { get; set; } = new[]
        {
            "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS",
        };

        /// <summary>
        /// Gets or sets the list of explicitly forbidden HTTP method patterns.
        /// </summary>
        /// <remarks>
        /// Default: empty.
        /// If configuration specifies <c>Blacklist</c>, it fully replaces this value.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> Blacklist { get; set; } = new[]
        {
         // Empty / missing method token:
         // Usually malformed requests, parsing artifacts, or broken clients.
         "",

         // Standard HTTP methods often disabled to reduce attack surface:
         // - TRACE: historically related to cross-site tracing style issues and diagnostic reflection.
         // - CONNECT: proxy tunneling (mostly relevant for forward proxies).
         // Origin: HTTP Semantics (RFC 7231; updated by RFC 9110).
         "TRACE",
         "CONNECT",

         // WebDAV authoring methods (remote file/collection operations over HTTP).
         // If you do NOT intentionally support WebDAV, these are typically safe to block and are often probed by scanners.
         // Origin: RFC 4918 (WebDAV core), RFC 5323 (DASL SEARCH), RFC 3253 (DeltaV), RFC 3744 (ACL), RFC 5842 (Bindings), RFC 4437 (Redirect References).
        "PROPFIND", "PROPPATCH", "MKCOL", "COPY", "MOVE", "LOCK", "UNLOCK", "SEARCH", "REPORT", "MKACTIVITY", "MKWORKSPACE", "VERSION-CONTROL", "CHECKIN",
        "CHECKOUT", "UNCHECKOUT", "MERGE", "UPDATE", "LABEL", "BASELINE-CONTROL", "MKREDIRECTREF", "MKRESOURCE", "BIND", "REBIND", "UNBIND", "ACL"
        };

        /// <summary>
        /// Gets or sets a value indicating whether method pattern matching is case sensitive.
        /// </summary>
        /// <remarks>
        /// Default is <see langword="false"/> because methods are typically normalized as upper-case tokens.
        /// </remarks>
        public bool CaseSensitive { get; set; } = true;

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