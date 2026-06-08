using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// Wraps an inner <see cref="IIndexedDbService"/> and transparently
    /// namespaces every database under a plugin's route identifier. A plugin
    /// that opens databases through this wrapper can't accidentally open,
    /// enumerate, or delete another plugin's (or the host's) IndexedDB
    /// databases, and need not worry about database-name collisions.
    /// <para>
    /// <see cref="OpenAsync"/> and <see cref="DeleteDatabaseAsync"/> prefix the
    /// database name with <c>"{route}::"</c>; <see cref="ListDatabasesAsync"/>
    /// only surfaces this plugin's databases (with the prefix stripped). Blob
    /// creation isn't database-scoped and passes straight through.
    /// </para>
    /// <para>
    /// This is collision-avoidance, not a sandbox: a plugin can still reach raw
    /// IndexedDB directly. The aim is to make cross-plugin clobbering hard to do
    /// <i>by accident</i>.
    /// </para>
    /// </summary>
    public sealed class ScopedIndexedDbService : IIndexedDbService
    {
        // Route identifiers match ^[a-z0-9-]+$ and never contain ':', so this
        // separator can't collide with a host or plugin database name.
        private const string Separator = "::";

        private readonly IIndexedDbService _inner;
        private readonly string _namePrefix; // "{route}::"

        public ScopedIndexedDbService(IIndexedDbService inner, string routeIdentifier)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentException.ThrowIfNullOrWhiteSpace(routeIdentifier);
            _inner = inner;
            _namePrefix = routeIdentifier + Separator;
        }

        public ValueTask<ValueResult<IIndexedDatabase, IndexedDbError>> OpenAsync(
            IndexedDbSchema schema, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(schema);
            return _inner.OpenAsync(schema with { Name = _namePrefix + schema.Name }, ct);
        }

        public ValueTask<Result<IndexedDbError>> DeleteDatabaseAsync(string name, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            return _inner.DeleteDatabaseAsync(_namePrefix + name, ct);
        }

        public async ValueTask<ValueResult<IReadOnlyList<DatabaseInfo>, IndexedDbError>> ListDatabasesAsync(
            CancellationToken ct = default)
        {
            var result = await _inner.ListDatabasesAsync(ct).ConfigureAwait(false);
            if (result.IsCanceled) return ValueResult<IReadOnlyList<DatabaseInfo>, IndexedDbError>.Canceled;
            if (!result.TryGetSuccess(out var infos)) return result.Error.Error;

            IReadOnlyList<DatabaseInfo> mine = infos
                .Where(i => i.Name.StartsWith(_namePrefix, StringComparison.Ordinal))
                .Select(i => new DatabaseInfo(i.Name[_namePrefix.Length..], i.Version))
                .ToList();
            return ValueResult<IReadOnlyList<DatabaseInfo>, IndexedDbError>.FromValue(mine);
        }

        public ValueTask<ValueResult<IndexedDbBlob, IndexedDbError>> CreateBlobAsync(
            ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct = default)
            => _inner.CreateBlobAsync(bytes, contentType, ct);

        public ValueTask<ValueResult<IndexedDbBlob, IndexedDbError>> CreateBlobAsync(
            Stream stream, long length, string contentType, bool leaveOpen = false, CancellationToken ct = default)
            => _inner.CreateBlobAsync(stream, length, contentType, leaveOpen, ct);

        /// <summary>
        /// Migrates between two of <i>this plugin's</i> databases — both names
        /// are prefixed with the plugin's route, so it can't reach outside the
        /// plugin's namespace. To pull a pre-scoping (unprefixed) legacy
        /// database into the namespace, use
        /// <see cref="MigrateLegacyDatabaseAsync"/> instead.
        /// </summary>
        public ValueTask<Result<IndexedDbError>> MigrateDatabaseAsync(
            string fromName, string toName, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fromName);
            ArgumentException.ThrowIfNullOrWhiteSpace(toName);
            return _inner.MigrateDatabaseAsync(_namePrefix + fromName, _namePrefix + toName, ct);
        }

        /// <summary>
        /// One-time import of a pre-scoping (unprefixed) <paramref name="legacyLiteralName"/>
        /// database into this plugin's namespace under
        /// <paramref name="targetSchema"/>'s name. The source name is passed
        /// through verbatim; only the destination is route-prefixed. Same
        /// guards as <see cref="MigrateDatabaseAsync"/>: a no-op once the
        /// destination exists or the source is gone.
        /// </summary>
        public ValueTask<Result<IndexedDbError>> MigrateLegacyDatabaseAsync(
            string legacyLiteralName, IndexedDbSchema targetSchema, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(legacyLiteralName);
            ArgumentNullException.ThrowIfNull(targetSchema);
            return _inner.MigrateDatabaseAsync(legacyLiteralName, _namePrefix + targetSchema.Name, ct);
        }
    }
}
