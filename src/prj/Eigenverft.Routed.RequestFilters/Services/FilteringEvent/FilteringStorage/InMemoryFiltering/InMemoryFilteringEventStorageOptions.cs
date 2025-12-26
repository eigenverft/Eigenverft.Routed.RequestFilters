using System;

namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.InMemoryFiltering
{
    /// <summary>
    /// Defines what happens when the store estimates it has exceeded its memory budget.
    /// </summary>
    public enum InMemoryFilteringEventStorageOverflowBehavior
    {
        /// <summary>
        /// Stops accepting new events while over the limit (existing data remains).
        /// </summary>
        DropNewEvents = 0,

        /// <summary>
        /// Evicts least-recently-used remote IP buckets until memory is under the target threshold.
        /// </summary>
        EvictOldestIpBuckets = 1,

        /// <summary>
        /// Clears all stored data.
        /// </summary>
        /// <remarks>
        /// Reviewer note: In the current in-memory implementation, this value is treated as a configuration
        /// signal only and is not executed as a side effect of <c>StoreAsync</c>. Wiping the dataset is
        /// intentionally reserved for explicit administrative calls to <c>ClearAsync</c>.
        /// </remarks>
        ClearAll = 2,
    }

    /// <summary>
    /// Options for <see cref="InMemoryFilteringEventStorage"/>.
    /// </summary>
    /// <remarks>
    /// Example <c>appsettings.json</c> configuration:
    /// <code>
    /// {
    ///   "InMemoryFilteringEventStorageOptions": {
    ///     "MemoryLimitBytes": 33554432,
    ///     "OverflowBehavior": "EvictOldestIpBuckets",
    ///     "TrimTargetRatio": 0.9,
    ///     "TrimCooldown": "00:00:01",
    ///     "MaxCandidateScanCount": 10000,
    ///     "MaxEvictionsPerTrim": 512
    ///   }
    /// }
    /// </code>
    /// </remarks>
    public sealed class InMemoryFilteringEventStorageOptions
    {
        /// <summary>
        /// Default: 32 MiB.
        /// </summary>
        public const long DefaultMemoryLimitBytes = 32L * 1024L * 1024L;

        /// <summary>
        /// Rough upper bound for the in-memory footprint.
        /// A value of 0 disables limiting.
        /// </summary>
        public long MemoryLimitBytes { get; set; } = DefaultMemoryLimitBytes;

        /// <summary>
        /// Defines what the store should do when it detects memory pressure.
        /// </summary>
        public InMemoryFilteringEventStorageOverflowBehavior OverflowBehavior { get; set; }
            = InMemoryFilteringEventStorageOverflowBehavior.EvictOldestIpBuckets;

        /// <summary>
        /// Target ratio of <see cref="MemoryLimitBytes"/> after a trim pass.
        /// </summary>
        public double TrimTargetRatio { get; set; } = 0.90;

        /// <summary>
        /// Prevents trimming too frequently under sustained load.
        /// </summary>
        public TimeSpan TrimCooldown { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Limits how many buckets are considered during a trim pass.
        /// </summary>
        public int MaxCandidateScanCount { get; set; } = 10_000;

        /// <summary>
        /// Limits how many buckets may be evicted in a single trim pass.
        /// </summary>
        public int MaxEvictionsPerTrim { get; set; } = 512;
    }
}
