using System;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;

namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvent
{
    /// <summary>
    /// Represents a filtering event recorded by a middleware.
    /// </summary>
    public sealed class FilteringEvent
    {
        /// <summary>
        /// Gets or sets the timestamp when the event occurred in UTC.
        /// </summary>
        public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Gets or sets the normalized remote ip address of the client.
        /// </summary>
        public string RemoteIpAddress { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the value that was checked by the filter.
        /// </summary>
        /// <remarks>
        /// For an http protocol filter this is typically the protocol string.
        /// For an ip based filter this can be a host name or similar.
        /// </remarks>
        public string ObservedValue { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the match kind that was determined for the observed value.
        /// </summary>
        public FilterMatchKind MatchKind { get; set; }

        /// <summary>
        /// Gets or sets a short name for the source middleware that produced the record.
        /// </summary>
        public string? EventSource { get; set; }
    }

    /// <summary>
    /// Aggregate row for one bucket: (remote IP, event source, match kind) with its count.
    /// </summary>
    public sealed class FilteringEventBySourceAndMatchAggregate
    {
        public FilteringEventBySourceAndMatchAggregate(string remoteIpAddress, string eventSource, FilterMatchKind matchKind, long count)
        {
            RemoteIpAddress = remoteIpAddress ?? string.Empty;
            EventSource = eventSource ?? string.Empty;
            MatchKind = matchKind;
            Count = count;
        }

        public string RemoteIpAddress { get; }
        public string EventSource { get; }
        public FilterMatchKind MatchKind { get; }
        public long Count { get; }
    }

    /// <summary>
    /// Aggregate row per source: (remote IP, event source) with its count summed across all match kinds.
    /// </summary>
    public sealed class FilteringEventBySourceAggregate
    {
        public FilteringEventBySourceAggregate(string remoteIpAddress, string eventSource, long count)
        {
            RemoteIpAddress = remoteIpAddress ?? string.Empty;
            EventSource = eventSource ?? string.Empty;
            Count = count;
        }

        public string RemoteIpAddress { get; }
        public string EventSource { get; }
        public long Count { get; }
    }

    /// <summary>
    /// Aggregate row per match kind: (remote IP, match kind) with its count summed across all sources.
    /// </summary>
    public sealed class FilteringEventByMatchAggregate
    {
        public FilteringEventByMatchAggregate(string remoteIpAddress, FilterMatchKind matchKind, long count)
        {
            RemoteIpAddress = remoteIpAddress ?? string.Empty;
            MatchKind = matchKind;
            Count = count;
        }

        public string RemoteIpAddress { get; }
        public FilterMatchKind MatchKind { get; }
        public long Count { get; }
    }
}
