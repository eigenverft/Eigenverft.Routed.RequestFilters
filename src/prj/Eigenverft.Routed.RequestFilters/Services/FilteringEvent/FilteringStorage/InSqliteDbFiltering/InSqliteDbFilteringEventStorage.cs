using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.InSqliteDbFiltering
{
    /// <summary>
    /// SQLite-backed implementation of <see cref="IFilteringEventStorage"/> using <see cref="Microsoft.Data.Sqlite"/>.
    /// </summary>
    /// <remarks>
    /// Reviewer note: This implementation stores aggregated counters only, keyed by
    /// (remote IP, event source, match kind). That matches the interface’s query patterns and keeps the database small.
    /// </remarks>
    public sealed class InSqliteDbFilteringEventStorage : IFilteringEventStorage
    {
        private const string TableName = "filtering_event_counts";

        private readonly IOptionsMonitor<InSqliteDbFilteringEventStorageOptions> _optionsMonitor;
        private readonly IDeferredLogger<InSqliteDbFilteringEventStorage> _logger;
        private readonly string _dataSourcePath;
        private readonly string _connectionString;

        /// <summary>
        /// Initializes a new instance of the storage.
        /// </summary>
        /// <param name="optionsMonitor">Options monitor providing the database location and behavior settings.</param>
        /// <param name="logger">Deferred logger used for internal diagnostics.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="optionsMonitor"/> or <paramref name="logger"/> is null.</exception>
        /// <remarks>
        /// Reviewer note: The constructor ensures the database file, schema, and indexes exist.
        /// </remarks>
        public InSqliteDbFilteringEventStorage(IOptionsMonitor<InSqliteDbFilteringEventStorageOptions> optionsMonitor, IDeferredLogger<InSqliteDbFilteringEventStorage> logger)
        {
            ArgumentNullException.ThrowIfNull(optionsMonitor);
            ArgumentNullException.ThrowIfNull(logger);

            _optionsMonitor = optionsMonitor;
            _logger = logger;

            InSqliteDbFilteringEventStorageOptions o = _optionsMonitor.CurrentValue;

            // Ensure directory exists and compute a stable full path.
            string dir = string.IsNullOrWhiteSpace(o.DatabaseDirectoryPath) ? AppContext.BaseDirectory : o.DatabaseDirectoryPath.Trim();
            string file = string.IsNullOrWhiteSpace(o.DatabaseFileName) ? "filtering-events.sqlite" : o.DatabaseFileName.Trim();

            Directory.CreateDirectory(dir);

            _dataSourcePath = Path.GetFullPath(Path.Combine(dir, file));

            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = _dataSourcePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true,
            };

            _connectionString = csb.ToString();

            EnsureDatabaseCreated();

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Filtering event storage configured: SQLite (DataSource={DataSource}).", () => _dataSourcePath);
            }
        }

        /// <inheritdoc />
        public async Task StoreAsync(FilteringEvent record, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            cancellationToken.ThrowIfCancellationRequested();

            string ipRaw = record.RemoteIpAddress;
            if (string.IsNullOrWhiteSpace(ipRaw))
            {
                return;
            }

            string ip = ipRaw.Trim();
            string source = (record.EventSource ?? string.Empty).Trim();
            int kind = (int)record.MatchKind;

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Reviewer note: Wrap in a transaction to keep the update atomic and reduce lock churn.
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (SqliteCommand cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText =
                    $@"
INSERT INTO {TableName} (remote_ip, event_source, match_kind, count)
VALUES ($ip, $source, $kind, 1)
ON CONFLICT(remote_ip, event_source, match_kind)
DO UPDATE SET count = count + 1;";

                cmd.Parameters.AddWithValue("$ip", ip);
                cmd.Parameters.AddWithValue("$source", source);
                cmd.Parameters.AddWithValue("$kind", kind);

                _ = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public int GetBlacklistCount(string remoteIpAddress)
        {
            return GetCountForKind(remoteIpAddress, FilterMatchKind.Blacklist);
        }

        /// <inheritdoc />
        public int GetUnmatchedCount(string remoteIpAddress)
        {
            return GetCountForKind(remoteIpAddress, FilterMatchKind.Unmatched);
        }

        /// <inheritdoc />
        public IReadOnlyCollection<FilteringEventBySourceAndMatchAggregate> GetByEventSourceAndMatchKind(string remoteIpAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return Array.Empty<FilteringEventBySourceAndMatchAggregate>();
            }

            string ip = remoteIpAddress.Trim();
            var results = new List<FilteringEventBySourceAndMatchAggregate>();

            using (SqliteConnection connection = CreateConnection())
            {
                connection.Open();

                using (SqliteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        $@"
SELECT event_source, match_kind, count
FROM {TableName}
WHERE remote_ip = $ip;";
                    cmd.Parameters.AddWithValue("$ip", ip);

                    using (SqliteDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string source = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                            FilterMatchKind kind = (FilterMatchKind)reader.GetInt32(1);
                            long count = reader.GetInt64(2);

                            results.Add(new FilteringEventBySourceAndMatchAggregate(ip, source, kind, count));
                        }
                    }
                }
            }

            if (results.Count == 0)
            {
                return Array.Empty<FilteringEventBySourceAndMatchAggregate>();
            }

            return results;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<FilteringEventBySourceAggregate> GetByEventSource(string remoteIpAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return Array.Empty<FilteringEventBySourceAggregate>();
            }

            string ip = remoteIpAddress.Trim();
            var results = new List<FilteringEventBySourceAggregate>();

            using (SqliteConnection connection = CreateConnection())
            {
                connection.Open();

                using (SqliteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        $@"
SELECT event_source, SUM(count) AS total_count
FROM {TableName}
WHERE remote_ip = $ip
GROUP BY event_source;";
                    cmd.Parameters.AddWithValue("$ip", ip);

                    using (SqliteDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string source = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                            long count = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);

                            results.Add(new FilteringEventBySourceAggregate(ip, source, count));
                        }
                    }
                }
            }

            if (results.Count == 0)
            {
                return Array.Empty<FilteringEventBySourceAggregate>();
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

            string ip = remoteIpAddress.Trim();
            var results = new List<FilteringEventByMatchAggregate>();

            using (SqliteConnection connection = CreateConnection())
            {
                connection.Open();

                using (SqliteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        $@"
SELECT match_kind, SUM(count) AS total_count
FROM {TableName}
WHERE remote_ip = $ip
GROUP BY match_kind;";
                    cmd.Parameters.AddWithValue("$ip", ip);

                    using (SqliteDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            FilterMatchKind kind = (FilterMatchKind)reader.GetInt32(0);
                            long count = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);

                            results.Add(new FilteringEventByMatchAggregate(ip, kind, count));
                        }
                    }
                }
            }

            if (results.Count == 0)
            {
                return Array.Empty<FilteringEventByMatchAggregate>();
            }

            return results;
        }

        /// <inheritdoc />
        public async Task<bool> RemoveByRemoteIpAddressAsync(string remoteIpAddress, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return false;
            }

            string ip = remoteIpAddress.Trim();

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (SqliteCommand cmd = connection.CreateCommand())
            {
                cmd.CommandText = $@"DELETE FROM {TableName} WHERE remote_ip = $ip;";
                cmd.Parameters.AddWithValue("$ip", ip);

                int affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return affected > 0;
            }
        }

        /// <inheritdoc />
        public async Task<bool> RemoveByRemoteIpAddressAsync(string remoteIpAddress, string eventSource, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return false;
            }

            string ip = remoteIpAddress.Trim();
            string source = (eventSource ?? string.Empty).Trim();

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (SqliteCommand cmd = connection.CreateCommand())
            {
                cmd.CommandText = $@"DELETE FROM {TableName} WHERE remote_ip = $ip AND event_source = $source;";
                cmd.Parameters.AddWithValue("$ip", ip);
                cmd.Parameters.AddWithValue("$source", source);

                int affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return affected > 0;
            }
        }

        /// <inheritdoc />
        public async Task<bool> RemoveByRemoteIpAddressAsync(string remoteIpAddress, FilterMatchKind matchKind, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return false;
            }

            string ip = remoteIpAddress.Trim();
            int kind = (int)matchKind;

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (SqliteCommand cmd = connection.CreateCommand())
            {
                cmd.CommandText = $@"DELETE FROM {TableName} WHERE remote_ip = $ip AND match_kind = $kind;";
                cmd.Parameters.AddWithValue("$ip", ip);
                cmd.Parameters.AddWithValue("$kind", kind);

                int affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return affected > 0;
            }
        }

        /// <inheritdoc />
        public async Task<bool> RemoveByRemoteIpAddressAsync(string remoteIpAddress, string eventSource, FilterMatchKind matchKind, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return false;
            }

            string ip = remoteIpAddress.Trim();
            string source = (eventSource ?? string.Empty).Trim();
            int kind = (int)matchKind;

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (SqliteCommand cmd = connection.CreateCommand())
            {
                cmd.CommandText = $@"DELETE FROM {TableName} WHERE remote_ip = $ip AND event_source = $source AND match_kind = $kind;";
                cmd.Parameters.AddWithValue("$ip", ip);
                cmd.Parameters.AddWithValue("$source", source);
                cmd.Parameters.AddWithValue("$kind", kind);

                int affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return affected > 0;
            }
        }

        /// <inheritdoc />
        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (SqliteCommand cmd = connection.CreateCommand())
            {
                cmd.CommandText = $@"DELETE FROM {TableName};";
                _ = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private int GetCountForKind(string remoteIpAddress, FilterMatchKind kind)
        {
            if (string.IsNullOrWhiteSpace(remoteIpAddress))
            {
                return 0;
            }

            string ip = remoteIpAddress.Trim();

            using (SqliteConnection connection = CreateConnection())
            {
                connection.Open();

                using (SqliteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        $@"
SELECT COALESCE(SUM(count), 0)
FROM {TableName}
WHERE remote_ip = $ip AND match_kind = $kind;";
                    cmd.Parameters.AddWithValue("$ip", ip);
                    cmd.Parameters.AddWithValue("$kind", (int)kind);

                    object? scalar = cmd.ExecuteScalar();
                    long value = scalar is null || scalar is DBNull ? 0L : Convert.ToInt64(scalar);

                    if (value <= 0)
                    {
                        return 0;
                    }

                    if (value > int.MaxValue)
                    {
                        return int.MaxValue;
                    }

                    return (int)value;
                }
            }
        }

        private SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection(_connectionString);

            // Reviewer note: Busy timeout reduces transient "database is locked" failures under concurrency.
            InSqliteDbFilteringEventStorageOptions o = _optionsMonitor.CurrentValue;
            connection.DefaultTimeout = (int)Math.Max(0, o.BusyTimeout.TotalSeconds);

            return connection;
        }

        private void EnsureDatabaseCreated()
        {
            InSqliteDbFilteringEventStorageOptions o = _optionsMonitor.CurrentValue;

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                using (SqliteCommand cmd = connection.CreateCommand())
                {
                    // Reviewer note: WAL is persistent; setting it once on init is usually sufficient.
                    if (o.EnableWriteAheadLogging)
                    {
                        cmd.CommandText = "PRAGMA journal_mode = WAL;";
                        _ = cmd.ExecuteNonQuery();
                    }

                    cmd.CommandText = "PRAGMA foreign_keys = ON;";
                    _ = cmd.ExecuteNonQuery();
                }

                using (SqliteTransaction tx = connection.BeginTransaction())
                {
                    using (SqliteCommand cmd = connection.CreateCommand())
                    {
                        cmd.Transaction = tx;

                        cmd.CommandText =
                            $@"
CREATE TABLE IF NOT EXISTS {TableName} (
    remote_ip    TEXT    NOT NULL,
    event_source TEXT    NOT NULL,
    match_kind   INTEGER NOT NULL,
    count        INTEGER NOT NULL,
    PRIMARY KEY (remote_ip, event_source, match_kind)
);";
                        _ = cmd.ExecuteNonQuery();

                        cmd.CommandText = $@"CREATE INDEX IF NOT EXISTS IX_{TableName}_remote_ip ON {TableName} (remote_ip);";
                        _ = cmd.ExecuteNonQuery();

                        cmd.CommandText = $@"CREATE INDEX IF NOT EXISTS IX_{TableName}_remote_ip_event_source ON {TableName} (remote_ip, event_source);";
                        _ = cmd.ExecuteNonQuery();

                        cmd.CommandText = $@"CREATE INDEX IF NOT EXISTS IX_{TableName}_remote_ip_match_kind ON {TableName} (remote_ip, match_kind);";
                        _ = cmd.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
            }
        }
    }
}
