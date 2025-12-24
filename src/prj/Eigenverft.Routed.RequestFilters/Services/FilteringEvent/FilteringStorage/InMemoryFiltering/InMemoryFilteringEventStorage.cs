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
    /// On construction, it emits one debug log line with the currently configured options.
    /// If configuration is reloadable, it also logs option changes at debug level.
    /// </remarks>
    public sealed class InMemoryFilteringEventStorage : IFilteringEventStorage, IDisposable
    {
        private sealed class IpBucket
        {
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

        // ip -> per-ip bucket store (fast lookups by ip)
        private readonly ConcurrentDictionary<string, IpBucket> _byIp = new(StringComparer.Ordinal);

        // Total of bucket.ApproxBytes deltas across the store (approximate).
        private long _approxTotalBytes;

        // Guard to prevent concurrent trim passes.
        private int _trimInProgress;
        private long _lastTrimTick;

        // Throttle for repeated "dropping" warnings.
        private long _lastDropLogTick;

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
        /// Disposes the options change subscription.
        /// </summary>
        /// <remarks>
        /// If this instance is registered as a singleton in DI, the container will call <see cref="Dispose"/> during shutdown.
        /// </remarks>
        public void Dispose()
        {
            _optionsReloadSubscription?.Dispose();
        }

        /// <inheritdoc />
        public Task StoreAsync(FilteringEvent record, CancellationToken cancellationToken = default)
        {
            if (record is null) throw new ArgumentNullException(nameof(record));
            cancellationToken.ThrowIfCancellationRequested();

            // No IP means nothing to bucket by. We keep this cheap and silent.
            var ipRaw = record.RemoteIpAddress;
            if (string.IsNullOrWhiteSpace(ipRaw))
            {
                return Task.CompletedTask;
            }

            // Normalize to the same string form used for reads.
            string ip = ipRaw.Trim();
            InMemoryFilteringEventStorageOptions options = _optionsMonitor.CurrentValue;
            long now = Environment.TickCount64;

            // Fast path for DropNewEvents: if over limit, do not mutate state.
            if (options.MemoryLimitBytes > 0 &&
                options.OverflowBehavior == InMemoryFilteringEventStorageOverflowBehavior.DropNewEvents &&
                Volatile.Read(ref _approxTotalBytes) >= options.MemoryLimitBytes)
            {
                ThrottledLogDrop(now, options);
                return Task.CompletedTask;
            }

            // Get bucket for this IP; creating a bucket contributes to the approx total.
            IpBucket bucket = GetOrCreateBucket(ip, now);

            // If a trim pass removed this bucket concurrently, re-create.
            if (Volatile.Read(ref bucket.IsEvicted) != 0)
            {
                bucket = GetOrCreateBucket(ip, now);
            }

            // Mark bucket as recently used (for LRU evictions).
            Volatile.Write(ref bucket.LastAccessTick, now);

            string source = (record.EventSource ?? string.Empty).Trim();
            FilterMatchKind kind = record.MatchKind;

            var key = (Source: source, Kind: kind);

            // Detect first occurrence of a (source, kind) pair to model memory growth.
            if (bucket.Counts.TryAdd(key, 1L))
            {
                long delta = EstimateCountsEntryBytes(source);
                Interlocked.Add(ref bucket.ApproxBytes, delta);
                Interlocked.Add(ref _approxTotalBytes, delta);
            }
            else
            {
                bucket.Counts.AddOrUpdate(key, 1L, static (_, current) => current + 1L);
            }

            // Maintain cheap per-kind totals for quick reads.
            if (kind == FilterMatchKind.Blacklist)
            {
                Interlocked.Increment(ref bucket.BlacklistTotal);
            }
            else if (kind == FilterMatchKind.Unmatched)
            {
                Interlocked.Increment(ref bucket.UnmatchedTotal);
            }

            // Apply memory policy if we crossed the configured limit.
            if (options.MemoryLimitBytes > 0 && Volatile.Read(ref _approxTotalBytes) > options.MemoryLimitBytes)
            {
                ApplyMemoryLimit(options, ip, now);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<FilteringEventBySourceAndMatchAggregate> GetByEventSourceAndMatchKind(string remoteIpAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return Array.Empty<FilteringEventBySourceAndMatchAggregate>();
            }

            var ip = remoteIpAddress.Trim();
            if (!_byIp.TryGetValue(ip, out var bucket))
            {
                return Array.Empty<FilteringEventBySourceAndMatchAggregate>();
            }

            // Reads also update LRU access to prevent frequently-queried IPs from being evicted.
            Touch(bucket);

            var results = new List<FilteringEventBySourceAndMatchAggregate>();
            foreach (var kvp in bucket.Counts)
            {
                results.Add(new FilteringEventBySourceAndMatchAggregate(ip, kvp.Key.Source, kvp.Key.Kind, kvp.Value));
            }

            return results.Count == 0 ? Array.Empty<FilteringEventBySourceAndMatchAggregate>() : results;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<FilteringEventBySourceAggregate> GetByEventSource(string remoteIpAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return Array.Empty<FilteringEventBySourceAggregate>();
            }

            var ip = remoteIpAddress.Trim();
            if (!_byIp.TryGetValue(ip, out var bucket))
            {
                return Array.Empty<FilteringEventBySourceAggregate>();
            }

            Touch(bucket);

            // Aggregate across match kinds by source.
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

        /// <inheritdoc />
        public IReadOnlyCollection<FilteringEventByMatchAggregate> GetByMatchKind(string remoteIpAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return Array.Empty<FilteringEventByMatchAggregate>();
            }

            var ip = remoteIpAddress.Trim();
            if (!_byIp.TryGetValue(ip, out var bucket))
            {
                return Array.Empty<FilteringEventByMatchAggregate>();
            }

            Touch(bucket);

            // Aggregate across sources by match kind.
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

        /// <inheritdoc />
        public int GetUnmatchedCount(string remoteIpAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return 0;
            }

            var ip = remoteIpAddress.Trim();
            if (!_byIp.TryGetValue(ip, out var bucket))
            {
                return 0;
            }

            Touch(bucket);

            // Saturate to int for consumer friendliness.
            var total = Volatile.Read(ref bucket.UnmatchedTotal);
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        /// <inheritdoc />
        public int GetBlacklistCount(string remoteIpAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return 0;
            }

            var ip = remoteIpAddress.Trim();
            if (!_byIp.TryGetValue(ip, out var bucket))
            {
                return 0;
            }

            Touch(bucket);

            // Saturate to int for consumer friendliness.
            var total = Volatile.Read(ref bucket.BlacklistTotal);
            return total > int.MaxValue ? int.MaxValue : (int)total;
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

        private static void Touch(IpBucket bucket)
        {
            Volatile.Write(ref bucket.LastAccessTick, Environment.TickCount64);
        }

        private IpBucket GetOrCreateBucket(string ip, long now)
        {
            // Hot path: existing bucket.
            if (_byIp.TryGetValue(ip, out var existing))
            {
                return existing;
            }

            // Slow path: create and race-add.
            for (; ; )
            {
                var created = new IpBucket
                {
                    LastAccessTick = now,
                    ApproxBytes = ApproxIpBucketOverheadBytes + EstimateStringBytes(ip),
                };

                if (_byIp.TryAdd(ip, created))
                {
                    // First time this IP is seen: account for approximate memory.
                    Interlocked.Add(ref _approxTotalBytes, created.ApproxBytes);
                    return created;
                }

                if (_byIp.TryGetValue(ip, out existing))
                {
                    return existing;
                }
            }
        }

        private void ApplyMemoryLimit(InMemoryFilteringEventStorageOptions options, string ipToKeep, long now)
        {
            // Guard against over-trimming during sustained load.
            long last = Volatile.Read(ref _lastTrimTick);
            if (now - last < (long)options.TrimCooldown.TotalMilliseconds)
            {
                return;
            }

            // Only one trimming thread at a time.
            if (Interlocked.CompareExchange(ref _trimInProgress, 1, 0) != 0)
            {
                return;
            }

            try
            {
                Volatile.Write(ref _lastTrimTick, now);

                switch (options.OverflowBehavior)
                {
                    case InMemoryFilteringEventStorageOverflowBehavior.ClearAll:
                        _logger.LogWarning(
                            "In-memory filtering store cleared all data because MemoryLimitBytes={MemoryLimitBytes} was exceeded. ApproxTotalBytes={ApproxTotalBytes}.",
                            () => options.MemoryLimitBytes,
                            () => Volatile.Read(ref _approxTotalBytes));

                        _byIp.Clear();
                        Volatile.Write(ref _approxTotalBytes, 0);
                        return;

                    case InMemoryFilteringEventStorageOverflowBehavior.DropNewEvents:
                        // StoreAsync handles the dropping behavior and warning throttling.
                        return;

                    case InMemoryFilteringEventStorageOverflowBehavior.EvictOldestIpBuckets:
                    default:
                        EvictOldestUntilUnderTarget(options, ipToKeep);
                        return;
                }
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

            // Clamp ratio defensively to avoid weird targets.
            double ratio = options.TrimTargetRatio;
            if (ratio <= 0.10) ratio = 0.10;
            if (ratio > 1.0) ratio = 1.0;

            long target = (long)(limit * ratio);
            long before = Volatile.Read(ref _approxTotalBytes);

            // Collect LRU candidates (bounded scan to cap time under huge stores).
            var candidates = new List<(string Ip, long LastAccess, long Bytes)>(capacity: 1024);

            int scanned = 0;
            foreach (var kvp in _byIp)
            {
                if (scanned >= options.MaxCandidateScanCount)
                {
                    break;
                }

                scanned++;

                // Prefer not to evict the IP that just wrote (keeps behavior stable under steady traffic).
                if (string.Equals(kvp.Key, ipToKeep, StringComparison.Ordinal))
                {
                    continue;
                }

                var bucket = kvp.Value;
                candidates.Add((
                    kvp.Key,
                    Volatile.Read(ref bucket.LastAccessTick),
                    Volatile.Read(ref bucket.ApproxBytes)));
            }

            if (candidates.Count == 0)
            {
                return;
            }

            // Oldest first.
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

                if (_byIp.TryRemove(ip, out var removed))
                {
                    // Mark as evicted so a concurrent StoreAsync can re-create cleanly.
                    Interlocked.Exchange(ref removed.IsEvicted, 1);

                    long removedBytes = Volatile.Read(ref removed.ApproxBytes);
                    Interlocked.Add(ref _approxTotalBytes, -removedBytes);

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
            // Avoid spamming logs if the system is under sustained memory pressure.
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

        private static long EstimateStringBytes(string value)
        {
            if (value is null) return 0;

            // Rough model: object header + character payload (UTF-16, 2 bytes per char).
            return ApproxStringOverheadBytes + ((long)value.Length * 2L);
        }

        private static long EstimateCountsEntryBytes(string source)
        {
            // Rough model: dictionary entry + tuple key + referenced string.
            return ApproxCountsEntryOverheadBytes + EstimateStringBytes(source);
        }
    }
}
