namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// Surface exposed to <see cref="UpgradeHandler"/> for mutating database
    /// schema and migrating data. Only valid for the duration of the upgrade
    /// callback — using a reference outside of it produces an
    /// <see cref="IndexedDbErrorKind.TransactionInactive"/> result from
    /// subsequent calls.
    /// </summary>
    /// <remarks>
    /// The upgrade callback runs inside a <c>versionchange</c> transaction
    /// which is both a schema-mutation surface (create / delete stores and
    /// indexes) and a data-access surface (read / write existing records to
    /// migrate them). The store accessors below give you the data view; the
    /// returned <see cref="IUpgradeStoreHandle"/> from create or
    /// <see cref="Store"/> exposes the schema-mutation view for that store.
    /// </remarks>
    public interface IUpgradeContext
    {
        int OldVersion { get; }
        int NewVersion { get; }

        /// <summary>Names of every object store currently in the database.</summary>
        IReadOnlyList<string> ObjectStoreNames { get; }

        /// <summary>
        /// Creates a new JSON-valued object store (read via
        /// <see cref="IObjectStore{TValue}"/> or <see cref="IJsonObjectStore"/>).
        /// Throws if a store with the same name already exists.
        /// </summary>
        IUpgradeStoreHandle CreateJsonObjectStore(
            string name,
            KeyPath? keyPath = null,
            bool autoIncrement = false);

        /// <summary>
        /// Creates a new blob-valued object store (read via
        /// <see cref="IBlobObjectStore"/>). Throws if a store with the same
        /// name already exists.
        /// </summary>
        IUpgradeStoreHandle CreateBlobObjectStore(
            string name,
            KeyPath? keyPath = null,
            bool autoIncrement = false);

        /// <summary>Returns the schema-mutation handle for an existing store.</summary>
        IUpgradeStoreHandle Store(string name);

        /// <summary>Permanently removes the named store and all its data.</summary>
        void DeleteObjectStore(string name);

        /// <summary>
        /// Typed POCO data view of an existing JSON store, for migration.
        /// Awaiting this also flushes any queued schema mutations (store
        /// creates / index changes / store deletes) to JS so the returned
        /// handle sees the post-mutation schema.
        /// </summary>
        ValueTask<IObjectStore<TValue>> ObjectStoreAsync<TValue>(string name);

        /// <summary>
        /// Untyped JSON data view of an existing JSON store, for migration.
        /// See <see cref="ObjectStoreAsync{TValue}"/> for the schema-flush
        /// behavior.
        /// </summary>
        ValueTask<IJsonObjectStore> JsonObjectStoreAsync(string name);

        /// <summary>
        /// Blob data view of an existing blob store, for migration. See
        /// <see cref="ObjectStoreAsync{TValue}"/> for the schema-flush
        /// behavior.
        /// </summary>
        ValueTask<IBlobObjectStore> BlobObjectStoreAsync(string name);
    }

    /// <summary>
    /// Schema-mutation handle to a single store during an upgrade transaction.
    /// Outside the upgrade callback, all members fail with
    /// <see cref="IndexedDbErrorKind.TransactionInactive"/>.
    /// </summary>
    public interface IUpgradeStoreHandle
    {
        string Name { get; }

        IReadOnlyList<string> IndexNames { get; }

        /// <summary>Defines a new index on this store.</summary>
        void CreateIndex(
            string name,
            KeyPath keyPath,
            bool unique = false,
            bool multiEntry = false);

        /// <summary>Removes a previously-defined index.</summary>
        void DeleteIndex(string name);
    }
}
