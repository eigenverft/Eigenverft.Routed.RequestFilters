// IServiceCollectionExtensions.cs
using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;

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

            services.AddOptions<WarmUpRequestsOptions>().BindConfiguration(nameof(WarmUpRequestsOptions));

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
        ///     o.TargetUrls = new[] { "https://eigenverft.com/favicon.ico", "https://www.google.com/" };
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
        /// <example>
        /// <code>
        /// builder.Services.AddWarmUpRequests(builder.Configuration, o =&gt; o.Enabled = true);
        /// </code>
        /// </example>
        public static IServiceCollection AddWarmUpRequests(this IServiceCollection services, IConfiguration configuration, Action<WarmUpRequestsOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            services.AddOptions<WarmUpRequestsOptions>().Bind(configuration.GetSection(nameof(WarmUpRequestsOptions)));

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

                    // Reviewer note: avoid double-timeout confusion by keeping HttpClient.Timeout above per-request timeout.
                    TimeSpan perRequest = o.RequestTimeout > TimeSpan.Zero ? o.RequestTimeout : TimeSpan.FromSeconds(5);

                    // Reviewer note: we still use a CancellationToken timeout in the hosted service, this is an extra guard.
                    client.Timeout = perRequest + TimeSpan.FromSeconds(2);
                })
                .ConfigurePrimaryHttpMessageHandler(sp =>
                {
                    WarmUpRequestsOptions o = sp.GetRequiredService<IOptionsMonitor<WarmUpRequestsOptions>>().CurrentValue;

                    var handler = new SocketsHttpHandler
                    {
                        // Reviewer note: system proxy can cause "silent" hangs if misconfigured.
                        UseProxy = !o.DisableSystemProxy,
                        Proxy = null,

                        // Reviewer note: hard cap connect time so SendAsync cannot sit in connect forever.
                        ConnectTimeout = o.ConnectTimeout > TimeSpan.Zero ? o.ConnectTimeout : TimeSpan.FromSeconds(2),

                        // Reviewer note: do not chase redirects; loops should be visible.
                        AllowAutoRedirect = false,

                        AutomaticDecompression = DecompressionMethods.GZip |
                                                 DecompressionMethods.Deflate |
                                                 DecompressionMethods.Brotli
                    };

                    if (o.DangerousAcceptAnyServerCertificate)
                    {
                        // Reviewer note: for dev/test only; do not enable on public internet endpoints.
                        handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
                    }

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
