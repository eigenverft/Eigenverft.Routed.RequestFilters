using System;
using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.Routed.RequestFilters.GenericExtensions.IApplicationBuilderExtensions
{
    /// <summary>
    /// Provides helpers for idempotent middleware registration on <see cref="IApplicationBuilder"/>.
    /// </summary>
    public static partial class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Registers the specified middleware type in the pipeline if it has not been added previously.
        /// </summary>
        /// <remarks>
        /// The method uses <see cref="IApplicationBuilder.Properties"/> with a type-based key to track
        /// whether the middleware has already been registered.
        ///
        /// <para>Example:</para>
        /// <code>
        /// app.UseMiddlewareOnce&lt;RemoteIpAddressContextMiddleware&gt;();
        /// app.UseMiddlewareOnce&lt;HttpProtocolFilteringMiddleware&gt;();
        /// </code>
        /// </remarks>
        /// <typeparam name="TMiddleware">The middleware type to register.</typeparam>
        /// <param name="app">The application builder.</param>
        /// <returns>The updated application builder.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="app"/> is <see langword="null"/>.
        /// </exception>
        public static IApplicationBuilder UseMiddlewareOnce<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TMiddleware>(this IApplicationBuilder app) where TMiddleware : class
        {
            ArgumentNullException.ThrowIfNull(app);

            var typeName = typeof(TMiddleware).FullName ?? typeof(TMiddleware).Name;
            var key = "__middleware_once_" + typeName;

            if (app.Properties.TryGetValue(key, out var raw) && raw is bool alreadyRegistered && alreadyRegistered)
            {
                return app;
            }

            app.Properties[key] = true;

            // IMPORTANT for AOT/trimming: use the generic overload so the linker keeps
            // the public constructor and Invoke/InvokeAsync methods on TMiddleware.
            return app.UseMiddleware<TMiddleware>();
        }
    }
}