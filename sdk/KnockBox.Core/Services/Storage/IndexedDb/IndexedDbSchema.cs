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
        /// Declarative store list applied synchronously by the JS-side
        /// upgrade handler. Stores in this list that don't exist are
        /// created; stores not in the list are left alone (the list
        /// describes the desired minimum, not an exclusive set). Apply
        /// declaratively here whenever possible — see <see cref="OnUpgrade"/>
        /// for why the async-callback path is unreliable for schema work.
        /// </summary>
        public IReadOnlyList<DeclaredStore>? Stores { get; init; }

        /// <summary>
        /// Optional data-migration callback. Runs AFTER <see cref="Stores"/>
        /// have been applied. Use this only for migrating existing rows
        /// between schema versions, not for declaring schema — schema work
        /// belongs on <see cref="Stores"/> because the IDB spec leaves the
        /// versionchange transaction's <i>active</i> flag <c>false</c>
        /// outside IDB event handlers, so any schema op issued from a
        /// resumed async function aborts the upgrade.
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
    /// Declarative description of an object store applied during a JS-side
    /// schema reconciliation pass. The reconciliation is idempotent: an
    /// existing store with the same name is kept and any missing indexes are
    /// added, no destructive changes occur.
    /// </summary>
    public sealed record DeclaredStore
    {
        public string Name { get; init; }
        public DeclaredStoreKind Kind { get; init; }
        public KeyPath? KeyPath { get; init; }
        public bool AutoIncrement { get; init; }
        public IReadOnlyList<DeclaredIndex>? Indexes { get; init; }

        public DeclaredStore(string name, DeclaredStoreKind kind)
        {
            Name = name;
            Kind = kind;
        }
    }

    public enum DeclaredStoreKind
    {
        Json,
        Blob,
    }

    public sealed record DeclaredIndex
    {
        public string Name { get; init; }
        public KeyPath KeyPath { get; init; }
        public bool Unique { get; init; }
        public bool MultiEntry { get; init; }

        public DeclaredIndex(string name, KeyPath keyPath)
        {
            Name = name;
            KeyPath = keyPath;
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
