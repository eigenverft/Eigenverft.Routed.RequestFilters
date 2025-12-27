using System;



namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.InSqliteDbFiltering
{

    /// <summary>
    /// Options for <see cref="InSqliteDbFilteringEventStorage"/>.
    /// </summary>
    /// <remarks>
    /// Reviewer note: The database path is assembled from <see cref="DatabaseDirectoryPath"/> and <see cref="DatabaseFileName"/>.
    /// The storage creates the directory and initializes schema/indexes on startup.
    /// </remarks>
    public sealed class InSqliteDbFilteringEventStorageOptions
    {
        /// <summary>
        /// Gets or sets the directory where the SQLite database file will be stored.
        /// </summary>
        /// <remarks>
        /// Reviewer note: The directory will be created if it does not exist.
        /// </remarks>
        public string DatabaseDirectoryPath { get; set; } = AppContext.BaseDirectory;

        /// <summary>
        /// Gets or sets the SQLite database file name.
        /// </summary>
        public string DatabaseFileName { get; set; } = "FilteringEventStorage.sqlite";

        /// <summary>
        /// Gets or sets a value indicating whether Write-Ahead Logging should be enabled.
        /// </summary>
        /// <remarks>
        /// Reviewer note: WAL generally improves concurrency for multi-reader scenarios.
        /// </remarks>
        public bool EnableWriteAheadLogging { get; set; } = true;

        /// <summary>
        /// Gets or sets the SQLite busy timeout.
        /// </summary>
        /// <remarks>
        /// Reviewer note: This helps under concurrent writers when SQLite is momentarily locked.
        /// </remarks>
        public TimeSpan BusyTimeout { get; set; } = TimeSpan.FromSeconds(5);
    }
}