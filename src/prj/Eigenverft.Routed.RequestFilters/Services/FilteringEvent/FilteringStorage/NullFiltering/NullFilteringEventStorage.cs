using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.NullFiltering
{
    /// <summary>
    /// Storage implementation that ignores all events.
    /// Useful as a default or for tests.
    /// </summary>
    /// <remarks>
    /// This implementation does not persist any data and always returns empty/zero results.
    /// On construction, it emits a single debug log line indicating that the null storage is active.
    /// </remarks>
    public sealed class NullFilteringEventStorage : IFilteringEventStorage
    {
        private readonly IDeferredLogger<NullFilteringEventStorage> _logger;

        /// <summary>
        /// Initializes a new instance of the storage.
        /// </summary>
        /// <param name="logger">Deferred logger used for internal diagnostics.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="logger"/> is null.</exception>
        public NullFilteringEventStorage(IDeferredLogger<NullFilteringEventStorage> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Filtering event storage configured: NullFiltering (no persistence).");
            }
        }

        /// <summary>
        /// Ignores the provided event record.
        /// </summary>
        /// <param name="record">The record that describes the event.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A completed task.</returns>
        public Task StoreAsync(FilteringEvent record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        /// <summary>
        /// Always returns 0 because this storage does not persist any events.
        /// </summary>
        /// <param name="remoteIpAddress">The normalized remote ip address string.</param>
        /// <returns>Always <c>0</c>.</returns>
        public int GetBlacklistCount(string remoteIpAddress) => 0;

        /// <summary>
        /// Always returns 0 because this storage does not persist any events.
        /// </summary>
        /// <param name="remoteIpAddress">The normalized remote ip address string.</param>
        /// <returns>Always <c>0</c>.</returns>
        public int GetUnmatchedCount(string remoteIpAddress) => 0;

        /// <summary>
        /// Always returns an empty collection because this storage does not persist any events.
        /// </summary>
        /// <param name="remoteIpAddress">The normalized remote ip address string.</param>
        /// <returns>An empty snapshot.</returns>
        public IReadOnlyCollection<FilteringEventBySourceAndMatchAggregate> GetByEventSourceAndMatchKind(string remoteIpAddress)
            => Array.Empty<FilteringEventBySourceAndMatchAggregate>();

        /// <summary>
        /// Always returns an empty collection because this storage does not persist any events.
        /// </summary>
        /// <param name="remoteIpAddress">The normalized remote ip address string.</param>
        /// <returns>An empty snapshot.</returns>
        public IReadOnlyCollection<FilteringEventBySourceAggregate> GetByEventSource(string remoteIpAddress)
            => Array.Empty<FilteringEventBySourceAggregate>();

        /// <summary>
        /// Always returns an empty collection because this storage does not persist any events.
        /// </summary>
        /// <param name="remoteIpAddress">The normalized remote ip address string.</param>
        /// <returns>An empty snapshot.</returns>
        public IReadOnlyCollection<FilteringEventByMatchAggregate> GetByMatchKind(string remoteIpAddress)
            => Array.Empty<FilteringEventByMatchAggregate>();
    }
}
