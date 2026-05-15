using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Services.Library
{
    /// <summary>
    /// Circuit-scoped orchestrator for the host's persistent DnD Mapper library.
    /// Owns the IndexedDB handle, the per-image blob cache, the per-image
    /// blob-share cache, and the debounced auto-save loop. Routes uploads
    /// through (a) blob create, (b) IndexedDB put, (c) share publish, (d)
    /// engine state mutation, with rollback if (d) fails.
    /// <para>
    /// The engine itself is a singleton and intentionally has no IndexedDB
    /// dependency — it accepts pre-validated <see cref="MapImage"/> metadata.
    /// This service exists on the host's circuit only; non-host callers may
    /// resolve it from DI but should not call its members.
    /// </para>
    /// </summary>
    public sealed class DndMapperLibraryService : IAsyncDisposable
    {
        // 500 ms quiet-period before flushing accumulated state changes to
        // IndexedDB. Coalesces high-frequency mutations (token drag, slider
        // scrubbing) into one write per burst.
        private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);

        private readonly IIndexedDbService _indexedDb;
        private readonly DndMapperGameEngine _engine;
        private readonly ILogger<DndMapperLibraryService> _logger;

        private readonly Dictionary<Guid, IndexedDbBlob> _blobCache = new();
        private readonly Dictionary<Guid, IBlobShare> _shareCache = new();
        // Serializes concurrent saves so we never overlap two write transactions
        // on the same store; also gives DisposeAsync a way to await an in-flight
        // flush before tearing down the DB handle.
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        private IIndexedDatabase? _db;
        private DndMapperGameState? _state;
        private IDisposable? _stateSub;
        private Timer? _saveTimer;
        private CancellationTokenSource? _saveCts;
        private bool _disposed;

        public DndMapperLibraryService(
            IIndexedDbService indexedDb,
            DndMapperGameEngine engine,
            ILogger<DndMapperLibraryService> logger)
        {
            _indexedDb = indexedDb;
            _engine = engine;
            _logger = logger;
        }

        /// <summary>
        /// <see langword="true"/> after <see cref="AttachAsync"/> finds a
        /// non-empty <c>library</c> store. Host UI binds to this to surface
        /// the "Load previous content / Start fresh" banner.
        /// </summary>
        public bool HasExistingLibrary { get; private set; }

        /// <summary>
        /// Opens the IndexedDB, reports whether a previous-session snapshot
        /// exists, and subscribes to the state's change feed for debounced
        /// auto-save. Idempotent: a second call against an already-attached
        /// service returns success without reopening.
        /// </summary>
        public async ValueTask<Result> AttachAsync(DndMapperGameState state, User host, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (state is null) return Result.FromError("State is required.");
            if (host is null) return Result.FromError("Host is required.");

            if (_db is not null) return Result.Success;

            var openResult = await _indexedDb.OpenAsync(DndMapperLibrarySchema.Create(), ct);
            if (!openResult.TryGetSuccess(out var db))
            {
                openResult.TryGetFailure(out var err);
                _logger.LogError("Failed to open DnD Mapper IndexedDB: {Error}", err.Message);
                return Result.FromError($"Failed to open library database: {err.Message}");
            }

            _db = db;
            _state = state;
            _saveCts = new CancellationTokenSource();

            var countResult = await _db.RunAsync<long>(
                [DndMapperLibrarySchema.LibraryStore],
                TransactionMode.ReadOnly,
                async (tx, token) =>
                {
                    var library = tx.JsonObjectStore(DndMapperLibrarySchema.LibraryStore);
                    return await library.CountAsync(range: null, token);
                },
                ct);

            if (countResult.TryGetSuccess(out var count))
            {
                HasExistingLibrary = count > 0;
            }
            else
            {
                countResult.TryGetFailure(out var err);
                _logger.LogWarning("Failed to probe DnD Mapper library store for existing content: {Error}", err.Message);
                HasExistingLibrary = false;
            }

            // Stand up the debounce timer in a stopped state and subscribe.
            _saveTimer = new Timer(OnSaveTimerTick, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _stateSub = state.StateChangedEventManager.Subscribe(OnStateChangedAsync);

            return Result.Success;
        }

        /// <summary>
        /// Uploads a new image. Stores the bytes in IndexedDB on the host's
        /// browser, publishes a blob-share so other players can fetch it
        /// through the host's circuit, then asks the engine to record the
        /// metadata. The blob and the share are cached for the lifetime of
        /// the circuit so subsequent re-publishes (after reconnect) can reuse
        /// them without re-reading the bytes.
        /// </summary>
        public async ValueTask<ValueResult<MapImage>> AddImageAsync(
            DndMapperGameState state,
            User host,
            Guid mapId,
            string contentType,
            long byteSize,
            double originalWidthCells,
            double originalHeightCells,
            Stream content,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null) return ValueResult<MapImage>.FromError("Library is not attached. Call AttachAsync first.");
            if (state is null) return ValueResult<MapImage>.FromError("State is required.");
            if (host is null) return ValueResult<MapImage>.FromError("Host is required.");
            if (content is null) return ValueResult<MapImage>.FromError("Image content stream is required.");
            if (byteSize <= 0) return ValueResult<MapImage>.FromError("Byte size must be positive.");

            var imageId = Guid.NewGuid();

            // Step 1: hand the stream to the IndexedDB SDK, which chunks bytes
            // across SignalR and constructs the JS-side Blob. The SDK takes
            // ownership of the stream (we pass leaveOpen=false by default).
            IndexedDbBlob blob;
            try
            {
                blob = await _indexedDb.CreateBlobAsync(content, byteSize, contentType, leaveOpen: false, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to create IndexedDB blob for image {ImageId}.", imageId);
                return ValueResult<MapImage>.FromError("Failed to buffer image into IndexedDB.");
            }

            // Step 2: persist the blob into the images store.
            var key = IndexedDbKey.String(imageId.ToString("D"));
            var putResult = await _db.RunAsync(
                [DndMapperLibrarySchema.ImagesStore],
                TransactionMode.ReadWrite,
                async (tx, token) =>
                {
                    var images = tx.BlobObjectStore(DndMapperLibrarySchema.ImagesStore);
                    var put = await images.PutAsync(blob, key, token);
                    if (put.IsSuccess) return Result<IndexedDbError>.Success;
                    put.TryGetFailure(out var perr);
                    return Result<IndexedDbError>.FromError(perr);
                },
                ct);

            if (!putResult.IsSuccess)
            {
                putResult.TryGetFailure(out var perr);
                _logger.LogError("Failed to persist image {ImageId} to IndexedDB: {Error}", imageId, perr.Message);
                await SafeDisposeAsync(blob);
                return ValueResult<MapImage>.FromError($"Failed to persist image: {perr.Message}");
            }

            // Step 3: publish a blob-share so player circuits can fetch the bytes.
            IBlobShare share;
            try
            {
                share = await blob.PublishForSharingAsync(options: null, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to publish blob share for image {ImageId}.", imageId);
                await SafeDeleteAsync(key);
                await SafeDisposeAsync(blob);
                return ValueResult<MapImage>.FromError("Failed to publish image share.");
            }

            // Step 4: register with the engine. If the engine rejects (cap race,
            // unknown map id), roll back the blob, share, and stored row.
            var image = new MapImage
            {
                Id = imageId,
                ContentType = contentType,
                ShareToken = share.Token,
                X = 0,
                Y = 0,
                Width = originalWidthCells > 0 ? originalWidthCells : 10,
                Height = originalHeightCells > 0 ? originalHeightCells : 10,
                OriginalWidth = originalWidthCells,
                OriginalHeight = originalHeightCells,
                Rotation = 0,
                Opacity = 1.0,
                LayerOrder = 0, // engine overwrites
                Locked = false,
                ByteSize = byteSize,
            };

            var addResult = _engine.AddImageAsync(state, host, mapId, image);
            if (!addResult.TryGetSuccess(out var added))
            {
                _logger.LogInformation("Engine rejected image {ImageId}; rolling back blob.", imageId);
                await SafeDisposeAsync(share);
                await SafeDeleteAsync(key);
                await SafeDisposeAsync(blob);
                return addResult;
            }

            _blobCache[imageId] = blob;
            _shareCache[imageId] = share;
            return ValueResult<MapImage>.FromValue(added);
        }

        /// <summary>
        /// Deletes every record in the library database (snapshot + all image
        /// blobs) and clears in-memory handle caches. Used by the host UI's
        /// "Start fresh" affordance. The IndexedDB stays open with empty stores
        /// so subsequent auto-saves and uploads continue to work.
        /// </summary>
        public async ValueTask<Result> DiscardLibraryAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null) return Result.FromError("Library is not attached.");

            // Stop the debounce timer first so an in-flight tick doesn't
            // re-write the snapshot we're about to delete.
            _saveTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            await _saveLock.WaitAsync(ct);
            try
            {
                var result = await _db.RunAsync(
                    [DndMapperLibrarySchema.LibraryStore, DndMapperLibrarySchema.ImagesStore],
                    TransactionMode.ReadWrite,
                    async (tx, token) =>
                    {
                        var library = tx.JsonObjectStore(DndMapperLibrarySchema.LibraryStore);
                        var libClear = await library.ClearAsync(token);
                        if (!libClear.IsSuccess)
                        {
                            libClear.TryGetFailure(out var lerr);
                            return Result<IndexedDbError>.FromError(lerr);
                        }
                        var images = tx.BlobObjectStore(DndMapperLibrarySchema.ImagesStore);
                        var imgClear = await images.ClearAsync(token);
                        if (!imgClear.IsSuccess)
                        {
                            imgClear.TryGetFailure(out var ierr);
                            return Result<IndexedDbError>.FromError(ierr);
                        }
                        return Result<IndexedDbError>.Success;
                    },
                    ct);

                if (!result.IsSuccess)
                {
                    result.TryGetFailure(out var err);
                    return Result.FromError($"Failed to clear library: {err.Message}");
                }

                foreach (var share in _shareCache.Values) await SafeDisposeAsync(share);
                _shareCache.Clear();
                foreach (var blob in _blobCache.Values) await SafeDisposeAsync(blob);
                _blobCache.Clear();
                HasExistingLibrary = false;
                return Result.Success;
            }
            finally
            {
                _saveLock.Release();
            }
        }

        // ── Debounced auto-save ───────────────────────────────────────────────

        private ValueTask OnStateChangedAsync()
        {
            // Subscribers are invoked OUTSIDE the state's Execute lock; restart
            // the debounce timer cheaply on every event.
            _saveTimer?.Change(SaveDebounce, Timeout.InfiniteTimeSpan);
            return ValueTask.CompletedTask;
        }

        private void OnSaveTimerTick(object? _)
        {
            // Fire-and-forget — the Timer callback signature is sync. Failures
            // are logged inside FlushSnapshotAsync; the next state change will
            // reschedule us.
            _ = FlushSnapshotAsync();
        }

        private async Task FlushSnapshotAsync()
        {
            if (_disposed) return;
            var db = _db;
            var state = _state;
            var cts = _saveCts;
            if (db is null || state is null || cts is null || cts.IsCancellationRequested) return;

            if (!await _saveLock.WaitAsync(TimeSpan.Zero))
            {
                // Another flush is already running; the latest state will get
                // picked up by the next debounce tick once the timer is rearmed
                // by subsequent StateChanged events.
                return;
            }
            try
            {
                if (_disposed || cts.IsCancellationRequested) return;

                // Build the snapshot under the state's read lock so we don't
                // race with a concurrent Execute. The read action is pure-sync
                // (no I/O) so use the synchronous overload, capturing via
                // closure. Exit the lock BEFORE doing IndexedDB I/O.
                LibrarySnapshot? snapshot = null;
                var readResult = state.WithExclusiveRead(() => { snapshot = LibrarySnapshotMapper.FromState(state); });
                if (!readResult.IsSuccess || snapshot is null)
                {
                    _logger.LogWarning("Auto-save read of game state failed; skipping flush.");
                    return;
                }

                var key = IndexedDbKey.String(DndMapperLibrarySchema.LibraryStoreKey);
                var writeResult = await db.RunAsync(
                    [DndMapperLibrarySchema.LibraryStore],
                    TransactionMode.ReadWrite,
                    async (tx, token) =>
                    {
                        var library = tx.ObjectStore<LibrarySnapshot>(DndMapperLibrarySchema.LibraryStore);
                        var put = await library.PutAsync(snapshot, key, token);
                        if (put.IsSuccess) return Result<IndexedDbError>.Success;
                        put.TryGetFailure(out var perr);
                        return Result<IndexedDbError>.FromError(perr);
                    },
                    cts.Token);

                if (!writeResult.IsSuccess)
                {
                    writeResult.TryGetFailure(out var werr);
                    _logger.LogWarning("DnD Mapper auto-save write failed: {Error}", werr.Message);
                }
                else
                {
                    HasExistingLibrary = true;
                }
            }
            catch (OperationCanceledException) { /* shutting down */ }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DnD Mapper auto-save flush threw unexpectedly.");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            // 1. Stop the debounce timer and prevent the in-flight callback
            //    from queuing further work.
            try { _saveCts?.Cancel(); } catch { /* ignore */ }
            if (_saveTimer is not null)
            {
                _saveTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                await _saveTimer.DisposeAsync();
                _saveTimer = null;
            }

            // 2. Drain any in-flight flush so the DB handle is safe to dispose.
            try { await _saveLock.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { /* ignore */ }
            try
            {
                // 3. Drop the state subscription.
                _stateSub?.Dispose();
                _stateSub = null;

                // 4. Release cached blobs and shares.
                foreach (var share in _shareCache.Values) await SafeDisposeAsync(share);
                _shareCache.Clear();
                foreach (var blob in _blobCache.Values) await SafeDisposeAsync(blob);
                _blobCache.Clear();

                // 5. Close the DB.
                if (_db is not null)
                {
                    await SafeDisposeAsync(_db);
                    _db = null;
                }
            }
            finally
            {
                try { _saveLock.Release(); } catch { /* ignore */ }
                _saveLock.Dispose();
                _saveCts?.Dispose();
                _saveCts = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DndMapperLibraryService));
        }

        private async ValueTask SafeDeleteAsync(IndexedDbKey key)
        {
            if (_db is null) return;
            try
            {
                await _db.RunAsync(
                    [DndMapperLibrarySchema.ImagesStore],
                    TransactionMode.ReadWrite,
                    async (tx, token) =>
                    {
                        var images = tx.BlobObjectStore(DndMapperLibrarySchema.ImagesStore);
                        var del = await images.DeleteAsync(key, token);
                        if (del.IsSuccess) return Result<IndexedDbError>.Success;
                        del.TryGetFailure(out var derr);
                        return Result<IndexedDbError>.FromError(derr);
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Best-effort delete of IndexedDB image row failed.");
            }
        }

        private async ValueTask SafeDisposeAsync(IAsyncDisposable disposable)
        {
            try { await disposable.DisposeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to dispose {Type}.", disposable.GetType().Name); }
        }
    }
}
