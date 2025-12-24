using System;
using System.Reflection;

using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Options
{
    /// <summary>
    /// Decorator for <see cref="IOptionsMonitor{TOptions}"/> that applies an additional configuration delegate
    /// on top of the underlying options instance while preserving the original values.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    public sealed class ConfiguredOptionsMonitor<TOptions> : IOptionsMonitor<TOptions> where TOptions : class, new()
    {
        private readonly IOptionsMonitor<TOptions> _inner;
        private readonly Action<TOptions> _configure;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfiguredOptionsMonitor{TOptions}"/> class.
        /// </summary>
        /// <param name="inner">The inner options monitor (resolved from DI).</param>
        /// <param name="configure">An additional configuration delegate to apply to a cloned instance.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="inner"/> or <paramref name="configure"/> is <c>null</c>.
        /// </exception>
        public ConfiguredOptionsMonitor(IOptionsMonitor<TOptions> inner, Action<TOptions> configure)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _configure = configure ?? throw new ArgumentNullException(nameof(configure));
        }

        /// <summary>
        /// Gets the current options value, applying the additional configuration to a cloned instance.
        /// </summary>
        public TOptions CurrentValue
        {
            get
            {
                var options = Clone(_inner.CurrentValue);
                _configure(options);
                return options;
            }
        }

        /// <summary>
        /// Gets the options for a specified named instance, applying the additional configuration to a cloned instance.
        /// If <paramref name="name"/> is <c>null</c> or empty, the default options name is used.
        /// </summary>
        /// <param name="name">The name of the options instance.</param>
        /// <returns>The configured options for the specified name.</returns>
        public TOptions Get(string? name)
        {
            var effectiveName = string.IsNullOrEmpty(name)
                ? Microsoft.Extensions.Options.Options.DefaultName
                : name;

            var options = Clone(_inner.Get(effectiveName));
            _configure(options);
            return options;
        }

        /// <summary>
        /// Registers a change listener on the underlying options monitor.
        /// </summary>
        /// <param name="listener">The listener to invoke when the options change.</param>
        /// <returns>
        /// An <see cref="IDisposable"/> that can be used to stop listening for changes.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="listener"/> is <c>null</c>.
        /// </exception>
        public IDisposable OnChange(Action<TOptions, string?> listener)
        {
            ArgumentNullException.ThrowIfNull(listener);
            // Underlying implementation is expected to return a non-null IDisposable.
            return _inner.OnChange(listener)!;
        }

        /// <summary>
        /// Creates a shallow clone of the options instance.
        /// </summary>
        /// <param name="source">The source options instance to clone.</param>
        /// <returns>
        /// A new <typeparamref name="TOptions"/> instance with public writable properties copied from <paramref name="source"/>.
        /// If <paramref name="source"/> is <c>null</c>, a new instance with default values is returned.
        /// </returns>
        private static TOptions Clone(TOptions? source)
        {
            var clone = new TOptions();

            if (source is null)
                return clone;

            foreach (PropertyInfo prop in typeof(TOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // Skip non-readable, non-writable, or indexer properties.
                if (!prop.CanRead || !prop.CanWrite)
                    continue;

                if (prop.GetIndexParameters().Length != 0)
                    continue;

                var value = prop.GetValue(source);
                prop.SetValue(clone, value);
            }

            return clone;
        }
    }
}