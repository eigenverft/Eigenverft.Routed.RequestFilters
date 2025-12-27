// File: Hosting/WarmUpRequests/WarmUpRequestsHostedService.cs

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Hosting.WarmUpRequests
{
    /// <summary>
    /// Hosted service that issues warm-up HTTP requests after a configurable delay.
    /// </summary>
    /// <remarks>
    /// Intended to prime first-use overhead (JIT, DI graphs, TLS/session caches, connection pools).
    /// Must not crash the process and should be observable via logs.
    /// </remarks>
    public sealed class WarmUpRequestsHostedService : BackgroundService
    {
        private static readonly Version DefaultHttpVersion = HttpVersion.Version20;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDeferredLogger<WarmUpRequestsHostedService> _logger;
        private readonly IOptionsMonitor<WarmUpRequestsOptions> _optionsMonitor;

        /// <summary>
        /// Initializes a new instance of the <see cref="WarmUpRequestsHostedService"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Factory used to create the HTTP client.</param>
        /// <param name="logger">Deferred logger instance.</param>
        /// <param name="optionsMonitor">Options monitor for <see cref="WarmUpRequestsOptions"/>.</param>
        public WarmUpRequestsHostedService(
            IHttpClientFactory httpClientFactory,
            IDeferredLogger<WarmUpRequestsHostedService> logger,
            IOptionsMonitor<WarmUpRequestsOptions> optionsMonitor)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));

            _optionsMonitor.OnChange(_ => _logger.LogDebug(
                "Configuration for {ServiceName} updated.",
                () => nameof(WarmUpRequestsHostedService)));
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            WarmUpRequestsOptions options = _optionsMonitor.CurrentValue;

            if (!options.Enabled)
            {
                return;
            }

            if (options.TargetUrls == null || options.TargetUrls.Length == 0)
            {
                if (options.LogLevel != LogLevel.None && _logger.IsEnabled(options.LogLevel))
                {
                    _logger.Log(options.LogLevel, "Warm-up skipped. No TargetUrls configured.");
                }

                return;
            }

            TimeSpan requestTimeout = options.RequestTimeout > TimeSpan.Zero ? options.RequestTimeout : TimeSpan.FromSeconds(5);
            TimeSpan connectTimeout = options.ConnectTimeout > TimeSpan.Zero ? options.ConnectTimeout : TimeSpan.FromSeconds(2);

            if (options.LogLevel != LogLevel.None && _logger.IsEnabled(options.LogLevel))
            {
                _logger.Log(
                    options.LogLevel,
                    "Warm-up configured. enabled=True initialDelay={InitialDelay} requestTimeout={RequestTimeout} connectTimeout={ConnectTimeout} disableProxy={DisableProxy} acceptAnyCert={AcceptAnyCert} hostOverride={HostOverride}.",
                    () => options.InitialDelay,
                    () => requestTimeout,
                    () => connectTimeout,
                    () => options.DisableSystemProxy,
                    () => options.DangerousAcceptAnyServerCertificate,
                    () => options.HostHeaderOverride ?? string.Empty);
            }

            if (options.InitialDelay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(options.InitialDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            HttpClient client = _httpClientFactory.CreateClient(WarmUpRequestsOptions.HttpClientName);

            foreach (string rawUrl in options.TargetUrls)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                string url = (rawUrl ?? string.Empty).Trim();
                if (url.Length == 0)
                {
                    continue;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                {
                    if (options.LogLevel != LogLevel.None && _logger.IsEnabled(options.LogLevel))
                    {
                        _logger.Log(options.LogLevel, "Warm-up skipped invalid url={Url}.", () => url);
                    }

                    continue;
                }

                long start = Stopwatch.GetTimestamp();

                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    cts.CancelAfter(requestTimeout);

                    using var request = new HttpRequestMessage(HttpMethod.Get, uri)
                    {
                        Version = DefaultHttpVersion,
                        VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
                    };

                    // Reviewer note: reduce foot-guns:
                    // only apply Host override when warming via loopback or IP endpoints (common: https://localhost/).
                    if (!string.IsNullOrWhiteSpace(options.HostHeaderOverride))
                    {
                        bool shouldOverrideHost =
                            uri.IsLoopback ||
                            IPAddress.TryParse(uri.Host, out _);

                        if (shouldOverrideHost)
                        {
                            request.Headers.Host = options.HostHeaderOverride;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(options.UserAgent))
                    {
                        request.Headers.TryAddWithoutValidation("User-Agent", options.UserAgent);
                    }

                    if (!string.IsNullOrWhiteSpace(options.AcceptLanguage))
                    {
                        request.Headers.TryAddWithoutValidation("Accept-Language", options.AcceptLanguage);
                    }

                    if (options.LogLevel != LogLevel.None && _logger.IsEnabled(options.LogLevel))
                    {
                        _logger.Log(options.LogLevel, "Warm-up sending. url={Url}.", () => url);
                    }

                    using HttpResponseMessage response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cts.Token);

                    double ms = GetElapsedMilliseconds(start);

                    if (options.LogLevel != LogLevel.None && _logger.IsEnabled(options.LogLevel))
                    {
                        _logger.Log(
                            options.LogLevel,
                            "Warm-up completed. url={Url} status={StatusCode} elapsedMs={ElapsedMs}.",
                            () => url,
                            () => (int)response.StatusCode,
                            () => ms);
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    double ms = GetElapsedMilliseconds(start);

                    if (options.LogLevel != LogLevel.None && _logger.IsEnabled(options.LogLevel))
                    {
                        _logger.Log(options.LogLevel, "Warm-up timed out. url={Url} elapsedMs={ElapsedMs}.", () => url, () => ms);
                    }
                }
                catch (Exception ex)
                {
                    double ms = GetElapsedMilliseconds(start);

                    _logger.LogWarning(ex, "Warm-up failed. url={Url} elapsedMs={ElapsedMs}.", () => url, () => ms);
                }
            }
        }

        private static double GetElapsedMilliseconds(long startTimestamp)
        {
            long end = Stopwatch.GetTimestamp();
            long delta = end - startTimestamp;
            return delta * 1000.0 / Stopwatch.Frequency;
        }
    }
}
