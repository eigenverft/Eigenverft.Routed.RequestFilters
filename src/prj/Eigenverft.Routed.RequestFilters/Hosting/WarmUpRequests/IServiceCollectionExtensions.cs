// File: Hosting/WarmUpRequests/IServiceCollectionExtensions.cs

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Hosting.WarmUpRequests
{
    /// <summary>
    /// Provides extension methods for registering warm-up requests.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers warm-up requests with the standard behavior:
        /// binds from configuration section <c>WarmUpRequestsOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="WarmUpRequestsOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        /// <example>
        /// <code>
        /// builder.Services.AddWarmUpRequests();
        /// </code>
        /// </example>
        public static IServiceCollection AddWarmUpRequests(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services
                .AddOptions<WarmUpRequestsOptions>()
                .BindConfiguration(nameof(WarmUpRequestsOptions));

            AddWarmUpHttpClient(services);

            services.AddHostedService<WarmUpRequestsHostedService>();

            return services;
        }

        /// <summary>
        /// Registers warm-up requests and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="manualConfigure"/> is null.</exception>
        /// <example>
        /// <code>
        /// builder.Services.AddWarmUpRequests(o =&gt;
        /// {
        ///     o.InitialDelay = TimeSpan.FromSeconds(10);
        ///     o.TargetUrls = new[] { "https://eigenverft.com/health" };
        /// });
        /// </code>
        /// </example>
        public static IServiceCollection AddWarmUpRequests(this IServiceCollection services, Action<WarmUpRequestsOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddWarmUpRequests();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers warm-up requests options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration root that contains a section named <c>WarmUpRequestsOptions</c>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
        public static IServiceCollection AddWarmUpRequests(this IServiceCollection services, IConfiguration configuration, Action<WarmUpRequestsOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            services
                .AddOptions<WarmUpRequestsOptions>()
                .Bind(configuration.GetSection(nameof(WarmUpRequestsOptions)));

            if (manualConfigure != null)
            {
                services.Configure(manualConfigure);
            }

            AddWarmUpHttpClient(services);

            services.AddHostedService<WarmUpRequestsHostedService>();

            return services;
        }

        private static void AddWarmUpHttpClient(IServiceCollection services)
        {
            services.AddHttpClient(WarmUpRequestsOptions.HttpClientName)
                .ConfigureHttpClient((sp, client) =>
                {
                    WarmUpRequestsOptions o = sp.GetRequiredService<IOptionsMonitor<WarmUpRequestsOptions>>().CurrentValue;

                    // Reviewer note: keep HttpClient.Timeout slightly above per-request timeout to avoid confusing dual timeouts.
                    TimeSpan perRequest = o.RequestTimeout > TimeSpan.Zero ? o.RequestTimeout : TimeSpan.FromSeconds(5);
                    client.Timeout = perRequest + TimeSpan.FromSeconds(2);
                })
                .ConfigurePrimaryHttpMessageHandler(sp =>
                {
                    WarmUpRequestsOptions o = sp.GetRequiredService<IOptionsMonitor<WarmUpRequestsOptions>>().CurrentValue;

                    TimeSpan requestTimeout = o.RequestTimeout > TimeSpan.Zero ? o.RequestTimeout : TimeSpan.FromSeconds(5);
                    TimeSpan connectTimeout = o.ConnectTimeout > TimeSpan.Zero ? o.ConnectTimeout : TimeSpan.FromSeconds(2);

                    // Reviewer note: if connectTimeout is too close to requestTimeout, fallback to other IPs cannot happen.
                    // Force a short per-attempt connect timeout to allow IPv4 fallback when IPv6 is broken.
                    TimeSpan perAttemptConnectTimeout = connectTimeout;
                    if (perAttemptConnectTimeout <= TimeSpan.Zero)
                    {
                        perAttemptConnectTimeout = TimeSpan.FromMilliseconds(500);
                    }

                    if (perAttemptConnectTimeout >= requestTimeout)
                    {
                        perAttemptConnectTimeout = TimeSpan.FromMilliseconds(
                            Math.Max(250, requestTimeout.TotalMilliseconds / 3));
                    }

                    var handler = new SocketsHttpHandler
                    {
                        // Reviewer note: system proxy on servers can introduce "silent" hangs. Default DisableSystemProxy=True.
                        UseProxy = !o.DisableSystemProxy,
                        Proxy = null,

                        // Safety net; ConnectCallback below enforces per-attempt timeout.
                        ConnectTimeout = perAttemptConnectTimeout,

                        // Reviewer note: do not chase redirects; redirect loops should be visible.
                        AllowAutoRedirect = false,

                        AutomaticDecompression = DecompressionMethods.GZip |
                                                 DecompressionMethods.Deflate |
                                                 DecompressionMethods.Brotli
                    };

                    if (o.DangerousAcceptAnyServerCertificate)
                    {
                        // Reviewer note: use only for dev/test. Never enable in production.
                        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                    }

                    // Reviewer note: Bullet-proof connect:
                    // resolve all IPs, try IPv4 first, use short per-attempt timeouts so IPv6 blackholes do not stall the request.
                    handler.ConnectCallback = async (context, cancellationToken) =>
                    {
                        string host = context.DnsEndPoint.Host;
                        int port = context.DnsEndPoint.Port;

                        IPAddress[] addresses;
                        try
                        {
                            addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            throw new HttpRequestException($"DNS resolution failed for host '{host}'.", ex);
                        }

                        if (addresses == null || addresses.Length == 0)
                        {
                            throw new HttpRequestException($"DNS resolution returned no addresses for host '{host}'.");
                        }

                        // Prefer IPv4 first for reliability when IPv6 routes are broken.
                        IPAddress[] ordered = addresses
                            .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                            .ToArray();

                        Exception? lastError = null;

                        foreach (IPAddress address in ordered)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            Socket socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                            {
                                NoDelay = true
                            };

                            try
                            {
                                var endPoint = new IPEndPoint(address, port);

                                Task connectTask = socket.ConnectAsync(endPoint);

                                Task completed = await Task.WhenAny(
                                        connectTask,
                                        Task.Delay(perAttemptConnectTimeout, cancellationToken))
                                    .ConfigureAwait(false);

                                if (completed != connectTask)
                                {
                                    // timed out on this address, try next
                                    socket.Dispose();
                                    continue;
                                }

                                // propagate connect exception if present
                                await connectTask.ConfigureAwait(false);

                                return new NetworkStream(socket, ownsSocket: true);
                            }
                            catch (Exception ex)
                            {
                                lastError = ex;
                                socket.Dispose();
                                continue;
                            }
                        }

                        throw new HttpRequestException(
                            $"Failed to connect to '{host}:{port}' via any resolved address.",
                            lastError);
                    };

                    return handler;
                });
        }

        /// <summary>
        /// Adds shared registrations required by warm-up requests.
        /// </summary>
        /// <param name="services">The service collection.</param>
        private static void AddInfrastructure(IServiceCollection services)
        {
            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.AddOptions();
        }
    }
}
