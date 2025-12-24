using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Eigenverft.Routed.RequestFilters.Options
{
    /// <summary>
    /// A dictionary that is seeded with default entries and automatically replaces those defaults when configuration binding performs the first configured write.
    /// </summary>
    /// <remarks>
    /// Reviewer note:
    /// The Microsoft configuration binder typically populates dictionary-like properties by writing keys through
    /// <see cref="IDictionary{TKey, TValue}.Add(TKey, TValue)"/> and/or the indexer setter.
    /// This type clears the seeded defaults exactly once on the first "override write" performed via
    /// <see cref="Add(TKey, TValue)"/>, <see cref="TryAdd(TKey, TValue)"/>, or the indexer setter.
    /// <para>
    /// Intended use:
    /// Use this in options/settings classes to express: "defaults exist in code, but if configuration supplies any value,
    /// the configured values fully replace the defaults (no merging)".
    /// </para>
    /// <para>
    /// Important:
    /// - Seed defaults via constructors that accept a collection (for example <see cref="OptionsConfigOverridesDefaultsDictionary{TKey, TValue}(IDictionary{TKey, TValue})"/>),
    ///   or via <see cref="SeedDefaults(IDictionary{TKey, TValue})"/>.
    /// - Avoid collection-initializer syntax for defaults, because it calls <see cref="Add(TKey, TValue)"/> (treated as an override write).
    /// </para>
    /// <para>
    /// Upgrade example (from dictionary defaults to configuration-overrides-defaults semantics):
    /// </para>
    /// <code>
    /// // BEFORE: defaults live in a Dictionary. If configuration binds any entry, your code might merge.
    /// public sealed class MyOptions
    /// {
    ///     public Dictionary&lt;string, string&gt; Headers { get; set; } = new()
    ///     {
    ///         ["X-Frame-Options"] = "DENY",
    ///         ["X-Content-Type-Options"] = "nosniff",
    ///     };
    /// }
    ///
    /// // AFTER: defaults exist, but the first config write clears defaults once and configured values fully replace them.
    /// public sealed class MyOptions
    /// {
    ///     public OptionsConfigOverridesDefaultsDictionary&lt;string, string&gt; Headers { get; set; }
    ///         = new(new Dictionary&lt;string, string&gt;
    ///         {
    ///             ["X-Frame-Options"] = "DENY",
    ///             ["X-Content-Type-Options"] = "nosniff",
    ///         });
    /// }
    /// </code>
    /// <para>
    /// Opting out of defaults:
    /// By design, a missing configuration section (or a present-but-empty section with no children) typically results in no binder writes,
    /// so the seeded defaults remain in effect.
    /// </para>
    /// <para>
    /// If you ever need an explicit “empty means empty” outcome, you can opt into that intentionally by adding a flag and clearing in post-configure.
    /// This keeps the default behavior safe and predictable, while still allowing an explicit override when required.
    /// </para>
    /// <code>
    /// public sealed class MyOptions
    /// {
    ///     public bool DisableHeaderDefaults { get; set; } = false;
    ///     public OptionsConfigOverridesDefaultsDictionary&lt;string, string&gt; Headers { get; set; }
    ///         = new(new Dictionary&lt;string, string&gt; { ["X-Frame-Options"] = "DENY" });
    /// }
    ///
    /// // Then in post-configure:
    /// // services.PostConfigure&lt;MyOptions&gt;(o =&gt;
    /// // {
    /// //     if (o.DisableHeaderDefaults) o.Headers.Clear(); // results in an empty dictionary
    /// // });
    /// </code>
    /// <para>
    /// Additive behavior:
    /// This wrapper intentionally implements “replace defaults on first write” semantics (not merging).
    /// If you want “keep defaults and add/override from configuration”, use a plain <see cref="Dictionary{TKey, TValue}"/> (or a dedicated merge helper) instead.
    /// </para>
    /// </remarks>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <typeparam name="TValue">Value type.</typeparam>
    public sealed class OptionsConfigOverridesDefaultsDictionary<TKey, TValue> :
        IDictionary<TKey, TValue>,
        IReadOnlyDictionary<TKey, TValue>,
        IDictionary
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> _items;
        private bool _hasDefaultSeed;
        private bool _wasOverriddenByConfiguration;

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsConfigOverridesDefaultsDictionary{TKey, TValue}"/> class with no defaults.
        /// </summary>
        /// <remarks>
        /// Reviewer note:
        /// This mirrors <see cref="Dictionary{TKey, TValue}"/> default construction.
        /// Since no defaults are seeded, there is nothing to replace; override semantics are effectively inactive until you seed defaults.
        /// </remarks>
        /// <example>
        /// <code>
        /// public sealed class MyOptions
        /// {
        ///     // No defaults: configuration (if present) simply fills it.
        ///     public OptionsConfigOverridesDefaultsDictionary&lt;string, string&gt; Headers { get; set; } = new();
        /// }
        /// </code>
        /// </example>
        public OptionsConfigOverridesDefaultsDictionary()
        {
            _items = new Dictionary<TKey, TValue>();
            _hasDefaultSeed = false;
            _wasOverriddenByConfiguration = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsConfigOverridesDefaultsDictionary{TKey, TValue}"/> class that is empty
        /// and has the specified initial capacity.
        /// </summary>
        /// <param name="capacity">Initial number of entries the dictionary can contain without resizing.</param>
        /// <remarks>
        /// Reviewer note:
        /// This mirrors <see cref="Dictionary{TKey, TValue}.Dictionary(int)"/>. Capacity does not seed defaults and does not activate override semantics.
        /// Use this for performance when you expect a known number of configured entries.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Expect about ~20 entries from configuration, so pre-allocate.
        /// var d = new OptionsConfigOverridesDefaultsDictionary&lt;string, string&gt;(capacity: 20);
        /// </code>
        /// </example>
        public OptionsConfigOverridesDefaultsDictionary(int capacity)
        {
            _items = new Dictionary<TKey, TValue>(capacity);
            _hasDefaultSeed = false;
            _wasOverriddenByConfiguration = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsConfigOverridesDefaultsDictionary{TKey, TValue}"/> class that is empty
        /// and uses the specified comparer.
        /// </summary>
        /// <param name="comparer">Key comparer used to determine equality of keys.</param>
        /// <remarks>
        /// Reviewer note:
        /// This mirrors <see cref="Dictionary{TKey, TValue}.Dictionary(IEqualityComparer{TKey})"/>.
        /// Use this when configuration keys should be treated case-insensitively, or when custom key semantics are required.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Case-insensitive header names:
        /// var d = new OptionsConfigOverridesDefaultsDictionary&lt;string, string&gt;(StringComparer.OrdinalIgnoreCase);
        /// </code>
        /// </example>
        public OptionsConfigOverridesDefaultsDictionary(IEqualityComparer<TKey>? comparer)
        {
            _items = new Dictionary<TKey, TValue>(comparer);
            _hasDefaultSeed = false;
            _wasOverriddenByConfiguration = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsConfigOverridesDefaultsDictionary{TKey, TValue}"/> class that is empty
        /// with the specified initial capacity and uses the specified comparer.
        /// </summary>
        /// <param name="capacity">Initial number of entries the dictionary can contain without resizing.</param>
        /// <param name="comparer">Key comparer used to determine equality of keys.</param>
        /// <remarks>
        /// Reviewer note:
        /// This mirrors <see cref="Dictionary{TKey, TValue}.Dictionary(int, IEqualityComparer{TKey})"/>.
        /// Capacity and comparer do not seed defaults; override semantics remain inactive until defaults are seeded.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Case-insensitive keys and fewer resizes:
        /// var d = new OptionsConfigOverridesDefaultsDictionary&lt;string, string&gt;(capacity: 16, comparer: StringComparer.OrdinalIgnoreCase);
        /// </code>
        /// </example>
        public OptionsConfigOverridesDefaultsDictionary(int capacity, IEqualityComparer<TKey>? comparer)
        {
            _items = new Dictionary<TKey, TValue>(capacity, comparer);
            _hasDefaultSeed = false;
            _wasOverriddenByConfiguration = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsConfigOverridesDefaultsDictionary{TKey, TValue}"/> class seeded with default entries
        /// copied from the specified dictionary.
        /// </summary>
        /// <param name="dictionary">Default entries to seed.</param>
        /// <remarks>
        /// Reviewer note:
        /// This is the primary constructor for "defaults exist in code" semantics. The instance is considered "using defaults"
        /// until configuration performs the first write (through <see cref="Add(TKey, TValue)"/>, <see cref="TryAdd(TKey, TValue)"/>, or the indexer setter),
        /// at which point the defaults are cleared once and the configured values fully replace them.
        /// <para>
        /// If the configuration key is missing, binding typically performs no writes and these defaults remain effective.
        /// If the configuration key exists but contains no children, binding typically performs no writes and these defaults remain effective.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// public sealed class MyOptions
        /// {
        ///     // Defaults: used if config is missing (or empty/no children).
        ///     public OptionsConfigOverridesDefaultsDictionary&lt;string, string&gt; Headers { get; set; }
        ///         = new(new Dictionary&lt;string, string&gt;
        ///         {
        ///             ["X-Frame-Options"] = "DENY",
        ///             ["X-Content-Type-Options"] = "nosniff",
        ///         });
        /// }
        ///
        /// // When configuration writes the first entry, defaults are cleared once and replaced.
        /// </code>
        /// </example>
        public OptionsConfigOverridesDefaultsDictionary(IDictionary<TKey, TValue> dictionary)
        {
            _items = dictionary is null ? new Dictionary<TKey, TValue>() : new Dictionary<TKey, TValue>(dictionary);
            _hasDefaultSeed = _items.Count > 0;
            _wasOverriddenByConfiguration = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsConfigOverridesDefaultsDictionary{TKey, TValue}"/> class seeded with default entries
        /// copied from the specified dictionary and using the specified comparer.
        /// </summary>
        /// <param name="dictionary">Default entries to seed.</param>
        /// <param name="comparer">Key comparer used to determine equality of keys.</param>
        /// <remarks>
        /// Reviewer note:
        /// This mirrors <see cref="Dictionary{TKey, TValue}.Dictionary(IDictionary{TKey, TValue}, IEqualityComparer{TKey})"/>,
        /// while still applying configuration-overrides-defaults semantics.
        /// Use this when defaults exist and configuration keys should be compared using a specific comparer.
        /// </remarks>
        /// <example>
        /// <code>
        /// var defaults = new Dictionary&lt;string, string&gt;(StringComparer.OrdinalIgnoreCase)
        /// {
        ///     ["X-Frame-Options"] = "DENY",
        /// };
        ///
        /// var d = new OptionsConfigOverridesDefaultsDictionary&lt;string, string&gt;(defaults, StringComparer.OrdinalIgnoreCase);
        /// </code>
        /// </example>
        public OptionsConfigOverridesDefaultsDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey>? comparer)
        {
            _items = dictionary is null ? new Dictionary<TKey, TValue>(comparer) : new Dictionary<TKey, TValue>(dictionary, comparer);
            _hasDefaultSeed = _items.Count > 0;
            _wasOverriddenByConfiguration = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsConfigOverridesDefaultsDictionary{TKey, TValue}"/> class seeded with default entries
        /// copied from the specified collection.
        /// </summary>
        /// <param name="collection">Default entries to seed.</param>
        /// <remarks>
        /// Reviewer note:
        /// This mirrors <see cref="Dictionary{TKey, TValue}.Dictionary(IEnumerable{KeyValuePair{TKey, TValue}})"/>.
        /// Seeding via <paramref name="collection"/> is treated as "defaults".
        /// The first subsequent write clears seeded defaults once so configuration values fully replace them.
        /// </remarks>
        /// <example>
        /// <code>
        /// var defaults = new[]
        /// {
        ///     new KeyValuePair&lt;string, string&gt;("X-Frame-Options", "DENY"),
        ///     new KeyValuePair&lt;string, string&gt;("X-Content-Type-Options", "nosniff"),
        /// };
        ///
        /// var d = new OptionsConfigOverridesDefaultsDictionary&lt;string, string&gt;(defaults);
        /// </code>
        /// </example>
        public OptionsConfigOverridesDefaultsDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection)
        {
            _items = collection is null ? new Dictionary<TKey, TValue>() : new Dictionary<TKey, TValue>(collection);
            _hasDefaultSeed = _items.Count > 0;
            _wasOverriddenByConfiguration = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsConfigOverridesDefaultsDictionary{TKey, TValue}"/> class seeded with default entries
        /// copied from the specified collection and using the specified comparer.
        /// </summary>
        /// <param name="collection">Default entries to seed.</param>
        /// <param name="comparer">Key comparer used to determine equality of keys.</param>
        /// <remarks>
        /// Reviewer note:
        /// This mirrors <see cref="Dictionary{TKey, TValue}.Dictionary(IEnumerable{KeyValuePair{TKey, TValue}}, IEqualityComparer{TKey})"/>,
        /// while applying configuration-overrides-defaults semantics.
        /// </remarks>
        /// <example>
        /// <code>
        /// var defaults = new[]
        /// {
        ///     new KeyValuePair&lt;string, string&gt;("x-frame-options", "DENY"),
        /// };
        ///
        /// var d = new OptionsConfigOverridesDefaultsDictionary&lt;string, string&gt;(defaults, StringComparer.OrdinalIgnoreCase);
        /// </code>
        /// </example>
        public OptionsConfigOverridesDefaultsDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey>? comparer)
        {
            _items = collection is null ? new Dictionary<TKey, TValue>(comparer) : new Dictionary<TKey, TValue>(collection, comparer);
            _hasDefaultSeed = _items.Count > 0;
            _wasOverriddenByConfiguration = false;
        }

        /// <summary>
        /// Creates a new instance seeded with the provided default entries.
        /// </summary>
        /// <param name="defaults">Default entries to seed.</param>
        /// <returns>A seeded <see cref="OptionsConfigOverridesDefaultsDictionary{TKey, TValue}"/>.</returns>
        /// <remarks>
        /// Reviewer note:
        /// This is a convenience factory to mirror the list variant’s <c>Defaults(...)</c> style.
        /// </remarks>
        /// <example>
        /// <code>
        /// var d = OptionsConfigOverridesDefaultsDictionary&lt;string, string&gt;.Defaults(
        ///     new("X-Frame-Options", "DENY"),
        ///     new("X-Content-Type-Options", "nosniff"));
        /// </code>
        /// </example>
        public static OptionsConfigOverridesDefaultsDictionary<TKey, TValue> Defaults(params KeyValuePair<TKey, TValue>[] defaults)
        {
            return new OptionsConfigOverridesDefaultsDictionary<TKey, TValue>(defaults ?? Array.Empty<KeyValuePair<TKey, TValue>>());
        }

        /// <summary>
        /// Gets a value indicating whether this instance was seeded with default entries.
        /// </summary>
        public bool HasDefaultSeed => _hasDefaultSeed;

        /// <summary>
        /// Gets a value indicating whether the seeded defaults were replaced by configuration (or any first override write).
        /// </summary>
        public bool WasOverriddenByConfiguration => _hasDefaultSeed && _wasOverriddenByConfiguration;

        /// <summary>
        /// Gets a value indicating whether this instance is currently using the seeded defaults.
        /// </summary>
        /// <remarks>
        /// This returns <see langword="true"/> only when defaults were seeded and no override write has happened.
        /// </remarks>
        public bool IsUsingDefaults => _hasDefaultSeed && !_wasOverriddenByConfiguration;

        /// <summary>
        /// Gets the comparer used to determine equality of keys.
        /// </summary>
        public IEqualityComparer<TKey> Comparer => _items.Comparer;

        /// <summary>
        /// Gets the number of key/value pairs in the dictionary.
        /// </summary>
        public int Count => _items.Count;

        /// <summary>
        /// Gets the keys in the dictionary.
        /// </summary>
        public Dictionary<TKey, TValue>.KeyCollection Keys => _items.Keys;

        /// <summary>
        /// Gets the values in the dictionary.
        /// </summary>
        public Dictionary<TKey, TValue>.ValueCollection Values => _items.Values;

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => _items.Keys;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => _items.Values;
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => _items.Keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => _items.Values;

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <summary>
        /// Gets or sets the value associated with the specified key.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>Value.</returns>
        public TValue this[TKey key]
        {
            get => _items[key];
            set
            {
                EnsureDefaultsReplacedForOverrideWrite();
                _items[key] = value;
            }
        }

        /// <summary>
        /// Seeds defaults without triggering override semantics.
        /// </summary>
        /// <param name="defaults">Default entries to add.</param>
        /// <remarks>
        /// Reviewer note:
        /// This mirrors <see cref="Dictionary{TKey, TValue}.Add(TKey, TValue)"/> semantics for each entry: duplicate keys throw.
        /// Use this only during initialization.
        /// </remarks>
        /// <example>
        /// <code>
        /// var d = new OptionsConfigOverridesDefaultsDictionary&lt;string, string&gt;();
        /// d.SeedDefaults(new Dictionary&lt;string, string&gt; { ["X-Frame-Options"] = "DENY" });
        /// </code>
        /// </example>
        public void SeedDefaults(IDictionary<TKey, TValue> defaults)
        {
            if (defaults is null || defaults.Count == 0)
            {
                return;
            }

            foreach (var kvp in defaults)
            {
                _items.Add(kvp.Key, kvp.Value);
            }

            _hasDefaultSeed = _items.Count > 0;
        }

        /// <inheritdoc />
        public void Add(TKey key, TValue value)
        {
            EnsureDefaultsReplacedForOverrideWrite();
            _items.Add(key, value);
        }

        /// <summary>
        /// Attempts to add the specified key and value to the dictionary.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="value">Value.</param>
        /// <returns><see langword="true"/> if the pair was added; otherwise <see langword="false"/>.</returns>
        public bool TryAdd(TKey key, TValue value)
        {
            EnsureDefaultsReplacedForOverrideWrite();
            return _items.TryAdd(key, value);
        }

        /// <inheritdoc />
        public void Clear()
        {
            _items.Clear();

            // Treat explicit Clear as "override semantics": defaults are no longer active.
            if (_hasDefaultSeed)
            {
                _wasOverriddenByConfiguration = true;
            }
        }

        /// <inheritdoc />
        public bool ContainsKey(TKey key) => _items.ContainsKey(key);

        /// <summary>
        /// Determines whether the dictionary contains the specified value.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns><see langword="true"/> if the value exists; otherwise <see langword="false"/>.</returns>
        public bool ContainsValue(TValue value) => _items.ContainsValue(value);

        /// <inheritdoc />
        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => _items.TryGetValue(key, out value);

        /// <inheritdoc />
        public bool Remove(TKey key) => _items.Remove(key);

        /// <summary>
        /// Removes the value with the specified key from the dictionary and returns the removed value.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="value">Removed value when found.</param>
        /// <returns><see langword="true"/> if removed; otherwise <see langword="false"/>.</returns>
        public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value) => _items.Remove(key, out value);

        /// <summary>
        /// Ensures that the dictionary can hold up to a specified number of entries without resizing.
        /// </summary>
        /// <param name="capacity">The number of entries.</param>
        /// <returns>The new capacity of the dictionary.</returns>
        public int EnsureCapacity(int capacity) => _items.EnsureCapacity(capacity);

        /// <summary>
        /// Sets the capacity of the dictionary to what it would be if it had been originally initialized with all its entries.
        /// </summary>
        public void TrimExcess() => _items.TrimExcess();

        /// <summary>
        /// Sets the capacity of the dictionary to the specified number of entries.
        /// </summary>
        /// <param name="capacity">The desired capacity.</param>
        public void TrimExcess(int capacity) => _items.TrimExcess(capacity);

        /// <summary>
        /// Returns the dictionary enumerator.
        /// </summary>
        /// <returns>A <see cref="Dictionary{TKey, TValue}.Enumerator"/>.</returns>
        public Dictionary<TKey, TValue>.Enumerator GetEnumerator() => _items.GetEnumerator();

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        /// <inheritdoc />
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            EnsureDefaultsReplacedForOverrideWrite();
            ((ICollection<KeyValuePair<TKey, TValue>>)_items).Add(item);
        }

        /// <inheritdoc />
        public bool Contains(KeyValuePair<TKey, TValue> item) => ((ICollection<KeyValuePair<TKey, TValue>>)_items).Contains(item);

        /// <inheritdoc />
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) =>
            ((ICollection<KeyValuePair<TKey, TValue>>)_items).CopyTo(array, arrayIndex);

        /// <inheritdoc />
        public bool Remove(KeyValuePair<TKey, TValue> item) => ((ICollection<KeyValuePair<TKey, TValue>>)_items).Remove(item);

        private void EnsureDefaultsReplacedForOverrideWrite()
        {
            if (_hasDefaultSeed && !_wasOverriddenByConfiguration)
            {
                _items.Clear();
                _wasOverriddenByConfiguration = true;
            }
        }

        // -----------------------------
        // Non-generic IDictionary support
        // -----------------------------

        bool IDictionary.IsFixedSize => false;
        bool IDictionary.IsReadOnly => false;

        object ICollection.SyncRoot => ((ICollection)_items).SyncRoot;
        bool ICollection.IsSynchronized => ((ICollection)_items).IsSynchronized;

        ICollection IDictionary.Keys => ((IDictionary)_items).Keys;
        ICollection IDictionary.Values => ((IDictionary)_items).Values;

        object? IDictionary.this[object key]
        {
            get
            {
                if (key is null) throw new ArgumentNullException(nameof(key));
                if (key is not TKey typedKey) throw new ArgumentException($"Key must be of type {typeof(TKey).FullName}.", nameof(key));
                return _items.TryGetValue(typedKey, out var value) ? value : null;
            }
            set
            {
                EnsureDefaultsReplacedForOverrideWrite();

                if (key is null) throw new ArgumentNullException(nameof(key));
                if (key is not TKey typedKey) throw new ArgumentException($"Key must be of type {typeof(TKey).FullName}.", nameof(key));

                if (value is null && default(TValue) is not null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                if (value is not TValue typedValue)
                {
                    throw new ArgumentException($"Value must be of type {typeof(TValue).FullName}.", nameof(value));
                }

                _items[typedKey] = typedValue!;
            }
        }

        void IDictionary.Add(object key, object? value)
        {
            EnsureDefaultsReplacedForOverrideWrite();

            if (key is null) throw new ArgumentNullException(nameof(key));
            if (key is not TKey typedKey) throw new ArgumentException($"Key must be of type {typeof(TKey).FullName}.", nameof(key));

            if (value is null && default(TValue) is not null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value is not TValue typedValue)
            {
                throw new ArgumentException($"Value must be of type {typeof(TValue).FullName}.", nameof(value));
            }

            _items.Add(typedKey, typedValue!);
        }

        bool IDictionary.Contains(object key) => key is TKey typedKey && _items.ContainsKey(typedKey);

        IDictionaryEnumerator IDictionary.GetEnumerator() => ((IDictionary)_items).GetEnumerator();

        void IDictionary.Remove(object key)
        {
            if (key is TKey typedKey)
            {
                _items.Remove(typedKey);
            }
        }

        void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    }
}
