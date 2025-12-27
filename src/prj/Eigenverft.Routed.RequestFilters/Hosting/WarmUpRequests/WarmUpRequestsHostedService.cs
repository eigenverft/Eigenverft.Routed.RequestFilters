using System;
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
    /// Typical use cases:
    /// - prime JIT and DI paths in a cold process
    /// - establish TLS/session caches
    /// - pre-create upstream connection pools for reverse proxy scenarios
    /// </remarks>
    public sealed class WarmUpRequestsHostedService : BackgroundService
    {
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
                    _logger.Log(options.LogLevel, "Warm-up skipped. No TargetUrls configured.", () => Array.Empty<object>());
                }

                return;
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
                if (stoppingToken.IsCancellationRequested) return;

                string url = (rawUrl ?? string.Empty).Trim();
                if (url.Length == 0) continue;

                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    if (options.RequestTimeout > TimeSpan.Zero)
                    {
                        cts.CancelAfter(options.RequestTimeout);
                    }

                    // inside the foreach, before SendAsync:

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);

                    if (!string.IsNullOrWhiteSpace(options.HostHeaderOverride))
                    {
                        // Reviewer note: this affects Host header and ASP.NET Core Request.Host.
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
                        _logger.Log(options.LogLevel, "Warm-up sending. url={Url} hostOverride={Host}.", () => url, () => options.HostHeaderOverride ?? "");
                    }

                    using HttpResponseMessage response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cts.Token);

                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Per-request timeout.
                    if (options.LogLevel != LogLevel.None && _logger.IsEnabled(options.LogLevel))
                    {
                        _logger.Log(options.LogLevel, "Warm-up request timed out. url={Url}.", () => url);
                    }
                }
                catch (Exception ex)
                {
                    // Reviewer note: failure should not crash the process; it should only be observable.
                    _logger.LogWarning(ex, "Warm-up request failed. url={Url}.", () => url);
                }
            }
        }
    }
}
