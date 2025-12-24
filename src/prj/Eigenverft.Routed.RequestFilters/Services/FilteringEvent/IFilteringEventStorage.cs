using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvent
{
    /// <summary>
    /// Defines a service that stores filtering events and exposes simple metrics and aggregates.
    /// </summary>
    public interface IFilteringEventStorage
    {
        /// <summary>
        /// Stores a filtering event.
        /// </summary>
        /// <param name="record">The record that describes the event.</param>
        /// <param name="cancellationToken">
        /// A cancellation token that can be used to cancel the operation.
        /// </param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task StoreAsync(FilteringEvent record, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the number of blacklist events stored for the specified remote ip address.
        /// </summary>
        /// <param name="remoteIpAddress">The normalized remote ip address string.</param>
        /// <returns>The number of stored blacklist events.</returns>
        int GetBlacklistCount(string remoteIpAddress);

        /// <summary>
        /// Gets the number of unmatched events stored for the specified remote ip address.
        /// </summary>
        /// <param name="remoteIpAddress">The normalized remote ip address string.</param>
        /// <returns>The number of stored unmatched events.</returns>
        int GetUnmatchedCount(string remoteIpAddress);

        /// <summary>
        /// Gets aggregates for the specified remote ip address grouped by event source and match kind.
        /// </summary>
        /// <param name="remoteIpAddress">The normalized remote ip address string.</param>
        /// <returns>
        /// A snapshot collection containing one row per (event source, match kind) for the given ip address.
        /// </returns>
        IReadOnlyCollection<FilteringEventBySourceAndMatchAggregate> GetByEventSourceAndMatchKind(string remoteIpAddress);

        /// <summary>
        /// Gets aggregates for the specified remote ip address grouped by event source
        /// (summed across all match kinds).
        /// </summary>
        /// <param name="remoteIpAddress">The normalized remote ip address string.</param>
        /// <returns>
        /// A snapshot collection containing one row per event source for the given ip address.
        /// </returns>
        IReadOnlyCollection<FilteringEventBySourceAggregate> GetByEventSource(string remoteIpAddress);

        /// <summary>
        /// Gets aggregates for the specified remote ip address grouped by match kind
        /// (summed across all event sources).
        /// </summary>
        /// <param name="remoteIpAddress">The normalized remote ip address string.</param>
        /// <returns>
        /// A snapshot collection containing one row per match kind for the given ip address.
        /// </returns>
        IReadOnlyCollection<FilteringEventByMatchAggregate> GetByMatchKind(string remoteIpAddress);
    }
}
