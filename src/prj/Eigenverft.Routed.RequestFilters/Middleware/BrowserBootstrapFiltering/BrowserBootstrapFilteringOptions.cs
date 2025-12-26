using System;

using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.BrowserBootstrapFiltering
{
    /// <summary>
    /// Provides configuration options for <see cref="BrowserBootstrapFiltering"/>.
    /// </summary>
    /// <remarks>
    /// Reviewer note:
    /// - This slimmed option set applies bootstrap only to <see cref="HtmlProtectedBootstrapScopePathPatterns"/>.
    /// - Exception/unmatched concepts are intentionally removed here.
    /// </remarks>
    public sealed class BrowserBootstrapFilteringOptions
    {
        /// <summary>
        /// Gets or sets patterns that define HTML entry paths where bootstrap applies.
        /// </summary>
        /// <remarks>
        /// Default: <c>/</c> and <c>/index.html</c>.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> HtmlProtectedBootstrapScopePathPatterns { get; set; } = new[] { "/", "/index.html" };

        /// <summary>
        /// Gets or sets the log level used when a request path is in bootstrap scope (classification only).
        /// </summary>
        public LogLevel LogLevelBootstrapScopePaths { get; set; } = LogLevel.None;

        /// <summary>
        /// Gets or sets the cookie name used as the bootstrap signal.
        /// </summary>
        public string CookieName { get; set; } = "Eigenverft.BrowserBootstrap";

        /// <summary>
        /// Gets or sets the cookie max-age.
        /// </summary>
        public TimeSpan CookieMaxAge { get; set; } = TimeSpan.FromDays(1);

        /// <summary>
        /// Gets or sets the log level used when the request is allowed to proceed
        /// because the bootstrap cookie validated.
        /// </summary>
        public LogLevel LogLevelBootstrapPassed { get; set; } = LogLevel.Debug;

        /// <summary>
        /// Gets or sets the log level used when the middleware serves the bootstrap loading HTML.
        /// </summary>
        public LogLevel LogLevelBootstrapServed { get; set; } = LogLevel.Information;
    }
}
