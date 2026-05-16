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
        // v3 (2026-05): split the single 'library/singleton' record into
        // per-slot snapshots keyed by slot id (with __auto__ reserved for the
        // Auto Save), plus a slots_index/singleton record listing the slots.
        // The v2→v3 migration runs at attach time (IDB upgrade transactions
        // can't survive the C# round-trip).
        public const int CurrentVersion = 3;

        /// <summary>Per-slot persisted snapshot. Key = slot id (<see cref="AutoSlotId"/> or a GUID string).</summary>
        public const string LibraryStore = "library";

        /// <summary>Legacy single-record key (v2). Used only by the migration path.</summary>
        public const string LegacySingletonKey = "singleton";

        /// <summary>Reserved slot id for the Auto Save slot. Cannot be renamed or deleted.</summary>
        public const string AutoSlotId = "__auto__";

        /// <summary>Display name for the Auto Save slot.</summary>
        public const string AutoSlotName = "Auto Save";

        /// <summary>Singleton JSON record holding the slot index.</summary>
        public const string SlotsIndexStore = "slots_index";

        /// <summary>The single key under which <see cref="SlotsIndexStore"/> writes its record.</summary>
        public const string SlotsIndexKey = "singleton";

        /// <summary>Per-image blob bytes, keyed by <c>MapImage.Id.ToString()</c>.</summary>
        public const string ImagesStore = "images";

        private static readonly IReadOnlyList<DeclaredStore> DeclaredStores =
        [
            new(LibraryStore, DeclaredStoreKind.Json),
            new(SlotsIndexStore, DeclaredStoreKind.Json),
            new(ImagesStore, DeclaredStoreKind.Blob),
        ];

        public static IndexedDbSchema Create()
            => new(DatabaseName, CurrentVersion) { Stores = DeclaredStores };
    }
}
