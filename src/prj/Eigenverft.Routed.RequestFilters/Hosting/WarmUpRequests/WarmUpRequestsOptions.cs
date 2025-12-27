using System;

using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Hosting.WarmUpRequests
{
    /// <summary>
    /// Options for <see cref="WarmUpRequestsHostedService"/>.
    /// </summary>
    /// <remarks>
    /// Bind from configuration section named <c>WarmUpRequestsOptions</c>. Defaults are safe for production.
    /// </remarks>
    public sealed class WarmUpRequestsOptions
    {
        /// <summary>
        /// Named client for warm-up requests.
        /// </summary>
        public const string HttpClientName = "Eigenverft.WarmUpRequests";

        /// <summary>
        /// Gets or sets a value indicating whether warm-up requests are executed.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the initial delay before issuing warm-up requests.
        /// </summary>
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Gets or sets the timeout applied per warm-up request.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Gets or sets target URLs to request once on startup.
        /// </summary>
        /// <remarks>
        /// Example: <c>https://eigenverft.com/</c>.
        /// Add additional representative paths if you want to prime more code paths.
        /// </remarks>
        public string[] TargetUrls { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the user agent header used for warm-up requests.
        /// </summary>
        public string UserAgent { get; set; } = "Mozilla/5.0 (WarmUpRequests)";

        /// <summary>
        /// Gets or sets the accept-language header used for warm-up requests.
        /// </summary>
        public string AcceptLanguage { get; set; } = "en-US,en;q=0.9";

        /// <summary>
        /// Gets or sets the log level used for warm-up completion messages.
        /// </summary>
        public LogLevel LogLevel { get; set; } = LogLevel.Debug;
    }
}
