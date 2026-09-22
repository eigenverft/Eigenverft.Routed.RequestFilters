using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.InMemoryFiltering;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.InSqliteDbFiltering;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Tests
{
    [TestClass]
    public sealed class FilteringEventStorageTests
    {
        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public async Task NullStorageIsStandaloneNoOpBackend()
        {
            using ServiceProvider provider = CreateServices(FilteringStorageKind.Null).BuildServiceProvider();
            IFilteringEventStorage storage = provider.GetRequiredService<IFilteringEventStorage>();

            await storage.StoreAsync(CreateEvent("203.0.113.30", "A", FilterMatchKind.Blacklist), TestContext.CancellationToken);

            Assert.AreEqual(0, storage.GetBlacklistCount("203.0.113.30"));
            Assert.AreEqual(0, storage.GetUnmatchedCount("203.0.113.30"));
            Assert.IsEmpty(storage.GetByEventSourceAndMatchKind("203.0.113.30"));
            Assert.IsEmpty(storage.GetByEventSource("203.0.113.30"));
            Assert.IsEmpty(storage.GetByMatchKind("203.0.113.30"));
            Assert.IsFalse(await storage.RemoveByRemoteIpAddressAsync("203.0.113.30", TestContext.CancellationToken));
            await storage.ClearAsync(TestContext.CancellationToken);
        }

        [TestMethod]
        [DataRow(FilteringStorageKind.InMemory)]
        [DataRow(FilteringStorageKind.Sqlite)]
        public async Task StatefulBackendsStoreAggregateRemoveAndClear(FilteringStorageKind kind)
        {
            string? sqliteDirectory = kind == FilteringStorageKind.Sqlite
                ? Path.Combine(Path.GetTempPath(), $"RequestFilters-StorageTests-{Guid.NewGuid():N}")
                : null;
            ServiceProvider? provider = null;

            try
            {
                ServiceCollection services = CreateServices(kind, sqliteDirectory);
                provider = services.BuildServiceProvider();
                IFilteringEventStorage storage = provider.GetRequiredService<IFilteringEventStorage>();
                const string ip = "203.0.113.31";

                await storage.StoreAsync(CreateEvent(ip, "A", FilterMatchKind.Blacklist), TestContext.CancellationToken);
                await storage.StoreAsync(CreateEvent(ip, "A", FilterMatchKind.Blacklist), TestContext.CancellationToken);
                await storage.StoreAsync(CreateEvent(ip, "A", FilterMatchKind.Unmatched), TestContext.CancellationToken);
                await storage.StoreAsync(CreateEvent(ip, "B", FilterMatchKind.Whitelist), TestContext.CancellationToken);

                Assert.AreEqual(2, storage.GetBlacklistCount(ip));
                Assert.AreEqual(1, storage.GetUnmatchedCount(ip));
                Assert.AreEqual(2L, storage.GetByEventSourceAndMatchKind(ip)
                    .Single(row => row.EventSource == "A" && row.MatchKind == FilterMatchKind.Blacklist).Count);
                Assert.AreEqual(3L, storage.GetByEventSource(ip).Single(row => row.EventSource == "A").Count);
                Assert.AreEqual(1L, storage.GetByMatchKind(ip).Single(row => row.MatchKind == FilterMatchKind.Whitelist).Count);

                Assert.IsTrue(await storage.RemoveByRemoteIpAddressAsync(ip, "A", FilterMatchKind.Blacklist, TestContext.CancellationToken));
                Assert.AreEqual(0, storage.GetBlacklistCount(ip));
                Assert.AreEqual(1, storage.GetUnmatchedCount(ip));

                Assert.IsTrue(await storage.RemoveByRemoteIpAddressAsync(ip, "A", TestContext.CancellationToken));
                Assert.AreEqual(0, storage.GetUnmatchedCount(ip));

                await storage.StoreAsync(CreateEvent(ip, "B", FilterMatchKind.Blacklist), TestContext.CancellationToken);
                Assert.IsTrue(await storage.RemoveByRemoteIpAddressAsync(ip, FilterMatchKind.Blacklist, TestContext.CancellationToken));
                Assert.AreEqual(0, storage.GetBlacklistCount(ip));

                Assert.IsTrue(await storage.RemoveByRemoteIpAddressAsync(ip, TestContext.CancellationToken));
                Assert.IsFalse(await storage.RemoveByRemoteIpAddressAsync(ip, TestContext.CancellationToken));
                Assert.IsEmpty(storage.GetByEventSourceAndMatchKind(ip));

                await storage.StoreAsync(CreateEvent(ip, "A", FilterMatchKind.Blacklist), TestContext.CancellationToken);
                await storage.StoreAsync(CreateEvent("203.0.113.32", "A", FilterMatchKind.Unmatched), TestContext.CancellationToken);
                await storage.ClearAsync(TestContext.CancellationToken);
                Assert.AreEqual(0, storage.GetBlacklistCount(ip));
                Assert.AreEqual(0, storage.GetUnmatchedCount("203.0.113.32"));
            }
            finally
            {
                provider?.Dispose();

                if (sqliteDirectory != null)
                {
                    SqliteConnection.ClearAllPools();
                    if (Directory.Exists(sqliteDirectory))
                    {
                        Directory.Delete(sqliteDirectory, recursive: true);
                    }
                }
            }
        }

        private static ServiceCollection CreateServices(FilteringStorageKind kind, string? sqliteDirectory = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));

            if (kind == FilteringStorageKind.InMemory)
            {
                services.AddFilteringEventStorage<InMemoryStorage>()
                    .Configure((InMemoryFilteringEventStorageOptions options) => options.MemoryLimitBytes = 0);
            }
            else if (kind == FilteringStorageKind.Sqlite)
            {
                services.AddFilteringEventStorage<SqliteStorage>()
                    .Configure((InSqliteDbFilteringEventStorageOptions options) =>
                    {
                        options.DatabaseDirectoryPath = sqliteDirectory!;
                        options.DatabaseFileName = "filtering-events.sqlite";
                        options.EnableWriteAheadLogging = false;
                    });
            }
            else
            {
                services.AddFilteringEventStorage<NullStorage>();
            }

            return services;
        }

        private static FilteringEvent CreateEvent(string remoteIpAddress, string source, FilterMatchKind matchKind)
        {
            return new FilteringEvent
            {
                RemoteIpAddress = remoteIpAddress,
                EventSource = source,
                MatchKind = matchKind,
            };
        }
    }
}
