using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.InMemoryFiltering
{
    /// <summary>
    /// In-memory implementation of <see cref="IFilteringEventStorage"/> with approximate memory limiting.
    /// </summary>
    /// <remarks>
    /// This storage keeps per-remote-IP counters grouped by event source and match kind.
    /// It uses a rough memory estimate (not exact heap measurement) to decide when to apply pressure handling
    /// defined by <see cref="InMemoryFilteringEventStorageOptions"/>.
    ///
    /// Reviewer note: Concurrency model:
    /// - A global reader/writer gate makes <see cref="IFilteringEventStorage.ClearAsync"/> atomic w.r.t. all other operations.
    /// - A per-IP bucket lock serializes Store/Read/Remove for the same remote IP.
    ///
    /// Reviewer note: Option A:
    /// - <see cref="InMemoryFilteringEventStorageOverflowBehavior.ClearAll"/> is NOT executed from <see cref="StoreAsync"/>.
    ///   If configured, it is treated as "evict oldest" (with a warning) to avoid surprising side effects on writes.
    ///   External code can still explicitly wipe data via <see cref="IFilteringEventStorage.ClearAsync"/>.
    /// </remarks>
    public sealed class InMemoryFilteringEventStorage : IFilteringEventStorage, IDisposable
    {
        private sealed class IpBucket
        {
            public readonly object Sync = new();

            public readonly ConcurrentDictionary<(string Source, FilterMatchKind Kind), long> Counts = new();

            public long BlacklistTotal;
            public long UnmatchedTotal;

            public long ApproxBytes;
            public long LastAccessTick;

            public int IsEvicted;
        }

        // Conservative estimation constants (intentionally approximate).
        // The intent is to drive trim decisions, not to perfectly model managed memory.
        private const long ApproxIpBucketOverheadBytes = 256;
        private const long ApproxCountsEntryOverheadBytes = 192;
        private const long ApproxStringOverheadBytes = 40;

        private readonly IOptionsMonitor<InMemoryFilteringEventStorageOptions> _optionsMonitor;
        private readonly IDeferredLogger<InMemoryFilteringEventStorage> _logger;
        private readonly IDisposable? _optionsReloadSubscription;

        // Global gate to provide atomic ClearAsync.
        // - All normal operations take a read lock.
        // - ClearAsync takes a write lock.
        private readonly ReaderWriterLockSlim _clearGate = new(LockRecursionPolicy.NoRecursion);

        // ip -> per-ip bucket store (fast lookups by ip)
        private readonly ConcurrentDictionary<string, IpBucket> _byIp = new(StringComparer.Ordinal);

        // Total of bucket.ApproxBytes deltas across the store (approximate).
        private long _approxTotalBytes;

        // Guard to prevent concurrent trim passes.
        private int _trimInProgress;
        private long _lastTrimTick;

        // Throttle for repeated "dropping" warnings.
        private long _lastDropLogTick;

        // Throttle for repeated "ClearAll configured but ignored" warnings.
        private long _lastClearAllIgnoredLogTick;

        /// <summary>
        /// Initializes a new instance of the storage.
        /// </summary>
        /// <param name="optionsMonitor">Options monitor controlling memory limiting behavior.</param>
        /// <param name="logger">Deferred logger used for internal diagnostics.</param>
        /// <remarks>
        /// This constructor logs the current effective options once at debug level.
        /// If the underlying configuration is reloadable, it also subscribes to option changes and logs them at debug level.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="optionsMonitor"/> or <paramref name="logger"/> is null.</exception>
        public InMemoryFilteringEventStorage(
            IOptionsMonitor<InMemoryFilteringEventStorageOptions> optionsMonitor,
            IDeferredLogger<InMemoryFilteringEventStorage> logger)
        {
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // One-time "configured" emit.
            LogConfiguredStats(_optionsMonitor.CurrentValue, isChange: false);

            // Optional: emit on change (only fires when options actually change via reload/binding).
            _optionsReloadSubscription = _optionsMonitor.OnChange(OnOptionsChanged);
        }

        /// <summary>
        /// Disposes the options change subscription and the global gate.
        /// </summary>
        /// <remarks>
        /// If this instance is registered as a singleton in DI, the container will call <see cref="Dispose"/> during shutdown.
        /// </remarks>
        public void Dispose()
        {
            _optionsReloadSubscription?.Dispose();
            _clearGate.Dispose();
        }

        /// <inheritdoc />
        public Task StoreAsync(FilteringEvent record, CancellationToken cancellationToken = default)
        {
            if (record is null) throw new ArgumentNullException(nameof(record));
            cancellationToken.ThrowIfCancellationRequested();

            var ipRaw = record.RemoteIpAddress;
            if (string.IsNullOrWhiteSpace(ipRaw))
            {
                return Task.CompletedTask;
            }

            string ip = ipRaw.Trim();
            InMemoryFilteringEventStorageOptions options = _optionsMonitor.CurrentValue;
            long now = Environment.TickCount64;

            _clearGate.EnterReadLock();
            try
            {
                // Fast path for DropNewEvents: if over limit, do not mutate state.
                if (options.MemoryLimitBytes > 0 &&
                    options.OverflowBehavior == InMemoryFilteringEventStorageOverflowBehavior.DropNewEvents &&
                    Volatile.Read(ref _approxTotalBytes) >= options.MemoryLimitBytes)
                {
                    ThrottledLogDrop(now, options);
                    return Task.CompletedTask;
                }

                // Retry loop covers races with evicted buckets.
                for (; ; )
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    IpBucket bucket = GetOrCreateBucket(ip, now);

                    bool needsEvictedCleanup;
                    lock (bucket.Sync)
                    {
                        needsEvictedCleanup = bucket.IsEvicted != 0;

                        if (!needsEvictedCleanup)
                        {
                            Volatile.Write(ref bucket.LastAccessTick, now);

                            string source = (record.EventSource ?? string.Empty).Trim();
                            FilterMatchKind kind = record.MatchKind;
                            var key = (Source: source, Kind: kind);

                            if (!bucket.Counts.TryGetValue(key, out var current))
                            {
                                bucket.Counts[key] = 1L;

                                long delta = EstimateCountsEntryBytes(source);
                                bucket.ApproxBytes += delta;
                                Interlocked.Add(ref _approxTotalBytes, delta);
                            }
                            else
                            {
                                bucket.Counts[key] = current + 1L;
                            }

                            if (kind == FilterMatchKind.Blacklist)
                            {
                                bucket.BlacklistTotal++;
                            }
                            else if (kind == FilterMatchKind.Unmatched)
                            {
                                bucket.UnmatchedTotal++;
                            }
                        }
                    }

                    if (!needsEvictedCleanup)
                    {
                        break;
                    }

                    // Bucket was evicted; best-effort cleanup of the mapping (only one thread wins).
                    TryRemoveBucketMapping(ip, bucket);
                }

                // Apply memory policy if we crossed the configured limit.
                if (options.MemoryLimitBytes > 0 && Volatile.Read(ref _approxTotalBytes) > options.MemoryLimitBytes)
                {
                    switch (options.OverflowBehavior)
                    {
                        case InMemoryFilteringEventStorageOverflowBehavior.DropNewEvents:
                            // Dropping is handled before writes, so do nothing here.
                            break;

                        case InMemoryFilteringEventStorageOverflowBehavior.ClearAll:
                            // Option A: never clear from StoreAsync.
                            // Treat "ClearAll" as "EvictOldestIpBuckets" and warn (throttled).
                            ThrottledLogClearAllIgnored(now, options);
                            ApplyMemoryLimitEvictOldest(options, ipToKeep: ip, now: now);
                            break;

                        case InMemoryFilteringEventStorageOverflowBehavior.EvictOldestIpBuckets:
                        default:
                            ApplyMemoryLimitEvictOldest(options, ipToKeep: ip, now: now);
                            break;
                    }
                }
            }
            finally
            {
                _clearGate.ExitReadLock();
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public int GetBlacklistCount(string remoteIpAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return 0;
            }

            var ip = remoteIpAddress.Trim();

            _clearGate.EnterReadLock();
            try
            {
                if (!_byIp.TryGetValue(ip, out var bucket))
                {
                    return 0;
                }

                lock (bucket.Sync)
                {
                    if (bucket.IsEvicted != 0)
                    {
                        return 0;
                    }

                    Volatile.Write(ref bucket.LastAccessTick, Environment.TickCount64);

                    long total = bucket.BlacklistTotal;
                    if (total <= 0) return 0;
                    return total > int.MaxValue ? int.MaxValue : (int)total;
                }
            }
            finally
            {
                _clearGate.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public int GetUnmatchedCount(string remoteIpAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return 0;
            }

            var ip = remoteIpAddress.Trim();

            _clearGate.EnterReadLock();
            try
            {
                if (!_byIp.TryGetValue(ip, out var bucket))
                {
                    return 0;
                }

                lock (bucket.Sync)
                {
                    if (bucket.IsEvicted != 0)
                    {
                        return 0;
                    }

                    Volatile.Write(ref bucket.LastAccessTick, Environment.TickCount64);

                    long total = bucket.UnmatchedTotal;
                    if (total <= 0) return 0;
                    return total > int.MaxValue ? int.MaxValue : (int)total;
                }
            }
            finally
            {
                _clearGate.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public IReadOnlyCollection<FilteringEventBySourceAndMatchAggregate> GetByEventSourceAndMatchKind(string remoteIpAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return Array.Empty<FilteringEventBySourceAndMatchAggregate>();
            }

            var ip = remoteIpAddress.Trim();

            _clearGate.EnterReadLock();
            try
            {
                if (!_byIp.TryGetValue(ip, out var bucket))
                {
                    return Array.Empty<FilteringEventBySourceAndMatchAggregate>();
                }

                lock (bucket.Sync)
                {
                    if (bucket.IsEvicted != 0 || bucket.Counts.IsEmpty)
                    {
                        return Array.Empty<FilteringEventBySourceAndMatchAggregate>();
                    }

                    Volatile.Write(ref bucket.LastAccessTick, Environment.TickCount64);

                    var results = new List<FilteringEventBySourceAndMatchAggregate>(bucket.Counts.Count);
                    foreach (var kvp in bucket.Counts)
                    {
                        results.Add(new FilteringEventBySourceAndMatchAggregate(ip, kvp.Key.Source, kvp.Key.Kind, kvp.Value));
                    }

                    return results.Count == 0 ? Array.Empty<FilteringEventBySourceAndMatchAggregate>() : results;
                }
            }
            finally
            {
                _clearGate.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public IReadOnlyCollection<FilteringEventBySourceAggregate> GetByEventSource(string remoteIpAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return Array.Empty<FilteringEventBySourceAggregate>();
            }

            var ip = remoteIpAddress.Trim();

            _clearGate.EnterReadLock();
            try
            {
                if (!_byIp.TryGetValue(ip, out var bucket))
                {
                    return Array.Empty<FilteringEventBySourceAggregate>();
                }

                lock (bucket.Sync)
                {
                    if (bucket.IsEvicted != 0 || bucket.Counts.IsEmpty)
                    {
                        return Array.Empty<FilteringEventBySourceAggregate>();
                    }

                    Volatile.Write(ref bucket.LastAccessTick, Environment.TickCount64);

                    var totals = new Dictionary<string, long>(StringComparer.Ordinal);
                    foreach (var kvp in bucket.Counts)
                    {
                        var source = kvp.Key.Source;
                        if (totals.TryGetValue(source, out var current))
                        {
                            totals[source] = current + kvp.Value;
                        }
                        else
                        {
                            totals.Add(source, kvp.Value);
                        }
                    }

                    if (totals.Count == 0)
                    {
                        return Array.Empty<FilteringEventBySourceAggregate>();
                    }

                    var results = new List<FilteringEventBySourceAggregate>(totals.Count);
                    foreach (var entry in totals)
                    {
                        results.Add(new FilteringEventBySourceAggregate(ip, entry.Key, entry.Value));
                    }

                    return results;
                }
            }
            finally
            {
                _clearGate.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public IReadOnlyCollection<FilteringEventByMatchAggregate> GetByMatchKind(string remoteIpAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return Array.Empty<FilteringEventByMatchAggregate>();
            }

            var ip = remoteIpAddress.Trim();

            _clearGate.EnterReadLock();
            try
            {
                if (!_byIp.TryGetValue(ip, out var bucket))
                {
                    return Array.Empty<FilteringEventByMatchAggregate>();
                }

                lock (bucket.Sync)
                {
                    if (bucket.IsEvicted != 0 || bucket.Counts.IsEmpty)
                    {
                        return Array.Empty<FilteringEventByMatchAggregate>();
                    }

                    Volatile.Write(ref bucket.LastAccessTick, Environment.TickCount64);

                    var totals = new Dictionary<FilterMatchKind, long>();
                    foreach (var kvp in bucket.Counts)
                    {
                        var kind = kvp.Key.Kind;
                        if (totals.TryGetValue(kind, out var current))
                        {
                            totals[kind] = current + kvp.Value;
                        }
                        else
                        {
                            totals.Add(kind, kvp.Value);
                        }
                    }

                    if (totals.Count == 0)
                    {
                        return Array.Empty<FilteringEventByMatchAggregate>();
                    }

                    var results = new List<FilteringEventByMatchAggregate>(totals.Count);
                    foreach (var entry in totals)
                    {
                        results.Add(new FilteringEventByMatchAggregate(ip, entry.Key, entry.Value));
                    }

                    return results;
                }
            }
            finally
            {
                _clearGate.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public Task<bool> RemoveByRemoteIpAddressAsync(string remoteIpAddress, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return Task.FromResult(false);
            }

            var ip = remoteIpAddress.Trim();

            _clearGate.EnterReadLock();
            try
            {
                if (!_byIp.TryGetValue(ip, out var bucket))
                {
                    return Task.FromResult(false);
                }

                bool markedEvicted;
                lock (bucket.Sync)
                {
                    markedEvicted = bucket.IsEvicted == 0;
                    bucket.IsEvicted = 1;
                }

                if (!markedEvicted)
                {
                    return Task.FromResult(false);
                }

                return Task.FromResult(TryRemoveBucketMapping(ip, bucket));
            }
            finally
            {
                _clearGate.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public Task<bool> RemoveByRemoteIpAddressAsync(string remoteIpAddress, string eventSource, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return Task.FromResult(false);
            }

            var ip = remoteIpAddress.Trim();
            string source = (eventSource ?? string.Empty).Trim();

            _clearGate.EnterReadLock();
            try
            {
                if (!_byIp.TryGetValue(ip, out var bucket))
                {
                    return Task.FromResult(false);
                }

                bool removedAny = false;
                bool removeBucket = false;

                lock (bucket.Sync)
                {
                    if (bucket.IsEvicted != 0)
                    {
                        return Task.FromResult(false);
                    }

                    int i = 0;
                    foreach (var kvp in bucket.Counts)
                    {
                        if ((++i & 0x3F) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        if (!string.Equals(kvp.Key.Source, source, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (bucket.Counts.TryRemove(kvp.Key, out var removedCount))
                        {
                            removedAny = true;

                            if (kvp.Key.Kind == FilterMatchKind.Blacklist)
                            {
                                bucket.BlacklistTotal -= removedCount;
                                if (bucket.BlacklistTotal < 0) bucket.BlacklistTotal = 0;
                            }
                            else if (kvp.Key.Kind == FilterMatchKind.Unmatched)
                            {
                                bucket.UnmatchedTotal -= removedCount;
                                if (bucket.UnmatchedTotal < 0) bucket.UnmatchedTotal = 0;
                            }

                            long delta = EstimateCountsEntryBytes(kvp.Key.Source);
                            bucket.ApproxBytes -= delta;
                            Interlocked.Add(ref _approxTotalBytes, -delta);
                        }
                    }

                    if (bucket.Counts.IsEmpty)
                    {
                        bucket.IsEvicted = 1;
                        removeBucket = true;
                    }
                }

                if (removeBucket)
                {
                    TryRemoveBucketMapping(ip, bucket);
                }

                return Task.FromResult(removedAny);
            }
            finally
            {
                _clearGate.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public Task<bool> RemoveByRemoteIpAddressAsync(string remoteIpAddress, FilterMatchKind matchKind, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return Task.FromResult(false);
            }

            var ip = remoteIpAddress.Trim();

            _clearGate.EnterReadLock();
            try
            {
                if (!_byIp.TryGetValue(ip, out var bucket))
                {
                    return Task.FromResult(false);
                }

                bool removedAny = false;
                bool removeBucket = false;

                lock (bucket.Sync)
                {
                    if (bucket.IsEvicted != 0)
                    {
                        return Task.FromResult(false);
                    }

                    int i = 0;
                    foreach (var kvp in bucket.Counts)
                    {
                        if ((++i & 0x3F) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        if (kvp.Key.Kind != matchKind)
                        {
                            continue;
                        }

                        if (bucket.Counts.TryRemove(kvp.Key, out var removedCount))
                        {
                            removedAny = true;

                            if (matchKind == FilterMatchKind.Blacklist)
                            {
                                bucket.BlacklistTotal -= removedCount;
                                if (bucket.BlacklistTotal < 0) bucket.BlacklistTotal = 0;
                            }
                            else if (matchKind == FilterMatchKind.Unmatched)
                            {
                                bucket.UnmatchedTotal -= removedCount;
                                if (bucket.UnmatchedTotal < 0) bucket.UnmatchedTotal = 0;
                            }

                            long delta = EstimateCountsEntryBytes(kvp.Key.Source);
                            bucket.ApproxBytes -= delta;
                            Interlocked.Add(ref _approxTotalBytes, -delta);
                        }
                    }

                    if (bucket.Counts.IsEmpty)
                    {
                        bucket.IsEvicted = 1;
                        removeBucket = true;
                    }
                }

                if (removeBucket)
                {
                    TryRemoveBucketMapping(ip, bucket);
                }

                return Task.FromResult(removedAny);
            }
            finally
            {
                _clearGate.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public Task<bool> RemoveByRemoteIpAddressAsync(string remoteIpAddress, string eventSource, FilterMatchKind matchKind, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return Task.FromResult(false);
            }

            var ip = remoteIpAddress.Trim();
            string source = (eventSource ?? string.Empty).Trim();
            var key = (Source: source, Kind: matchKind);

            _clearGate.EnterReadLock();
            try
            {
                if (!_byIp.TryGetValue(ip, out var bucket))
                {
                    return Task.FromResult(false);
                }

                bool removeBucket = false;

                lock (bucket.Sync)
                {
                    if (bucket.IsEvicted != 0)
                    {
                        return Task.FromResult(false);
                    }

                    if (!bucket.Counts.TryRemove(key, out var removedCount))
                    {
                        return Task.FromResult(false);
                    }

                    if (matchKind == FilterMatchKind.Blacklist)
                    {
                        bucket.BlacklistTotal -= removedCount;
                        if (bucket.BlacklistTotal < 0) bucket.BlacklistTotal = 0;
                    }
                    else if (matchKind == FilterMatchKind.Unmatched)
                    {
                        bucket.UnmatchedTotal -= removedCount;
                        if (bucket.UnmatchedTotal < 0) bucket.UnmatchedTotal = 0;
                    }

                    long delta = EstimateCountsEntryBytes(source);
                    bucket.ApproxBytes -= delta;
                    Interlocked.Add(ref _approxTotalBytes, -delta);

                    if (bucket.Counts.IsEmpty)
                    {
                        bucket.IsEvicted = 1;
                        removeBucket = true;
                    }
                }

                if (removeBucket)
                {
                    TryRemoveBucketMapping(ip, bucket);
                }

                return Task.FromResult(true);
            }
            finally
            {
                _clearGate.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _clearGate.EnterWriteLock();
            try
            {
                foreach (var kvp in _byIp)
                {
                    var bucket = kvp.Value;
                    lock (bucket.Sync)
                    {
                        bucket.IsEvicted = 1;
                    }
                }

                _byIp.Clear();
                Volatile.Write(ref _approxTotalBytes, 0);

                return Task.CompletedTask;
            }
            finally
            {
                _clearGate.ExitWriteLock();
            }
        }

        private void OnOptionsChanged(InMemoryFilteringEventStorageOptions options)
        {
            LogConfiguredStats(options, isChange: true);
        }

        private void LogConfiguredStats(InMemoryFilteringEventStorageOptions o, bool isChange)
        {
            if (!_logger.IsEnabled(LogLevel.Debug))
            {
                return;
            }

            _logger.LogDebug(
                isChange
                    ? "In-memory filtering store options changed: MemoryLimitBytes={MemoryLimitBytes}, OverflowBehavior={OverflowBehavior}, TrimTargetRatio={TrimTargetRatio}, TrimCooldown={TrimCooldown}, MaxCandidateScanCount={MaxCandidateScanCount}, MaxEvictionsPerTrim={MaxEvictionsPerTrim}."
                    : "In-memory filtering store configured: MemoryLimitBytes={MemoryLimitBytes}, OverflowBehavior={OverflowBehavior}, TrimTargetRatio={TrimTargetRatio}, TrimCooldown={TrimCooldown}, MaxCandidateScanCount={MaxCandidateScanCount}, MaxEvictionsPerTrim={MaxEvictionsPerTrim}.",
                () => o.MemoryLimitBytes,
                () => o.OverflowBehavior,
                () => o.TrimTargetRatio,
                () => o.TrimCooldown,
                () => o.MaxCandidateScanCount,
                () => o.MaxEvictionsPerTrim);
        }

        private IpBucket GetOrCreateBucket(string ip, long now)
        {
            if (_byIp.TryGetValue(ip, out var existing))
            {
                return existing;
            }

            for (; ; )
            {
                var created = new IpBucket
                {
                    LastAccessTick = now,
                    ApproxBytes = ApproxIpBucketOverheadBytes + EstimateStringBytes(ip),
                };

                if (_byIp.TryAdd(ip, created))
                {
                    Interlocked.Add(ref _approxTotalBytes, created.ApproxBytes);
                    return created;
                }

                if (_byIp.TryGetValue(ip, out existing))
                {
                    return existing;
                }
            }
        }

        private bool TryRemoveBucketMapping(string ip, IpBucket bucket)
        {
            if (_byIp.TryRemove(new KeyValuePair<string, IpBucket>(ip, bucket)))
            {
                long removedBytes;
                lock (bucket.Sync)
                {
                    removedBytes = bucket.ApproxBytes;
                }

                Interlocked.Add(ref _approxTotalBytes, -removedBytes);
                return true;
            }

            return false;
        }

        private void ApplyMemoryLimitEvictOldest(InMemoryFilteringEventStorageOptions options, string ipToKeep, long now)
        {
            long last = Volatile.Read(ref _lastTrimTick);
            if (now - last < (long)options.TrimCooldown.TotalMilliseconds)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _trimInProgress, 1, 0) != 0)
            {
                return;
            }

            try
            {
                Volatile.Write(ref _lastTrimTick, now);
                EvictOldestUntilUnderTarget(options, ipToKeep);
            }
            finally
            {
                Volatile.Write(ref _trimInProgress, 0);
            }
        }

        private void EvictOldestUntilUnderTarget(InMemoryFilteringEventStorageOptions options, string ipToKeep)
        {
            long limit = options.MemoryLimitBytes;
            if (limit <= 0)
            {
                return;
            }

            double ratio = options.TrimTargetRatio;
            if (ratio <= 0.10) ratio = 0.10;
            if (ratio > 1.0) ratio = 1.0;

            long target = (long)(limit * ratio);
            long before = Volatile.Read(ref _approxTotalBytes);

            var candidates = new List<(string Ip, long LastAccess)>(capacity: 1024);

            int scanned = 0;
            foreach (var kvp in _byIp)
            {
                if (scanned >= options.MaxCandidateScanCount)
                {
                    break;
                }

                scanned++;

                if (string.Equals(kvp.Key, ipToKeep, StringComparison.Ordinal))
                {
                    continue;
                }

                var bucket = kvp.Value;
                candidates.Add((kvp.Key, Volatile.Read(ref bucket.LastAccessTick)));
            }

            if (candidates.Count == 0)
            {
                return;
            }

            candidates.Sort(static (a, b) => a.LastAccess.CompareTo(b.LastAccess));

            int evicted = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (Volatile.Read(ref _approxTotalBytes) <= target)
                {
                    break;
                }

                if (evicted >= options.MaxEvictionsPerTrim)
                {
                    break;
                }

                string ip = candidates[i].Ip;

                if (!_byIp.TryGetValue(ip, out var bucket))
                {
                    continue;
                }

                bool markedEvicted;
                lock (bucket.Sync)
                {
                    markedEvicted = bucket.IsEvicted == 0;
                    bucket.IsEvicted = 1;
                }

                if (!markedEvicted)
                {
                    continue;
                }

                if (TryRemoveBucketMapping(ip, bucket))
                {
                    evicted++;
                }
            }

            long after = Volatile.Read(ref _approxTotalBytes);

            if (evicted > 0)
            {
                _logger.LogInformation(
                    "In-memory filtering store trimmed: evicted={Evicted}, scanned={Scanned}, beforeBytes={BeforeBytes}, afterBytes={AfterBytes}, limitBytes={LimitBytes}, targetBytes={TargetBytes}.",
                    () => evicted,
                    () => scanned,
                    () => before,
                    () => after,
                    () => limit,
                    () => target);
            }
            else
            {
                _logger.LogWarning(
                    "In-memory filtering store exceeded MemoryLimitBytes={MemoryLimitBytes} but could not evict any buckets. scanned={Scanned}, approxBytes={ApproxBytes}.",
                    () => limit,
                    () => scanned,
                    () => after);
            }
        }

        private void ThrottledLogDrop(long now, InMemoryFilteringEventStorageOptions options)
        {
            const long throttleMs = 5_000;

            long last = Volatile.Read(ref _lastDropLogTick);
            if (now - last < throttleMs)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _lastDropLogTick, now, last) == last)
            {
                _logger.LogWarning(
                    "In-memory filtering store dropping new events because MemoryLimitBytes={MemoryLimitBytes} was exceeded. ApproxTotalBytes={ApproxTotalBytes}.",
                    () => options.MemoryLimitBytes,
                    () => Volatile.Read(ref _approxTotalBytes));
            }
        }

        private void ThrottledLogClearAllIgnored(long now, InMemoryFilteringEventStorageOptions options)
        {
            const long throttleMs = 30_000;

            long last = Volatile.Read(ref _lastClearAllIgnoredLogTick);
            if (now - last < throttleMs)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _lastClearAllIgnoredLogTick, now, last) == last)
            {
                _logger.LogWarning(
                    "In-memory filtering store is configured with OverflowBehavior=ClearAll, but ClearAll is ignored in StoreAsync (Option A). Falling back to EvictOldestIpBuckets. MemoryLimitBytes={MemoryLimitBytes}, ApproxTotalBytes={ApproxTotalBytes}.",
                    () => options.MemoryLimitBytes,
                    () => Volatile.Read(ref _approxTotalBytes));
            }
        }

        private static long EstimateStringBytes(string value)
        {
            if (value is null) return 0;
            return ApproxStringOverheadBytes + ((long)value.Length * 2L);
        }

        private static long EstimateCountsEntryBytes(string source)
        {
            return ApproxCountsEntryOverheadBytes + EstimateStringBytes(source);
        }
    }
}
