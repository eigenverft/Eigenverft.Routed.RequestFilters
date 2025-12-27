using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.DevelopmentUnlocker
{
    /// <summary>
    /// Provides configuration options for <see cref="DevelopmentUnlocker"/>.
    /// </summary>
    /// <remarks>
    /// This options type is designed to be bound from configuration, for example from a section named
    /// <c>DevelopmentUnlockerOptions</c>. If the section is missing, property initializers act as defaults.
    /// </remarks>
    public sealed class DevelopmentUnlockerOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether the middleware is active.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the request path that triggers the unlock behavior.
        /// </summary>
        /// <remarks>
        /// The value is compared against <c>HttpContext.Request.Path</c> using case-insensitive comparison.
        /// A leading slash is added if missing. Trailing slashes are ignored for the configured endpoint.
        /// </remarks>
        public string EndpointPath { get; set; } = "/__dev/unlock";

        /// <summary>
        /// Gets or sets the log level used when an unlock is triggered.
        /// </summary>
        /// <remarks>
        /// Use <see cref="LogLevel.None"/> to disable logging for unlock events.
        /// </remarks>
        public LogLevel LogLevelUnlock { get; set; } = LogLevel.Warning;
    }
}
