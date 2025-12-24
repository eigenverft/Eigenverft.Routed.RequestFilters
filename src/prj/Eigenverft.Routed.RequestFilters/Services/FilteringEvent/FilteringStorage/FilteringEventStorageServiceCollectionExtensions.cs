using System;

using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.InMemoryFiltering;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.NullFiltering;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage
{

    /// <summary>
    /// Identifies which filtering event storage should be registered as the active <see cref="IFilteringEventStorage"/>.
    /// </summary>
    public enum FilteringStorageKind
    {
        /// <summary>
        /// Uses <see cref="InMemoryFilteringEventStorage"/> as the active storage.
        /// </summary>
        InMemory = 0,

        /// <summary>
        /// Uses <see cref="NullFilteringEventStorage"/> as the active storage.
        /// </summary>
        Null = 1,
    }

    /// <summary>
    /// Marker interface used for the generic filtering event storage registration API.
    /// </summary>
    /// <remarks>
    /// Implementations of this interface are simple marker types (no members) that represent a concrete
    /// <see cref="IFilteringEventStorage"/> backend (for example, in-memory or SQLite).
    /// </remarks>
    public interface IFilteringEventStorageSelection { }

    /// <summary>
    /// Marker type that selects <see cref="InMemoryFilteringEventStorage"/> as the active <see cref="IFilteringEventStorage"/>.
    /// </summary>
    public sealed class InMemoryStorage : IFilteringEventStorageSelection { }

    /// <summary>
    /// Marker type that selects <see cref="NullFilteringEventStorage"/> as the active <see cref="IFilteringEventStorage"/>.
    /// </summary>
    public sealed class NullStorage : IFilteringEventStorageSelection { }

    /// <summary>
    /// Builder returned by <see cref="FilteringEventStorageServiceCollectionExtensions.AddFilteringEventStorage{TSelection}(IServiceCollection)"/>.
    /// </summary>
    /// <remarks>
    /// The builder enables fluent configuration that is scoped to the chosen storage backend.
    /// </remarks>
    public sealed class FilteringEventStorageBuilder
    {
        internal FilteringEventStorageBuilder(IServiceCollection services, Type? optionsType)
        {
            Services = services;
            OptionsType = optionsType;
        }

        /// <summary>
        /// Gets the service collection that is being configured.
        /// </summary>
        public IServiceCollection Services { get; }

        /// <summary>
        /// Gets the options type associated with the selected storage, if any.
        /// </summary>
        /// <remarks>
        /// This is used by builder extension methods to validate that the configured options match
        /// the chosen storage.
        /// </remarks>
        internal Type? OptionsType { get; }
    }

    /// <summary>
    /// Registers exactly one active <see cref="IFilteringEventStorage"/> implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This extension uses a "last call wins" model: each call replaces any registrations previously created by this
    /// extension while intentionally leaving user-added services and option configuration untouched.
    /// </para>
    /// <para>
    /// Configurable storages may bind their options from configuration and may also support additional fluent configuration
    /// via a builder returned from <see cref="AddFilteringEventStorage{TSelection}(IServiceCollection)"/>.
    /// </para>
    /// </remarks>
    public static class FilteringEventStorageServiceCollectionExtensions
    {
        private const string InMemorySectionPath = "InMemoryFilteringEventStorageOptions";

        /// <summary>
        /// Central registry of known storages.
        /// Add new storages here; cleanup and binding is derived from this list.
        /// </summary>
        /// <remarks>
        /// Each registry entry defines:
        /// - the marker type (generic selection),
        /// - the storage implementation type,
        /// - optionally the options type and configuration section path.
        /// </remarks>
        private static readonly StorageRegistration[] KnownStorages =
        {
            StorageRegistration.Configurable(typeof(InMemoryStorage), typeof(InMemoryFilteringEventStorage), typeof(InMemoryFilteringEventStorageOptions), InMemorySectionPath),
            StorageRegistration.Fixed(typeof(NullStorage), typeof(NullFilteringEventStorage)),
        };

        /// <summary>
        /// Registers the chosen storage (selected by <typeparamref name="TSelection"/>) and binds options via DI configuration.
        /// </summary>
        /// <typeparam name="TSelection">Marker type selecting the storage backend.</typeparam>
        /// <param name="services">The service collection to register services into.</param>
        /// <returns>A builder that enables fluent, storage-specific configuration.</returns>
        /// <remarks>
        /// <para>
        /// "Last call wins": all registrations created by this extension for the filtering storage are removed and replaced.
        /// </para>
        /// <para>
        /// If the chosen storage is configurable, options are bound from the section specified by the registry entry using
        /// the <see cref="IConfiguration"/> available in DI.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if <typeparamref name="TSelection"/> is not a registered storage selection.</exception>
        public static FilteringEventStorageBuilder AddFilteringEventStorage<TSelection>(this IServiceCollection services)
            where TSelection : class, IFilteringEventStorageSelection
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);
            RemoveExtensionOwnedRegistrations(services);

            StorageRegistration registration = GetRegistrationOrThrow(typeof(TSelection));
            AddStorageFromRegistry(services, registration, configurationOverride: null);

            return new FilteringEventStorageBuilder(services, registration.OptionsType);
        }

        /// <summary>
        /// Registers the chosen storage (selected by <typeparamref name="TSelection"/>) and binds options from <paramref name="configuration"/>.
        /// </summary>
        /// <typeparam name="TSelection">Marker type selecting the storage backend.</typeparam>
        /// <param name="services">The service collection to register services into.</param>
        /// <param name="configuration">
        /// The configuration root to bind options from.
        /// If provided, this overrides the configuration available from DI for the extension-owned binder.
        /// </param>
        /// <returns>A builder that enables fluent, storage-specific configuration.</returns>
        /// <remarks>
        /// <para>
        /// "Last call wins": all registrations created by this extension for the filtering storage are removed and replaced.
        /// </para>
        /// <para>
        /// If the chosen storage is configurable, options are bound from the section specified by the registry entry using
        /// <paramref name="configuration"/>.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if <typeparamref name="TSelection"/> is not a registered storage selection.</exception>
        public static FilteringEventStorageBuilder AddFilteringEventStorage<TSelection>(this IServiceCollection services, IConfiguration configuration)
            where TSelection : class, IFilteringEventStorageSelection
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);
            RemoveExtensionOwnedRegistrations(services);

            StorageRegistration registration = GetRegistrationOrThrow(typeof(TSelection));
            AddStorageFromRegistry(services, registration, configurationOverride: configuration);

            return new FilteringEventStorageBuilder(services, registration.OptionsType);
        }

        /// <summary>
        /// Applies additional configuration to <see cref="InMemoryFilteringEventStorageOptions"/> for the active storage.
        /// </summary>
        /// <param name="builder">The storage builder returned from <see cref="AddFilteringEventStorage{TSelection}(IServiceCollection)"/>.</param>
        /// <param name="configure">An action that configures the options.</param>
        /// <returns>The same builder instance for fluent chaining.</returns>
        /// <remarks>
        /// <para>
        /// The configuration added here is extension-owned and will be removed and replaced when <see cref="AddFilteringEventStorage{TSelection}(IServiceCollection)"/>
        /// is called again (last call wins).
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="builder"/> or <paramref name="configure"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the active storage does not use <see cref="InMemoryFilteringEventStorageOptions"/>.
        /// </exception>
        public static FilteringEventStorageBuilder Configure(this FilteringEventStorageBuilder builder, Action<InMemoryFilteringEventStorageOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configure);

            if (builder.OptionsType != typeof(InMemoryFilteringEventStorageOptions))
            {
                throw new InvalidOperationException($"Active storage does not use options type '{typeof(InMemoryFilteringEventStorageOptions).FullName}'.");
            }

            AddExtensionOwnedOptionsConfigure(builder.Services, configure);
            return builder;
        }

        /// <summary>
        /// Ensures shared dependencies are available once, independent of the chosen storage.
        /// </summary>
        /// <param name="services">The service collection to register dependencies into.</param>
        private static void AddInfrastructure(IServiceCollection services)
        {
            services.AddOptions();
        }

        /// <summary>
        /// Registers the selected storage from the registry and adds optional options binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="registration">The registry entry describing the storage.</param>
        /// <param name="configurationOverride">
        /// Optional configuration root to use for binding instead of DI-provided configuration.
        /// </param>
        private static void AddStorageFromRegistry(IServiceCollection services, StorageRegistration registration, IConfiguration? configurationOverride)
        {
            services.AddSingleton(typeof(IFilteringEventStorage), registration.StorageType);

            if (registration.IsConfigurable)
            {
                AddExtensionOwnedOptionsBinding(services, registration.OptionsType!, registration.SectionPath!, configurationOverride);
            }
        }

        /// <summary>
        /// Removes only registrations created by this extension, without touching user-added services and option configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        private static void RemoveExtensionOwnedRegistrations(IServiceCollection services)
        {
            services.RemoveAll<IFilteringEventStorage>();

            for (int i = 0; i < KnownStorages.Length; i++)
            {
                if (KnownStorages[i].IsConfigurable)
                {
                    RemoveExtensionOwnedOptionsBinding(services, KnownStorages[i].OptionsType!);
                    RemoveExtensionOwnedOptionsConfigure(services, KnownStorages[i].OptionsType!);
                }
            }
        }

        /// <summary>
        /// Adds an extension-owned options binder that can be removed later without affecting unrelated user configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="optionsType">The options type to bind.</param>
        /// <param name="sectionPath">The configuration section path.</param>
        /// <param name="configurationOverride">Optional configuration override.</param>
        /// <exception cref="ArgumentException">Thrown if <paramref name="sectionPath"/> is null or whitespace.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the internal binding instance cannot be created.</exception>
        private static void AddExtensionOwnedOptionsBinding(IServiceCollection services, Type optionsType, string sectionPath, IConfiguration? configurationOverride)
        {
            if (string.IsNullOrWhiteSpace(sectionPath))
            {
                throw new ArgumentException("Options section path must not be null or whitespace.", nameof(sectionPath));
            }

            Type bindingType = typeof(StorageOptionsBinding<>).MakeGenericType(optionsType);
            Type binderImplType = typeof(StorageOptionsBinder<>).MakeGenericType(optionsType);
            Type binderServiceType = typeof(IConfigureOptions<>).MakeGenericType(optionsType);
            Type changeTokenImplType = typeof(StorageOptionsChangeTokenSource<>).MakeGenericType(optionsType);
            Type changeTokenServiceType = typeof(IOptionsChangeTokenSource<>).MakeGenericType(optionsType);

            object bindingInstance = Activator.CreateInstance(bindingType, sectionPath, configurationOverride)
                ?? throw new InvalidOperationException($"Failed to create binding instance for options type '{optionsType.FullName}'.");

            services.AddSingleton(bindingType, bindingInstance);
            services.AddSingleton(binderServiceType, binderImplType);
            services.AddSingleton(changeTokenServiceType, changeTokenImplType);
        }

        /// <summary>
        /// Removes only the options binder and change token source created by this extension for a given options type.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="optionsType">The options type.</param>
        private static void RemoveExtensionOwnedOptionsBinding(IServiceCollection services, Type optionsType)
        {
            Type bindingType = typeof(StorageOptionsBinding<>).MakeGenericType(optionsType);
            Type binderImplType = typeof(StorageOptionsBinder<>).MakeGenericType(optionsType);
            Type binderServiceType = typeof(IConfigureOptions<>).MakeGenericType(optionsType);
            Type changeTokenImplType = typeof(StorageOptionsChangeTokenSource<>).MakeGenericType(optionsType);
            Type changeTokenServiceType = typeof(IOptionsChangeTokenSource<>).MakeGenericType(optionsType);

            RemoveAllWhere(services, d => d.ServiceType == bindingType);
            RemoveAllWhere(services, d => d.ServiceType == binderServiceType && d.ImplementationType == binderImplType);
            RemoveAllWhere(services, d => d.ServiceType == changeTokenServiceType && d.ImplementationType == changeTokenImplType);
        }

        /// <summary>
        /// Adds an extension-owned options configurer that can be removed later, enabling "last call wins" semantics.
        /// </summary>
        /// <typeparam name="TOptions">The options type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">The configuration action.</param>
        /// <remarks>
        /// Any previous extension-owned configurer for <typeparamref name="TOptions"/> is removed first.
        /// </remarks>
        private static void AddExtensionOwnedOptionsConfigure<TOptions>(IServiceCollection services, Action<TOptions> configure)
            where TOptions : class, new()
        {
            RemoveExtensionOwnedOptionsConfigure(services, typeof(TOptions));

            Type stateType = typeof(StorageOptionsConfigure<>).MakeGenericType(typeof(TOptions));
            Type implType = typeof(StorageOptionsConfigurer<>).MakeGenericType(typeof(TOptions));
            Type svcType = typeof(IConfigureOptions<>).MakeGenericType(typeof(TOptions));

            object state = Activator.CreateInstance(stateType, configure)
                ?? throw new InvalidOperationException($"Failed to create configure state for options type '{typeof(TOptions).FullName}'.");

            services.AddSingleton(stateType, state);
            services.AddSingleton(svcType, implType);
        }

        /// <summary>
        /// Removes the extension-owned options configurer created for the specified options type.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="optionsType">The options type.</param>
        private static void RemoveExtensionOwnedOptionsConfigure(IServiceCollection services, Type optionsType)
        {
            Type stateType = typeof(StorageOptionsConfigure<>).MakeGenericType(optionsType);
            Type implType = typeof(StorageOptionsConfigurer<>).MakeGenericType(optionsType);
            Type svcType = typeof(IConfigureOptions<>).MakeGenericType(optionsType);

            RemoveAllWhere(services, d => d.ServiceType == stateType);
            RemoveAllWhere(services, d => d.ServiceType == svcType && d.ImplementationType == implType);
        }

        /// <summary>
        /// Enables precise cleanup by removing only descriptors matching a predicate.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="predicate">Predicate to match service descriptors to remove.</param>
        private static void RemoveAllWhere(IServiceCollection services, Func<ServiceDescriptor, bool> predicate)
        {
            for (int i = services.Count - 1; i >= 0; i--)
            {
                if (predicate(services[i]))
                {
                    services.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Finds the registry entry for the given marker type or throws for unknown marker types.
        /// </summary>
        /// <param name="markerType">The marker type that selects the storage.</param>
        /// <returns>The matching registry entry.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the marker type is not recognized.</exception>
        private static StorageRegistration GetRegistrationOrThrow(Type markerType)
        {
            for (int i = 0; i < KnownStorages.Length; i++)
            {
                if (KnownStorages[i].MarkerType == markerType)
                {
                    return KnownStorages[i];
                }
            }

            throw new ArgumentOutOfRangeException(nameof(markerType), markerType, "Unknown filtering storage selection type.");
        }

        /// <summary>
        /// Describes how a storage is registered and optionally bound to configuration.
        /// </summary>
        private readonly struct StorageRegistration
        {
            private StorageRegistration(Type markerType, Type storageType, Type? optionsType, string? sectionPath)
            {
                MarkerType = markerType;
                StorageType = storageType;
                OptionsType = optionsType;
                SectionPath = sectionPath;
            }

            /// <summary>
            /// Gets the marker type that selects this storage (used by the generic API).
            /// </summary>
            public Type MarkerType { get; }

            /// <summary>
            /// Gets the concrete <see cref="IFilteringEventStorage"/> implementation type.
            /// </summary>
            public Type StorageType { get; }

            /// <summary>
            /// Gets the options type for this storage, if it is configurable.
            /// </summary>
            public Type? OptionsType { get; }

            /// <summary>
            /// Gets the configuration section path for this storage's options, if it is configurable.
            /// </summary>
            public string? SectionPath { get; }

            /// <summary>
            /// Gets a value indicating whether this storage has options that can be bound/configured.
            /// </summary>
            public bool IsConfigurable => OptionsType != null;

            /// <summary>
            /// Creates a registry entry for a storage that has no options.
            /// </summary>
            /// <param name="markerType">The marker type used to select the storage.</param>
            /// <param name="storageType">The implementation type.</param>
            /// <returns>A registry entry.</returns>
            public static StorageRegistration Fixed(Type markerType, Type storageType) => new(markerType, storageType, null, null);

            /// <summary>
            /// Creates a registry entry for a storage that is configurable via options bound from configuration.
            /// </summary>
            /// <param name="markerType">The marker type used to select the storage.</param>
            /// <param name="storageType">The implementation type.</param>
            /// <param name="optionsType">The options type.</param>
            /// <param name="sectionPath">The configuration section path to bind.</param>
            /// <returns>A registry entry.</returns>
            public static StorageRegistration Configurable(Type markerType, Type storageType, Type optionsType, string sectionPath)
                => new(markerType, storageType, optionsType, sectionPath);
        }

        /// <summary>
        /// Captures binding instructions so the extension can identify and remove its own binders later.
        /// </summary>
        /// <typeparam name="TOptions">The options type.</typeparam>
        internal sealed class StorageOptionsBinding<TOptions> where TOptions : class, new()
        {
            /// <summary>
            /// Initializes a new instance of the binding descriptor.
            /// </summary>
            /// <param name="sectionPath">Configuration section path.</param>
            /// <param name="configurationOverride">
            /// Optional configuration root to use instead of the configuration provided in DI.
            /// </param>
            public StorageOptionsBinding(string sectionPath, IConfiguration? configurationOverride)
            {
                SectionPath = sectionPath;
                ConfigurationOverride = configurationOverride;
            }

            /// <summary>
            /// Gets the configuration section path used for binding.
            /// </summary>
            public string SectionPath { get; }

            /// <summary>
            /// Gets an optional configuration override.
            /// </summary>
            public IConfiguration? ConfigurationOverride { get; }
        }

        /// <summary>
        /// Applies configuration binding for <typeparamref name="TOptions"/> using extension-owned services.
        /// </summary>
        /// <typeparam name="TOptions">The options type.</typeparam>
        internal sealed class StorageOptionsBinder<TOptions> : IConfigureOptions<TOptions> where TOptions : class, new()
        {
            private readonly IConfiguration _configurationFromDi;
            private readonly StorageOptionsBinding<TOptions> _binding;

            /// <summary>
            /// Initializes a new instance of the binder.
            /// </summary>
            /// <param name="configurationFromDi">The configuration instance provided by DI.</param>
            /// <param name="binding">Binding instructions.</param>
            public StorageOptionsBinder(IConfiguration configurationFromDi, StorageOptionsBinding<TOptions> binding)
            {
                _configurationFromDi = configurationFromDi;
                _binding = binding;
            }

            /// <inheritdoc />
            public void Configure(TOptions options)
            {
                IConfiguration configurationToUse = _binding.ConfigurationOverride ?? _configurationFromDi;
                configurationToUse.GetSection(_binding.SectionPath).Bind(options);
            }
        }

        /// <summary>
        /// Enables reload support for <typeparamref name="TOptions"/> when configuration is reloadable.
        /// </summary>
        /// <typeparam name="TOptions">The options type.</typeparam>
        internal sealed class StorageOptionsChangeTokenSource<TOptions> : IOptionsChangeTokenSource<TOptions> where TOptions : class, new()
        {
            private readonly IConfiguration _configurationFromDi;
            private readonly StorageOptionsBinding<TOptions> _binding;

            /// <summary>
            /// Initializes a new instance of the change token source.
            /// </summary>
            /// <param name="configurationFromDi">The configuration instance provided by DI.</param>
            /// <param name="binding">Binding instructions.</param>
            public StorageOptionsChangeTokenSource(IConfiguration configurationFromDi, StorageOptionsBinding<TOptions> binding)
            {
                _configurationFromDi = configurationFromDi;
                _binding = binding;
            }

            /// <inheritdoc />
            public string Name { get; } = Microsoft.Extensions.Options.Options.DefaultName;

            /// <inheritdoc />
            public IChangeToken GetChangeToken()
            {
                IConfiguration configurationToUse = _binding.ConfigurationOverride ?? _configurationFromDi;
                return configurationToUse.GetSection(_binding.SectionPath).GetReloadToken();
            }
        }

        /// <summary>
        /// Captures an extension-owned options configuration action so it can be removed and replaced later.
        /// </summary>
        /// <typeparam name="TOptions">The options type.</typeparam>
        internal sealed class StorageOptionsConfigure<TOptions> where TOptions : class, new()
        {
            /// <summary>
            /// Initializes a new instance of the configuration state container.
            /// </summary>
            /// <param name="configure">The configuration action.</param>
            public StorageOptionsConfigure(Action<TOptions> configure)
            {
                ConfigureAction = configure ?? throw new ArgumentNullException(nameof(configure));
            }

            /// <summary>
            /// Gets the configuration action.
            /// </summary>
            public Action<TOptions> ConfigureAction { get; }
        }

        /// <summary>
        /// Applies the extension-owned configuration action for <typeparamref name="TOptions"/>.
        /// </summary>
        /// <typeparam name="TOptions">The options type.</typeparam>
        internal sealed class StorageOptionsConfigurer<TOptions> : IConfigureOptions<TOptions> where TOptions : class, new()
        {
            private readonly StorageOptionsConfigure<TOptions> _state;

            /// <summary>
            /// Initializes a new instance of the configurer.
            /// </summary>
            /// <param name="state">The configuration state container.</param>
            public StorageOptionsConfigurer(StorageOptionsConfigure<TOptions> state)
            {
                _state = state;
            }

            /// <inheritdoc />
            public void Configure(TOptions options)
            {
                _state.ConfigureAction(options);
            }
        }

        /// <summary>
        /// Registers the chosen storage (selected by <paramref name="kind"/>) and binds options via DI configuration.
        /// </summary>
        /// <param name="services">The service collection to register services into.</param>
        /// <param name="kind">The storage backend selection.</param>
        /// <returns>A builder that enables fluent, storage-specific configuration.</returns>
        public static FilteringEventStorageBuilder AddFilteringEventStorage(this IServiceCollection services, FilteringStorageKind kind)
        {
            ArgumentNullException.ThrowIfNull(services);

            return kind switch
            {
                FilteringStorageKind.InMemory => services.AddFilteringEventStorage<InMemoryStorage>(),
                FilteringStorageKind.Null => services.AddFilteringEventStorage<NullStorage>(),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown filtering storage kind."),
            };
        }

        /// <summary>
        /// Registers the chosen storage (selected by <paramref name="kind"/>) and binds options from <paramref name="configuration"/>.
        /// </summary>
        /// <param name="services">The service collection to register services into.</param>
        /// <param name="configuration">The configuration root to bind options from.</param>
        /// <param name="kind">The storage backend selection.</param>
        /// <returns>A builder that enables fluent, storage-specific configuration.</returns>
        public static FilteringEventStorageBuilder AddFilteringEventStorage(this IServiceCollection services, IConfiguration configuration, FilteringStorageKind kind)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            return kind switch
            {
                FilteringStorageKind.InMemory => services.AddFilteringEventStorage<InMemoryStorage>(configuration),
                FilteringStorageKind.Null => services.AddFilteringEventStorage<NullStorage>(configuration),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown filtering storage kind."),
            };
        }

    }
}
