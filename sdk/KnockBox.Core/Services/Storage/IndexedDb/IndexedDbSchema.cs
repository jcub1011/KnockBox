using System.Text.Json;

namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// Declarative description of a database used by
    /// <see cref="IIndexedDbService.OpenAsync"/>. The <see cref="OnUpgrade"/>
    /// callback is invoked inside the browser's <c>versionchange</c> transaction
    /// when the requested <see cref="Version"/> exceeds the version currently
    /// stored on disk (including the fresh-database case where the on-disk
    /// version is <c>0</c>).
    /// </summary>
    public sealed record IndexedDbSchema
    {
        /// <summary>Database name, scoped to the current origin.</summary>
        public string Name { get; init; }

        /// <summary>Target version. Must be ≥ 1.</summary>
        public int Version { get; init; }

        /// <summary>
        /// Schema-upgrade callback. Receives an <see cref="IUpgradeContext"/>
        /// that can create / delete / mutate object stores and indexes and
        /// migrate data. Required on first open of a database (when
        /// <c>oldVersion == 0</c>) and on every version bump — passing
        /// <see langword="null"/> against a missing or out-of-date database
        /// fails the open with <see cref="IndexedDbErrorKind.Version"/>.
        /// </summary>
        public UpgradeHandler? OnUpgrade { get; init; }

        /// <summary>
        /// Serializer options used for typed object stores opened from this
        /// database. <see langword="null"/> selects the Core default
        /// (case-insensitive property names, ignore nulls on write).
        /// </summary>
        public JsonSerializerOptions? JsonOptions { get; init; }

        public IndexedDbSchema(string name, int version)
        {
            Name = name;
            Version = version;
        }
    }

    /// <summary>
    /// Schema upgrade delegate. Invoked from C# inside the JS-side
    /// <c>versionchange</c> transaction. The callback may itself perform
    /// async operations via the supplied <paramref name="ctx"/>.
    /// </summary>
    public delegate ValueTask UpgradeHandler(
        IUpgradeContext ctx,
        int oldVersion,
        int newVersion,
        CancellationToken ct);
}
