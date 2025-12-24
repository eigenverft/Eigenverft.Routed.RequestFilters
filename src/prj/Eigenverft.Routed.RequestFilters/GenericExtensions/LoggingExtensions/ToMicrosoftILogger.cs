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
        private static readonly ConcurrentDictionary<Type, Lazy<Microsoft.Extensions.Logging.ILogger>> ILoggerCache = new();

        /// <summary>
        /// Gets a cached Microsoft logger for the given category type, backed by the current Serilog logger instance.
        /// </summary>
        /// <typeparam name="TCategoryName">The category type used for the logger.</typeparam>
        /// <param name="serilogLogger">The Serilog logger to bridge.</param>
        /// <returns>A cached <see cref="ILogger"/> for <typeparamref name="TCategoryName"/>.</returns>
        /// <remarks>
        /// The first call per category type captures the provided <paramref name="serilogLogger"/>.
        /// Call this after your Serilog configuration has assigned <see cref="Log.Logger"/>.
        /// </remarks>
        public static Microsoft.Extensions.Logging.ILogger ToMicrosoftILogger<TCategoryName>(this Serilog.ILogger serilogLogger)
        {
            if (serilogLogger is null) throw new ArgumentNullException(nameof(serilogLogger));

            return ILoggerCache.GetOrAdd(typeof(TCategoryName), static _ => new Lazy<Microsoft.Extensions.Logging.ILogger>(() => new SerilogLoggerFactory(Log.Logger, dispose: false).CreateLogger<TCategoryName>())).Value;
        }
    }
}