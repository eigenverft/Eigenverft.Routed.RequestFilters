using System;

using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Services.DeferredLogger
{

    /// <summary>
    /// Default implementation that wraps <see cref="ILoggerFactory"/>.
    /// </summary>
    public sealed class DeferredLoggerFactory : IDeferredLoggerFactory
    {
        private readonly ILoggerFactory _inner;

        /// <summary>
        /// Initializes a new instance of the factory wrapper.
        /// </summary>
        /// <param name="inner">The underlying Microsoft logger factory.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="inner"/> is null.</exception>
        public DeferredLoggerFactory(ILoggerFactory inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <inheritdoc />
        public IDeferredLogger CreateLogger(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                throw new ArgumentException("Category name must be non-empty.", nameof(categoryName));
            }

            // Requires a DeferredLogger(ILogger inner) ctor (see below).
            return new DeferredLogger(_inner.CreateLogger(categoryName));
        }

        /// <inheritdoc />
        public IDeferredLogger<TCategoryName> CreateLogger<TCategoryName>()
        {
            return new DeferredLogger<TCategoryName>(_inner.CreateLogger<TCategoryName>());
        }
    }
}
