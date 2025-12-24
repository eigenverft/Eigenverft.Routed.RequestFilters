using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Eigenverft.Routed.RequestFilters.Options
{
    /// <summary>
    /// A list that is seeded with default items and automatically replaces those defaults when configuration binding writes the first configured element.
    /// If the configuration key is missing, binding performs no writes and the seeded defaults remain effective.
    /// If the configuration key contains one or more elements, binding writes values (via <see cref="ICollection{T}.Add(T)"/> and/or the indexer),
    /// which clears the defaults once so the configured values fully replace them.
    /// If the configuration key is present but an empty array (<c>[]</c>), binding typically performs no writes (because there are no children to iterate),
    /// so no override is detected and the seeded defaults remain effective.
    /// </summary>
    /// <remarks>
    /// Reviewer note:
    /// The configuration binder typically populates list-like properties by calling <see cref="ICollection{T}.Add(T)"/> and/or by using the indexer setter.
    /// This type clears the seeded default items exactly once on the first "override write" performed via <see cref="Add(T)"/>, <see cref="Insert(int, T)"/>,
    /// the indexer setter, <see cref="AddRange(IEnumerable{T})"/>, or the non-generic <see cref="IList.Add(object)"/> / indexer.
    /// <para>
    /// Intended use:
    /// Use this in options/settings classes to express: "defaults exist in code, but if configuration supplies any value, the configured values fully replace the defaults (no merging)".
    /// </para>
    /// <para>
    /// Important:
    /// - Seed defaults via <see cref="OptionsConfigOverridesDefaultsList{T}(IEnumerable{T})"/>, <see cref="OptionsConfigOverridesDefaultsList{T}(T[])"/>,
    ///   <see cref="Defaults(T[])"/>, or <see cref="SeedDefaults(IEnumerable{T})"/>.
    /// - Avoid collection initializer syntax for defaults, because it calls <see cref="Add(T)"/> and will be treated as a configuration override.
    /// </para>
    /// <para>
    /// Upgrade example (from array defaults to configuration-overrides-defaults semantics):
    /// </para>
    /// <code>
    /// // BEFORE (array): when the key exists, configuration replaces the whole array (including [] overriding to empty).
    /// public sealed class MyOptions
    /// {
    ///     public string[] Whitelist { get; set; } = new[] { "*" };
    /// }
    ///
    /// // AFTER (list wrapper): once configuration writes the first element, defaults are cleared once and configured values win.
    /// // Note: if the key is present but empty ([]), binding usually performs no writes, so defaults remain.
    /// public sealed class MyOptions
    /// {
    ///     // Minimal-change upgrade: keep the old initializer style.
    ///     public OptionsConfigOverridesDefaultsList&lt;string&gt; Whitelist { get; set; } = new[] { "*" };
    ///
    ///     // Alternative: params-style seeding.
    ///     // public OptionsConfigOverridesDefaultsList&lt;string&gt; Whitelist { get; set; } = new("*");
    /// }
    /// </code>
    /// <para>
    /// Clearing defaults explicitly:
    /// </para>
    /// <code>
    /// // If you need a reliable "empty means empty" behavior, introduce an explicit flag and clear in post-configure.
    /// public sealed class MyOptions
    /// {
    ///     public bool DisableWhitelistDefaults { get; set; } = false;
    ///     public OptionsConfigOverridesDefaultsList&lt;string&gt; Whitelist { get; set; } = new("*");
    /// }
    ///
    /// // Then in post-configure:
    /// services.PostConfigure&lt;MyOptions&gt;(o =&gt;
    /// {
    ///     if (o.DisableWhitelistDefaults)
    ///     {
    ///         o.Whitelist.Clear(); // forces override semantics and results in empty list
    ///     }
    /// });
    /// </code>
    /// </remarks>
    /// <typeparam name="T">Item type.</typeparam>
    public sealed class OptionsConfigOverridesDefaultsList<T> : IList<T>, IReadOnlyList<T>, IList
    {
        private readonly List<T> _items;
        private bool _hasDefaultSeed;
        private bool _wasOverriddenByConfiguration;

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsConfigOverridesDefaultsList{T}"/> class with no defaults.
        /// </summary>
        public OptionsConfigOverridesDefaultsList()
        {
            _items = new List<T>();
            _hasDefaultSeed = false;
            _wasOverriddenByConfiguration = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsConfigOverridesDefaultsList{T}"/> class seeded with defaults.
        /// </summary>
        /// <param name="defaults">Default items to seed.</param>
        public OptionsConfigOverridesDefaultsList(IEnumerable<T> defaults)
        {
            _items = defaults is null ? new List<T>() : new List<T>(defaults);
            _hasDefaultSeed = _items.Count > 0;
            _wasOverriddenByConfiguration = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsConfigOverridesDefaultsList{T}"/> class seeded with defaults.
        /// </summary>
        /// <remarks>
        /// Reviewer note:
        /// This overload exists to support common "params-style" usage such as:
        /// <code>
        /// public OptionsConfigOverridesDefaultsList{string} Whitelist { get; set; } = new("HTTP/2", "HTTP/3");
        /// </code>
        /// </remarks>
        /// <param name="defaults">Default items to seed.</param>
        public OptionsConfigOverridesDefaultsList(params T[] defaults)
            : this((IEnumerable<T>)(defaults ?? Array.Empty<T>()))
        {
        }

        /// <summary>
        /// Creates a new instance seeded with the provided default items.
        /// </summary>
        /// <param name="defaults">Default items.</param>
        /// <returns>A seeded <see cref="OptionsConfigOverridesDefaultsList{T}"/>.</returns>
        public static OptionsConfigOverridesDefaultsList<T> Defaults(params T[] defaults)
        {
            return new OptionsConfigOverridesDefaultsList<T>(defaults ?? Array.Empty<T>());
        }

        /// <summary>
        /// Gets a value indicating whether this instance was seeded with default items.
        /// </summary>
        public bool HasDefaultSeed => _hasDefaultSeed;

        /// <summary>
        /// Gets a value indicating whether the seeded defaults were replaced by configuration (or any "override write").
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
        /// Gets the current items as a read-only view (no allocation).
        /// </summary>
        public IReadOnlyList<T> Items => _items;

        /// <summary>
        /// Gets or sets the underlying list capacity.
        /// </summary>
        public int Capacity
        {
            get => _items.Capacity;
            set => _items.Capacity = value;
        }

        /// <summary>
        /// Gets the number of elements in the list.
        /// </summary>
        public int Count => _items.Count;

        /// <summary>
        /// Gets the number of elements in the list (array-like alias for <see cref="Count"/>).
        /// </summary>
        public int Length => _items.Count;

        /// <summary>
        /// Gets a value indicating whether the list is empty.
        /// </summary>
        public bool IsEmpty => _items.Count == 0;

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <summary>
        /// Returns a new array containing the current items.
        /// </summary>
        public T[] ToArray() => _items.ToArray();

        /// <summary>
        /// Returns a new list containing the current items.
        /// </summary>
        public List<T> ToList() => new List<T>(_items);

        /// <summary>
        /// Returns a read-only wrapper around the current items.
        /// </summary>
        public ReadOnlyCollection<T> AsReadOnly() => _items.AsReadOnly();

        /// <summary>
        /// Gets or sets the element at the given <see cref="Index"/> (supports <c>^1</c> etc).
        /// </summary>
        /// <param name="index">Index.</param>
        /// <returns>Element.</returns>
        public T this[Index index]
        {
            get => _items[index.GetOffset(_items.Count)];
            set
            {
                EnsureDefaultsReplacedForOverrideWrite();
                _items[index.GetOffset(_items.Count)] = value;
            }
        }

        /// <summary>
        /// Returns a new array containing the elements described by the given <see cref="Range"/>.
        /// </summary>
        /// <remarks>
        /// Reviewer note:
        /// This allocates a new array (slice).
        /// </remarks>
        /// <param name="range">Range.</param>
        /// <returns>Array slice.</returns>
        public T[] Slice(Range range)
        {
            var (offset, length) = range.GetOffsetAndLength(_items.Count);
            var arr = new T[length];
            for (int i = 0; i < length; i++)
            {
                arr[i] = _items[offset + i];
            }

            return arr;
        }

        /// <summary>
        /// Executes an action for each element in the list.
        /// </summary>
        /// <param name="action">Action.</param>
        public void ForEach(Action<T> action)
        {
            if (action is null) throw new ArgumentNullException(nameof(action));
            _items.ForEach(action);
        }

        /// <summary>
        /// Seeds defaults without triggering override semantics.
        /// </summary>
        /// <remarks>
        /// Use this only during initialization. If defaults were already seeded, this appends to them.
        /// This method does not clear existing items and does not mark defaults as overridden.
        /// </remarks>
        /// <param name="defaults">Defaults to add.</param>
        public void SeedDefaults(IEnumerable<T> defaults)
        {
            if (defaults is null)
            {
                return;
            }

            // Intentionally does NOT call EnsureDefaultsReplacedForOverrideWrite().
            _items.AddRange(defaults);

            if (_items.Count > 0)
            {
                _hasDefaultSeed = true;
            }
        }

        /// <inheritdoc />
        public T this[int index]
        {
            get => _items[index];
            set
            {
                EnsureDefaultsReplacedForOverrideWrite();
                _items[index] = value;
            }
        }

        /// <inheritdoc />
        public void Add(T item)
        {
            EnsureDefaultsReplacedForOverrideWrite();
            _items.Add(item);
        }

        /// <summary>
        /// Adds a range of items, replacing defaults once if needed.
        /// </summary>
        /// <param name="items">Items to add.</param>
        public void AddRange(IEnumerable<T> items)
        {
            if (items is null)
            {
                return;
            }

            EnsureDefaultsReplacedForOverrideWrite();
            _items.AddRange(items);
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
        public bool Contains(T item) => _items.Contains(item);

        /// <inheritdoc />
        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        /// <inheritdoc />
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        /// <inheritdoc />
        public int IndexOf(T item) => _items.IndexOf(item);

        /// <inheritdoc />
        public void Insert(int index, T item)
        {
            EnsureDefaultsReplacedForOverrideWrite();
            _items.Insert(index, item);
        }

        /// <inheritdoc />
        public bool Remove(T item) => _items.Remove(item);

        /// <inheritdoc />
        public void RemoveAt(int index) => _items.RemoveAt(index);

        /// <summary>
        /// Determines whether the list contains elements that match the specified predicate.
        /// </summary>
        /// <param name="match">Predicate.</param>
        /// <returns><see langword="true"/> when any element matches; otherwise <see langword="false"/>.</returns>
        public bool Exists(Predicate<T> match) => _items.Exists(match);

        /// <summary>
        /// Finds the first element that matches the specified predicate.
        /// </summary>
        /// <param name="match">Predicate.</param>
        /// <returns>The found element or default.</returns>
        public T? Find(Predicate<T> match) => _items.Find(match);

        /// <summary>
        /// Retrieves all elements that match the specified predicate.
        /// </summary>
        /// <param name="match">Predicate.</param>
        /// <returns>Matching elements.</returns>
        public List<T> FindAll(Predicate<T> match) => _items.FindAll(match);

        /// <summary>
        /// Searches for an element that matches the specified predicate and returns the index.
        /// </summary>
        /// <param name="match">Predicate.</param>
        /// <returns>Index or -1.</returns>
        public int FindIndex(Predicate<T> match) => _items.FindIndex(match);

        /// <summary>
        /// Determines whether all elements match the specified predicate.
        /// </summary>
        /// <param name="match">Predicate.</param>
        /// <returns><see langword="true"/> when all elements match; otherwise <see langword="false"/>.</returns>
        public bool TrueForAll(Predicate<T> match) => _items.TrueForAll(match);

        private void EnsureDefaultsReplacedForOverrideWrite()
        {
            if (_hasDefaultSeed && !_wasOverriddenByConfiguration)
            {
                _items.Clear();
                _wasOverriddenByConfiguration = true;
            }
        }

        /// <summary>
        /// Implicitly converts this list to an array by calling <see cref="ToArray"/>.
        /// </summary>
        /// <remarks>
        /// Reviewer note:
        /// Defensive behavior: when <paramref name="list"/> is <see langword="null"/>, this returns <see cref="Array.Empty{T}"/> to avoid hard failures.
        /// </remarks>
        /// <param name="list">List.</param>
        public static implicit operator T[](OptionsConfigOverridesDefaultsList<T> list)
        {
            return list?.ToArray() ?? Array.Empty<T>();
        }

        /// <summary>
        /// Implicitly converts this list to a new <see cref="List{T}"/> by calling <see cref="ToList"/>.
        /// </summary>
        /// <remarks>
        /// Reviewer note:
        /// Defensive behavior: when <paramref name="list"/> is <see langword="null"/>, this returns a new empty <see cref="List{T}"/> to avoid hard failures.
        /// </remarks>
        /// <param name="list">List.</param>
        public static implicit operator List<T>(OptionsConfigOverridesDefaultsList<T> list)
        {
            return list?.ToList() ?? new List<T>();
        }

        /// <summary>
        /// Implicitly converts an array into a new list seeded with those items as defaults.
        /// </summary>
        /// <remarks>
        /// Reviewer note:
        /// This is primarily a usability feature to make upgrades from <c>T[]</c> to
        /// <see cref="OptionsConfigOverridesDefaultsList{T}"/> almost drop-in:
        /// <code>
        /// public OptionsConfigOverridesDefaultsList{string} Whitelist { get; set; } = new[] { "HTTP/2", "HTTP/3" };
        /// </code>
        /// The created instance is considered "seeded with defaults" until configuration writes an item (which then clears it once).
        /// </remarks>
        /// <param name="defaults">Default items.</param>
        public static implicit operator OptionsConfigOverridesDefaultsList<T>(T[] defaults)
        {
            return new OptionsConfigOverridesDefaultsList<T>(defaults ?? Array.Empty<T>());
        }

        // -----------------------------
        // Non-generic IList support
        // -----------------------------

        bool IList.IsFixedSize => false;
        bool IList.IsReadOnly => false;

        object ICollection.SyncRoot => ((ICollection)_items).SyncRoot;
        bool ICollection.IsSynchronized => ((ICollection)_items).IsSynchronized;

        object? IList.this[int index]
        {
            get => _items[index];
            set
            {
                EnsureDefaultsReplacedForOverrideWrite();

                if (value is null && default(T) is not null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                if (value is not T typed)
                {
                    throw new ArgumentException($"Value must be of type {typeof(T).FullName}.", nameof(value));
                }

                _items[index] = typed;
            }
        }

        int IList.Add(object? value)
        {
            EnsureDefaultsReplacedForOverrideWrite();

            if (value is null && default(T) is not null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value is not T typed)
            {
                throw new ArgumentException($"Value must be of type {typeof(T).FullName}.", nameof(value));
            }

            _items.Add(typed);
            return _items.Count - 1;
        }

        bool IList.Contains(object? value) => value is T t && _items.Contains(t);

        int IList.IndexOf(object? value) => value is T t ? _items.IndexOf(t) : -1;

        void IList.Insert(int index, object? value)
        {
            EnsureDefaultsReplacedForOverrideWrite();

            if (value is null && default(T) is not null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value is not T typed)
            {
                throw new ArgumentException($"Value must be of type {typeof(T).FullName}.", nameof(value));
            }

            _items.Insert(index, typed);
        }

        void IList.Remove(object? value)
        {
            if (value is T t)
            {
                _items.Remove(t);
            }
        }

        void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    }
}
