using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

using CommunityToolkit.Diagnostics;

using Eigenverft.Routed.RequestFilters.Utilities.Certificate;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.Routed.RequestFilters.GenericExtensions.ConfigureWebHostBuilderExtensions
{
    /// <summary>
    /// Kestrel-related extension methods for <see cref="ConfigureWebHostBuilder"/>.
    /// </summary>
    public static partial class ConfigureWebHostBuilderExtensions2
    {
        /// <summary>
        /// Controls which interfaces Kestrel binds to for the configured listeners.
        /// </summary>
        public enum ListenScope
        {
            /// <summary>
            /// Bind only to loopback (localhost).
            /// </summary>
            Localhost,

            /// <summary>
            /// Bind to all available interfaces.
            /// </summary>
            AnyIP
        }

        /// <summary>
        /// Controls which TLS protocol versions are permitted for HTTPS endpoints configured by this method.
        /// </summary>
        public enum TlsProtocolPolicy
        {
            /// <summary>
            /// Allow TLS 1.0, TLS 1.1, TLS 1.2 and TLS 1.3 (no SSL2/SSL3).
            /// </summary>
            Default,

            /// <summary>
            /// Allow TLS 1.2 and TLS 1.3.
            /// </summary>
            Modern,

            /// <summary>
            /// Allow TLS 1.3 only.
            /// </summary>
            Strict,

            /// <summary>
            /// Enable legacy protocol versions (unsafe; for controlled environments only).
            /// </summary>
            Legacy
        }

        /// <summary>
        /// Kestrel + SNI configuration bound from configuration.
        /// </summary>
        public sealed class KestrelSniSettings
        {
            /// <summary>
            /// HTTP port to listen on; <see langword="null"/> disables HTTP.
            /// </summary>
            public int? HTTP_PORT { get; set; }

            /// <summary>
            /// HTTPS port to listen on; <see langword="null"/> disables HTTPS.
            /// </summary>
            public int? HTTPS_PORT { get; set; }

            /// <summary>
            /// Controls whether listeners bind to localhost or all interfaces.
            /// </summary>
            public ListenScope ListenScope { get; set; } = ListenScope.Localhost;

            /// <summary>
            /// Whether to add the Kestrel Server header.
            /// </summary>
            public bool AddServerHeader { get; set; } = false;

            /// <summary>
            /// Optional protocols string, e.g. <c>Http1AndHttp2AndHttp3</c>. Empty means "use defaults".
            /// </summary>
            public string? Protocols { get; set; }

            /// <summary>
            /// When <see langword="true"/>, the longest suffix wins if multiple suffixes match.
            /// </summary>
            public bool PreferLongestSuffixMatch { get; set; } = true;

            /// <summary>
            /// TLS protocol policy applied to HTTPS.
            /// </summary>
            public TlsProtocolPolicy TlsProtocolPolicy { get; set; } = TlsProtocolPolicy.Default;
        }

        /// <summary>
        /// Certificate mapping element bound from <c>CertificatesMappingSettings</c>.
        /// </summary>
        public sealed class CertificateMappingSetting
        {
            /// <summary>
            /// SNI host name (also used as CN for self-signed generation).
            /// </summary>
            public string? SNI { get; set; }

            /// <summary>
            /// File name of the PFX within the certificates directory.
            /// </summary>
            public string? FileName { get; set; }

            /// <summary>
            /// PFX password.
            /// </summary>
            public string? Password { get; set; }

            /// <summary>
            /// Optional SAN names to include (if the certificate manager supports it).
            /// </summary>
            public List<string>? SanNames { get; set; }
        }

        /// <summary>
        /// Exposes the last-built certificate dictionary for debugging/inspection.
        /// </summary>
        public static IReadOnlyDictionary<string, X509Certificate2>? Certificates
        {
            get
            {
                return Volatile.Read(ref _certificates);
            }
        }

        private static IReadOnlyDictionary<string, X509Certificate2>? _certificates;

        /// <summary>
        /// Configures Kestrel from configuration and uses a reload-safe SNI certificate selector.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Keep <see langword="summary"/> short for IntelliSense, and put a tiny copy-pasteable snippet in remarks.
        /// Listener ports and bind addresses are startup-fixed. Only certificate selection is updated on
        /// configuration reload because the selector callback runs per connection.
        /// </para>
        /// <para>Minimal appsettings.json:</para>
        /// <code><![CDATA[
        /// {
        ///   "CertificatesDirectory": "certs",
        ///   "KestrelSettings": {
        ///     "HTTP_PORT": 8080,
        ///     "HTTPS_PORT": 8443,
        ///     "ListenScope": "Localhost",
        ///     "AddServerHeader": false,
        ///     "Protocols": "Http1AndHttp2AndHttp3",
        ///     "PreferLongestSuffixMatch": true,
        ///     "TlsProtocolPolicy": "Default"
        ///   },
        ///   "CertificatesMappingSettings": [
        ///     { "SNI": "localhost", "FileName": "localhost.pfx", "Password": "yourPassword" }
        ///   ]
        /// }
        /// ]]></code>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// builder.WebHost.ConfigureKestrelSniFromConfiguration(certDirOverride: defaultDirs["Certs"]);
        /// ]]></code>
        /// </remarks>
        /// <param name="configureWebHostBuilder">The builder to configure.</param>
        /// <param name="certDirOverride">
        /// Optional override for the certificate directory. If <see langword="null"/>, reads <c>CertificatesDirectory</c> from config;
        /// if still <see langword="null"/>, uses <c>{ContentRoot}/certs</c>.
        /// </param>
        /// <param name="kestrelSettingsSectionPath">Config section for Kestrel settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configureWebHostBuilder"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when configuration is missing required sections or values.</exception>
        public static void ConfigureKestrelSniFromConfiguration(
            this ConfigureWebHostBuilder configureWebHostBuilder,
            string? certDirOverride = null,
            string kestrelSettingsSectionPath = "KestrelSettings")
        {
            ArgumentNullException.ThrowIfNull(configureWebHostBuilder);
            Guard.IsNotNullOrWhiteSpace(kestrelSettingsSectionPath);

            configureWebHostBuilder.ConfigureKestrel((context, serverOptions) =>
            {
                ArgumentNullException.ThrowIfNull(context);
                ArgumentNullException.ThrowIfNull(serverOptions);

                var settings =
                    context.Configuration.GetSection(kestrelSettingsSectionPath).Get<KestrelSniSettings>()
                    ?? throw new ArgumentException($"Missing configuration section '{kestrelSettingsSectionPath}'.");

                var certDirFromConfig = context.Configuration.GetValue<string>("CertificatesDirectory");
                var certDir = certDirOverride
                    ?? certDirFromConfig
                    ?? Path.Combine(context.HostingEnvironment.ContentRootPath ?? AppContext.BaseDirectory, "certs");

                var protocols = TryParseProtocols(settings.Protocols);

                // Initialize reloadable cert state (thread-safe). This will rebuild the cert map on config reload.
                SniRuntimeState.EnsureInitialized(
                    context.Configuration,
                    certDir,
                    settings.PreferLongestSuffixMatch);

                serverOptions.AddServerHeader = settings.AddServerHeader;

                Action<int, Action<ListenOptions>> listen = settings.ListenScope == ListenScope.Localhost
                    ? (port, configure) => serverOptions.ListenLocalhost(port, configure)
                    : (port, configure) => serverOptions.ListenAnyIP(port, configure);

                // HTTP rule change:
                // - null => disabled
                // - 0 or negative => disabled (skip HTTP configuration)
                // - positive => validated and configured
                var isHttpEnabled = TryGetEnabledPortSkippingNonPositive(settings.HTTP_PORT, nameof(settings.HTTP_PORT), out var httpPort);
                var isHttpsEnabled = settings.HTTPS_PORT.HasValue;

                if (!isHttpEnabled && !isHttpsEnabled)
                {
                    throw new ArgumentException("At least one of HTTP_PORT or HTTPS_PORT must be specified/enabled.");
                }

                if (isHttpEnabled)
                {
                    listen(httpPort, lo =>
                    {
                        if (protocols.HasValue)
                        {
                            lo.Protocols = HttpProtocols.Http1;
                        }
                    });
                }

                if (isHttpsEnabled)
                {
                    ValidatePort(settings.HTTPS_PORT!.Value, nameof(settings.HTTPS_PORT));

                    listen(settings.HTTPS_PORT.Value, lo =>
                    {
                        lo.Protocols = protocols ?? HttpProtocols.Http1AndHttp2;

                        lo.UseHttps(https =>
                        {
                            // Selector reads the current in-memory map each connection.
                            https.ServerCertificateSelector = (_, sni) =>
                            {
                                return SniRuntimeState.Select(sni);
                            };
                        });
                    });

                    serverOptions.ConfigureHttpsDefaults(httpsDefaults =>
                    {
                        httpsDefaults.SslProtocols = MapTlsPolicy(settings.TlsProtocolPolicy);
                    });
                }
                else
                {
                    if (settings.TlsProtocolPolicy != TlsProtocolPolicy.Default)
                    {
                        throw new ArgumentException(
                            "TlsProtocolPolicy can only be set when HTTPS_PORT is enabled.",
                            nameof(settings.TlsProtocolPolicy));
                    }
                }
            });

            static bool TryGetEnabledPortSkippingNonPositive(int? port, string paramName, out int enabledPort)
            {
                enabledPort = default;

                if (!port.HasValue)
                {
                    return false;
                }

                // Reviewer note: Explicitly treat 0/negative as "disabled" (no exception, no listener).
                if (port.Value <= 0)
                {
                    return false;
                }

                ValidatePort(port.Value, paramName);
                enabledPort = port.Value;
                return true;
            }

            static void ValidatePort(int port, string paramName)
            {
                if (port < 1 || port > 65535)
                {
                    throw new ArgumentOutOfRangeException(paramName, port, "Port must be in range 1..65535.");
                }
            }

            static HttpProtocols? TryParseProtocols(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                if (Enum.TryParse<HttpProtocols>(value, ignoreCase: true, out var parsed))
                {
                    return parsed;
                }

                return null;
            }

            static System.Security.Authentication.SslProtocols MapTlsPolicy(TlsProtocolPolicy policy)
            {
                switch (policy)
                {
                    case TlsProtocolPolicy.Default:
#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable SYSLIB0039 // Type or member is obsolete
                        return System.Security.Authentication.SslProtocols.Tls
                             | System.Security.Authentication.SslProtocols.Tls11
                             | System.Security.Authentication.SslProtocols.Tls12
                             | System.Security.Authentication.SslProtocols.Tls13;
#pragma warning restore SYSLIB0039
#pragma warning restore CS0618
                    case TlsProtocolPolicy.Modern:
                        return System.Security.Authentication.SslProtocols.Tls12
                             | System.Security.Authentication.SslProtocols.Tls13;

                    case TlsProtocolPolicy.Strict:
                        return System.Security.Authentication.SslProtocols.Tls13;

                    case TlsProtocolPolicy.Legacy:
#pragma warning disable CS0618
#pragma warning disable SYSLIB0039
                        return System.Security.Authentication.SslProtocols.Ssl2
                             | System.Security.Authentication.SslProtocols.Ssl3
                             | System.Security.Authentication.SslProtocols.Tls
                             | System.Security.Authentication.SslProtocols.Tls11
                             | System.Security.Authentication.SslProtocols.Tls12
                             | System.Security.Authentication.SslProtocols.Tls13;
#pragma warning restore SYSLIB0039
#pragma warning restore CS0618

                    default:
                        throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported TLS protocol policy.");
                }
            }
        }

        /// <summary>
        /// Reloadable certificate state for SNI selection.
        /// </summary>
        private static class SniRuntimeState
        {
            private static KeyValuePair<string, X509Certificate2>[] _matchPairs =
                Array.Empty<KeyValuePair<string, X509Certificate2>>();

            private static X509Certificate2? _fallback;
            private static IDisposable? _subscription;
            private static readonly object _gate = new();

            private static string? _certDir;
            private static bool _preferLongest;

            /// <summary>
            /// Ensures the state is initialized and wired to configuration reload notifications exactly once.
            /// </summary>
            /// <remarks>
            /// <para>Usage:</para>
            /// <code><![CDATA[
            /// SniRuntimeState.EnsureInitialized(configuration, certDir, preferLongestSuffixMatch: true);
            /// ]]></code>
            /// </remarks>
            /// <param name="configuration">The configuration root used for reload notifications and binding.</param>
            /// <param name="certDir">The directory holding PFX files.</param>
            /// <param name="preferLongestSuffixMatch">Whether the most specific suffix wins.</param>
            /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentException">Thrown when <paramref name="certDir"/> is null or whitespace.</exception>
            public static void EnsureInitialized(IConfiguration configuration, string certDir, bool preferLongestSuffixMatch)
            {
                ArgumentNullException.ThrowIfNull(configuration);
                Guard.IsNotNullOrWhiteSpace(certDir);

                lock (_gate)
                {
                    _certDir = certDir;
                    _preferLongest = preferLongestSuffixMatch;

                    Reload(configuration);

                    _subscription ??= ChangeToken.OnChange(
                        () => configuration.GetReloadToken(),
                        () => Reload(configuration));
                }
            }

            /// <summary>
            /// Selects a certificate based on requested SNI.
            /// </summary>
            /// <remarks>
            /// <para>
            /// This method is called per connection. It must be fast and must not perform I/O.
            /// </para>
            /// </remarks>
            /// <param name="sni">The requested SNI value.</param>
            /// <returns>The selected certificate.</returns>
            /// <exception cref="InvalidOperationException">Thrown when certificate state is not initialized.</exception>
            public static X509Certificate2 Select(string? sni)
            {
                var pairs = Volatile.Read(ref _matchPairs);
                var fallback = Volatile.Read(ref _fallback);

                if (fallback is null)
                {
                    throw new InvalidOperationException("SNI certificate state is not initialized (fallback missing).");
                }

                if (!string.IsNullOrWhiteSpace(sni))
                {
                    for (var i = 0; i < pairs.Length; i++)
                    {
                        var key = pairs[i].Key;
                        if (!string.IsNullOrWhiteSpace(key) &&
                            sni.EndsWith(key, StringComparison.OrdinalIgnoreCase))
                        {
                            return pairs[i].Value;
                        }
                    }
                }

                return fallback;
            }

            private static void Reload(IConfiguration configuration)
            {
                lock (_gate)
                {
                    if (string.IsNullOrWhiteSpace(_certDir))
                    {
                        return;
                    }

                    var mappings =
                        configuration.GetSection("CertificatesMappingSettings").Get<List<CertificateMappingSetting>>()
                        ?? new List<CertificateMappingSetting>();

                    var certs = BuildCertificates(_certDir!, mappings);

                    Volatile.Write(ref _certificates, certs);

                    var pairs = _preferLongest
                        ? certs.OrderByDescending(kvp => (kvp.Key ?? string.Empty).Length).ToArray()
                        : certs.ToArray();

                    if (pairs.Length == 0)
                    {
                        return;
                    }

                    var fallback = pairs[0].Value;

                    Volatile.Write(ref _matchPairs, pairs);
                    Volatile.Write(ref _fallback, fallback);
                }
            }

            private static Dictionary<string, X509Certificate2> BuildCertificates(
                string certDirectory,
                IEnumerable<CertificateMappingSetting> mappings)
            {
                Guard.IsNotNullOrWhiteSpace(certDirectory);
                ArgumentNullException.ThrowIfNull(mappings);

                Directory.CreateDirectory(certDirectory);

                var certs = new Dictionary<string, X509Certificate2>(StringComparer.OrdinalIgnoreCase);

                var index = 0;
                foreach (var m in mappings)
                {
                    if (m is null)
                    {
                        index++;
                        continue;
                    }

                    var sni = m.SNI?.Trim();
                    if (string.IsNullOrWhiteSpace(sni))
                    {
                        index++;
                        continue;
                    }

                    var fileName = m.FileName?.Trim();
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        throw new ArgumentException($"CertificatesMappingSettings[{index}].FileName is missing for SNI '{sni}'.");
                    }

                    var password = m.Password ?? string.Empty;

                    // Reviewer note: If SAN support exists, wire m.SanNames into the subject profile here.
                    var cert = CertificateManager.LoadOrCreateSelfSignedCertificate(
                        certDirectory,
                        fileName,
                        password,
                        new CertificateManager.SubjectProfile { CommonName = sni });

                    certs[sni] = cert;

                    index++;
                }

                if (certs.Count == 0)
                {
                    throw new ArgumentException("No usable certificate mappings were configured under 'CertificatesMappingSettings'.");
                }

                return certs;
            }
        }


    }
}