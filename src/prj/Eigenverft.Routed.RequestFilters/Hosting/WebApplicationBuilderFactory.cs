using System;
using System.Collections.Generic;
using System.Linq;

using Eigenverft.Routed.RequestFilters.Utilities.Storage.AppDirectoryLayout;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.Routed.RequestFilters.Hosting
{
    /// <summary>
    /// Creates a <see cref="WebApplicationBuilder"/> and applies project-wide defaults.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the host builder.</param>
    /// <returns>A configured <see cref="WebApplicationBuilder"/>.</returns>
    /// <example>
    /// <code>
    /// var builder = WebApplicationBuilderFactory.CreateWithDefaults(args);
    /// var app = builder.Build();
    /// app.Run();
    /// </code>
    /// </example>
    public static class WebApplicationBuilderFactory
    {
        /// <summary>
        /// Creates a <see cref="WebApplicationBuilder"/> and fixes an <see cref="AppDirectoryLayout"/> onto it.
        /// </summary>
        /// <param name="args">Command-line arguments passed to the host builder.</param>
        /// <param name="directoryMap">Semantic key to relative path mapping under the resolved root.</param>
        /// <returns>A configured <see cref="WebApplicationBuilder"/>.</returns>
        public static WebApplicationBuilder CreateWithDefaults(IReadOnlyDictionary<string, string>? directoryMap = null, bool addArgs = false)
        {
            directoryMap ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Logs"] = "Logs",
                ["Data"] = "Data",
                ["Certs"] = "Certs",
                ["Settings"] = "Settings",
                ["Web"] = "wwwroot", // Convention: leaf folder "wwwroot" => WebRootPath
            };

            // Environment.GetCommandLineArgs includes the executable path at index 0; ASP.NET expects args without it.
            var args = addArgs ? Environment.GetCommandLineArgs().Skip(1).ToArray() : Array.Empty<string>();

            var layout = AppDirectoryLayoutResolver.Resolve(directoryMap);

            WebApplicationBuilder builder;
            if (layout.TryGetWebRoot(out var webRoot))
            {
                builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    Args = args,
                    WebRootPath = webRoot,
                });
            }
            else
            {
                builder = WebApplication.CreateBuilder(args);
            }

            // Fix layout on builder (pre-Build access) and also provide it via DI (post-Build access).
            builder.SetDirectoryLayout(layout);
            builder.Services.AddSingleton(layout);

            return builder;
        }
    }

}
