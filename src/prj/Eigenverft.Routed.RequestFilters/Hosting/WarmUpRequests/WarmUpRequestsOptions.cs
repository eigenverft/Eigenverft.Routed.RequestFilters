// File: Hosting/WarmUpRequests/WarmUpRequestsOptions.cs

using System;

using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Hosting.WarmUpRequests
{
    /// <summary>
    /// Options for <see cref="WarmUpRequestsHostedService"/>.
    /// </summary>
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
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Gets or sets the connect timeout for establishing TCP/TLS connections.
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Gets or sets a value indicating whether the OS/system proxy should be ignored.
        /// </summary>
        /// <remarks>
        /// In server scenarios this should typically be <c>true</c> to avoid unexpected proxy hangs.
        /// </remarks>
        public bool DisableSystemProxy { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether server certificates should be accepted without validation.
        /// </summary>
        /// <remarks>
        /// Reviewer note: use only for dev/test. Never enable in production.
        /// </remarks>
        public bool DangerousAcceptAnyServerCertificate { get; set; } = false;

        /// <summary>
        /// Gets or sets a host header override (optional).
        /// </summary>
        /// <remarks>
        /// Useful when warming via loopback or IP endpoints while exercising canonical host logic for a public domain.
        /// </remarks>
        public string? HostHeaderOverride { get; set; } = null;

        /// <summary>
        /// Gets or sets target URLs to request once on startup.
        /// </summary>
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
        /// Gets or sets the log level used for warm-up messages.
        /// </summary>
        public LogLevel LogLevel { get; set; } = LogLevel.Debug;
    }
}
