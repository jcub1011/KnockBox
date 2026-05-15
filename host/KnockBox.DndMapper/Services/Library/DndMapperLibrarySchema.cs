using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.DndMapper.Services.Library
{
    /// <summary>
    /// Schema descriptor for the per-browser-profile DnD Mapper IndexedDB
    /// database. IndexedDB is already origin + browser-profile scoped, so a
    /// single database per origin is the correct unit of persistence: the
    /// existing <c>User.Id</c> is regenerated per tab via sessionStorage and
    /// therefore unsuitable for keying long-lived storage.
    /// </summary>
    internal static class DndMapperLibrarySchema
    {
        public const string DatabaseName = "KnockBox.DndMapper";
        // v2 (2026-05): switched the schema to the declarative Stores path
        // because the previous OnUpgrade callback couldn't survive the C#
        // round-trip — the IDB spec leaves a versionchange transaction's
        // active flag false outside IDB event handlers, so any schema op
        // issued from the resumed handler aborts the upgrade. Bump the
        // version any time DeclaredStores changes so the upgrade fires
        // against already-installed databases.
        public const int CurrentVersion = 2;

        /// <summary>Singleton record holding the host's persisted library snapshot (JSON).</summary>
        public const string LibraryStore = "library";

        /// <summary>The single key under which <see cref="LibraryStore"/> writes the snapshot.</summary>
        public const string LibraryStoreKey = "singleton";

        /// <summary>Per-image blob bytes, keyed by <c>MapImage.Id.ToString()</c>.</summary>
        public const string ImagesStore = "images";

        private static readonly IReadOnlyList<DeclaredStore> DeclaredStores =
        [
            new(LibraryStore, DeclaredStoreKind.Json),
            new(ImagesStore, DeclaredStoreKind.Blob),
        ];

        public static IndexedDbSchema Create()
            => new(DatabaseName, CurrentVersion) { Stores = DeclaredStores };
    }
}
