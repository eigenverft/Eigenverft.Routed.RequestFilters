using System;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators.NullFilteringEvaluation;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators.SimpleFilteringScoreEvaluator;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators.SourceAndMatchKindWeighted;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators
{
    /// <summary>
    /// Identifies which filtering evaluator should be registered as the active <see cref="IFilteringEvaluationService"/>.
    /// </summary>
    public enum FilteringEvaluatorKind
    {
        /// <summary>
        /// Uses <see cref="NullFilteringEvaluator"/>.
        /// </summary>
        NullFiltering = 0,

        /// <summary>
        /// Uses <see cref="SimpleFilteringScoreFilteringEvaluator"/>.
        /// </summary>
        SimpleFilteringScore = 1,

        /// <summary>
        /// Uses <see cref="SourceAndMatchKindWeightedFilteringEvaluator"/>.
        /// </summary>
        SourceAndMatchKindWeighted = 2,
    }

    /// <summary>
    /// Registers exactly one active <see cref="IFilteringEvaluationService"/> implementation.
    /// </summary>
    public static class FilteringEvaluatorServiceCollectionExtensions
    {
        // ===== Extension points (add new evaluators here) =====

        private const string SourceAndMatchKindWeightedSectionPath = "SourceAndMatchKindWeightedFilteringEvaluatorOptions";

        /// <summary>
        /// Central registry of known evaluators.
        /// Add new evaluators here; cleanup and binding is derived from this list.
        /// </summary>
        private static readonly EvaluatorRegistration[] KnownEvaluators =
        {
            EvaluatorRegistration.Configurable(FilteringEvaluatorKind.SourceAndMatchKindWeighted, typeof(SourceAndMatchKindWeightedFilteringEvaluator), typeof(SourceAndMatchKindWeightedFilteringEvaluatorOptions), SourceAndMatchKindWeightedSectionPath),
            EvaluatorRegistration.Fixed(FilteringEvaluatorKind.SimpleFilteringScore, typeof(SimpleFilteringScoreFilteringEvaluator)),
            EvaluatorRegistration.Fixed(FilteringEvaluatorKind.NullFiltering, typeof(NullFilteringEvaluator)),
        };

        // ===== Public API =====

        /// <summary>
        /// Registers the chosen evaluator and binds options via DI configuration.
        /// Last call wins; extension-owned registrations are replaced.
        /// </summary>
        public static IServiceCollection AddFilteringEvaluator(this IServiceCollection services, FilteringEvaluatorKind kind)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);
            RemoveExtensionOwnedRegistrations(services);
            SetSelectionMarker(services, kind);

            AddEvaluatorFromRegistry(services, kind, null);

            return services;
        }

        /// <summary>
        /// Registers the chosen evaluator and binds options from the provided <paramref name="configuration"/>.
        /// Last call wins; extension-owned registrations are replaced.
        /// </summary>
        public static IServiceCollection AddFilteringEvaluator(this IServiceCollection services, IConfiguration configuration, FilteringEvaluatorKind kind)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);
            RemoveExtensionOwnedRegistrations(services);
            SetSelectionMarker(services, kind);

            AddEvaluatorFromRegistry(services, kind, configuration);

            return services;
        }

        // ===== Infrastructure & orchestration =====

        /// <summary>
        /// Ensures shared dependencies are available once, independent of the chosen evaluator.
        /// </summary>
        private static void AddInfrastructure(IServiceCollection services)
        {
            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.AddOptions();
        }

        /// <summary>
        /// Resolves the evaluator definition and registers evaluator plus optional binding services.
        /// </summary>
        private static void AddEvaluatorFromRegistry(IServiceCollection services, FilteringEvaluatorKind kind, IConfiguration? configurationOverride)
        {
            EvaluatorRegistration registration = GetRegistrationOrThrow(kind);

            services.AddSingleton(typeof(IFilteringEvaluationService), registration.EvaluatorType);

            if (registration.IsConfigurable)
            {
                AddExtensionOwnedOptionsBinding(services, registration.OptionsType!, registration.SectionPath!, configurationOverride);
            }
        }

        /// <summary>
        /// Removes only registrations created by this extension, without touching user-added <see cref="IConfigureOptions{TOptions}"/>.
        /// </summary>
        private static void RemoveExtensionOwnedRegistrations(IServiceCollection services)
        {
            services.RemoveAll<IFilteringEvaluationService>();
            services.RemoveAll<FilteringEvaluatorSelectionMarker>();

            for (int i = 0; i < KnownEvaluators.Length; i++)
            {
                if (KnownEvaluators[i].IsConfigurable)
                {
                    RemoveExtensionOwnedOptionsBinding(services, KnownEvaluators[i].OptionsType!);
                }
            }
        }

        /// <summary>
        /// Stores the chosen evaluator kind in DI for diagnostics and follow-up configuration patterns.
        /// </summary>
        private static void SetSelectionMarker(IServiceCollection services, FilteringEvaluatorKind kind)
        {
            services.AddSingleton(new FilteringEvaluatorSelectionMarker(kind));
        }

        // ===== Options binding (extension-owned, removable) =====

        /// <summary>
        /// Adds a binder that the extension can remove later without affecting unrelated user configuration.
        /// </summary>
        private static void AddExtensionOwnedOptionsBinding(IServiceCollection services, Type optionsType, string sectionPath, IConfiguration? configurationOverride)
        {
            if (string.IsNullOrWhiteSpace(sectionPath))
            {
                throw new ArgumentException("Options section path must not be null or whitespace.", nameof(sectionPath));
            }

            Type bindingType = typeof(FilteringEvaluatorOptionsBinding<>).MakeGenericType(optionsType);
            Type binderImplType = typeof(FilteringEvaluatorOptionsBinder<>).MakeGenericType(optionsType);
            Type binderServiceType = typeof(IConfigureOptions<>).MakeGenericType(optionsType);
            Type changeTokenImplType = typeof(FilteringEvaluatorOptionsChangeTokenSource<>).MakeGenericType(optionsType);
            Type changeTokenServiceType = typeof(IOptionsChangeTokenSource<>).MakeGenericType(optionsType);

            object bindingInstance = Activator.CreateInstance(bindingType, sectionPath, configurationOverride)
                ?? throw new InvalidOperationException($"Failed to create binding instance for options type '{optionsType.FullName}'.");

            services.AddSingleton(bindingType, bindingInstance);
            services.AddSingleton(binderServiceType, binderImplType);
            services.AddSingleton(changeTokenServiceType, changeTokenImplType);
        }

        /// <summary>
        /// Removes only the binder and change token source created by this extension for a given options type.
        /// </summary>
        private static void RemoveExtensionOwnedOptionsBinding(IServiceCollection services, Type optionsType)
        {
            Type bindingType = typeof(FilteringEvaluatorOptionsBinding<>).MakeGenericType(optionsType);
            Type binderImplType = typeof(FilteringEvaluatorOptionsBinder<>).MakeGenericType(optionsType);
            Type binderServiceType = typeof(IConfigureOptions<>).MakeGenericType(optionsType);
            Type changeTokenImplType = typeof(FilteringEvaluatorOptionsChangeTokenSource<>).MakeGenericType(optionsType);
            Type changeTokenServiceType = typeof(IOptionsChangeTokenSource<>).MakeGenericType(optionsType);

            RemoveAllWhere(services, d => d.ServiceType == bindingType);
            RemoveAllWhere(services, d => d.ServiceType == binderServiceType && d.ImplementationType == binderImplType);
            RemoveAllWhere(services, d => d.ServiceType == changeTokenServiceType && d.ImplementationType == changeTokenImplType);
        }

        /// <summary>
        /// Enables precise cleanup by removing only descriptors matching a predicate.
        /// </summary>
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

        // ===== Registry helpers =====

        /// <summary>
        /// Finds the registry entry for <paramref name="kind"/> or throws for unknown kinds.
        /// </summary>
        private static EvaluatorRegistration GetRegistrationOrThrow(FilteringEvaluatorKind kind)
        {
            for (int i = 0; i < KnownEvaluators.Length; i++)
            {
                if (KnownEvaluators[i].Kind == kind)
                {
                    return KnownEvaluators[i];
                }
            }

            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown filtering evaluator kind.");
        }

        /// <summary>
        /// Describes how an evaluator is registered and optionally bound to configuration.
        /// </summary>
        private readonly struct EvaluatorRegistration
        {
            private EvaluatorRegistration(FilteringEvaluatorKind kind, Type evaluatorType, Type? optionsType, string? sectionPath)
            {
                Kind = kind;
                EvaluatorType = evaluatorType;
                OptionsType = optionsType;
                SectionPath = sectionPath;
            }

            public FilteringEvaluatorKind Kind { get; }
            public Type EvaluatorType { get; }
            public Type? OptionsType { get; }
            public string? SectionPath { get; }
            public bool IsConfigurable => OptionsType != null;

            public static EvaluatorRegistration Fixed(FilteringEvaluatorKind kind, Type evaluatorType)
            {
                return new EvaluatorRegistration(kind, evaluatorType, null, null);
            }

            public static EvaluatorRegistration Configurable(FilteringEvaluatorKind kind, Type evaluatorType, Type optionsType, string sectionPath)
            {
                return new EvaluatorRegistration(kind, evaluatorType, optionsType, sectionPath);
            }
        }

        // ===== Internal marker & binders =====

        /// <summary>
        /// Records the current evaluator selection.
        /// </summary>
        internal sealed class FilteringEvaluatorSelectionMarker
        {
            public FilteringEvaluatorSelectionMarker(FilteringEvaluatorKind kind)
            { Kind = kind; }

            public FilteringEvaluatorKind Kind { get; }
        }

        /// <summary>
        /// Captures binding instructions so the extension can identify and remove its own binders later.
        /// </summary>
        internal sealed class FilteringEvaluatorOptionsBinding<TOptions> where TOptions : class, new()
        {
            public FilteringEvaluatorOptionsBinding(string sectionPath, IConfiguration? configurationOverride)
            { SectionPath = sectionPath; ConfigurationOverride = configurationOverride; }

            public string SectionPath { get; }
            public IConfiguration? ConfigurationOverride { get; }
        }

        /// <summary>
        /// Applies configuration binding for <typeparamref name="TOptions"/> using extension-owned services.
        /// </summary>
        internal sealed class FilteringEvaluatorOptionsBinder<TOptions> : IConfigureOptions<TOptions> where TOptions : class, new()
        {
            private readonly IConfiguration _configurationFromDi;
            private readonly FilteringEvaluatorOptionsBinding<TOptions> _binding;

            public FilteringEvaluatorOptionsBinder(IConfiguration configurationFromDi, FilteringEvaluatorOptionsBinding<TOptions> binding)
            { _configurationFromDi = configurationFromDi; _binding = binding; }

            public void Configure(TOptions options)
            {
                IConfiguration configurationToUse = _binding.ConfigurationOverride ?? _configurationFromDi;
                configurationToUse.GetSection(_binding.SectionPath).Bind(options);
            }
        }

        /// <summary>
        /// Enables reload support for <typeparamref name="TOptions"/> when configuration is reloadable.
        /// </summary>
        internal sealed class FilteringEvaluatorOptionsChangeTokenSource<TOptions> : IOptionsChangeTokenSource<TOptions> where TOptions : class, new()
        {
            private readonly IConfiguration _configurationFromDi;
            private readonly FilteringEvaluatorOptionsBinding<TOptions> _binding;

            public FilteringEvaluatorOptionsChangeTokenSource(IConfiguration configurationFromDi, FilteringEvaluatorOptionsBinding<TOptions> binding)
            { _configurationFromDi = configurationFromDi; _binding = binding; }

            public string Name { get; } = Microsoft.Extensions.Options.Options.DefaultName;

            public IChangeToken GetChangeToken()
            {
                IConfiguration configurationToUse = _binding.ConfigurationOverride ?? _configurationFromDi;
                return configurationToUse.GetSection(_binding.SectionPath).GetReloadToken();
            }
        }
    }
}