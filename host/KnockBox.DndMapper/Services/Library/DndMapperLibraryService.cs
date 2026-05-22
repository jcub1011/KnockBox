using System.Security.Cryptography;
using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Library.Vtf;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

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

        // Plugin-owned ES module that decodes natural pixel dimensions from a
        // blob: URL. Lives in this plugin's wwwroot so the shared adoption API
        // (IIndexedDatabase.AdoptInputElementFilesAsync) can stay free of
        // image-specific concerns.
        private const string ImageDimensionsModulePath =
            "/_content/KnockBox.DndMapper/js/dndMapperImageDimensions.js";

        private readonly IIndexedDbService _indexedDb;
        private readonly DndMapperGameEngine _engine;
        private readonly IJSRuntime _jsRuntime;
        private readonly Lazy<Task<IJSObjectReference>> _imageDimsModule;
        private readonly ILogger<DndMapperLibraryService> _logger;

        private readonly Dictionary<Guid, IndexedDbBlob> _blobCache = new();
        private readonly Dictionary<Guid, IBlobShare> _shareCache = new();
        // Serializes concurrent saves so we never overlap two write transactions
        // on the same store; also gives DisposeAsync a way to await an in-flight
        // flush before tearing down the DB handle.
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        private IIndexedDatabase? _db;
        private DndMapperGameState? _state;
        private User? _host;
        private IDisposable? _stateSub;
        private Timer? _saveTimer;
        private CancellationTokenSource? _saveCts;
        private bool _disposed;
        // Set to 1 by every StateChanged; cleared by a flush right before it
        // reads state. If a StateChanged fires *during* an in-flight flush
        // (or a flush returns early on lock collision), this flag stays 1 and
        // FlushSnapshotAsync re-arms the debounce timer after release so the
        // last burst of edits doesn't get lost when no further events fire.
        private int _pendingDirty;

        // Per-shard SHA-256 of the last serialized JSON for the Auto Save slot,
        // keyed by IDB compound key (e.g., "__auto__:core", "__auto__:map:{g}",
        // "__auto__:sheet:{g}"). FlushSnapshotAsync compares against this and
        // only writes shards whose hash differs, then refreshes the cache.
        // Cleared on Attach/Detach; not used for manual-slot ops (those always
        // write every shard).
        private readonly Dictionary<string, byte[]> _autoFlushHashes = new();

        // Slot ids whose v3 → v4 single-record migration has been probed in
        // this attachment. LoadSlotAsync consults this to avoid reissuing the
        // legacy-key read on every refresh of the saves panel.
        private readonly HashSet<string> _migratedSlots = new();

        public DndMapperLibraryService(
            IIndexedDbService indexedDb,
            DndMapperGameEngine engine,
            IJSRuntime jsRuntime,
            ILogger<DndMapperLibraryService> logger)
        {
            _indexedDb = indexedDb;
            _engine = engine;
            _jsRuntime = jsRuntime;
            _logger = logger;
            _imageDimsModule = new Lazy<Task<IJSObjectReference>>(() =>
                _jsRuntime.InvokeAsync<IJSObjectReference>("import", ImageDimensionsModulePath).AsTask());
        }

        /// <summary>
        /// <see langword="true"/> after <see cref="AttachAsync"/> finds a
        /// non-empty <c>library</c> store. Host UI binds to this to surface
        /// the "Load previous content / Start fresh" banner.
        /// </summary>
        public bool HasExistingLibrary { get; private set; }

        // True while either an auto-save is in flight (lock held by FlushSnapshotAsync)
        // or a state change is pending in the debounce window, or a manual slot
        // operation is mutating IndexedDB. The host UI subscribes to SavingChanged
        // to show a "Saving…" indicator and toggle the beforeunload guard.
        public bool IsSaving { get; private set; }
        public event Action? SavingChanged;
        // Bumped each time the slot list mutates so the saves panel can refresh.
        public event Action? SlotsChanged;

        private int _manualOpsInFlight;
        private void SetSaving(bool v)
        {
            if (IsSaving == v) return;
            IsSaving = v;
            try { SavingChanged?.Invoke(); } catch { /* subscriber failures shouldn't kill the flush */ }
        }
        private void NotifySlotsChanged()
        {
            try { SlotsChanged?.Invoke(); } catch { /* same */ }
        }

        /// <summary>
        /// Opens the IndexedDB, reports whether a previous-session snapshot
        /// exists, and subscribes to the state's change feed for debounced
        /// auto-save. Idempotent: a second call against an already-attached
        /// service returns success without reopening.
        /// <para>
        /// The service is registered <c>Scoped</c> (one instance per circuit).
        /// A single circuit may host more than one DnD Mapper session in
        /// sequence (user leaves room A then enters room B without losing the
        /// circuit), so the lifecycle is Attach → DetachAsync → Attach. A
        /// fresh attach after detach reopens the DB, re-probes the snapshot,
        /// and re-runs republish; the idempotence on a *live* attach is just
        /// a safety net for <c>ImageUploadButton</c>'s lazy-attach path.
        /// </para>
        /// </summary>
        public async ValueTask<Result> AttachAsync(DndMapperGameState state, User host, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (state is null) return Result.FromError("State is required.");
            if (host is null) return Result.FromError("Host is required.");

            if (_db is not null) return Result.Success;

            var openOrRecover = await OpenWithRecoveryAsync(ct);
            if (!openOrRecover.TryGetSuccess(out var db))
            {
                openOrRecover.TryGetFailure(out var oerr);
                return Result.FromError(oerr);
            }

            _db = db;
            _state = state;
            _host = host;
            _saveCts = new CancellationTokenSource();

            // v2→v3 migration: lift the legacy `library/singleton` snapshot into
            // the Auto Save slot and seed the slots index. Idempotent.
            await EnsureMigratedAsync(_db, ct);

            // "Existing content" now means the Auto Save slot exists in the
            // index (the banner offers to load it).
            var slotsResult = await _db.JsonGetSingleAsync<SlotsIndex>(
                DndMapperLibrarySchema.SlotsIndexStore,
                IndexedDbKey.String(DndMapperLibrarySchema.SlotsIndexKey), ct);
            HasExistingLibrary = slotsResult.TryGetSuccess(out var idx)
                && idx is not null
                && idx.Slots.Any(s => s.Id == DndMapperLibrarySchema.AutoSlotId);

            // Stand up the debounce timer in a stopped state and subscribe.
            _saveTimer = new Timer(OnSaveTimerTick, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _stateSub = state.StateChangedEventManager.Subscribe(OnStateChangedAsync);

            // Reconnect path: if the state already carries images from a prior
            // circuit (1-minute reconnect grace), their ShareTokens are dead.
            // Re-load each blob from IndexedDB, publish a fresh share, and
            // broadcast the new token via the engine. Failures here are
            // logged but non-fatal — the player UI keeps the placeholder
            // until the next attempt.
            await RepublishExistingImagesAsync(state, host, ct);

            // Notify subscribers (e.g. HostSavesPanel) that the slots are now
            // readable. Without this, a panel that mounted while Attach was
            // still awaiting the IndexedDB open would have cached a "Library
            // is not attached" error and would never refresh until a state
            // change happened to trigger one. Firing here clears that race.
            NotifySlotsChanged();

            return Result.Success;
        }

        /// <summary>
        /// Opens the IndexedDB and verifies the expected stores exist. If the
        /// DB opens at the target version but is missing one or both stores —
        /// the symptom of a partially-applied upgrade transaction left over
        /// from a stale build — the broken database is deleted and reopened
        /// once so a fresh upgrade can run from <c>v0</c>. A second failure
        /// surfaces as an error to the caller; we never delete more than once
        /// per Attach.
        /// </summary>
        private async ValueTask<ValueResult<IIndexedDatabase>> OpenWithRecoveryAsync(CancellationToken ct)
        {
            var openResult = await _indexedDb.OpenAsync(DndMapperLibrarySchema.Create(), ct);
            if (!openResult.TryGetSuccess(out var db))
            {
                openResult.TryGetFailure(out var err);
                _logger.LogError("Failed to open DnD Mapper IndexedDB: {Error}", err.Message);
                return ValueResult<IIndexedDatabase>.FromError($"Failed to open library database: {err.Message}");
            }

            if (HasExpectedStores(db)) return ValueResult<IIndexedDatabase>.FromValue(db);

            _logger.LogWarning(
                "DnD Mapper IndexedDB opened at v{Version} but is missing expected stores; actual store list: [{Stores}]. Recreating the database.",
                db.Version, string.Join(", ", db.ObjectStoreNames));

            await SafeDisposeAsync(db);
            var deleteResult = await _indexedDb.DeleteDatabaseAsync(DndMapperLibrarySchema.DatabaseName, ct);
            if (deleteResult.TryGetFailure(out var delErr))
            {
                _logger.LogError("Failed to delete corrupt DnD Mapper IndexedDB during recovery: {Error}", delErr.Message);
                return ValueResult<IIndexedDatabase>.FromError(
                    $"Failed to recover library database: {delErr.Message}");
            }

            var reopenResult = await _indexedDb.OpenAsync(DndMapperLibrarySchema.Create(), ct);
            if (!reopenResult.TryGetSuccess(out var reopened))
            {
                reopenResult.TryGetFailure(out var rerr);
                _logger.LogError("Failed to reopen DnD Mapper IndexedDB after recovery: {Error}", rerr.Message);
                return ValueResult<IIndexedDatabase>.FromError(
                    $"Failed to reopen library database after recovery: {rerr.Message}");
            }

            if (!HasExpectedStores(reopened))
            {
                _logger.LogError(
                    "DnD Mapper IndexedDB still missing stores after recovery; actual store list: [{Stores}].",
                    string.Join(", ", reopened.ObjectStoreNames));
                await SafeDisposeAsync(reopened);
                return ValueResult<IIndexedDatabase>.FromError(
                    "Library database is missing expected object stores after recovery; the schema upgrade did not apply.");
            }

            return ValueResult<IIndexedDatabase>.FromValue(reopened);
        }

        private static bool HasExpectedStores(IIndexedDatabase db)
        {
            var stores = db.ObjectStoreNames;
            return stores.Contains(DndMapperLibrarySchema.LibraryStore)
                && stores.Contains(DndMapperLibrarySchema.SlotsIndexStore)
                && stores.Contains(DndMapperLibrarySchema.ImagesStore);
        }

        // Migrates a pre-v3 layout (single `library/singleton` snapshot, no
        // slots index) to the multi-slot layout. Idempotent: a no-op when the
        // slots index already exists. Always returns success; failures are
        // logged but won't block attach (the host can fall back to Start fresh).
        private async ValueTask EnsureMigratedAsync(IIndexedDatabase db, CancellationToken ct)
        {
            var indexKey = IndexedDbKey.String(DndMapperLibrarySchema.SlotsIndexKey);
            var idxResult = await db.JsonGetSingleAsync<SlotsIndex>(
                DndMapperLibrarySchema.SlotsIndexStore, indexKey, ct);

            if (idxResult.TryGetSuccess(out var existingIdx) && existingIdx is not null) return;

            // Look for a legacy `library/singleton` record from v2.
            var legacyKey = IndexedDbKey.String(DndMapperLibrarySchema.LegacySingletonKey);
            var legacy = await db.JsonGetSingleAsync<LibrarySnapshot>(
                DndMapperLibrarySchema.LibraryStore, legacyKey, ct);

            var index = new SlotsIndex();
            if (legacy.TryGetSuccess(out var legacySnap) && legacySnap is not null)
            {
                // Move legacy snapshot under the reserved Auto Save id.
                var autoKey = IndexedDbKey.String(DndMapperLibrarySchema.AutoSlotId);
                var write = await db.JsonPutSingleAsync(
                    DndMapperLibrarySchema.LibraryStore, legacySnap, autoKey, ct);
                if (write.IsSuccess)
                {
                    await SafeLibraryDeleteAsync(db, legacyKey);
                    index.Slots.Add(new SlotIndexEntry
                    {
                        Id = DndMapperLibrarySchema.AutoSlotId,
                        Name = DndMapperLibrarySchema.AutoSlotName,
                        Kind = SlotKind.Auto,
                        UpdatedUtc = DateTime.UtcNow,
                    });
                }
                else
                {
                    write.TryGetFailure(out var werr);
                    _logger.LogWarning("Failed to migrate legacy library snapshot to slot: {Error}", werr.Message);
                }
            }

            // Write a (possibly empty) index so subsequent attaches skip migration.
            var putIdx = await db.JsonPutSingleAsync(
                DndMapperLibrarySchema.SlotsIndexStore, index, indexKey, ct);
            if (!putIdx.IsSuccess)
            {
                putIdx.TryGetFailure(out var perr);
                _logger.LogWarning("Failed to initialize slots index: {Error}", perr.Message);
            }
        }

        private async ValueTask SafeLibraryDeleteAsync(IIndexedDatabase db, IndexedDbKey key)
        {
            try { await db.DeleteSingleAsync(DndMapperLibrarySchema.LibraryStore, key); }
            catch (Exception ex) { _logger.LogWarning(ex, "Best-effort delete of library record failed."); }
        }

        private async ValueTask RepublishExistingImagesAsync(DndMapperGameState state, User host, CancellationToken ct)
        {
            if (_db is null) return;
            var imageIds = new List<(Guid MapId, Guid ImageId)>();
            state.WithExclusiveRead(() =>
            {
                foreach (var map in state.Maps)
                    foreach (var image in map.Images)
                        imageIds.Add((map.Id, image.Id));
            });

            // Skip images already wired up by an earlier attach in this circuit.
            var pending = imageIds.Where(p => !_shareCache.ContainsKey(p.ImageId)).ToArray();
            if (pending.Length == 0) return;

            // Run blob fetch + share publish in parallel for every pending
            // image. Each pair is independent at the IDB level; the JS side
            // can process them concurrently and SignalR streams the round-
            // trips in parallel. This collapses a sequential 50× ~50 ms walk
            // into a single parallel batch for the reconnect path.
            var fetchTasks = pending
                .Select(p => RepublishOneAsync(p.MapId, p.ImageId, ct).AsTask())
                .ToArray();
            var results = await Task.WhenAll(fetchTasks);

            // Cache mutation + engine notify run serially since both touch
            // shared state (the cache dictionaries are non-concurrent and
            // engine.UpdateImageShareTokenAsync takes the state lock).
            foreach (var (mapId, imageId, blob, share) in results)
            {
                if (ct.IsCancellationRequested) return;
                if (blob is null || share is null) continue;

                await ReplaceCacheEntryAsync(imageId, blob, share);

                var update = _engine.UpdateImageShareTokenAsync(state, host, mapId, imageId, share.Token);
                if (update.TryGetFailure(out var uerr))
                {
                    _logger.LogWarning("Reconnect: engine rejected new share token for image {ImageId}: {Error}", imageId, uerr.PublicMessage);
                }
            }
        }

        private async ValueTask<(Guid MapId, Guid ImageId, IndexedDbBlob? Blob, IBlobShare? Share)>
            RepublishOneAsync(Guid mapId, Guid imageId, CancellationToken ct)
        {
            if (_db is null || ct.IsCancellationRequested) return (mapId, imageId, null, null);

            var blobResult = await _db.BlobGetSingleAsync(
                DndMapperLibrarySchema.ImagesStore,
                IndexedDbKey.String(imageId.ToString("D")),
                ct);

            if (!blobResult.TryGetSuccess(out var blob) || blob is null)
            {
                _logger.LogWarning("Reconnect: image {ImageId} no longer in IndexedDB; leaving placeholder.", imageId);
                return (mapId, imageId, null, null);
            }

            IBlobShare share;
            try { share = await blob.PublishForSharingAsync(options: null, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnect: failed to republish image {ImageId}.", imageId);
                await SafeDisposeAsync(blob);
                return (mapId, imageId, null, null);
            }

            return (mapId, imageId, blob, share);
        }

        /// <summary>
        /// One file's outcome from
        /// <see cref="UploadImagesFromInputElementAsync"/>. Successful
        /// entries include the assigned <see cref="MapImage"/>; failed
        /// entries carry a human-readable <see cref="Error"/> describing
        /// which stage rejected the file (JS type/size filter, image decode,
        /// IndexedDB put, engine cap, …).
        /// </summary>
        public sealed record UploadOutcome(
            string Filename,
            MapImage? Image,
            string? Error);

        /// <summary>
        /// Pulls every file from the host's <c>&lt;input type="file"&gt;</c>
        /// element straight into the host's IndexedDB on the JS side (bytes
        /// never cross the SignalR boundary), then registers each image with
        /// the engine. Returns one <see cref="UploadOutcome"/> per file in
        /// selection order; per-file failures are reported on the outcome
        /// rather than aborting the whole batch.
        /// </summary>
        public async ValueTask<ValueResult<IReadOnlyList<UploadOutcome>>>
            UploadImagesFromInputElementAsync(
                DndMapperGameState state,
                User host,
                Guid mapId,
                ElementReference inputElement,
                CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null) return ValueResult<IReadOnlyList<UploadOutcome>>.FromError("Library is not attached.");
            if (state is null) return ValueResult<IReadOnlyList<UploadOutcome>>.FromError("State is required.");
            if (host is null) return ValueResult<IReadOnlyList<UploadOutcome>>.FromError("Host is required.");

            // Convert pixel dimensions from the JS-side image decode into
            // cell units. Falls back to 1 if the map has been removed
            // between the user picking files and this call.
            int cellPixels = 1;
            bool mapMissing = false;
            state.WithExclusiveRead(() =>
            {
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { mapMissing = true; return; }
                cellPixels = Math.Max(1, map.Grid.CellPixels);
            });
            if (mapMissing) return ValueResult<IReadOnlyList<UploadOutcome>>.FromError("Map not found.");

            var adoptResult = await _db.AdoptInputElementFilesAsync(
                inputElement,
                DndMapperLibrarySchema.ImagesStore,
                new AdoptInputFilesOptions(
                    AcceptedTypes: DndMapperGameEngine.AllowedImageContentTypeList,
                    MaxBytes: DndMapperGameEngine.PerFileCapBytes),
                ct);

            if (adoptResult.IsCanceled) return ValueResult<IReadOnlyList<UploadOutcome>>.FromCancellation();
            if (!adoptResult.TryGetSuccess(out var adopted))
            {
                adoptResult.TryGetFailure(out var aerr);
                return ValueResult<IReadOnlyList<UploadOutcome>>.FromError(aerr.Message);
            }

            // Load the plugin-owned dimension decoder once for the batch. If
            // the module import itself fails, surface that as a batch error
            // (rather than mis-attributing it to per-file decode failures).
            IJSObjectReference dimsModule;
            try { dimsModule = await _imageDimsModule.Value.WaitAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to load dndMapperImageDimensions.js.");
                // Roll back any adopted blobs we won't use.
                foreach (var item in adopted)
                {
                    if (item.Blob is null || item.Key is null) continue;
                    await SafeDeleteAsync(IndexedDbKey.String(item.Key.Value.ToString("D")));
                    await SafeDisposeAsync(item.Blob);
                }
                return ValueResult<IReadOnlyList<UploadOutcome>>.FromError("Image decoder unavailable.");
            }

            var outcomes = new List<UploadOutcome>(adopted.Count);
            foreach (var item in adopted)
            {
                if (item.Error is not null || item.Blob is null || item.Key is null)
                {
                    outcomes.Add(new UploadOutcome(item.Filename, null, item.Error ?? "adoption failed"));
                    continue;
                }

                int pxW, pxH;
                try
                {
                    var url = await item.Blob.CreateObjectUrlAsync(ct);
                    var dims = await dimsModule.InvokeAsync<ImageDimensionsDto>(
                        "decodeImageDimensionsFromUrl", ct, url);
                    pxW = dims.WidthPx;
                    pxH = dims.HeightPx;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, "Image dimension decode failed for {File}; rolling back.", item.Filename);
                    await SafeDeleteAsync(IndexedDbKey.String(item.Key.Value.ToString("D")));
                    await SafeDisposeAsync(item.Blob);
                    outcomes.Add(new UploadOutcome(item.Filename, null, "not a decodable image"));
                    continue;
                }

                var originalW = pxW > 0 ? pxW / (double)cellPixels : 0;
                var originalH = pxH > 0 ? pxH / (double)cellPixels : 0;

                var addResult = await AddAdoptedImageAsync(
                    state, host, mapId, item.Key.Value, item.Blob, originalW, originalH, ct);
                if (addResult.TryGetSuccess(out var image))
                    outcomes.Add(new UploadOutcome(item.Filename, image, null));
                else if (addResult.TryGetFailure(out var err))
                    outcomes.Add(new UploadOutcome(item.Filename, null, err.PublicMessage));
                else
                    outcomes.Add(new UploadOutcome(item.Filename, null, "engine registration failed"));
            }

            return ValueResult<IReadOnlyList<UploadOutcome>>.FromValue(outcomes);
        }

        // Mirror of the { widthPx, heightPx } shape returned by
        // dndMapperImageDimensions.js#decodeImageDimensionsFromUrl. Field
        // names use the JS casing because Blazor's default JSInterop JSON
        // options are camelCase-preserving.
        private sealed record ImageDimensionsDto(int WidthPx, int HeightPx);

        /// <summary>
        /// Registers an image that the host's browser has already persisted
        /// into the IndexedDB images store via
        /// <see cref="IIndexedDatabase.AdoptInputElementFilesAsync"/>. The
        /// bytes never crossed to the .NET side; this method publishes a
        /// share, builds the <see cref="MapImage"/> from
        /// <paramref name="adoptedBlob"/>'s metadata, and registers with the
        /// engine. On engine rejection the IDB row and the JS-side blob
        /// handle are rolled back so the slot is clean for retry.
        /// </summary>
        public async ValueTask<ValueResult<MapImage>> AddAdoptedImageAsync(
            DndMapperGameState state,
            User host,
            Guid mapId,
            Guid imageId,
            IndexedDbBlob adoptedBlob,
            double originalWidthCells,
            double originalHeightCells,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null) return ValueResult<MapImage>.FromError("Library is not attached. Call AttachAsync first.");
            if (state is null) return ValueResult<MapImage>.FromError("State is required.");
            if (host is null) return ValueResult<MapImage>.FromError("Host is required.");
            if (adoptedBlob is null) return ValueResult<MapImage>.FromError("Blob handle is required.");
            if (adoptedBlob.Length <= 0) return ValueResult<MapImage>.FromError("Adopted blob has non-positive length.");

            var key = IndexedDbKey.String(imageId.ToString("D"));

            IBlobShare share;
            try
            {
                share = await adoptedBlob.PublishForSharingAsync(options: null, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to publish blob share for image {ImageId}.", imageId);
                await SafeDeleteAsync(key);
                await SafeDisposeAsync(adoptedBlob);
                return ValueResult<MapImage>.FromError("Failed to publish image share.");
            }

            var image = new MapImage
            {
                Id = imageId,
                ContentType = adoptedBlob.ContentType,
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
                ByteSize = adoptedBlob.Length,
            };

            var addResult = _engine.AddImageAsync(state, host, mapId, image);
            if (!addResult.TryGetSuccess(out var added))
            {
                _logger.LogInformation("Engine rejected image {ImageId}; rolling back blob.", imageId);
                await SafeDisposeAsync(share);
                await SafeDeleteAsync(key);
                await SafeDisposeAsync(adoptedBlob);
                return addResult;
            }

            await ReplaceCacheEntryAsync(imageId, adoptedBlob, share);
            return ValueResult<MapImage>.FromValue(added);
        }

        /// <summary>
        /// Removes an image: forwards to the engine, then (on success) deletes
        /// the IndexedDB row and disposes the cached blob + share handles so
        /// JS-side resources don't leak for the rest of the circuit.
        /// </summary>
        public async ValueTask<Result> RemoveImageAsync(
            DndMapperGameState state,
            User host,
            Guid mapId,
            Guid imageId,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null) return Result.FromError("Library is not attached.");

            var engineResult = _engine.RemoveImageAsync(state, host, mapId, imageId);
            if (!engineResult.IsSuccess) return engineResult;

            await SafeDeleteAsync(IndexedDbKey.String(imageId.ToString("D")));

            if (_shareCache.Remove(imageId, out var share)) await SafeDisposeAsync(share);
            if (_blobCache.Remove(imageId, out var blob)) await SafeDisposeAsync(blob);

            return Result.Success;
        }

        /// <summary>
        /// Deletes a map: snapshots the map's image ids first, forwards to the
        /// engine, then (on success) deletes each image's IndexedDB row and
        /// disposes the cached blob + share handles. Disposing an
        /// <see cref="IBlobShare"/> revokes its token from
        /// <c>BlobShareRegistry</c>, which in turn evicts the byte cache —
        /// so the capability URLs for every image on the deleted map stop
        /// resolving immediately.
        /// </summary>
        /// <remarks>
        /// Map *swap* (<c>SetActiveMapAsync</c>) intentionally does not revoke
        /// anything: off-screen maps stay in <c>state.Maps</c> with their
        /// images, and the host can swap back at any time. Only map deletion
        /// (and explicit per-image removal) reaches this cleanup path.
        /// </remarks>
        public async ValueTask<Result> DeleteMapAsync(
            DndMapperGameState state,
            User host,
            Guid mapId,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null) return Result.FromError("Library is not attached.");

            // Snapshot the image ids BEFORE the engine mutates state — once
            // the verb runs, map.Images is gone.
            var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
            var imageIds = map is null
                ? Array.Empty<Guid>()
                : map.Images.Select(i => i.Id).ToArray();

            var engineResult = _engine.DeleteMapAsync(state, host, mapId);
            if (!engineResult.IsSuccess) return engineResult;

            foreach (var imageId in imageIds)
            {
                await SafeDeleteAsync(IndexedDbKey.String(imageId.ToString("D")));
                if (_shareCache.Remove(imageId, out var share)) await SafeDisposeAsync(share);
                if (_blobCache.Remove(imageId, out var blob)) await SafeDisposeAsync(blob);
            }

            return Result.Success;
        }

        // Replaces (or inserts) the cached blob + share for an image, disposing
        // any previous handles so we never silently overwrite a live JS resource.
        private async ValueTask ReplaceCacheEntryAsync(Guid imageId, IndexedDbBlob blob, IBlobShare share)
        {
            if (_shareCache.Remove(imageId, out var oldShare)) await SafeDisposeAsync(oldShare);
            if (_blobCache.Remove(imageId, out var oldBlob)) await SafeDisposeAsync(oldBlob);
            _blobCache[imageId] = blob;
            _shareCache[imageId] = share;
        }

        /// <summary>
        /// Returns a <c>blob:</c> URL pointing directly at the host's
        /// in-browser IndexedDB blob for <paramref name="imageId"/>, or
        /// <see langword="null"/> if this circuit isn't the owning host
        /// (cache miss) or the service is shutting down. <c>MapCanvas</c>
        /// uses this to render the host's own images without an HTTP round-
        /// trip to <c>/blob-share</c> — the bytes are already in this
        /// browser, so shipping them up through SignalR just to fetch them
        /// back via HTTP is the dial-up case we're killing here.
        /// <para>
        /// Non-throwing by design: the rendering caller treats <c>null</c>
        /// as "fall back to the share URL". The underlying
        /// <see cref="IndexedDbBlob.CreateObjectUrlAsync"/> caches the
        /// URL on the blob handle, so repeat calls are cheap.
        /// </para>
        /// </summary>
        public async ValueTask<string?> TryGetLocalObjectUrlAsync(Guid imageId, CancellationToken ct = default)
        {
            if (_disposed) return null;
            if (!_blobCache.TryGetValue(imageId, out var blob)) return null;
            try
            {
                return await blob.CreateObjectUrlAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to create local object URL for image {ImageId}; caller will fall back to /blob-share.", imageId);
                return null;
            }
        }

        /// <summary>
        /// Reads the persisted snapshot and replays it into the live state.
        /// Loads every map's image blobs from IndexedDB, publishes fresh
        /// blob-shares, and atomically swaps state.Maps / state.Sheets /
        /// state.Settings / state.AttributeSchema inside one Execute so
        /// subscribers see a single change. Tokens hydrate as NPCToken with
        /// no owner; the host reassigns via ReassignTokenOwnerAsync.
        /// </summary>
        public ValueTask<Result> HydrateAsync(CancellationToken ct = default)
            => LoadSlotAsync(DndMapperLibrarySchema.AutoSlotId, ct);

        public async ValueTask<Result> LoadSlotAsync(string slotId, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null || _state is null) return Result.FromError("Library is not attached.");
            if (string.IsNullOrWhiteSpace(slotId)) return Result.FromError("Slot id is required.");

            // Pause autosave for the duration of hydration so we don't write
            // a half-built snapshot back over the one we're loading.
            _saveTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            // 1. Try the v4 sharded layout first: read {slotId}:core, then
            //    fan out map and sheet shards from the spine. ConfigureAwait(false)
            //    on every await so the post-yield continuations (and the
            //    state.Execute lambda below) run on the thread pool, not on
            //    the Blazor circuit context that the caller's event handler
            //    captured.
            var (snapshot, _, shardHashes) = await ReadShardedSlotAsync(_db, slotId, ct).ConfigureAwait(false);

            // 2. v4 miss → fall back to the legacy v3 single-record at key
            //    `{slotId}`. If found, MigrateV3SlotIfNeededAsync rewrites it
            //    as shards and deletes the legacy record; we hydrate from the
            //    returned in-memory snapshot directly (no need to re-read).
            if (snapshot is null)
            {
                var legacy = await MigrateV3SlotIfNeededAsync(_db, slotId, ct).ConfigureAwait(false);
                if (legacy is not null) snapshot = legacy;
            }

            if (snapshot is null) return Result.Success; // nothing to hydrate

            // 2. Pre-load every image blob and publish a share outside the lock.
            //    Build a parallel structure mapping imageId -> fresh MapImage.
            var hydratedImages = await HydrateImagesFromSnapshotAsync(_db, snapshot, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return Result.FromCancellation();

            // 3. Pre-build the new state collections OFF the circuit thread.
            //    All the LINQ/projection work (maps + tokens + sheets + roll
            //    templates) happens here without the state lock; the Execute
            //    lambda then only does bulk swaps, dropping lock-hold time
            //    from 5-50 ms to sub-ms and freeing the circuit to paint
            //    during the rebuild.
            var hydration = await Task.Run(() => BuildHydration(snapshot, hydratedImages), ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return Result.FromCancellation();

            // 4. Apply atomically inside one Execute. Subscribers see one
            //    StateChanged notification covering the entire hydration.
            var state = _state;
            var execResult = state.Execute(() =>
            {
                state.SetSettings(snapshot.Settings.Clone());
                state.SetAttributeSchema(hydration.AttrSchema);

                state.Maps.Clear();
                state.Maps.AddRange(hydration.NewMaps);
                state.SetBytesUsed(hydration.TotalBytes);

                state.Sheets.Clear();
                foreach (var (id, sheet) in hydration.NewSheets)
                    state.Sheets[id] = sheet;

                state.GlobalRollTemplates.Clear();
                state.GlobalRollTemplates.AddRange(hydration.NewGlobalRollTemplates);

                // Re-seed built-ins before user templates so their Rows are
                // re-derived from the in-code preset definitions (which can
                // evolve across releases); the snapshot's persisted Rows are
                // ignored for built-ins.
                state.CustomTemplates.Clear();
                state.SeedBuiltInTemplates();
                foreach (var t in hydration.TemplateSnapshots)
                {
                    var existing = state.CustomTemplates.TryGetValue(t.Id, out var seeded) ? seeded : null;
                    if (existing is { IsBuiltIn: true })
                    {
                        // Built-in: keep seeded Rows, overlay persisted effects.
                        existing.StatusEffectTemplates.Clear();
                        foreach (var s in t.StatusEffectTemplates)
                            existing.StatusEffectTemplates.Add(LibrarySnapshotMapper.FromStatusEffectTemplateSnapshot(s));
                        // V3+ trusts the persisted value verbatim (including
                        // null/empty — the host can pick "— none (bare d20) —"
                        // and that choice survives reload). V1 snapshots
                        // predate the field entirely; the seeded default
                        // ("DEX" for presets containing DEX) wins.
                        if (snapshot.SchemaVersion >= 3)
                            existing.InitiativeAttributeName = t.InitiativeAttributeName;
                    }
                    else
                    {
                        state.CustomTemplates[t.Id] = new NamedTemplate
                        {
                            Id = t.Id,
                            Name = t.Name,
                            IsBuiltIn = false,
                            Rows = t.Rows
                                .Select(r => new AttributeRow(r.Name, r.Type, LibrarySnapshotMapper.ToAttributeValue(r.Default)))
                                .ToList(),
                            StatusEffectTemplates = t.StatusEffectTemplates
                                .Select(LibrarySnapshotMapper.FromStatusEffectTemplateSnapshot)
                                .ToList(),
                            InitiativeAttributeName = t.InitiativeAttributeName,
                        };
                    }
                }

                // Restore the active schema pointer. The lookup id was computed
                // off-circuit (preset → deterministic id); only the existence
                // check needs the post-Seed CustomTemplates dictionary.
                var resolvedActiveId = hydration.PreliminaryActiveSchemaId;
                if (resolvedActiveId is { } rid && !state.CustomTemplates.ContainsKey(rid))
                    resolvedActiveId = null;
                state.SetActiveSchemaTemplateId(resolvedActiveId);

                // Restore the state-level initiative attribute. Newer snapshots
                // carry it directly; for older ones we fall back to the active
                // template's value, then to "DEX" if the schema has it (legacy
                // d20 convention). Final guard validates against the actual
                // schema rows so a stale value can't survive a schema swap.
                string? restoredInitiative = snapshot.InitiativeAttributeName;
                if (string.IsNullOrEmpty(restoredInitiative) && resolvedActiveId is { } activeId
                    && state.CustomTemplates.TryGetValue(activeId, out var activeTemplate))
                {
                    restoredInitiative = activeTemplate.InitiativeAttributeName;
                }
                if (string.IsNullOrEmpty(restoredInitiative)
                    && state.AttributeSchema.Rows.Any(r => string.Equals(r.Name, "DEX", StringComparison.OrdinalIgnoreCase)))
                {
                    restoredInitiative = "DEX";
                }
                if (!string.IsNullOrEmpty(restoredInitiative)
                    && !state.AttributeSchema.Rows.Any(r => r.Name == restoredInitiative))
                {
                    restoredInitiative = null;
                }
                state.SetInitiativeAttributeName(restoredInitiative);

                // Activate the first map if any exist (NewMaps is already
                // ordered by ListOrder by BuildHydration).
                var firstId = hydration.NewMaps.Count > 0 ? hydration.NewMaps[0].Id : (Guid?)null;
                state.SetActiveMapId(firstId);
            });

            if (execResult.IsCanceled) return Result.FromCancellation();
            if (execResult.TryGetFailure(out var execErr)) return Result.FromError(execErr);

            HasExistingLibrary = true;

            // Seed the auto-flush hash cache from the just-loaded shard
            // bytes so the next FlushSnapshotAsync only writes deltas (the
            // user moves a token; we don't rewrite everything we just read).
            // Migration-path loads don't carry shard hashes — the next
            // flush will populate the cache lazily.
            if (slotId == DndMapperLibrarySchema.AutoSlotId)
            {
                _autoFlushHashes.Clear();
                foreach (var (k, h) in shardHashes) _autoFlushHashes[k] = h;
            }

            return Result.Success;
        }

        // Pulls every image blob referenced by the snapshot, publishes a fresh
        // share for each, and returns a parallel map of imageId -> hydrated
        // MapImage. Missing blobs and republish failures are logged and skipped
        // — callers see fewer images than the snapshot references, never an
        // exception.
        //
        // Each image's (BlobGet → PublishForSharing) round-trip pair runs in
        // parallel via Task.WhenAll; cache mutation (_blobCache / _shareCache)
        // is serialized afterward since those dictionaries aren't thread-safe.
        // For a 10-map / 5-image-per-map session this drops hydrate from ~50
        // sequential round-trips on the circuit thread to one parallel batch.
        private async ValueTask<Dictionary<Guid, MapImage>> HydrateImagesFromSnapshotAsync(
            IIndexedDatabase db,
            LibrarySnapshot snapshot,
            CancellationToken ct)
        {
            var imgSnaps = snapshot.Maps.SelectMany(m => m.Images).ToArray();
            if (imgSnaps.Length == 0) return new Dictionary<Guid, MapImage>();

            var fetchTasks = imgSnaps
                .Select(imgSnap => FetchAndPublishAsync(db, imgSnap, ct).AsTask())
                .ToArray();
            var results = await Task.WhenAll(fetchTasks).ConfigureAwait(false);

            var hydrated = new Dictionary<Guid, MapImage>(imgSnaps.Length);
            foreach (var result in results)
            {
                if (result.Blob is null || result.Share is null || result.Image is null) continue;
                await ReplaceCacheEntryAsync(result.ImageId, result.Blob, result.Share).ConfigureAwait(false);
                hydrated[result.ImageId] = result.Image;
            }
            return hydrated;
        }

        // One-image fetch + publish step. Returns a tuple so the caller can
        // dispatch the cache update serially against the (non-thread-safe)
        // _blobCache / _shareCache dictionaries.
        private readonly record struct ImageHydrationResult(
            Guid ImageId,
            IndexedDbBlob? Blob,
            IBlobShare? Share,
            MapImage? Image);

        private async ValueTask<ImageHydrationResult> FetchAndPublishAsync(
            IIndexedDatabase db,
            MapImageSnapshot imgSnap,
            CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return new ImageHydrationResult(imgSnap.Id, null, null, null);

            var blobResult = await db.BlobGetSingleAsync(
                DndMapperLibrarySchema.ImagesStore,
                IndexedDbKey.String(imgSnap.Id.ToString("D")),
                ct).ConfigureAwait(false);

            if (!blobResult.TryGetSuccess(out var blob) || blob is null)
            {
                _logger.LogWarning("Image {ImageId} referenced by snapshot is missing from IndexedDB; skipping.", imgSnap.Id);
                return new ImageHydrationResult(imgSnap.Id, null, null, null);
            }

            IBlobShare share;
            try { share = await blob.PublishForSharingAsync(options: null, ct).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to republish share for hydrated image {ImageId}.", imgSnap.Id);
                await SafeDisposeAsync(blob).ConfigureAwait(false);
                return new ImageHydrationResult(imgSnap.Id, null, null, null);
            }

            var image = new MapImage
            {
                Id = imgSnap.Id,
                Name = imgSnap.Name,
                ContentType = imgSnap.ContentType,
                ShareToken = share.Token,
                X = imgSnap.X,
                Y = imgSnap.Y,
                Width = imgSnap.Width,
                Height = imgSnap.Height,
                OriginalWidth = imgSnap.OriginalWidth,
                OriginalHeight = imgSnap.OriginalHeight,
                Rotation = imgSnap.Rotation,
                Opacity = imgSnap.Opacity,
                LayerOrder = imgSnap.LayerOrder,
                Locked = imgSnap.Locked,
                Hidden = imgSnap.Hidden,
                ByteSize = imgSnap.ByteSize,
            };
            return new ImageHydrationResult(imgSnap.Id, blob, share, image);
        }

        /// <summary>
        /// Deletes every record in the library database (snapshot + all image
        /// blobs) and clears in-memory handle caches. Used by the host UI's
        /// "Start fresh" affordance. The IndexedDB stays open with empty stores
        /// so subsequent auto-saves and uploads continue to work.
        /// </summary>
        /// <summary>
        /// "Start fresh" — drops ONLY the Auto Save record so the host begins
        /// with a clean board and the banner stops showing. Named slot
        /// snapshots live in the same <see cref="DndMapperLibrarySchema.LibraryStore"/>
        /// keyed by their own GUIDs and are intentionally left intact, as are
        /// their image blobs. The next auto-save tick will recreate the
        /// <c>__auto__</c> record from the (clean) current state.
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
                // Delete ONLY the auto-save shards from LibraryStore. Manual
                // slot shards (keyed by their slot GUID prefix in the same
                // store) and the ImagesStore are preserved — the user clicked
                // "Start fresh", not "Delete everything". Read the auto core
                // first to recover the spine; if it's missing fall back to
                // the in-memory hash cache so we still GC any shards the
                // current attachment knows about.
                var autoSlot = DndMapperLibrarySchema.AutoSlotId;
                var coreRead = await _db.JsonGetSingleAsync<LibraryCoreSnapshot>(
                    DndMapperLibrarySchema.LibraryStore, DndMapperLibrarySchema.CoreKey(autoSlot), ct);
                if (coreRead.TryGetSuccess(out var core) && core is not null)
                {
                    foreach (var mapId in core.MapIds)
                    {
                        await _db.DeleteSingleAsync(DndMapperLibrarySchema.LibraryStore,
                            DndMapperLibrarySchema.MapKey(autoSlot, mapId), ct);
                    }
                    foreach (var sheetId in core.SheetIds)
                    {
                        await _db.DeleteSingleAsync(DndMapperLibrarySchema.LibraryStore,
                            DndMapperLibrarySchema.SheetKey(autoSlot, sheetId), ct);
                    }
                }
                await _db.DeleteSingleAsync(DndMapperLibrarySchema.LibraryStore,
                    DndMapperLibrarySchema.CoreKey(autoSlot), ct);

                // Legacy v3 single-record fallback (covers the case where the
                // user "Start fresh"-es before any v4 shards were written).
                await _db.DeleteSingleAsync(DndMapperLibrarySchema.LibraryStore,
                    IndexedDbKey.String(autoSlot), ct);

                _autoFlushHashes.Clear();

                // Tear down the in-memory blob + share caches. The on-disk
                // ImagesStore is left intact (the user clicked "Start fresh",
                // not "Delete everything"), but the cached JS handles and
                // their registered share tokens point at content that's no
                // longer in the live state. Holding them would surface as:
                //   • A subsequent load creating a fresh handle for the same
                //     image and racing the dispose of the stale handle.
                //   • Stale share tokens lingering in BlobShareRegistry that
                //     resolve to disposed-by-the-next-load blobs.
                // Disposing here gives the next load a clean slate.
                foreach (var share in _shareCache.Values) await SafeDisposeAsync(share);
                _shareCache.Clear();
                foreach (var blob in _blobCache.Values) await SafeDisposeAsync(blob);
                _blobCache.Clear();

                // Drop the __auto__ entry from the slots index so the Saves
                // panel doesn't show a stale "Auto Save" row pointing at the
                // record we just deleted. The next auto-save flush re-adds it.
                var idx = await ReadSlotsIndexAsync(_db, ct);
                var removed = idx.Slots.RemoveAll(s => s.Id == DndMapperLibrarySchema.AutoSlotId) > 0;
                if (removed)
                {
                    var idxResult = await WriteSlotsIndexAsync(_db, idx, ct);
                    if (!idxResult.IsSuccess)
                    {
                        idxResult.TryGetFailure(out var ierr);
                        return Result.FromError($"Failed to update slots index: {ierr.PublicMessage}");
                    }
                }

                HasExistingLibrary = false;
                // Always notify — even if __auto__ wasn't in the index, a
                // panel that previously cached "Library is not attached" from
                // an Attach race still needs this nudge to re-refresh.
                NotifySlotsChanged();
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
            // the debounce timer cheaply on every event. _pendingDirty=1
            // signals to any concurrent flush that more changes arrived; on
            // that flush's release it re-arms us if the timer didn't already
            // fire from this Change() call.
            Interlocked.Exchange(ref _pendingDirty, 1);
            // Flip the indicator now — the user has unsaved work, even if the
            // write itself is still 500 ms away. FlushSnapshotAsync clears it
            // when the lock is released.
            SetSaving(true);
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

        // Test hook: runs the auto-save flush deterministically. Production
        // callers never use this — the debounce timer drives FlushSnapshotAsync.
        internal Task ForTestingFlushAsync() => FlushSnapshotAsync();

        private async Task FlushSnapshotAsync()
        {
            if (_disposed) return;
            var db = _db;
            var state = _state;
            var cts = _saveCts;
            if (db is null || state is null || cts is null || cts.IsCancellationRequested) return;

            if (!await _saveLock.WaitAsync(TimeSpan.Zero))
            {
                // Another flush is already running; _pendingDirty stays set so
                // the in-flight flush re-arms us on release.
                return;
            }
            try
            {
                if (_disposed || cts.IsCancellationRequested) return;

                // Take ownership of the dirty signal before we read state; any
                // StateChanged that fires while we're mid-write will set it
                // back to 1 and trigger a re-arm in the finally.
                Interlocked.Exchange(ref _pendingDirty, 0);

                // Build the shard set under the state's read lock so we don't
                // race with a concurrent Execute. The read action is pure-sync
                // (no I/O); IDB writes happen AFTER lock release.
                var shards = TakeSnapshot();
                if (shards is null)
                {
                    _logger.LogWarning("Auto-save read of game state failed; skipping flush.");
                    return;
                }

                var slotId = DndMapperLibrarySchema.AutoSlotId;
                var wroteAnything = false;

                // 1. Write new-or-changed map shards.
                foreach (var (mapId, mapShard) in shards.MapsById)
                {
                    if (cts.IsCancellationRequested) return;
                    var cacheKey = $"{slotId}:map:{mapId:D}";
                    var hash = HashShard(mapShard);
                    if (HashesEqual(_autoFlushHashes.GetValueOrDefault(cacheKey), hash)) continue;

                    var write = await db.JsonPutSingleAsync(
                        DndMapperLibrarySchema.LibraryStore,
                        mapShard,
                        DndMapperLibrarySchema.MapKey(slotId, mapId),
                        cts.Token);
                    if (!write.IsSuccess)
                    {
                        write.TryGetFailure(out var werr);
                        _logger.LogWarning("Auto-save: failed to write map shard {MapId}: {Error}", mapId, werr.Message);
                        return;
                    }
                    _autoFlushHashes[cacheKey] = hash;
                    wroteAnything = true;
                }

                // 2. Write new-or-changed sheet shards.
                foreach (var (sheetId, sheetShard) in shards.SheetsById)
                {
                    if (cts.IsCancellationRequested) return;
                    var cacheKey = $"{slotId}:sheet:{sheetId:D}";
                    var hash = HashShard(sheetShard);
                    if (HashesEqual(_autoFlushHashes.GetValueOrDefault(cacheKey), hash)) continue;

                    var write = await db.JsonPutSingleAsync(
                        DndMapperLibrarySchema.LibraryStore,
                        sheetShard,
                        DndMapperLibrarySchema.SheetKey(slotId, sheetId),
                        cts.Token);
                    if (!write.IsSuccess)
                    {
                        write.TryGetFailure(out var werr);
                        _logger.LogWarning("Auto-save: failed to write sheet shard {SheetId}: {Error}", sheetId, werr.Message);
                        return;
                    }
                    _autoFlushHashes[cacheKey] = hash;
                    wroteAnything = true;
                }

                // 3. Write core if changed. Core is the commit point — written
                //    AFTER all map / sheet shards so a crash between (1/2) and
                //    (3) leaves the old core spine still pointing at the prior
                //    state. Orphan new shards are silently ignored on load.
                var coreCacheKey = $"{slotId}:core";
                var coreHash = HashShard(shards.Core);
                if (!HashesEqual(_autoFlushHashes.GetValueOrDefault(coreCacheKey), coreHash))
                {
                    var coreWrite = await db.JsonPutSingleAsync(
                        DndMapperLibrarySchema.LibraryStore,
                        shards.Core,
                        DndMapperLibrarySchema.CoreKey(slotId),
                        cts.Token);
                    if (!coreWrite.IsSuccess)
                    {
                        coreWrite.TryGetFailure(out var werr);
                        _logger.LogWarning("Auto-save: failed to write core shard: {Error}", werr.Message);
                        return;
                    }
                    _autoFlushHashes[coreCacheKey] = coreHash;
                    wroteAnything = true;
                }

                // 4. Delete shards for entities that disappeared since the
                //    previous flush. Runs AFTER core write so the spine
                //    matches the on-disk layout if a crash occurs here.
                var staleKeys = new List<string>();
                foreach (var cached in _autoFlushHashes.Keys)
                {
                    if (cached == coreCacheKey) continue;
                    if (cached.StartsWith($"{slotId}:map:", StringComparison.Ordinal))
                    {
                        var idStr = cached[(slotId.Length + 5)..];
                        if (Guid.TryParseExact(idStr, "D", out var mapId)
                            && !shards.MapsById.ContainsKey(mapId))
                            staleKeys.Add(cached);
                    }
                    else if (cached.StartsWith($"{slotId}:sheet:", StringComparison.Ordinal))
                    {
                        var idStr = cached[(slotId.Length + 7)..];
                        if (Guid.TryParseExact(idStr, "D", out var sheetId)
                            && !shards.SheetsById.ContainsKey(sheetId))
                            staleKeys.Add(cached);
                    }
                }
                foreach (var stale in staleKeys)
                {
                    if (cts.IsCancellationRequested) return;
                    var del = await db.DeleteSingleAsync(
                        DndMapperLibrarySchema.LibraryStore, IndexedDbKey.String(stale), cts.Token);
                    if (!del.IsSuccess)
                    {
                        del.TryGetFailure(out var derr);
                        _logger.LogWarning("Auto-save: failed to delete stale shard {Key}: {Error}", stale, derr.Message);
                        // Don't abort — orphan records are harmless on load.
                    }
                    _autoFlushHashes.Remove(stale);
                }

                if (wroteAnything)
                {
                    HasExistingLibrary = true;
                    await TouchSlotEntryAsync(db,
                        DndMapperLibrarySchema.AutoSlotId,
                        DndMapperLibrarySchema.AutoSlotName,
                        SlotKind.Auto, cts.Token);
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

                // If a StateChanged fired during the flush (or a colliding
                // flush bailed without clearing the flag), re-arm so the
                // trailing edits get written; otherwise clear the indicator.
                if (!_disposed
                    && Volatile.Read(ref _pendingDirty) == 1
                    && _saveCts is not null
                    && !_saveCts.IsCancellationRequested)
                {
                    _saveTimer?.Change(SaveDebounce, Timeout.InfiniteTimeSpan);
                }
                else if (_manualOpsInFlight == 0)
                {
                    SetSaving(false);
                }
            }
        }

        /// <summary>
        /// Tears down the current attachment — cancels the auto-save timer,
        /// broadcasts placeholder share tokens to player circuits, drains the
        /// in-flight flush, releases cached blob/share handles, and closes the
        /// IndexedDB — but leaves the service in a state where a fresh
        /// <see cref="AttachAsync"/> can re-bind to a new
        /// <see cref="DndMapperGameState"/>.
        /// <para>
        /// Called from <c>DndMapperPlayingPhase.DisposeAsync</c> on page
        /// teardown. The DI scope (circuit) keeps this service alive — and
        /// reusable — beyond a single page mount; only <see cref="DisposeAsync"/>
        /// at scope teardown disposes the synchronization primitives. Idempotent:
        /// a second call against an already-detached service is a no-op.
        /// </para>
        /// </summary>
        public async ValueTask DetachAsync()
        {
            if (_disposed) return;
            if (_db is null) return;

            // 1. Stop the debounce timer and prevent the in-flight callback
            //    from queuing further work.
            try { _saveCts?.Cancel(); } catch { /* ignore */ }
            if (_saveTimer is not null)
            {
                _saveTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                await _saveTimer.DisposeAsync();
                _saveTimer = null;
            }

            // 2. Broadcast the disconnect to player circuits by nulling every
            //    image's ShareToken in state. Player UIs render placeholders
            //    immediately rather than waiting for the next 410 from the
            //    soon-to-be-evicted capability URL. The state lives on past
            //    this circuit (1-minute grace) so this mutation is durable
            //    until the host reconnects and republishes.
            //
            // Two benign-race outcomes get downgraded to Debug:
            //   • "State was disposed." — host left and ended the game; the
            //     state is already gone and there are no player circuits to
            //     broadcast to.
            //   • "State was disposed during execute." — the dispose ran
            //     concurrently with this mutation.
            // Anything else stays at Warning.
            if (_state is not null && _host is not null)
            {
                var clear = _engine.ClearAllImageShareTokensAsync(_state, _host);
                if (clear.TryGetFailure(out var cerr))
                {
                    if (IsStateDisposedRace(cerr.PublicMessage))
                        _logger.LogDebug("Skipped image share-token clear on detach: {Error}", cerr.PublicMessage);
                    else
                        _logger.LogWarning("Failed to clear image share tokens on detach: {Error}", cerr.PublicMessage);
                }
            }

            // 3. Drain any in-flight flush so the DB handle is safe to dispose.
            //    _saveLock is only released in the finally if we actually
            //    acquired it; on timeout we proceed with teardown anyway since
            //    the alternative is leaking the DB handle forever.
            bool locked;
            try { locked = await _saveLock.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { locked = false; }
            try
            {
                // 4. Drop the state subscription.
                _stateSub?.Dispose();
                _stateSub = null;

                // 5. Release cached blobs and shares (also revokes blob: URLs
                //    on the JS side; the SafeDispose wrapper swallows
                //    JSDisconnectedException if the circuit is already dead).
                foreach (var share in _shareCache.Values) await SafeDisposeAsync(share);
                _shareCache.Clear();
                foreach (var blob in _blobCache.Values) await SafeDisposeAsync(blob);
                _blobCache.Clear();

                // 6. Close the DB.
                if (_db is not null)
                {
                    await SafeDisposeAsync(_db);
                    _db = null;
                }

                // 7. Reset the attachment state so a fresh AttachAsync can
                //    re-bind. _saveLock survives (re-attach reuses it);
                //    _disposed is NOT set.
                _state = null;
                _host = null;
                HasExistingLibrary = false;
                Interlocked.Exchange(ref _pendingDirty, 0);
                _autoFlushHashes.Clear();
                _migratedSlots.Clear();
                _saveCts?.Dispose();
                _saveCts = null;
            }
            finally
            {
                if (locked) _saveLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            // Detach first so the active attachment (if any) is torn down with
            // share-token clearing and DB close. Detach is a no-op when the
            // service is already detached.
            await DetachAsync();
            _disposed = true;
            _saveLock.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DndMapperLibraryService));
        }

        // Matches the unified failure messages AbstractGameState returns when
        // an Execute lands on a disposed state (both already-disposed and
        // during-execute paths). Used by DetachAsync to keep the share-token
        // clear race off the Warning channel — the host leaving and the
        // game state disposing are the same event from the engine's view.
        private static bool IsStateDisposedRace(string message) =>
            message is "State was disposed." or "State was disposed during execute.";

        // ── Slot management ───────────────────────────────────────────────

        public async ValueTask<ValueResult<IReadOnlyList<SlotInfo>>> ListSlotsAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null) return ValueResult<IReadOnlyList<SlotInfo>>.FromError("Library is not attached.");

            var idx = await ReadSlotsIndexAsync(_db, ct).ConfigureAwait(false);
            // Auto Save pinned to the top, then manual slots by most-recent-first.
            var ordered = idx.Slots
                .OrderBy(s => s.Kind == SlotKind.Auto ? 0 : 1)
                .ThenByDescending(s => s.UpdatedUtc)
                .Select(s => new SlotInfo(s.Id, s.Name, s.Kind, s.UpdatedUtc))
                .ToList();
            return ValueResult<IReadOnlyList<SlotInfo>>.FromValue(ordered);
        }

        public async ValueTask<ValueResult<string>> CreateSlotAsync(string name, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null || _state is null) return ValueResult<string>.FromError("Library is not attached.");
            if (string.IsNullOrWhiteSpace(name)) return ValueResult<string>.FromError("Slot name cannot be empty.");
            var trimmed = name.Trim();
            if (trimmed.Length > 60) return ValueResult<string>.FromError("Slot name cannot exceed 60 characters.");

            Interlocked.Increment(ref _manualOpsInFlight);
            SetSaving(true);
            try
            {
                var idx = await ReadSlotsIndexAsync(_db, ct).ConfigureAwait(false);
                if (idx.Slots.Any(s => s.Kind == SlotKind.Manual
                        && string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    return ValueResult<string>.FromError("A slot with that name already exists.");
                }

                // Offload the snapshot read + LINQ projection to a thread-pool
                // thread. TakeSnapshot's internal state.WithExclusiveRead still
                // takes the lock synchronously, but on the pool thread instead
                // of the circuit, so Blazor can keep painting while the
                // snapshot is built.
                var shards = await Task.Run(TakeSnapshot, ct).ConfigureAwait(false);
                if (shards is null) return ValueResult<string>.FromError("Failed to read current state for save.");

                var slotId = Guid.NewGuid().ToString("D");
                var throwaway = new Dictionary<string, byte[]>();
                var write = await WriteAllShardsAsync(_db, slotId, shards, throwaway, ct).ConfigureAwait(false);
                if (!write.IsSuccess)
                {
                    write.TryGetFailure(out var werr);
                    return ValueResult<string>.FromError(werr.PublicMessage);
                }

                idx.Slots.Add(new SlotIndexEntry
                {
                    Id = slotId,
                    Name = trimmed,
                    Kind = SlotKind.Manual,
                    UpdatedUtc = DateTime.UtcNow,
                });
                var idxResult = await WriteSlotsIndexAsync(_db, idx, ct).ConfigureAwait(false);
                if (!idxResult.IsSuccess)
                {
                    idxResult.TryGetFailure(out var ierr);
                    return ValueResult<string>.FromError($"Failed to update slots index: {ierr.PublicMessage}");
                }

                NotifySlotsChanged();
                return ValueResult<string>.FromValue(slotId);
            }
            finally
            {
                if (Interlocked.Decrement(ref _manualOpsInFlight) == 0 && Volatile.Read(ref _pendingDirty) == 0)
                    SetSaving(false);
            }
        }

        public async ValueTask<Result> SaveToSlotAsync(string slotId, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null || _state is null) return Result.FromError("Library is not attached.");
            if (string.IsNullOrWhiteSpace(slotId)) return Result.FromError("Slot id is required.");
            if (slotId == DndMapperLibrarySchema.AutoSlotId)
                return Result.FromError("Auto Save is written automatically; choose a manual slot or Save As.");

            Interlocked.Increment(ref _manualOpsInFlight);
            SetSaving(true);
            try
            {
                var idx = await ReadSlotsIndexAsync(_db, ct).ConfigureAwait(false);
                var entry = idx.Slots.FirstOrDefault(s => s.Id == slotId);
                if (entry is null) return Result.FromError("Unknown slot id.");

                // See CreateSlotAsync for the rationale: snapshot work goes
                // to a pool thread so the host's "Overwrite" click doesn't
                // freeze the circuit while the LINQ projection runs.
                var shards = await Task.Run(TakeSnapshot, ct).ConfigureAwait(false);
                if (shards is null) return Result.FromError("Failed to read current state for save.");

                // Manual slots don't carry a per-slot hash cache: the user
                // clicked Overwrite, so just write every shard. Also clean
                // up any stale shards from a prior larger save under the
                // same slot id by reading the previous core's spine first.
                var previousCoreRead = await _db.JsonGetSingleAsync<LibraryCoreSnapshot>(
                    DndMapperLibrarySchema.LibraryStore,
                    DndMapperLibrarySchema.CoreKey(slotId), ct).ConfigureAwait(false);
                LibraryCoreSnapshot? previousCore = null;
                if (previousCoreRead.TryGetSuccess(out var pc)) previousCore = pc;

                var throwaway = new Dictionary<string, byte[]>();
                var write = await WriteAllShardsAsync(_db, slotId, shards, throwaway, ct).ConfigureAwait(false);
                if (!write.IsSuccess)
                {
                    write.TryGetFailure(out var werr);
                    return Result.FromError(werr.PublicMessage);
                }

                // Delete obsolete shards (entries from previousCore that are
                // not in the freshly-written shard set). Runs after the new
                // core is committed so the spine matches on-disk reality.
                if (previousCore is not null)
                {
                    foreach (var staleMapId in previousCore.MapIds.Where(id => !shards.MapsById.ContainsKey(id)))
                    {
                        await _db.DeleteSingleAsync(DndMapperLibrarySchema.LibraryStore,
                            DndMapperLibrarySchema.MapKey(slotId, staleMapId), ct).ConfigureAwait(false);
                    }
                    foreach (var staleSheetId in previousCore.SheetIds.Where(id => !shards.SheetsById.ContainsKey(id)))
                    {
                        await _db.DeleteSingleAsync(DndMapperLibrarySchema.LibraryStore,
                            DndMapperLibrarySchema.SheetKey(slotId, staleSheetId), ct).ConfigureAwait(false);
                    }
                }

                // Replace the entry with one carrying the new timestamp.
                idx.Slots.Remove(entry);
                idx.Slots.Add(entry with { UpdatedUtc = DateTime.UtcNow });
                var idxResult = await WriteSlotsIndexAsync(_db, idx, ct).ConfigureAwait(false);
                if (!idxResult.IsSuccess)
                {
                    idxResult.TryGetFailure(out var ierr);
                    return Result.FromError($"Failed to update slots index: {ierr.PublicMessage}");
                }

                NotifySlotsChanged();
                return Result.Success;
            }
            finally
            {
                if (Interlocked.Decrement(ref _manualOpsInFlight) == 0 && Volatile.Read(ref _pendingDirty) == 0)
                    SetSaving(false);
            }
        }

        public async ValueTask<Result> DeleteSlotAsync(string slotId, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null) return Result.FromError("Library is not attached.");
            if (string.IsNullOrWhiteSpace(slotId)) return Result.FromError("Slot id is required.");
            if (slotId == DndMapperLibrarySchema.AutoSlotId)
                return Result.FromError("The Auto Save slot cannot be deleted.");

            Interlocked.Increment(ref _manualOpsInFlight);
            SetSaving(true);
            try
            {
                var idx = await ReadSlotsIndexAsync(_db, ct).ConfigureAwait(false);
                var entry = idx.Slots.FirstOrDefault(s => s.Id == slotId);
                if (entry is null) return Result.FromError("Unknown slot id.");

                // Read core to recover the spine. If it's missing (the slot
                // was already orphaned), we just clear the slots-index entry;
                // any leftover shards are silently ignored on future reads.
                var coreRead = await _db.JsonGetSingleAsync<LibraryCoreSnapshot>(
                    DndMapperLibrarySchema.LibraryStore, DndMapperLibrarySchema.CoreKey(slotId), ct).ConfigureAwait(false);
                if (coreRead.TryGetSuccess(out var core) && core is not null)
                {
                    foreach (var mapId in core.MapIds)
                    {
                        await _db.DeleteSingleAsync(DndMapperLibrarySchema.LibraryStore,
                            DndMapperLibrarySchema.MapKey(slotId, mapId), ct).ConfigureAwait(false);
                    }
                    foreach (var sheetId in core.SheetIds)
                    {
                        await _db.DeleteSingleAsync(DndMapperLibrarySchema.LibraryStore,
                            DndMapperLibrarySchema.SheetKey(slotId, sheetId), ct).ConfigureAwait(false);
                    }
                    await _db.DeleteSingleAsync(DndMapperLibrarySchema.LibraryStore,
                        DndMapperLibrarySchema.CoreKey(slotId), ct).ConfigureAwait(false);
                }

                // Best-effort cleanup of any leftover v3 legacy record (e.g.,
                // the slot was deleted before it was migrated this attachment).
                await _db.DeleteSingleAsync(DndMapperLibrarySchema.LibraryStore, IndexedDbKey.String(slotId), ct).ConfigureAwait(false);

                idx.Slots.Remove(entry);
                var idxResult = await WriteSlotsIndexAsync(_db, idx, ct).ConfigureAwait(false);
                if (!idxResult.IsSuccess)
                {
                    idxResult.TryGetFailure(out var ierr);
                    return Result.FromError($"Failed to update slots index: {ierr.PublicMessage}");
                }
                NotifySlotsChanged();
                return Result.Success;
            }
            finally
            {
                if (Interlocked.Decrement(ref _manualOpsInFlight) == 0 && Volatile.Read(ref _pendingDirty) == 0)
                    SetSaving(false);
            }
        }

        public async ValueTask<Result> RenameSlotAsync(string slotId, string newName, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null) return Result.FromError("Library is not attached.");
            if (string.IsNullOrWhiteSpace(slotId)) return Result.FromError("Slot id is required.");
            if (slotId == DndMapperLibrarySchema.AutoSlotId)
                return Result.FromError("The Auto Save slot cannot be renamed.");
            if (string.IsNullOrWhiteSpace(newName)) return Result.FromError("Slot name cannot be empty.");
            var trimmed = newName.Trim();
            if (trimmed.Length > 60) return Result.FromError("Slot name cannot exceed 60 characters.");

            Interlocked.Increment(ref _manualOpsInFlight);
            SetSaving(true);
            try
            {
                var idx = await ReadSlotsIndexAsync(_db, ct).ConfigureAwait(false);
                var entry = idx.Slots.FirstOrDefault(s => s.Id == slotId);
                if (entry is null) return Result.FromError("Unknown slot id.");
                if (idx.Slots.Any(s => s.Id != slotId
                        && s.Kind == SlotKind.Manual
                        && string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                    return Result.FromError("A slot with that name already exists.");

                idx.Slots.Remove(entry);
                idx.Slots.Add(entry with { Name = trimmed });
                var idxResult = await WriteSlotsIndexAsync(_db, idx, ct).ConfigureAwait(false);
                if (!idxResult.IsSuccess)
                {
                    idxResult.TryGetFailure(out var ierr);
                    return Result.FromError($"Failed to update slots index: {ierr.PublicMessage}");
                }
                NotifySlotsChanged();
                return Result.Success;
            }
            finally
            {
                if (Interlocked.Decrement(ref _manualOpsInFlight) == 0 && Volatile.Read(ref _pendingDirty) == 0)
                    SetSaving(false);
            }
        }

        // ── .vtf import / export ──────────────────────────────────────────

        /// <summary>
        /// Packages a slot — its core spine, every map and sheet shard, and
        /// every referenced image blob — into a `.vtf` (Virtual Table Format)
        /// archive. The result is returned as a fresh <see cref="IndexedDbBlob"/>
        /// the caller owns and must dispose; trigger a download via
        /// <see cref="IndexedDbBlob.CreateObjectUrlAsync"/> + an &lt;a download&gt;
        /// click, then dispose to revoke. Reads from IndexedDB only; the live
        /// state is not touched.
        /// </summary>
        public async ValueTask<ValueResult<VtfExportResult>> ExportSlotAsync(
            string slotId, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null) return ValueResult<VtfExportResult>.FromError("Library is not attached.");
            if (string.IsNullOrWhiteSpace(slotId)) return ValueResult<VtfExportResult>.FromError("Slot id is required.");

            Interlocked.Increment(ref _manualOpsInFlight);
            SetSaving(true);
            try
            {
                var idx = await ReadSlotsIndexAsync(_db, ct);
                var entry = idx.Slots.FirstOrDefault(s => s.Id == slotId);
                if (entry is null) return ValueResult<VtfExportResult>.FromError("Unknown slot id.");

                var (snapshot, core, _) = await ReadShardedSlotAsync(_db, slotId, ct);
                if (snapshot is null || core is null)
                {
                    // Fall back to v3 single-record + migrate-on-the-fly.
                    var legacy = await MigrateV3SlotIfNeededAsync(_db, slotId, ct);
                    if (legacy is null)
                        return ValueResult<VtfExportResult>.FromError("Slot has no persisted content to export.");
                    snapshot = legacy;
                    core = new LibraryCoreSnapshot
                    {
                        SchemaVersion = 4,
                        Settings = legacy.Settings,
                        AttributeSchema = legacy.AttributeSchema,
                        ActiveSchemaTemplateId = legacy.ActiveSchemaTemplateId,
                        InitiativeAttributeName = legacy.InitiativeAttributeName,
                        CustomTemplates = legacy.CustomTemplates,
                        GlobalRollTemplates = legacy.GlobalRollTemplates,
                        MapIds = legacy.Maps.OrderBy(m => m.ListOrder).Select(m => m.Id).ToList(),
                        SheetIds = legacy.Sheets.Select(s => s.Id).ToList(),
                    };
                }

                var maps = snapshot.Maps;
                var sheets = snapshot.Sheets;

                // Pull every image referenced by the slot's maps. Each
                // BlobGet returns a live JS handle; we read the bytes into
                // a managed buffer and dispose the handle immediately so we
                // don't leak references across the (potentially long) Pack.
                var imageIds = maps.SelectMany(m => m.Images.Select(i => (i.Id, i.ContentType)))
                    .Distinct()
                    .ToList();
                var images = new Dictionary<Guid, VtfPackager.VtfImageAsset>(imageIds.Count);
                foreach (var (imageId, declaredType) in imageIds)
                {
                    if (ct.IsCancellationRequested) return ValueResult<VtfExportResult>.FromCancellation();
                    var blobResult = await _db.BlobGetSingleAsync(
                        DndMapperLibrarySchema.ImagesStore,
                        IndexedDbKey.String(imageId.ToString("D")),
                        ct);
                    if (!blobResult.TryGetSuccess(out var blob) || blob is null)
                    {
                        _logger.LogWarning("Export: image {ImageId} missing from IndexedDB; skipping.", imageId);
                        continue;
                    }
                    try
                    {
                        var bytes = await blob.ReadAllBytesAsync(ct);
                        var contentType = !string.IsNullOrWhiteSpace(blob.ContentType)
                            ? blob.ContentType
                            : (string.IsNullOrWhiteSpace(declaredType) ? "application/octet-stream" : declaredType);
                        images[imageId] = new VtfPackager.VtfImageAsset(contentType, bytes);
                    }
                    finally
                    {
                        await SafeDisposeAsync(blob);
                    }
                }

                var extension = new VtfPackager.VtfExtensionPayload(
                    ActiveCombat: null,
                    Phase: DndMapperPhase.Lobby);

                var packInput = new VtfPackager.PackInput(
                    SlotTitle: entry.Name,
                    Core: core,
                    Maps: maps,
                    Sheets: sheets,
                    Images: images,
                    Extension: extension);

                // Pack onto a thread-pool thread so the LINQ + JSON + Deflate
                // work doesn't freeze the circuit on big slots.
                var ms = new MemoryStream();
                try
                {
                    await Task.Run(() => VtfPackager.Pack(packInput, ms), ct);
                    ms.Position = 0;
                }
                catch
                {
                    await ms.DisposeAsync();
                    throw;
                }

                // Wrap as an IndexedDbBlob (allocates on the JS side); the
                // CreateBlobAsync impl disposes ms when leaveOpen is false.
                IndexedDbBlob blobResultBlob;
                try
                {
                    blobResultBlob = await _indexedDb.CreateBlobAsync(
                        ms, ms.Length, "application/zip", leaveOpen: false, ct);
                }
                catch
                {
                    await ms.DisposeAsync();
                    throw;
                }

                return ValueResult<VtfExportResult>.FromValue(
                    new VtfExportResult(entry.Name, blobResultBlob));
            }
            catch (OperationCanceledException) { return ValueResult<VtfExportResult>.FromCancellation(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DnD Mapper VTF export failed for slot {SlotId}.", slotId);
                return ValueResult<VtfExportResult>.FromError("Export failed.");
            }
            finally
            {
                if (Interlocked.Decrement(ref _manualOpsInFlight) == 0 && Volatile.Read(ref _pendingDirty) == 0)
                    SetSaving(false);
            }
        }

        /// <summary>
        /// Reads a `.vtf` archive from <paramref name="vtfBlob"/> and writes
        /// it as a new manual slot. Image GUIDs are minted fresh so importing
        /// the same archive twice produces two independent slots without
        /// aliasing image rows in the IndexedDB blob store. The live state is
        /// not touched — the user must explicitly Load the new slot to swap.
        /// </summary>
        public async ValueTask<ValueResult<string>> ImportSlotAsync(
            IndexedDbBlob vtfBlob, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null) return ValueResult<string>.FromError("Library is not attached.");
            if (vtfBlob is null) return ValueResult<string>.FromError("Archive is required.");

            Interlocked.Increment(ref _manualOpsInFlight);
            SetSaving(true);
            try
            {
                // Read the archive into a seekable buffer. ZipArchive.Read
                // requires seeking back to the central directory at the end
                // of the stream, which IndexedDbBlob.OpenReadAsync does not
                // support (forward-only async stream).
                byte[] archiveBytes;
                try { archiveBytes = await vtfBlob.ReadAllBytesAsync(ct); }
                catch (OperationCanceledException) { return ValueResult<string>.FromCancellation(); }

                VtfPackager.UnpackResult unpacked;
                try
                {
                    unpacked = await Task.Run(() =>
                    {
                        using var ms = new MemoryStream(archiveBytes, writable: false);
                        return VtfPackager.Unpack(ms);
                    }, ct);
                }
                catch (OperationCanceledException) { return ValueResult<string>.FromCancellation(); }
                catch (InvalidDataException ex)
                {
                    return ValueResult<string>.FromError(ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "DnD Mapper VTF import: archive could not be read.");
                    return ValueResult<string>.FromError("Archive is not a valid .vtf file.");
                }

                // Enforce engine caps so a malicious or oversized .vtf can't
                // immediately blow the room budget once Loaded.
                long totalBytes = 0;
                foreach (var asset in unpacked.Images.Values)
                {
                    if (asset.Bytes.LongLength > DndMapperGameEngine.PerFileCapBytes)
                        return ValueResult<string>.FromError(
                            $"Archive contains an image larger than {DndMapperGameEngine.PerFileCapBytes / (1024 * 1024)} MB.");
                    totalBytes += asset.Bytes.LongLength;
                }
                if (totalBytes > DndMapperGameEngine.PerRoomCapBytes)
                    return ValueResult<string>.FromError(
                        $"Archive image total exceeds the {DndMapperGameEngine.PerRoomCapBytes / (1024 * 1024 * 1024)} GB room cap.");

                // Mint fresh GUIDs for every imported image so the same .vtf
                // imported a second time doesn't overwrite the first import's
                // rows. Rewrite map shards' MapImageSnapshot.Id with the new
                // ids; ByteSize is preserved (used for the bytes-used readout).
                var idRemap = new Dictionary<Guid, Guid>(unpacked.Images.Count);
                var remappedImages = new Dictionary<Guid, VtfPackager.VtfImageAsset>(unpacked.Images.Count);
                foreach (var (oldId, asset) in unpacked.Images)
                {
                    var newId = Guid.NewGuid();
                    idRemap[oldId] = newId;
                    remappedImages[newId] = asset;
                }

                var remappedMaps = new List<MapSnapshot>(unpacked.Maps.Count);
                foreach (var map in unpacked.Maps)
                {
                    var imagesList = new List<MapImageSnapshot>(map.Images.Count);
                    foreach (var img in map.Images)
                    {
                        if (idRemap.TryGetValue(img.Id, out var newId))
                            imagesList.Add(img with { Id = newId });
                        // else: image binary was missing from the archive;
                        // drop the metadata row to avoid dangling refs.
                    }
                    remappedMaps.Add(map with { Images = imagesList });
                }

                // Write the image blobs to IndexedDB under their fresh GUIDs.
                foreach (var (newId, asset) in remappedImages)
                {
                    if (ct.IsCancellationRequested) return ValueResult<string>.FromCancellation();
                    IndexedDbBlob? created = null;
                    try
                    {
                        created = await _indexedDb.CreateBlobAsync(asset.Bytes, asset.ContentType, ct);
                        var put = await _db.BlobPutSingleAsync(
                            DndMapperLibrarySchema.ImagesStore,
                            created,
                            IndexedDbKey.String(newId.ToString("D")),
                            ct);
                        if (!put.IsSuccess)
                        {
                            put.TryGetFailure(out var perr);
                            return ValueResult<string>.FromError($"Failed to write image to library: {perr.Message}");
                        }
                    }
                    finally
                    {
                        if (created is not null) await SafeDisposeAsync(created);
                    }
                }

                // Reconcile the core spine: rewrite MapIds in maps' display
                // order (every map snapshot just had its image ids remapped;
                // the maps themselves keep their original ids).
                var core = unpacked.Core with
                {
                    MapIds = remappedMaps.Select(m => m.Id).ToList(),
                    SheetIds = unpacked.Sheets.Select(s => s.Id).ToList(),
                };

                var shards = new ShardSet(
                    core,
                    remappedMaps.ToDictionary(m => m.Id, m => m),
                    unpacked.Sheets.ToDictionary(s => s.Id, s => s));

                // Resolve a non-colliding slot name.
                var idx = await ReadSlotsIndexAsync(_db, ct);
                var desiredName = unpacked.SlotTitle;
                if (string.IsNullOrWhiteSpace(desiredName)) desiredName = "Imported slot";
                if (desiredName.Length > 60) desiredName = desiredName[..60];
                var finalName = desiredName;
                for (int n = 1; n < 1000; n++)
                {
                    if (!idx.Slots.Any(s => s.Kind == SlotKind.Manual
                            && string.Equals(s.Name, finalName, StringComparison.OrdinalIgnoreCase)))
                        break;
                    finalName = TruncateForSuffix(desiredName, n);
                }

                var slotId = Guid.NewGuid().ToString("D");
                var throwawayHashes = new Dictionary<string, byte[]>();
                var write = await WriteAllShardsAsync(_db, slotId, shards, throwawayHashes, ct);
                if (!write.IsSuccess)
                {
                    write.TryGetFailure(out var werr);
                    return ValueResult<string>.FromError(werr.PublicMessage);
                }

                idx.Slots.Add(new SlotIndexEntry
                {
                    Id = slotId,
                    Name = finalName,
                    Kind = SlotKind.Manual,
                    UpdatedUtc = DateTime.UtcNow,
                });
                var idxResult = await WriteSlotsIndexAsync(_db, idx, ct);
                if (!idxResult.IsSuccess)
                {
                    idxResult.TryGetFailure(out var ierr);
                    return ValueResult<string>.FromError($"Failed to update slots index: {ierr.PublicMessage}");
                }

                NotifySlotsChanged();
                return ValueResult<string>.FromValue(slotId);
            }
            catch (OperationCanceledException) { return ValueResult<string>.FromCancellation(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DnD Mapper VTF import failed.");
                return ValueResult<string>.FromError("Import failed.");
            }
            finally
            {
                if (Interlocked.Decrement(ref _manualOpsInFlight) == 0 && Volatile.Read(ref _pendingDirty) == 0)
                    SetSaving(false);
            }
        }

        /// <summary>
        /// Convenience wrapper that adopts a single <c>&lt;input type="file"&gt;</c>
        /// selection through the existing IndexedDB adoption pipeline (bytes
        /// never cross SignalR), then hands the resulting blob to
        /// <see cref="ImportSlotAsync"/>. Caller picks the file; this method
        /// owns the temp-row lifecycle.
        /// </summary>
        public async ValueTask<ValueResult<string>> ImportSlotFromInputElementAsync(
            ElementReference inputElement, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_db is null) return ValueResult<string>.FromError("Library is not attached.");

            var adoptResult = await _db.AdoptInputElementFilesAsync(
                inputElement,
                DndMapperLibrarySchema.ImagesStore,
                new AdoptInputFilesOptions(
                    AcceptedTypes: VtfArchiveAcceptedMimeTypes,
                    MaxBytes: VtfArchiveMaxBytes),
                ct);

            if (adoptResult.IsCanceled) return ValueResult<string>.FromCancellation();
            if (!adoptResult.TryGetSuccess(out var adopted))
            {
                adoptResult.TryGetFailure(out var aerr);
                return ValueResult<string>.FromError($"Could not read archive: {aerr.Message}");
            }

            var file = adopted.FirstOrDefault();
            if (file is null) return ValueResult<string>.FromError("No file was selected.");

            try
            {
                if (file.Error is not null || file.Blob is null || file.Key is null)
                    return ValueResult<string>.FromError(file.Error ?? "Archive rejected.");

                return await ImportSlotAsync(file.Blob, ct);
            }
            finally
            {
                if (file.Blob is not null) await SafeDisposeAsync(file.Blob);
                if (file.Key is { } key) await SafeDeleteAsync(IndexedDbKey.String(key.ToString("D")));
            }
        }

        // Caps and MIME allow-list for the .vtf archive itself. 2 GB is well
        // above the worst-case slot (1 GB image cap + JSON) but still fits in
        // a managed byte[] for the import path.
        private const long VtfArchiveMaxBytes = 2L * 1024 * 1024 * 1024;
        private static readonly IReadOnlyList<string> VtfArchiveAcceptedMimeTypes =
        [
            "application/zip",
            "application/x-zip-compressed",
            "application/octet-stream",
            "",
        ];

        /// <summary>
        /// Returned by <see cref="ExportSlotAsync"/>. The caller owns
        /// <see cref="Blob"/> and must dispose it after triggering the
        /// download (which revokes its object URL).
        /// </summary>
        public sealed record VtfExportResult(string SlotName, IndexedDbBlob Blob);

        private static string TruncateForSuffix(string baseName, int n)
        {
            var suffix = $" ({n})";
            if (baseName.Length + suffix.Length <= 60) return baseName + suffix;
            var head = baseName[..(60 - suffix.Length)];
            return head + suffix;
        }

        // Per-shard view of the current state used by every save path. The
        // dictionaries are keyed by entity id so FlushSnapshotAsync can diff
        // against the prior flush by id and SaveToSlotAsync can iterate in
        // core-declared order.
        private sealed record ShardSet(
            LibraryCoreSnapshot Core,
            Dictionary<Guid, MapSnapshot> MapsById,
            Dictionary<Guid, SheetSnapshot> SheetsById);

        // Pre-built artifacts of a hydrate pass assembled OUTSIDE the state
        // lock (and off the circuit thread). The Execute lambda in
        // LoadSlotAsync only does bulk swaps against these collections, so
        // lock-hold time stays sub-ms regardless of map / token / sheet count.
        //
        // TemplateSnapshots stay in raw v3-snapshot form because their final
        // shape depends on SeedBuiltInTemplates() — a state-mutating call
        // that must run inside Execute.
        private sealed record PreBuiltHydration(
            AttributeSchema AttrSchema,
            List<Map> NewMaps,
            Dictionary<Guid, CharacterSheet> NewSheets,
            List<RollTemplate> NewGlobalRollTemplates,
            List<NamedTemplateSnapshot> TemplateSnapshots,
            long TotalBytes,
            Guid? PreliminaryActiveSchemaId);

        private static PreBuiltHydration BuildHydration(
            LibrarySnapshot snapshot,
            Dictionary<Guid, MapImage> hydratedImages)
        {
            var attrSchema = LibrarySnapshotMapper.ToAttributeSchema(snapshot.AttributeSchema);

            var newMaps = new List<Map>(snapshot.Maps.Count);
            long totalBytes = 0;
            foreach (var mapSnap in snapshot.Maps.OrderBy(m => m.ListOrder))
            {
                var map = new Map
                {
                    Id = mapSnap.Id,
                    Name = mapSnap.Name,
                    ListOrder = mapSnap.ListOrder,
                    CreatedUtc = mapSnap.CreatedUtc,
                    Grid = mapSnap.Grid.Clone(),
                    DefaultSpawnPosition = mapSnap.DefaultSpawnX is double sx && mapSnap.DefaultSpawnY is double sy
                        ? (sx, sy)
                        : null,
                    // Legacy snapshots (pre-fog) deserialize FogMask to [],
                    // which Map.IsFogged treats as "all revealed".
                    FogMask = mapSnap.FogMask ?? [],
                };
                foreach (var imgSnap in mapSnap.Images.OrderBy(i => i.LayerOrder))
                {
                    if (hydratedImages.TryGetValue(imgSnap.Id, out var image))
                    {
                        map.Images.Add(image);
                        totalBytes += image.ByteSize;
                    }
                }
                foreach (var tokSnap in mapSnap.Tokens)
                {
                    // All persisted tokens hydrate as NPCs with no owner.
                    // The host promotes any of them to PlayerToken via
                    // ReassignTokenOwnerAsync after players join.
                    map.Tokens.Add(new Token
                    {
                        Id = tokSnap.Id,
                        Type = TokenType.NPCToken,
                        OwnerUserId = null,
                        RepresentsUserId = null,
                        Name = tokSnap.Name,
                        Color = tokSnap.Color,
                        IconKind = tokSnap.IconKind,
                        MapId = tokSnap.MapId,
                        X = tokSnap.X,
                        Y = tokSnap.Y,
                        SheetId = tokSnap.SheetId,
                        Hidden = tokSnap.Hidden,
                    });
                }
                newMaps.Add(map);
            }

            var newSheets = new Dictionary<Guid, CharacterSheet>(snapshot.Sheets.Count);
            foreach (var sheetSnap in snapshot.Sheets)
            {
                var sheet = new CharacterSheet
                {
                    Id = sheetSnap.Id,
                    OwnerUserId = null,
                    CharacterName = sheetSnap.CharacterName,
                    Notes = sheetSnap.Notes,
                    Hp = sheetSnap.Hp,
                    MaxHp = sheetSnap.MaxHp,
                };
                foreach (var kv in sheetSnap.Values)
                    sheet.Values[kv.Key] = LibrarySnapshotMapper.ToAttributeValue(kv.Value);
                foreach (var effectSnap in sheetSnap.StatusEffects)
                    sheet.StatusEffects.Add(LibrarySnapshotMapper.FromStatusEffectSnapshot(effectSnap));
                foreach (var rtSnap in sheetSnap.RollTemplates)
                    sheet.RollTemplates.Add(LibrarySnapshotMapper.FromRollTemplateSnapshot(rtSnap, RollTemplateScope.Sheet));
                newSheets[sheet.Id] = sheet;
            }

            var newGlobalRollTemplates = snapshot.GlobalRollTemplates
                .Select(rt => LibrarySnapshotMapper.FromRollTemplateSnapshot(rt, RollTemplateScope.Global))
                .ToList();

            // Default V1 snapshots have no id stored; fall back to the
            // deterministic id for the persisted preset so the library lands
            // on the right schema. The existence check against state.CustomTemplates
            // is deferred to inside Execute (it needs the post-Seed dict).
            var preliminaryActiveId = snapshot.ActiveSchemaTemplateId
                ?? DndMapperGameState.BuiltInTemplateIdFor(snapshot.AttributeSchema.Preset);

            return new PreBuiltHydration(
                attrSchema,
                newMaps,
                newSheets,
                newGlobalRollTemplates,
                snapshot.CustomTemplates,
                totalBytes,
                preliminaryActiveId);
        }

        private ShardSet? TakeSnapshot()
        {
            var state = _state;
            if (state is null) return null;
            ShardSet? shards = null;
            var read = state.WithExclusiveRead(() =>
            {
                var core = LibrarySnapshotMapper.ToCoreSnapshot(state);
                var maps = new Dictionary<Guid, MapSnapshot>(state.Maps.Count);
                foreach (var m in state.Maps) maps[m.Id] = LibrarySnapshotMapper.ToMapSnapshot(m);
                var sheets = new Dictionary<Guid, SheetSnapshot>(state.Sheets.Count);
                foreach (var kv in state.Sheets) sheets[kv.Key] = LibrarySnapshotMapper.ToSheetSnapshot(kv.Value);
                shards = new ShardSet(core, maps, sheets);
            });
            return read.IsSuccess ? shards : null;
        }

        private static byte[] HashShard<T>(T value) =>
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value));

        private static bool HashesEqual(byte[]? a, byte[]? b)
        {
            if (a is null || b is null) return false;
            if (a.Length != b.Length) return false;
            return a.AsSpan().SequenceEqual(b);
        }

        // Writes every shard in the set under the given slot id and refreshes
        // `hashCache` to match. Caller decides whether `hashCache` is the
        // auto-save cache or a throwaway dict. Map and sheet shards write
        // concurrently (capped at MaxShardWriteConcurrency); core is written
        // LAST as the commit point so a crash before core leaves the prior
        // spine intact. The whole batch runs inside Task.Run so the per-shard
        // JsonSerializer + SHA256 CPU happens off the Blazor circuit even
        // when the caller awaits from a Razor event handler.
        private async ValueTask<Result> WriteAllShardsAsync(
            IIndexedDatabase db,
            string slotId,
            ShardSet shards,
            Dictionary<string, byte[]> hashCache,
            CancellationToken ct)
        {
            return await Task.Run<Result>(async () =>
            {
                using var gate = new SemaphoreSlim(MaxShardWriteConcurrency, MaxShardWriteConcurrency);

                async Task<Result> WriteShardAsync<T>(string label, IndexedDbKey key, T shard, string cacheKey)
                {
                    await gate.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        var write = await db.JsonPutSingleAsync(
                            DndMapperLibrarySchema.LibraryStore, shard, key, ct).ConfigureAwait(false);
                        if (!write.IsSuccess)
                        {
                            write.TryGetFailure(out var werr);
                            return Result.FromError($"Failed to write {label} shard: {werr.Message}");
                        }
                        var hash = HashShard(shard);
                        lock (hashCache) { hashCache[cacheKey] = hash; }
                        return Result.Success;
                    }
                    finally
                    {
                        gate.Release();
                    }
                }

                var pending = new List<Task<Result>>(shards.MapsById.Count + shards.SheetsById.Count);
                foreach (var (mapId, mapShard) in shards.MapsById)
                {
                    pending.Add(WriteShardAsync(
                        "map",
                        DndMapperLibrarySchema.MapKey(slotId, mapId),
                        mapShard,
                        $"{slotId}:map:{mapId:D}"));
                }
                foreach (var (sheetId, sheetShard) in shards.SheetsById)
                {
                    pending.Add(WriteShardAsync(
                        "sheet",
                        DndMapperLibrarySchema.SheetKey(slotId, sheetId),
                        sheetShard,
                        $"{slotId}:sheet:{sheetId:D}"));
                }

                var results = await Task.WhenAll(pending).ConfigureAwait(false);
                foreach (var r in results)
                {
                    if (!r.IsSuccess) return r;
                }

                var coreWrite = await db.JsonPutSingleAsync(
                    DndMapperLibrarySchema.LibraryStore,
                    shards.Core,
                    DndMapperLibrarySchema.CoreKey(slotId),
                    ct).ConfigureAwait(false);
                if (!coreWrite.IsSuccess)
                {
                    coreWrite.TryGetFailure(out var werr);
                    return Result.FromError($"Failed to write core shard: {werr.Message}");
                }
                var coreHash = HashShard(shards.Core);
                lock (hashCache) { hashCache[$"{slotId}:core"] = coreHash; }

                return Result.Success;
            }, ct).ConfigureAwait(false);
        }

        // SignalR + IndexedDB pipeline tolerates several concurrent json puts
        // well, but past ~8 the per-call overhead stops amortizing and the
        // queue depth just grows. Keeps a 500-shard slot from opening 500
        // simultaneous JS interop calls.
        private const int MaxShardWriteConcurrency = 8;

        // Reads a slot back into a LibrarySnapshot shape by fanning out shard
        // reads from the core spine. Returns null if {slotId}:core is missing
        // (caller decides whether to fall back to v3 migration). Missing map
        // or sheet shards are logged and skipped — load proceeds with the
        // remaining entities rather than failing the whole hydrate.
        private async ValueTask<(LibrarySnapshot? Snapshot, LibraryCoreSnapshot? Core, List<(string Key, byte[] Hash)> ShardHashes)>
            ReadShardedSlotAsync(IIndexedDatabase db, string slotId, CancellationToken ct)
        {
            var coreKey = DndMapperLibrarySchema.CoreKey(slotId);
            var coreRead = await db.JsonGetSingleAsync<LibraryCoreSnapshot>(
                DndMapperLibrarySchema.LibraryStore, coreKey, ct).ConfigureAwait(false);

            if (!coreRead.TryGetSuccess(out var core) || core is null)
                return (null, null, []);

            var hashes = new List<(string, byte[])>
            {
                ($"{slotId}:core", HashShard(core)),
            };

            // Read all map shards in parallel — they're independent IDB
            // transactions and the JS side coalesces well under load.
            var mapTasks = core.MapIds
                .Select(id => db.JsonGetSingleAsync<MapSnapshot>(
                    DndMapperLibrarySchema.LibraryStore, DndMapperLibrarySchema.MapKey(slotId, id), ct))
                .ToArray();
            var mapResults = await Task.WhenAll(mapTasks.Select(t => t.AsTask())).ConfigureAwait(false);

            var maps = new List<MapSnapshot>(core.MapIds.Count);
            for (int i = 0; i < core.MapIds.Count; i++)
            {
                if (mapResults[i].TryGetSuccess(out var mapShard) && mapShard is not null)
                {
                    maps.Add(mapShard);
                    hashes.Add(($"{slotId}:map:{core.MapIds[i]:D}", HashShard(mapShard)));
                }
                else
                {
                    _logger.LogWarning("Map shard {MapId} listed by slot {SlotId} core is missing; skipping.",
                        core.MapIds[i], slotId);
                }
            }

            var sheetTasks = core.SheetIds
                .Select(id => db.JsonGetSingleAsync<SheetSnapshot>(
                    DndMapperLibrarySchema.LibraryStore, DndMapperLibrarySchema.SheetKey(slotId, id), ct))
                .ToArray();
            var sheetResults = await Task.WhenAll(sheetTasks.Select(t => t.AsTask())).ConfigureAwait(false);

            var sheets = new List<SheetSnapshot>(core.SheetIds.Count);
            for (int i = 0; i < core.SheetIds.Count; i++)
            {
                if (sheetResults[i].TryGetSuccess(out var sheetShard) && sheetShard is not null)
                {
                    sheets.Add(sheetShard);
                    hashes.Add(($"{slotId}:sheet:{core.SheetIds[i]:D}", HashShard(sheetShard)));
                }
                else
                {
                    _logger.LogWarning("Sheet shard {SheetId} listed by slot {SlotId} core is missing; skipping.",
                        core.SheetIds[i], slotId);
                }
            }

            // Reassemble the v3-shape LibrarySnapshot so the existing
            // hydration body in LoadSlotAsync can consume it unchanged.
            var snapshot = new LibrarySnapshot
            {
                SchemaVersion = core.SchemaVersion,
                Settings = core.Settings,
                AttributeSchema = core.AttributeSchema,
                ActiveSchemaTemplateId = core.ActiveSchemaTemplateId,
                InitiativeAttributeName = core.InitiativeAttributeName,
                Maps = maps,
                Sheets = sheets,
                CustomTemplates = core.CustomTemplates,
                GlobalRollTemplates = core.GlobalRollTemplates,
            };
            return (snapshot, core, hashes);
        }

        // Reads the legacy {slotId} record (v3 single-blob layout), rewrites
        // it as v4 shards, then deletes the legacy record. Returns the v3
        // snapshot if found so the caller can hydrate from it without a
        // second IDB round-trip. Idempotent within an attachment via
        // `_migratedSlots`.
        private async ValueTask<LibrarySnapshot?> MigrateV3SlotIfNeededAsync(
            IIndexedDatabase db, string slotId, CancellationToken ct)
        {
            if (!_migratedSlots.Add(slotId)) return null;

            var legacyKey = IndexedDbKey.String(slotId);
            var legacyRead = await db.JsonGetSingleAsync<LibrarySnapshot>(
                DndMapperLibrarySchema.LibraryStore, legacyKey, ct).ConfigureAwait(false);

            if (!legacyRead.TryGetSuccess(out var legacy) || legacy is null) return null;

            // Build shards directly from the legacy snapshot — no live state
            // round-trip needed. Maps and sheets are already in the v3 DTO
            // shape and identical to the v4 per-shard payload.
            var mapsById = legacy.Maps.ToDictionary(m => m.Id, m => m);
            var sheetsById = legacy.Sheets.ToDictionary(s => s.Id, s => s);
            var core = new LibraryCoreSnapshot
            {
                SchemaVersion = 4,
                Settings = legacy.Settings,
                AttributeSchema = legacy.AttributeSchema,
                ActiveSchemaTemplateId = legacy.ActiveSchemaTemplateId,
                InitiativeAttributeName = legacy.InitiativeAttributeName,
                CustomTemplates = legacy.CustomTemplates,
                GlobalRollTemplates = legacy.GlobalRollTemplates,
                MapIds = legacy.Maps.OrderBy(m => m.ListOrder).Select(m => m.Id).ToList(),
                SheetIds = legacy.Sheets.Select(s => s.Id).ToList(),
            };

            var migratedShards = new ShardSet(core, mapsById, sheetsById);
            // Populate the auto-flush hash cache during the migration write
            // only when migrating the Auto Save slot, so the immediately-
            // following flush correctly skips the unchanged shards. Manual
            // slots use a throwaway dict (we don't cache hashes for them).
            var hashTarget = slotId == DndMapperLibrarySchema.AutoSlotId
                ? _autoFlushHashes
                : new Dictionary<string, byte[]>();
            var write = await WriteAllShardsAsync(db, slotId, migratedShards, hashTarget, ct).ConfigureAwait(false);
            if (!write.IsSuccess)
            {
                write.TryGetFailure(out var werr);
                _logger.LogWarning("Failed to migrate v3 slot {SlotId} to sharded layout: {Error}",
                    slotId, werr.PublicMessage);
                return legacy; // hydrate from legacy this session; retry migration on next attachment
            }

            var del = await db.DeleteSingleAsync(DndMapperLibrarySchema.LibraryStore, legacyKey, ct).ConfigureAwait(false);
            if (!del.IsSuccess)
            {
                del.TryGetFailure(out var derr);
                _logger.LogWarning("Migrated v3 slot {SlotId} but failed to remove legacy record: {Error}",
                    slotId, derr.Message);
            }
            return legacy;
        }

        private async ValueTask<SlotsIndex> ReadSlotsIndexAsync(IIndexedDatabase db, CancellationToken ct)
        {
            var res = await db.JsonGetSingleAsync<SlotsIndex>(
                DndMapperLibrarySchema.SlotsIndexStore,
                IndexedDbKey.String(DndMapperLibrarySchema.SlotsIndexKey), ct).ConfigureAwait(false);
            if (res.TryGetSuccess(out var idx) && idx is not null) return idx;
            return new SlotsIndex();
        }

        private async ValueTask<Result> WriteSlotsIndexAsync(IIndexedDatabase db, SlotsIndex idx, CancellationToken ct)
        {
            var res = await db.JsonPutSingleAsync(
                DndMapperLibrarySchema.SlotsIndexStore, idx,
                IndexedDbKey.String(DndMapperLibrarySchema.SlotsIndexKey), ct).ConfigureAwait(false);
            if (res.IsSuccess) return Result.Success;
            res.TryGetFailure(out var err);
            return Result.FromError(err.Message);
        }

        private async ValueTask TouchSlotEntryAsync(IIndexedDatabase db, string slotId, string name, SlotKind kind, CancellationToken ct)
        {
            var idx = await ReadSlotsIndexAsync(db, ct).ConfigureAwait(false);
            var existing = idx.Slots.FirstOrDefault(s => s.Id == slotId);
            if (existing is not null) idx.Slots.Remove(existing);
            idx.Slots.Add(new SlotIndexEntry
            {
                Id = slotId,
                Name = name,
                Kind = kind,
                UpdatedUtc = DateTime.UtcNow,
            });
            var write = await WriteSlotsIndexAsync(db, idx, ct).ConfigureAwait(false);
            if (!write.IsSuccess)
            {
                write.TryGetFailure(out var werr);
                _logger.LogWarning("Failed to touch slot index for {SlotId}: {Error}", slotId, werr.PublicMessage);
            }
            NotifySlotsChanged();
        }

        private async ValueTask SafeDeleteAsync(IndexedDbKey key)
        {
            if (_db is null) return;
            try
            {
                await _db.DeleteSingleAsync(DndMapperLibrarySchema.ImagesStore, key);
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
