using System.Text.Json;

namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// Declarative description of a database used by
    /// <see cref="IIndexedDbService.OpenAsync"/>. The store list is applied
    /// synchronously by the JS-side upgrade handler when the requested
    /// <see cref="Version"/> exceeds the version currently stored on disk,
    /// including the fresh-database case (on-disk version <c>0</c>).
    /// </summary>
    /// <remarks>
    /// There is no async data-migration hook. Versionchange transactions stop
    /// being IDB-active outside of their event handlers, so any C#-driven
    /// migration that crosses a SignalR await would abort the upgrade. If
    /// you need to rewrite existing rows, do it in a pass that runs after
    /// open completes — one record per <c>JsonPutSingleAsync</c> /
    /// <c>BlobPutSingleAsync</c>.
    /// </remarks>
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
        /// describes the desired minimum, not an exclusive set).
        /// </summary>
        public IReadOnlyList<DeclaredStore>? Stores { get; init; }

        /// <summary>
        /// Serializer options used for JSON object stores opened from this
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
}
