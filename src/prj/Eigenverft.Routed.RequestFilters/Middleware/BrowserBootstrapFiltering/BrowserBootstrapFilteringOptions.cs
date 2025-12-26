using System;

using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.BrowserBootstrapFiltering
{
    /// <summary>
    /// Provides configuration options for <see cref="BrowserBootstrapFiltering"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This middleware is intended to protect “HTML entry” routes (for example <c>/</c> or <c>/index.html</c>) by
    /// requiring that the client can execute JavaScript and persist a cookie.
    /// </para>
    /// <para>
    /// If an in-scope request arrives without a valid bootstrap cookie, the middleware responds with a small loading
    /// HTML document that attempts to write the cookie and then reload the original URL. Non-JavaScript clients and
    /// clients with cookies disabled will remain on the loading page.
    /// </para>
    /// <code>
    ///   "BrowserBootstrapFiltering": {
    ///     "HtmlProtectedBootstrapScopePathPatterns": [
    ///         "/",
    ///         "/index.html"
    ///    ],
    ///    "CookieName": "Eigenverft.BrowserBootstrap",
    ///    "CookieMaxAge": "1.00:00:00",
    ///    "LogLevelScope": "Debug",
    ///    "LogLevelBootstrapPassed": "Debug",
    ///    "LogLevelBootstrapServed": "Warning"
    ///    }
    /// </code>
    /// <para>
    /// Logging is separated into: scope decisions (in-scope vs out-of-scope) and bootstrap outcomes (cookie validated
    /// vs bootstrap page served).
    /// </para>
    /// </remarks>
    public sealed class BrowserBootstrapFilteringOptions
    {
        /// <summary>
        /// Gets or sets wildcard patterns that define which request paths are protected by the bootstrap check.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only GET/HEAD requests whose <see cref="Microsoft.AspNetCore.Http.HttpRequest.Path"/> matches one of these
        /// patterns are handled by the middleware; all other requests are passed through unchanged.
        /// </para>
        /// <para>
        /// Default: <c>/</c> and <c>/index.html</c>.
        /// </para>
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> HtmlProtectedBootstrapScopePathPatterns { get; set; } = new[] { "/", "/index.html" };

        /// <summary>
        /// Gets or sets the cookie name used as the bootstrap signal.
        /// </summary>
        /// <remarks>
        /// The cookie value is a protected token that includes an expiry and a User-Agent binding.
        /// </remarks>
        public string CookieName { get; set; } = "Eigenverft.BrowserBootstrap";

        /// <summary>
        /// Gets or sets the maximum age used when issuing the bootstrap cookie.
        /// </summary>
        public TimeSpan CookieMaxAge { get; set; } = TimeSpan.FromDays(1);

        /// <summary>
        /// Gets or sets the log level used for scope decisions (in-scope vs out-of-scope).
        /// </summary>
        /// <remarks>
        /// This log helps operators validate that the configured scope patterns match the intended entry routes.
        /// </remarks>
        public LogLevel LogLevelScope { get; set; } = LogLevel.Debug;

        /// <summary>
        /// Gets or sets the log level used when the request is allowed to proceed because a valid bootstrap cookie
        /// was present.
        /// </summary>
        public LogLevel LogLevelBootstrapPassed { get; set; } = LogLevel.Debug;

        /// <summary>
        /// Gets or sets the log level used when the middleware serves the bootstrap loading HTML because the cookie
        /// was missing or invalid.
        /// </summary>
        /// <remarks>
        /// Typical causes are first-time visits, expired cookies, or clients that do not persist cookies.
        /// </remarks>
        public LogLevel LogLevelBootstrapServed { get; set; } = LogLevel.Warning;
    }
}
