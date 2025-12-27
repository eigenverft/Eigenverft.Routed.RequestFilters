// WarmUpRequestsHostedService.cs
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
    /// Intended to prime first-use overhead (JIT, DI graphs, TLS/session caches, proxy pools).
    /// Must not crash the process and should be fully observable via logs.
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

            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {ServiceName} updated.", () => nameof(WarmUpRequestsHostedService)));
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

            if (options.LogLevel != LogLevel.None && _logger.IsEnabled(options.LogLevel))
            {
                _logger.Log(
                    options.LogLevel,
                    "Warm-up configured. enabled=True initialDelay={InitialDelay} requestTimeout={RequestTimeout} connectTimeout={ConnectTimeout} disableProxy={DisableProxy} hostOverride={HostOverride}.",
                    () => options.InitialDelay,
                    () => requestTimeout,
                    () => options.ConnectTimeout,
                    () => options.DisableSystemProxy,
                    () => options.HostHeaderOverride ?? string.Empty);
            }

            if (options.InitialDelay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(options.InitialDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            HttpClient client = _httpClientFactory.CreateClient(WarmUpRequestsOptions.HttpClientName);

            foreach (string rawUrl in options.TargetUrls)
            {
                if (stoppingToken.IsCancellationRequested) return;

                string url = (rawUrl ?? string.Empty).Trim();
                if (url.Length == 0) continue;

                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                {
                    if (options.LogLevel != LogLevel.None && _logger.IsEnabled(options.LogLevel))
                    {
                        _logger.Log(options.LogLevel, "Warm-up skipped invalid url={Url}.", () => url);
                    }

                    continue;
                }

                var sw = Stopwatch.StartNew();

                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    cts.CancelAfter(requestTimeout);

                    using var request = new HttpRequestMessage(HttpMethod.Get, uri)
                    {
                        // Reviewer note: deterministic behavior; avoid unexpected protocol attempts.
                        Version = DefaultHttpVersion,
                        VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
                    };

                    if (!string.IsNullOrWhiteSpace(options.HostHeaderOverride))
                    {
                        request.Headers.Host = options.HostHeaderOverride;
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

                    // Reviewer note: fail-safe against rare cases where cancellation is not observed promptly.
                    Task<HttpResponseMessage> sendTask = client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cts.Token);

                    Task timeoutTask = Task.Delay(requestTimeout + TimeSpan.FromMilliseconds(250), stoppingToken);

                    Task completed = await Task.WhenAny(sendTask, timeoutTask).ConfigureAwait(false);

                    if (completed == timeoutTask && !stoppingToken.IsCancellationRequested)
                    {
                        cts.Cancel();

                        _ = sendTask.ContinueWith(
                            t => _ = t.Exception,
                            TaskContinuationOptions.OnlyOnFaulted);

                        if (options.LogLevel != LogLevel.None && _logger.IsEnabled(options.LogLevel))
                        {
                            _logger.Log(
                                options.LogLevel,
                                "Warm-up timed out. url={Url} elapsedMs={ElapsedMs}.",
                                () => url,
                                () => sw.Elapsed.TotalMilliseconds);
                        }

                        continue;
                    }

                    using HttpResponseMessage response = await sendTask.ConfigureAwait(false);

                    if (options.LogLevel != LogLevel.None && _logger.IsEnabled(options.LogLevel))
                    {
                        _logger.Log(
                            options.LogLevel,
                            "Warm-up completed. url={Url} status={StatusCode} elapsedMs={ElapsedMs}.",
                            () => url,
                            () => (int)response.StatusCode,
                            () => sw.Elapsed.TotalMilliseconds);
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    if (options.LogLevel != LogLevel.None && _logger.IsEnabled(options.LogLevel))
                    {
                        _logger.Log(options.LogLevel, "Warm-up timed out. url={Url} elapsedMs={ElapsedMs}.", () => url, () => sw.Elapsed.TotalMilliseconds);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Warm-up failed. url={Url} elapsedMs={ElapsedMs}.", () => url, () => sw.Elapsed.TotalMilliseconds);
                }
            }
        }
    }
}
