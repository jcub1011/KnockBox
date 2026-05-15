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
        public const int CurrentVersion = 1;

        /// <summary>Singleton record holding the host's persisted library snapshot (JSON).</summary>
        public const string LibraryStore = "library";

        /// <summary>The single key under which <see cref="LibraryStore"/> writes the snapshot.</summary>
        public const string LibraryStoreKey = "singleton";

        /// <summary>Per-image blob bytes, keyed by <c>MapImage.Id.ToString()</c>.</summary>
        public const string ImagesStore = "images";

        public static IndexedDbSchema Create()
            => new(DatabaseName, CurrentVersion)
            {
                OnUpgrade = UpgradeAsync,
            };

        private static ValueTask UpgradeAsync(IUpgradeContext ctx, int oldVersion, int newVersion, CancellationToken ct)
        {
            // v0 -> v1: fresh-install path. New stores get a default keyPath of
            // null (caller-supplied keys), matching how the service writes records.
            if (oldVersion < 1)
            {
                ctx.CreateJsonObjectStore(LibraryStore);
                ctx.CreateBlobObjectStore(ImagesStore);
            }
            return ValueTask.CompletedTask;
        }
    }
}
