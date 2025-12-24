using System;
using System.Collections.Generic;
using System.Reflection;

namespace Eigenverft.Routed.RequestFilters.GenericExtensions.IServiceProviderExtensions
{
    public static partial class IServiceProviderExtensions
    {
        /// <summary>
        /// Ensures that the specified services are registered in the given service provider.
        /// Supports both concrete and open generic service types.
        /// </summary>
        /// <param name="serviceProvider">The service provider.</param>
        /// <param name="registrationHint">Optional hint text describing how to register the missing dependencies.</param>
        /// <param name="requiredServiceTypes">The required service types.</param>
        /// <exception cref="InvalidOperationException">Thrown when one or more of the required services are not registered or not activatable.</exception>
        public static void EnsureServicesRegistered(this IServiceProvider serviceProvider, string? registrationHint, params Type[] requiredServiceTypes)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);
            ArgumentNullException.ThrowIfNull(requiredServiceTypes);

            var missing = new List<string>();

            foreach (var requiredType in requiredServiceTypes)
            {
                if (requiredType is null)
                    continue;

                Type serviceTypeToResolve = requiredType;

                if (requiredType.IsGenericTypeDefinition)
                {
                    if (!TryCreateProbeClosedGeneric(requiredType, out serviceTypeToResolve))
                    {
                        missing.Add((requiredType.FullName ?? requiredType.Name) + " (cannot probe open generic constraints)");
                        continue;
                    }
                }

                object? instance;

                try
                {
                    instance = serviceProvider.GetService(serviceTypeToResolve);
                }
                catch (Exception ex)
                {
                    missing.Add((requiredType.FullName ?? requiredType.Name) + " (activation failed: " + ex.GetType().Name + ")");
                    continue;
                }

                if (instance is null)
                    missing.Add(requiredType.FullName ?? requiredType.Name);
            }

            if (missing.Count == 0)
                return;

            var message = "The following required services are not registered: " + string.Join(", ", missing) + ".";
            if (!string.IsNullOrWhiteSpace(registrationHint))
                message += " " + registrationHint;

            throw new InvalidOperationException(message);
        }

        /// <summary>
        /// Ensures that the specified services are registered in the given service provider.
        /// </summary>
        public static void EnsureServicesRegistered(this IServiceProvider serviceProvider, params Type[] requiredServiceTypes) => EnsureServicesRegistered(serviceProvider, null, requiredServiceTypes);

        /// <summary>
        /// Ensures that a single closed generic or non-generic service is registered.
        /// </summary>
        public static void EnsureServicesRegistered<TService>(this IServiceProvider serviceProvider, string? registrationHint = null) => EnsureServicesRegistered(serviceProvider, registrationHint, typeof(TService));

        /// <summary>
        /// Ensures that two closed generic or non-generic services are registered.
        /// </summary>
        public static void EnsureServicesRegistered<TService1, TService2>(this IServiceProvider serviceProvider, string? registrationHint = null) => EnsureServicesRegistered(serviceProvider, registrationHint, typeof(TService1), typeof(TService2));

        /// <summary>
        /// Ensures that three closed generic or non-generic services are registered.
        /// </summary>
        public static void EnsureServicesRegistered<TService1, TService2, TService3>(this IServiceProvider serviceProvider, string? registrationHint = null) => EnsureServicesRegistered(serviceProvider, registrationHint, typeof(TService1), typeof(TService2), typeof(TService3));

        /// <summary>
        /// Creates a best-effort closed generic type for probing an open generic registration.
        /// </summary>
        private static bool TryCreateProbeClosedGeneric(Type openGenericServiceType, out Type closedGenericServiceType)
        {
            closedGenericServiceType = openGenericServiceType;

            try
            {
                var genericParameters = openGenericServiceType.GetGenericArguments();
                var closedArgs = new Type[genericParameters.Length];

                for (var i = 0; i < genericParameters.Length; i++)
                    closedArgs[i] = ChooseProbeTypeArgument(genericParameters[i]);

                closedGenericServiceType = openGenericServiceType.MakeGenericType(closedArgs);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Picks a probe type argument that satisfies common constraints, to avoid false negatives.
        /// </summary>
        private static Type ChooseProbeTypeArgument(Type genericParameter)
        {
            if (!genericParameter.IsGenericParameter)
                return genericParameter;

            var attrs = genericParameter.GenericParameterAttributes;

            if ((attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                return typeof(int);

            var constraints = genericParameter.GetGenericParameterConstraints();

            for (var i = 0; i < constraints.Length; i++)
            {
                var c = constraints[i];
                if (c is null)
                    continue;

                if (c.IsClass && c != typeof(object))
                    return c;
            }

            if ((attrs & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                return typeof(object);

            return typeof(object);
        }
    }
}