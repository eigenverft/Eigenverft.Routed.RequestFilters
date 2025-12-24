using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Eigenverft.Routed.RequestFilters.GenericExtensions.ConfigureWebHostBuilderExtensions
{
    /// <summary>Kestrel-related extension methods for <see cref="ConfigureWebHostBuilder"/>.</summary>
    public static partial class ConfigureWebHostBuilderExtensions
    {
        /// <summary>Controls which interfaces Kestrel binds to for the configured listeners.</summary>
        public enum ListenScope
        {
            /// <summary>Bind only to loopback (localhost).</summary>
            Localhost,

            /// <summary>Bind to all available interfaces.</summary>
            AnyIP
        }

        /// <summary>Controls which TLS protocol versions are permitted for HTTPS endpoints configured by this method.</summary>
        public enum TlsProtocolPolicy
        {
            /// <summary>Allow TLS 1.0, TLS 1.1, TLS 1.2 and TLS 1.3 (no SSL2/SSL3).</summary>
            Default,

            /// <summary>Allow TLS 1.2 and TLS 1.3.</summary>
            Modern,

            /// <summary>Allow TLS 1.3 only.</summary>
            Strict,

            /// <summary>Enable legacy protocol versions (unsafe; for controlled environments only).</summary>
            Legacy
        }

        /// <summary>Configures Kestrel with optional HTTP and HTTPS listeners and SNI-based certificate selection.</summary>
        /// <remarks>
        /// Fallback certificate behavior (kept intentionally simple): if no SNI match exists, the method returns <c>certificates.Last().Value</c>.
        /// That means the enumeration order of <paramref name="certificates"/> determines the fallback.
        /// </remarks>
        /// <param name="configureWebHostBuilder">The builder to configure.</param>
        /// <param name="certificates">Mapping of hostname suffix to certificate used for SNI selection. Keys are matched using a case-insensitive <c>EndsWith</c> comparison against the requested SNI value.</param>
        /// <param name="httpPort">HTTP port to listen on; set to <c>null</c> to disable HTTP.</param>
        /// <param name="httpsPort">HTTPS port to listen on; set to <c>null</c> to disable HTTPS.</param>
        /// <param name="listenScope">Controls whether listeners bind to localhost or all interfaces.</param>
        /// <param name="addServerHeader">Whether to add the Kestrel <c>Server</c> header.</param>
        /// <param name="protocols">Optional protocols applied to both HTTP and HTTPS listeners. If <c>null</c>, Kestrel's defaults are used: typically HTTP defaults for the HTTP endpoint, and <see cref="HttpProtocols.Http1AndHttp2"/> for the HTTPS endpoint (as configured by this method).</param>
        /// <param name="preferLongestSuffixMatch">When <c>true</c>, the most specific suffix (longest key) wins if multiple keys match the same SNI.</param>
        /// <param name="tlsProtocolPolicy">TLS protocol policy applied to HTTPS. Defaults to <see cref="TlsProtocolPolicy.Default"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configureWebHostBuilder"/> or <paramref name="certificates"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Thrown when both <paramref name="httpPort"/> and <paramref name="httpsPort"/> are <c>null</c>, when HTTPS is enabled but no usable certificates are provided, or when <paramref name="tlsProtocolPolicy"/> is not <see cref="TlsProtocolPolicy.Default"/> while HTTPS is disabled.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a provided port is outside the valid range.</exception>
        public static void ConfigureKestrelSni(this ConfigureWebHostBuilder configureWebHostBuilder, Dictionary<string, X509Certificate2> certificates, int? httpPort = 80, int? httpsPort = 443, ListenScope listenScope = ListenScope.Localhost, bool addServerHeader = false, HttpProtocols? protocols = null, bool preferLongestSuffixMatch = true, TlsProtocolPolicy tlsProtocolPolicy = TlsProtocolPolicy.Default)
        {
            if (configureWebHostBuilder is null) throw new ArgumentNullException(nameof(configureWebHostBuilder));
            if (certificates is null) throw new ArgumentNullException(nameof(certificates));
            if (!httpPort.HasValue && !httpsPort.HasValue) throw new ArgumentException("At least one of httpPort or httpsPort must be specified.");
            if (!httpsPort.HasValue && tlsProtocolPolicy != TlsProtocolPolicy.Default) throw new ArgumentException("tlsProtocolPolicy can only be set when httpsPort is enabled.", nameof(tlsProtocolPolicy));

            if (httpPort.HasValue) ValidatePort(httpPort.Value, nameof(httpPort));
            if (httpsPort.HasValue) ValidatePort(httpsPort.Value, nameof(httpsPort));

            if (httpsPort.HasValue)
            {
                if (certificates.Count == 0) throw new ArgumentException("HTTPS is enabled but no certificates were provided.", nameof(certificates));
                if (certificates.Any(kvp => kvp.Value is null)) throw new ArgumentException("HTTPS is enabled but at least one provided certificate is null.", nameof(certificates));
            }

            var matchPairs = preferLongestSuffixMatch ? certificates.OrderByDescending(kvp => (kvp.Key ?? string.Empty).Length).ToArray() : certificates.ToArray();

            configureWebHostBuilder.ConfigureKestrel(serverOptions =>
            {
                serverOptions.AddServerHeader = addServerHeader;

                Action<int, Action<ListenOptions>> listen = listenScope == ListenScope.Localhost
                    ? (port, configure) => serverOptions.ListenLocalhost(port, configure)
                    : (port, configure) => serverOptions.ListenAnyIP(port, configure);

                if (httpPort.HasValue)
                {
                    listen(httpPort.Value, listenOptions => { if (protocols.HasValue) listenOptions.Protocols = protocols.Value; });
                }

                if (httpsPort.HasValue)
                {
                    listen(httpsPort.Value, listenOptions =>
                    {
                        listenOptions.Protocols = protocols ?? HttpProtocols.Http1AndHttp2;
                        listenOptions.UseHttps(httpsOptions =>
                        {
                            httpsOptions.ServerCertificateSelector = (_, sni) =>
                            {
                                if (!string.IsNullOrWhiteSpace(sni))
                                {
                                    foreach (var kvp in matchPairs)
                                    {
                                        var key = kvp.Key;
                                        if (!string.IsNullOrWhiteSpace(key) && sni.EndsWith(key, StringComparison.OrdinalIgnoreCase)) return kvp.Value;
                                    }
                                }

                                return certificates.Last().Value;
                            };
                        });
                    });

                    serverOptions.ConfigureHttpsDefaults(config =>
                    {
                        switch (tlsProtocolPolicy)
                        {
                            case TlsProtocolPolicy.Default:
                                config.SslProtocols =
#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable SYSLIB0039 // Type or member is obsolete
                                    System.Security.Authentication.SslProtocols.Tls |
                                    System.Security.Authentication.SslProtocols.Tls11 |
#pragma warning restore SYSLIB0039 // Type or member is obsolete
#pragma warning restore CS0618 // Type or member is obsolete
                                    System.Security.Authentication.SslProtocols.Tls12 |
                                    System.Security.Authentication.SslProtocols.Tls13;
                                break;

                            case TlsProtocolPolicy.Modern:
                                config.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
                                break;

                            case TlsProtocolPolicy.Strict:
                                config.SslProtocols = System.Security.Authentication.SslProtocols.Tls13;
                                break;

                            case TlsProtocolPolicy.Legacy:
#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable SYSLIB0039 // Type or member is obsolete
                                config.SslProtocols =
                                    System.Security.Authentication.SslProtocols.Ssl2 |
                                    System.Security.Authentication.SslProtocols.Ssl3 |
                                    System.Security.Authentication.SslProtocols.Tls |
                                    System.Security.Authentication.SslProtocols.Tls11 |
                                    System.Security.Authentication.SslProtocols.Tls12 |
                                    System.Security.Authentication.SslProtocols.Tls13;
#pragma warning restore SYSLIB0039 // Type or member is obsolete
#pragma warning restore CS0618 // Type or member is obsolete
                                break;

                            default:
                                throw new ArgumentOutOfRangeException(nameof(tlsProtocolPolicy), tlsProtocolPolicy, "Unsupported TLS protocol policy.");
                        }
                    });
                }
            });

            static void ValidatePort(int port, string paramName)
            {
                if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(paramName, port, "Port must be in range 1..65535.");
            }
        }
    }
}
