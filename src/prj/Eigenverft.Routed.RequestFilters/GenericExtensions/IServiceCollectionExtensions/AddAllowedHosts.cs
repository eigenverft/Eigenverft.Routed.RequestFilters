using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.Routed.RequestFilters.GenericExtensions.IServiceCollectionExtensions
{
    /// <summary>
    /// Provides <see cref="IServiceCollection"/> extensions for host filtering configuration.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Configures <see cref="HostFilteringOptions"/> from <see cref="IConfiguration"/> resolved from DI.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Use this when configuration sources were cleared and rebuilt manually, and you want a single call site.
        /// This overload supports both the single-string format (optionally separator-delimited) and the array format.
        /// </para>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// builder.Services.AddAllowedHosts();
        /// ]]></code>
        /// </remarks>
        /// <param name="services">The service collection to configure.</param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
        public static IServiceCollection AddAllowedHosts(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddOptions();

            services.AddOptions<HostFilteringOptions>()
                .Configure<IConfiguration>((options, configuration) =>
                {
                    const string configurationKey = "AllowedHosts";

                    char[] separators = new[] { ';', ',', '\n', '\r' };
                    bool defaultToWildcard = true;

                    IConfigurationSection section = configuration.GetSection(configurationKey);

                    // 1) Prefer array-style configuration: "AllowedHosts": [ "a", "b" ]
                    string[] fromChildren = section
                        .GetChildren()
                        .Select(static c => c.Value)
                        .Where(static v => !string.IsNullOrWhiteSpace(v))
                        .Select(static v => v!.Trim())
                        .Where(static v => v.Length != 0)
                        .ToArray();

                    string[] configuredHosts;

                    if (fromChildren.Length != 0)
                    {
                        configuredHosts = fromChildren;
                    }
                    else
                    {
                        // 2) Fallback to a single string: "AllowedHosts": "a;b;c"
                        string raw = section.Value ?? configuration[configurationKey] ?? string.Empty;

                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            configuredHosts = raw
                                .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Where(static h => h.Length != 0)
                                .ToArray();
                        }
                        else
                        {
                            configuredHosts = Array.Empty<string>();
                        }
                    }

                    if (configuredHosts.Length == 0 && defaultToWildcard)
                    {
                        configuredHosts = new[] { "*" };
                    }

                    configuredHosts = configuredHosts
                        .Where(static h => !string.IsNullOrWhiteSpace(h))
                        .Select(static h => h.Trim())
                        .Where(static h => h.Length != 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    if (configuredHosts.Length != 0)
                    {
                        options.AllowedHosts = configuredHosts;
                    }
                });

            return services;
        }

        /// <summary>
        /// Configures <see cref="HostFilteringOptions"/> with the provided allowed hosts list.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Use this to keep the registration concise and consistent across projects. The provided host values should be host
        /// names only (no ports). A wildcard entry such as <c>*</c> is supported by the host filtering middleware.
        /// </para>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// builder.Services.AddAllowedHosts(new[] { "*" });
        /// ]]></code>
        /// </remarks>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="allowedHosts">Allowed host names for host filtering.</param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="allowedHosts"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="allowedHosts"/> contains no usable entries.</exception>
        public static IServiceCollection AddAllowedHosts(this IServiceCollection services, IEnumerable<string> allowedHosts)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(allowedHosts);

            string[] allowedHostsArray = allowedHosts
                .Where(static h => !string.IsNullOrWhiteSpace(h))
                .Select(static h => h.Trim())
                .Where(static h => h.Length != 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (allowedHostsArray.Length == 0)
            {
                throw new ArgumentException("At least one allowed host is required.", nameof(allowedHosts));
            }

            services.Configure<HostFilteringOptions>(options =>
            {
                options.AllowedHosts = allowedHostsArray;
            });

            return services;
        }

        /// <summary>
        /// Configures <see cref="HostFilteringOptions"/> with a separator-delimited allowed hosts string.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This overload is handy when your configuration stores allowed hosts as a single string
        /// (for example: <c>"example.com; localhost; *.contoso.com;*"</c>).
        /// </para>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// builder.Services.AddAllowedHosts("example.com;localhost;*.contoso.com;*");
        /// ]]></code>
        /// </remarks>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="allowedHosts">Allowed host names for host filtering as a single string.</param>
        /// <param name="separators">Delimiters used to split <paramref name="allowedHosts"/>.</param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="allowedHosts"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the resulting allowed host list is empty.</exception>
        public static IServiceCollection AddAllowedHosts(this IServiceCollection services, string allowedHosts, params char[] separators)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(allowedHosts);

            if (separators == null || separators.Length == 0)
            {
                separators = new[] { ';', ',', '\n', '\r' };
            }

            string[] parts = allowedHosts
                .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return services.AddAllowedHosts(parts);
        }

        /// <summary>
        /// Configures <see cref="HostFilteringOptions"/> from application configuration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Reads the configured allowed hosts (default key <c>AllowedHosts</c>). Supports either a single string value
        /// or an array section. When missing/empty, defaults to <c>*</c> to keep behavior deterministic when sources were cleared.
        /// </para>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// builder.Services.AddAllowedHosts(builder.Configuration);
        /// ]]></code>
        /// </remarks>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="configuration">The application configuration (for example <c>builder.Configuration</c>).</param>
        /// <param name="configurationKey">The configuration key or path to read. Defaults to <c>AllowedHosts</c>.</param>
        /// <param name="separators">Delimiters used when the configured value is a single string.</param>
        /// <param name="defaultToWildcard">
        /// When <see langword="true"/>, uses <c>*</c> if the configuration value is missing or empty.
        /// </param>
        /// <param name="manualConfigure">Optional delegate to modify or augment options (applied after parsing).</param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="configurationKey"/> is null or whitespace.</exception>
        public static IServiceCollection AddAllowedHosts(
            this IServiceCollection services,
            IConfiguration configuration,
            string configurationKey = "AllowedHosts",
            char[]? separators = null,
            bool defaultToWildcard = true,
            Action<HostFilteringOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            if (string.IsNullOrWhiteSpace(configurationKey))
            {
                throw new ArgumentException("A configuration key/path is required.", nameof(configurationKey));
            }

            if (separators == null || separators.Length == 0)
            {
                separators = new[] { ';', ',', '\n', '\r' };
            }

            IConfigurationSection section = configuration.GetSection(configurationKey);

            // 1) Prefer array-style configuration: "AllowedHosts": [ "a", "b" ]
            string[] fromChildren = section
                .GetChildren()
                .Select(static c => c.Value)
                .Where(static v => !string.IsNullOrWhiteSpace(v))
                .Select(static v => v!.Trim())
                .Where(static v => v.Length != 0)
                .ToArray();

            string[] configuredHosts;

            if (fromChildren.Length != 0)
            {
                configuredHosts = fromChildren;
            }
            else
            {
                // 2) Fallback to a single string: "AllowedHosts": "a;b;c"
                string raw = section.Value ?? configuration[configurationKey] ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(raw))
                {
                    configuredHosts = raw
                        .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(static h => h.Length != 0)
                        .ToArray();
                }
                else
                {
                    configuredHosts = Array.Empty<string>();
                }
            }

            if (configuredHosts.Length == 0 && defaultToWildcard)
            {
                configuredHosts = new[] { "*" };
            }

            if (configuredHosts.Length != 0)
            {
                services.AddAllowedHosts(configuredHosts);
            }

            if (manualConfigure != null)
            {
                services.PostConfigure(manualConfigure);
            }

            return services;
        }
    }
}
