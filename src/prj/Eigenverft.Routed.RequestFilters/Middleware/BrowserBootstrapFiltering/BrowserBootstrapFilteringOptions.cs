using System;

using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.BrowserBootstrapFiltering
{
    /// <summary>
    /// Provides configuration options for <see cref="BrowserBootstrapFiltering"/>.
    /// </summary>
    public sealed class BrowserBootstrapFilteringOptions
    {
        public OptionsConfigOverridesDefaultsList<string> HtmlProtectedBootstrapScopePathPatterns { get; set; } = new[] { "/", "/index.html" };

        /// <summary>
        /// Gets or sets the log level used for scope decisions (in-scope vs out-of-scope).
        /// </summary>
        public LogLevel LogLevelScope { get; set; } = LogLevel.Debug;

        public string CookieName { get; set; } = "Eigenverft.BrowserBootstrap";
        public TimeSpan CookieMaxAge { get; set; } = TimeSpan.FromDays(1);

        public LogLevel LogLevelBootstrapPassed { get; set; } = LogLevel.Debug;
        public LogLevel LogLevelBootstrapServed { get; set; } = LogLevel.Information;
    }
}
