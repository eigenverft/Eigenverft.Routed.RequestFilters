using System;
using System.Linq;
using System.Reflection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Utilities.Logging.BootstrapLogger
{
    /// <summary>
    /// Provides a refactor-friendly pre-host logger for early startup logging.
    /// </summary>
    /// <typeparam name="TCategoryName">The category type used to name the logger.</typeparam>
    /// <remarks>
    /// Intended for logging before the host is built, when DI resolution of <see cref="ILoggerFactory"/> is not available.
    ///
    /// Resolution strategy:
    /// - If Serilog and the Serilog-to-MEL bridge are available, a Serilog-backed <see cref="ILoggerFactory"/> is used.
    /// - Otherwise, a minimal Microsoft logging factory is created (Console and Debug if available),
    ///   optionally applying the "Logging" configuration section when possible.
    /// </remarks>
    public static class BootstrapLogger<TCategoryName>
    {
        /// <summary>
        /// Creates a pre-host <see cref="ILogger{TCategoryName}"/> using <typeparamref name="TCategoryName"/> as category.
        /// </summary>
        /// <param name="configuration">
        /// Optional configuration used to apply the "Logging" section (levels and filters) if the required
        /// logging configuration package is available.
        /// </param>
        /// <returns>A logger instance that can be used prior to building the host.</returns>
        /// <example>
        /// <code>
        /// ILogger startupLogger = BootstrapLogger&lt;Program&gt;.CreateLogger(builder.Configuration);
        /// </code>
        /// </example>
        public static ILogger<TCategoryName> CreateLogger(IConfiguration? configuration = null)
        {
            var factory = BootstrapLoggerFactoryCache.GetOrCreate(configuration);
            return factory.CreateLogger<TCategoryName>();
        }

        /// <summary>
        /// Creates (or returns the cached) <see cref="ILoggerFactory"/> suitable for pre-host usage.
        /// </summary>
        /// <param name="configuration">
        /// Optional configuration used to apply the "Logging" section (levels and filters) if available.
        /// </param>
        /// <returns>The cached <see cref="ILoggerFactory"/>.</returns>
        public static ILoggerFactory CreateLoggerFactory(IConfiguration? configuration = null)
        {
            return BootstrapLoggerFactoryCache.GetOrCreate(configuration);
        }

        /// <summary>
        /// Clears the cached factory. Intended for tests.
        /// </summary>
        public static void ResetForTests()
        {
            BootstrapLoggerFactoryCache.ResetForTests();
        }
    }

    /// <summary>
    /// Provides non-generic convenience APIs for creating a pre-host logger using an explicit category name.
    /// </summary>
    /// <remarks>
    /// This is a convenience facade over the same cached factory used by <see cref="BootstrapLogger{TCategoryName}"/>.
    /// </remarks>
    public static class BootstrapLogger
    {
        /// <summary>
        /// Creates a pre-host <see cref="ILogger"/> for the given category name.
        /// </summary>
        /// <param name="categoryName">The logger category name.</param>
        /// <param name="configuration">
        /// Optional configuration used to apply the "Logging" section (levels and filters) if the required
        /// logging configuration package is available.
        /// </param>
        /// <returns>A logger instance that can be used prior to building the host.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="categoryName"/> is null.</exception>
        /// <example>
        /// <code>
        /// ILogger startupLogger = BootstrapLogger.CreateLogger("Startup", builder.Configuration);
        /// </code>
        /// </example>
        public static ILogger CreateLogger(string categoryName, IConfiguration? configuration = null)
        {
            if (categoryName is null)
            {
                throw new ArgumentNullException(nameof(categoryName));
            }

            var factory = BootstrapLoggerFactoryCache.GetOrCreate(configuration);
            return factory.CreateLogger(categoryName);
        }

        /// <summary>
        /// Creates (or returns the cached) <see cref="ILoggerFactory"/> suitable for pre-host usage.
        /// </summary>
        /// <param name="configuration">
        /// Optional configuration used to apply the "Logging" section (levels and filters) if available.
        /// </param>
        /// <returns>The cached <see cref="ILoggerFactory"/>.</returns>
        public static ILoggerFactory CreateLoggerFactory(IConfiguration? configuration = null)
        {
            return BootstrapLoggerFactoryCache.GetOrCreate(configuration);
        }

        /// <summary>
        /// Clears the cached factory. Intended for tests.
        /// </summary>
        public static void ResetForTests()
        {
            BootstrapLoggerFactoryCache.ResetForTests();
        }
    }

    /// <summary>
    /// Centralized cache and creation logic for the pre-host <see cref="ILoggerFactory"/>.
    /// </summary>
    /// <remarks>
    /// Kept internal to ensure there is exactly one cached factory per process, regardless of how many generic
    /// categories are used.
    /// </remarks>
    internal static class BootstrapLoggerFactoryCache
    {
        private static readonly object Gate = new();
        private static ILoggerFactory? _cachedFactory;

        /// <summary>
        /// Returns a cached factory or creates one if absent.
        /// </summary>
        /// <param name="configuration">
        /// Optional configuration used only when creating the factory for the first time.
        /// Subsequent calls ignore this parameter if the factory is already cached.
        /// </param>
        /// <returns>An <see cref="ILoggerFactory"/>.</returns>
        public static ILoggerFactory GetOrCreate(IConfiguration? configuration)
        {
            lock (Gate)
            {
                if (_cachedFactory is not null)
                {
                    return _cachedFactory;
                }

                _cachedFactory =
                    TryCreateSerilogBackedFactory()
                    ?? CreateMicrosoftFallbackFactory(configuration);

                return _cachedFactory;
            }
        }

        /// <summary>
        /// Clears the cached factory. Intended for tests.
        /// </summary>
        public static void ResetForTests()
        {
            lock (Gate)
            {
                _cachedFactory = null;
            }
        }

        private static ILoggerFactory CreateMicrosoftFallbackFactory(IConfiguration? configuration)
        {
            return LoggerFactory.Create(logging =>
            {
                // Optional: apply IConfiguration "Logging" section if the extension is available.
                if (configuration is not null)
                {
                    var loggingSection = configuration.GetSection("Logging");
                    TryInvokeLoggingBuilderExtension(
                        logging,
                        assemblyName: "Microsoft.Extensions.Logging.Configuration",
                        typeName: "Microsoft.Extensions.Logging.Configuration.LoggingBuilderConfigurationExtensions",
                        methodName: "AddConfiguration",
                        args: new object?[] { loggingSection });
                }

                // Optional: add Console if the package is referenced.
                TryInvokeLoggingBuilderExtension(
                    logging,
                    assemblyName: "Microsoft.Extensions.Logging.Console",
                    typeName: "Microsoft.Extensions.Logging.ConsoleLoggerExtensions",
                    methodName: "AddConsole",
                    args: Array.Empty<object?>());
            });
        }

        private static ILoggerFactory? TryCreateSerilogBackedFactory()
        {
            // Goal: Produce Microsoft ILoggerFactory backed by Serilog without compile-time dependency on Serilog.
            // This requires:
            // - Serilog assembly: Serilog.Log and Serilog.ILogger
            // - Bridge assembly: Serilog.Extensions.Logging.SerilogLoggerFactory

            var serilogLogType = TryLoadType("Serilog", "Serilog.Log");
            var serilogILoggerType = TryLoadType("Serilog", "Serilog.ILogger");
            if (serilogLogType is null || serilogILoggerType is null)
            {
                return null;
            }

            var loggerProperty = serilogLogType.GetProperty("Logger", BindingFlags.Public | BindingFlags.Static);
            if (loggerProperty is null)
            {
                return null;
            }

            var serilogLogger = loggerProperty.GetValue(null);
            if (serilogLogger is null)
            {
                return null;
            }

            var serilogLoggerFactoryType =
                TryLoadType("Serilog.Extensions.Logging", "Serilog.Extensions.Logging.SerilogLoggerFactory");
            if (serilogLoggerFactoryType is null)
            {
                return null;
            }

            var providerCollectionType =
                TryLoadType("Serilog.Extensions.Logging", "Serilog.Extensions.Logging.LoggerProviderCollection");

            var ctors = serilogLoggerFactoryType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            // Prefer "most specific" first (3 -> 2 -> 1 -> 0)
            foreach (var ctor in ctors.OrderByDescending(c => c.GetParameters().Length))
            {
                var parameters = ctor.GetParameters();

                try
                {
                    if (parameters.Length == 3 &&
                        parameters[0].ParameterType.IsInstanceOfType(serilogLogger) &&
                        parameters[1].ParameterType == typeof(bool) &&
                        IsLoggerProviderCollectionParameter(parameters[2].ParameterType, providerCollectionType))
                    {
                        // Do not dispose Serilog's global logger from this bootstrap factory.
                        return (ILoggerFactory)ctor.Invoke(new object?[] { serilogLogger, false, null });
                    }

                    if (parameters.Length == 2 &&
                        parameters[0].ParameterType.IsInstanceOfType(serilogLogger) &&
                        parameters[1].ParameterType == typeof(bool))
                    {
                        // Do not dispose Serilog's global logger from this bootstrap factory.
                        return (ILoggerFactory)ctor.Invoke(new object?[] { serilogLogger, false });
                    }

                    if (parameters.Length == 1 &&
                        parameters[0].ParameterType.IsInstanceOfType(serilogLogger))
                    {
                        return (ILoggerFactory)ctor.Invoke(new object?[] { serilogLogger });
                    }

                    if (parameters.Length == 0)
                    {
                        // Some versions allow a parameterless ctor (uses Serilog.Log.Logger internally).
                        return (ILoggerFactory)ctor.Invoke(Array.Empty<object?>());
                    }
                }
                catch
                {
                    // Swallow and continue: incompatible version, missing dependencies, etc.
                }
            }

            return null;
        }

        private static bool IsLoggerProviderCollectionParameter(Type actualParameterType, Type? providerCollectionType)
        {
            if (providerCollectionType is not null)
            {
                return actualParameterType == providerCollectionType;
            }

            // Fall back to name-based check if the type cannot be loaded.
            return string.Equals(
                actualParameterType.FullName,
                "Serilog.Extensions.Logging.LoggerProviderCollection",
                StringComparison.Ordinal);
        }

        private static void TryInvokeLoggingBuilderExtension(
            ILoggingBuilder loggingBuilder,
            string assemblyName,
            string typeName,
            string methodName,
            object?[] args)
        {
            var extensionType = TryLoadType(assemblyName, typeName);
            if (extensionType is null)
            {
                return;
            }

            var candidates = extensionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
                .ToArray();

            if (candidates.Length == 0)
            {
                return;
            }

            foreach (var method in candidates)
            {
                var parameters = method.GetParameters();
                if (parameters.Length != args.Length + 1)
                {
                    continue;
                }

                if (!parameters[0].ParameterType.IsInstanceOfType(loggingBuilder))
                {
                    continue;
                }

                try
                {
                    var invokeArgs = new object?[args.Length + 1];
                    invokeArgs[0] = loggingBuilder;
                    Array.Copy(args, 0, invokeArgs, 1, args.Length);

                    method.Invoke(null, invokeArgs);
                    return;
                }
                catch
                {
                    // Swallow and continue: missing package, incompatible version, etc.
                }
            }
        }

        private static Type? TryLoadType(string assemblyName, string fullTypeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullTypeName, throwOnError: false, ignoreCase: false);
                    if (t is not null)
                    {
                        return t;
                    }
                }
                catch
                {
                    // Ignore and continue probing.
                }
            }

            try
            {
                var asm = Assembly.Load(new AssemblyName(assemblyName));
                return asm.GetType(fullTypeName, throwOnError: false, ignoreCase: false);
            }
            catch
            {
                return null;
            }
        }
    }
}
