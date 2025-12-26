using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;

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

        /// <summary>
        /// Removes all stored filtering events (and thus all aggregates) for the specified remote ip address.
        /// </summary>
        /// <remarks>
        /// Reviewer note: This is a destructive maintenance operation intended for administrative scenarios
        /// (for example, when you want to reset counters for one client).
        /// </remarks>
        /// <param name="remoteIpAddress">The normalized remote ip address string.</param>
        /// <param name="cancellationToken">
        /// A cancellation token that can be used to cancel the operation.
        /// </param>
        /// <returns>
        /// A task that completes with <c>true</c> when data for <paramref name="remoteIpAddress"/> existed and was removed;
        /// otherwise <c>false</c>.
        /// </returns>
        Task<bool> RemoveByRemoteIpAddressAsync(string remoteIpAddress, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes all stored filtering events (and thus related aggregates) for the specified remote ip address
        /// that match the given event source.
        /// </summary>
        /// <remarks>
        /// Reviewer note: Use this when you want to reset counters produced by one middleware/source only,
        /// while keeping other sources intact for the same IP.
        /// </remarks>
        /// <param name="remoteIpAddress">The normalized remote ip address string.</param>
        /// <param name="eventSource">The event source to remove (as stored in <see cref="FilteringEvent.EventSource"/>).</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>
        /// A task that completes with <c>true</c> when any matching data existed and was removed; otherwise <c>false</c>.
        /// </returns>
        Task<bool> RemoveByRemoteIpAddressAsync(string remoteIpAddress, string eventSource, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes all stored filtering events (and thus related aggregates) for the specified remote ip address
        /// that match the given match kind.
        /// </summary>
        /// <remarks>
        /// Reviewer note: Use this to reset one match bucket (e.g., <see cref="FilterMatchKind.Blacklist"/>) for an IP
        /// while keeping other match kinds intact.
        /// </remarks>
        /// <param name="remoteIpAddress">The normalized remote ip address string.</param>
        /// <param name="matchKind">The match kind to remove.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>
        /// A task that completes with <c>true</c> when any matching data existed and was removed; otherwise <c>false</c>.
        /// </returns>
        Task<bool> RemoveByRemoteIpAddressAsync(string remoteIpAddress, FilterMatchKind matchKind, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes all stored filtering events (and thus related aggregates) for the specified remote ip address
        /// that match the given event source and match kind.
        /// </summary>
        /// <remarks>
        /// Reviewer note: This is the most specific removal overload, effectively clearing one bucket:
        /// (remote IP, event source, match kind).
        /// </remarks>
        /// <param name="remoteIpAddress">The normalized remote ip address string.</param>
        /// <param name="eventSource">The event source to remove (as stored in <see cref="FilteringEvent.EventSource"/>).</param>
        /// <param name="matchKind">The match kind to remove.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>
        /// A task that completes with <c>true</c> when any matching data existed and was removed; otherwise <c>false</c>.
        /// </returns>
        Task<bool> RemoveByRemoteIpAddressAsync(string remoteIpAddress, string eventSource, FilterMatchKind matchKind, CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears all stored filtering events and all aggregates.
        /// </summary>
        /// <remarks>
        /// Reviewer note: This wipes the entire dataset in the underlying storage. Use with care.
        /// </remarks>
        /// <param name="cancellationToken">
        /// A cancellation token that can be used to cancel the operation.
        /// </param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task ClearAsync(CancellationToken cancellationToken = default);
    }
}
