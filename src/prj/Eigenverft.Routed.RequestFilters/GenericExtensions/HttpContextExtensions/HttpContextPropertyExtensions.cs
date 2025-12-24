using System;

using Microsoft.AspNetCore.Http;

namespace Eigenverft.Routed.RequestFilters.GenericExtensions.HttpContextExtensions
{
    /// <summary>
    /// Provides extension methods for storing and retrieving typed values in <see cref="HttpContext.Items"/>
    /// using explicit "context item" semantics.
    /// </summary>
    /// <remarks>
    /// <see cref="HttpContext.Items"/> is a request-scoped key/value bag. These helpers make reads type-safe and
    /// make the intent at call sites obvious (set/get context item).
    /// </remarks>
    public static partial class HttpContextExtensions
    {
        /// <summary>
        /// Stores a typed context item in <see cref="HttpContext.Items"/> under the specified key.
        /// </summary>
        /// <typeparam name="T">The type of the value to store.</typeparam>
        /// <param name="context">The current HTTP context.</param>
        /// <param name="key">The key used to store and later retrieve the context item.</param>
        /// <param name="value">The value to store as a context item.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="context"/> or <paramref name="key"/> is <see langword="null"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// context.SetContextItem("UserId", userId);
        /// </code>
        /// </example>
        public static void SetContextItem<T>(this HttpContext context, string key, T value)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(key);

            context.Items[key] = value!;
        }

        /// <summary>
        /// Gets a required typed context item from <see cref="HttpContext.Items"/> for the specified key.
        /// </summary>
        /// <remarks>
        /// Use this method when the context item must exist and must be of type <typeparamref name="T"/>.
        /// If the context item is optional, prefer <see cref="TryGetContextItem{T}(HttpContext, string, out T?)"/> or
        /// <see cref="GetContextItemOrDefault{T}(HttpContext, string, T?)"/>.
        /// </remarks>
        /// <typeparam name="T">The expected type of the stored context item.</typeparam>
        /// <param name="context">The current HTTP context.</param>
        /// <param name="key">The key used to retrieve the context item.</param>
        /// <returns>The stored context item cast to type <typeparamref name="T"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="context"/> or <paramref name="key"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the key is missing or the stored value is not of type <typeparamref name="T"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// var userId = context.GetRequiredContextItem&lt;string&gt;("UserId");
        /// </code>
        /// </example>
        public static T GetContextItem<T>(this HttpContext context, string key)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(key);

            if (context.Items.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }

            throw new InvalidOperationException(
                $"Missing or invalid context item for key '{key}' in HttpContext.Items (expected: {typeof(T).FullName}).");
        }

        /// <summary>
        /// Tries to get a typed context item from <see cref="HttpContext.Items"/> for the specified key.
        /// </summary>
        /// <typeparam name="T">The expected type of the stored context item.</typeparam>
        /// <param name="context">The current HTTP context.</param>
        /// <param name="key">The key used to retrieve the context item.</param>
        /// <param name="value">
        /// When this method returns, contains the context item if it exists and is of type <typeparamref name="T"/>;
        /// otherwise, the default value for <typeparamref name="T"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the context item exists and is of type <typeparamref name="T"/>;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="context"/> or <paramref name="key"/> is <see langword="null"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// if (context.TryGetContextItem&lt;string&gt;("UserId", out var userId))
        /// {
        ///     // Use userId
        /// }
        /// </code>
        /// </example>
        public static bool TryGetContextItem<T>(this HttpContext context, string key, out T? value)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(key);

            if (context.Items.TryGetValue(key, out var rawValue) && rawValue is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Gets a typed context item from <see cref="HttpContext.Items"/> or returns a provided default value.
        /// </summary>
        /// <typeparam name="T">The expected type of the stored context item.</typeparam>
        /// <param name="context">The current HTTP context.</param>
        /// <param name="key">The key used to retrieve the context item.</param>
        /// <param name="defaultValue">
        /// The value to return when the key is missing or the stored value is not of type <typeparamref name="T"/>.
        /// </param>
        /// <returns>
        /// The context item if found and of type <typeparamref name="T"/>; otherwise, <paramref name="defaultValue"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="context"/> or <paramref name="key"/> is <see langword="null"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// var userId = context.GetContextItemOrDefault("UserId", defaultValue: "anonymous");
        /// </code>
        /// </example>
        public static T? GetContextItemOrDefault<T>(this HttpContext context, string key, T? defaultValue = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(key);

            return context.Items.TryGetValue(key, out var value) && value is T typedValue ? typedValue : defaultValue;
        }
    }
}
