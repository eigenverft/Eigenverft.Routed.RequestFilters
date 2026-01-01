using System;

using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Services.DeferredLogger
{
    /// <summary>
    /// Factory for creating <see cref="IDeferredLogger"/> instances based on the Microsoft logging infrastructure.
    /// </summary>
    public interface IDeferredLoggerFactory
    {
        /// <summary>
        /// Creates a deferred logger using the specified category name.
        /// </summary>
        /// <param name="categoryName">The log category name.</param>
        /// <returns>A deferred logger instance.</returns>
        IDeferredLogger CreateLogger(string categoryName);

        /// <summary>
        /// Creates a deferred logger using the full name of <typeparamref name="TCategoryName"/> as category.
        /// </summary>
        /// <typeparam name="TCategoryName">The category type.</typeparam>
        /// <returns>A deferred logger instance.</returns>
        IDeferredLogger<TCategoryName> CreateLogger<TCategoryName>();
    }
}
