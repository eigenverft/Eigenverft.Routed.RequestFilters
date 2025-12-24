using System;
using System.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Eigenverft.Routed.RequestFilters.GenericExtensions.WebApplicationBuilderExtensions
{
    /// <summary>
    /// Extension methods for <see cref="WebApplicationBuilder"/> configuration setup.
    /// </summary>
    public static partial class WebApplicationBuilderExtensions
    {
        /// <summary>
        /// Clears all configuration sources and adds a minimal default set (environment variables, optionally command line).
        /// </summary>
        /// <param name="builder">The builder to modify.</param>
        /// <param name="useArgs">
        /// When <c>true</c>, adds command-line configuration using args from <see cref="Environment.GetCommandLineArgs"/>
        /// (excluding index 0).
        /// </param>
        /// <param name="addEnvironmentVariables">
        /// When <c>true</c>, adds environment variables via
        /// <see cref="ConfigurationBuilderExtensions.AddEnvironmentVariables(IConfigurationBuilder)"/>.
        /// </param>
        /// <returns>The same <see cref="WebApplicationBuilder"/> instance for chaining.</returns>
        /// <remarks>
        /// This method clears the underlying <see cref="IConfigurationBuilder.Sources"/> collection.
        /// Call it early, before adding other providers.
        /// </remarks>
        /// <example>
        /// <code>
        /// var builder = WebApplication.CreateBuilder();
        /// builder.AddDefaultConfigurationSources(useArgs: true);
        /// </code>
        /// </example>
        public static WebApplicationBuilder AddDefaultConfigurationSources(this WebApplicationBuilder builder, bool useArgs = false, bool addEnvironmentVariables = true)
        {
            ArgumentNullException.ThrowIfNull(builder);

            ((IConfigurationBuilder)builder.Configuration).Sources.Clear();

            if (addEnvironmentVariables)
            {
                builder.Configuration.AddEnvironmentVariables();
            }

            if (useArgs)
            {
                var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
                builder.Configuration.AddCommandLine(args);
            }

            return builder;
        }
    }
}
