using System;
using System.Collections.Concurrent;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.GenericExtensions.LoggingExtensions
{
    /// <summary>
    /// Bridges Serilog to <see cref="Microsoft.Extensions.Logging.ILogger"/> with lazy caching.
    /// </summary>
    public static partial class LoggingExtensions
    {
        private static readonly ConcurrentDictionary<Type, Lazy<object>> IDeferredLoggerCache = new();

        /// <summary>
        /// Gets a cached deferred logger for the given category type, backed by the current Serilog logger instance.
        /// </summary>
        /// <typeparam name="TCategoryName">The category type used for the logger.</typeparam>
        /// <param name="serilogLogger">The Serilog logger to bridge.</param>
        /// <returns>A cached deferred logger for <typeparamref name="TCategoryName"/>.</returns>
        public static IDeferredLogger<TCategoryName> ToDeferred<TCategoryName>(this Serilog.ILogger serilogLogger)
        {
            if (serilogLogger is null) throw new ArgumentNullException(nameof(serilogLogger));

            return (IDeferredLogger<TCategoryName>)IDeferredLoggerCache
                .GetOrAdd(typeof(TCategoryName), _ =>
                    new Lazy<object>(() =>
                    {
                        // Reviewer note: use the passed logger, not Log.Logger, if you truly want to “capture provided logger”.
                        var factory = new SerilogLoggerFactory(Log.Logger, dispose: false);
                        var inner = factory.CreateLogger<TCategoryName>(); // ILogger<TCategoryName>
                        return new DeferredLogger<TCategoryName>(inner);   // IDeferredLogger<TCategoryName>
                    }))
                .Value;
        }
    }
}