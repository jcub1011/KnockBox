using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.DndMapper.Models;
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

            foreach (var (mapId, imageId) in imageIds)
            {
                if (ct.IsCancellationRequested) return;
                if (_shareCache.ContainsKey(imageId)) continue; // already wired

                var blobResult = await _db.BlobGetSingleAsync(
                    DndMapperLibrarySchema.ImagesStore,
                    IndexedDbKey.String(imageId.ToString("D")),
                    ct);

                if (!blobResult.TryGetSuccess(out var blob) || blob is null)
                {
                    _logger.LogWarning("Reconnect: image {ImageId} no longer in IndexedDB; leaving placeholder.", imageId);
                    continue;
                }

                IBlobShare share;
                try { share = await blob.PublishForSharingAsync(options: null, ct); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Reconnect: failed to republish image {ImageId}.", imageId);
                    await SafeDisposeAsync(blob);
                    continue;
                }

                await ReplaceCacheEntryAsync(imageId, blob, share);

                var update = _engine.UpdateImageShareTokenAsync(state, host, mapId, imageId, share.Token);
                if (update.TryGetFailure(out var uerr))
                {
                    _logger.LogWarning("Reconnect: engine rejected new share token for image {ImageId}: {Error}", imageId, uerr.PublicMessage);
                }
            }
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
            var putResult = await _db.BlobPutSingleAsync(DndMapperLibrarySchema.ImagesStore, blob, key, ct);
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

            await ReplaceCacheEntryAsync(imageId, blob, share);
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

            // 1. Read snapshot from the requested slot.
            var snapKey = IndexedDbKey.String(slotId);
            var readResult = await _db.JsonGetSingleAsync<LibrarySnapshot>(
                DndMapperLibrarySchema.LibraryStore, snapKey, ct);

            if (!readResult.TryGetSuccess(out var snapshot))
            {
                readResult.TryGetFailure(out var rerr);
                return Result.FromError($"Failed to read library snapshot: {rerr.Message}");
            }
            if (snapshot is null) return Result.Success; // nothing to hydrate

            // 2. Pre-load every image blob and publish a share outside the lock.
            //    Build a parallel structure mapping imageId -> fresh MapImage.
            var hydratedImages = await HydrateImagesFromSnapshotAsync(_db, snapshot, ct);
            if (ct.IsCancellationRequested) return Result.FromCancellation();

            // 3. Apply atomically inside one Execute. Subscribers see one
            //    StateChanged notification covering the entire hydration.
            var state = _state;
            var attrSchema = LibrarySnapshotMapper.ToAttributeSchema(snapshot.AttributeSchema);
            var execResult = state.Execute(() =>
            {
                state.SetSettings(snapshot.Settings.Clone());
                state.SetAttributeSchema(attrSchema);

                state.Maps.Clear();
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
                    state.Maps.Add(map);
                }
                state.SetBytesUsed(totalBytes);

                state.Sheets.Clear();
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
                    state.Sheets[sheet.Id] = sheet;
                }

                state.GlobalRollTemplates.Clear();
                foreach (var rtSnap in snapshot.GlobalRollTemplates)
                    state.GlobalRollTemplates.Add(LibrarySnapshotMapper.FromRollTemplateSnapshot(rtSnap, RollTemplateScope.Global));

                state.CustomTemplates.Clear();
                // Re-seed built-ins before user templates so their Rows are
                // re-derived from the in-code preset definitions (which can
                // evolve across releases); the snapshot's persisted Rows are
                // ignored for built-ins.
                state.SeedBuiltInTemplates();
                foreach (var t in snapshot.CustomTemplates)
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

                // Restore the active schema pointer. Default V1 snapshots have
                // no id stored; fall back to the deterministic id for the
                // persisted preset so the library lands on the right schema.
                var resolvedActiveId = snapshot.ActiveSchemaTemplateId
                    ?? DndMapperGameState.BuiltInTemplateIdFor(snapshot.AttributeSchema.Preset);
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

                // Activate the first map if any exist so the host doesn't
                // land on an empty canvas after hydration.
                var first = state.Maps.OrderBy(m => m.ListOrder).FirstOrDefault();
                state.SetActiveMapId(first?.Id);
            });

            if (execResult.IsCanceled) return Result.FromCancellation();
            if (execResult.TryGetFailure(out var execErr)) return Result.FromError(execErr);

            HasExistingLibrary = true;
            return Result.Success;
        }

        // Pulls every image blob referenced by the snapshot, publishes a fresh
        // share for each, and returns a parallel map of imageId -> hydrated
        // MapImage. Missing blobs and republish failures are logged and skipped
        // — callers see fewer images than the snapshot references, never an
        // exception. The cache is updated in lock-step so the new share is
        // owned by this service for the rest of the circuit.
        private async ValueTask<Dictionary<Guid, MapImage>> HydrateImagesFromSnapshotAsync(
            IIndexedDatabase db,
            LibrarySnapshot snapshot,
            CancellationToken ct)
        {
            var hydrated = new Dictionary<Guid, MapImage>();
            foreach (var mapSnap in snapshot.Maps)
            {
                foreach (var imgSnap in mapSnap.Images)
                {
                    if (ct.IsCancellationRequested) return hydrated;

                    var blobResult = await db.BlobGetSingleAsync(
                        DndMapperLibrarySchema.ImagesStore,
                        IndexedDbKey.String(imgSnap.Id.ToString("D")),
                        ct);

                    if (!blobResult.TryGetSuccess(out var blob) || blob is null)
                    {
                        _logger.LogWarning("Image {ImageId} referenced by snapshot is missing from IndexedDB; skipping.", imgSnap.Id);
                        continue;
                    }

                    IBlobShare share;
                    try { share = await blob.PublishForSharingAsync(options: null, ct); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to republish share for hydrated image {ImageId}.", imgSnap.Id);
                        await SafeDisposeAsync(blob);
                        continue;
                    }

                    await ReplaceCacheEntryAsync(imgSnap.Id, blob, share);
                    hydrated[imgSnap.Id] = new MapImage
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
                }
            }
            return hydrated;
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
                // Delete ONLY the auto-save record from LibraryStore. Manual
                // slot snapshots (keyed by their slot GUID in the same store)
                // and the ImagesStore are preserved — the user clicked
                // "Start fresh", not "Delete everything".
                var autoKey = IndexedDbKey.String(DndMapperLibrarySchema.AutoSlotId);
                var delResult = await _db.DeleteSingleAsync(
                    DndMapperLibrarySchema.LibraryStore, autoKey, ct);
                if (!delResult.IsSuccess)
                {
                    delResult.TryGetFailure(out var derr);
                    return Result.FromError($"Failed to clear auto-save: {derr.Message}");
                }

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

                var key = IndexedDbKey.String(DndMapperLibrarySchema.AutoSlotId);
                var writeResult = await db.JsonPutSingleAsync(
                    DndMapperLibrarySchema.LibraryStore, snapshot, key, cts.Token);

                if (!writeResult.IsSuccess)
                {
                    writeResult.TryGetFailure(out var werr);
                    _logger.LogWarning("DnD Mapper auto-save write failed: {Error}", werr.Message);
                }
                else
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

            var idx = await ReadSlotsIndexAsync(_db, ct);
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
                var idx = await ReadSlotsIndexAsync(_db, ct);
                if (idx.Slots.Any(s => s.Kind == SlotKind.Manual
                        && string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    return ValueResult<string>.FromError("A slot with that name already exists.");
                }

                var snapshot = TakeSnapshot();
                if (snapshot is null) return ValueResult<string>.FromError("Failed to read current state for save.");

                var slotId = Guid.NewGuid().ToString("D");
                var key = IndexedDbKey.String(slotId);
                var write = await _db.JsonPutSingleAsync(DndMapperLibrarySchema.LibraryStore, snapshot, key, ct);
                if (!write.IsSuccess)
                {
                    write.TryGetFailure(out var werr);
                    return ValueResult<string>.FromError($"Failed to write slot: {werr.Message}");
                }

                idx.Slots.Add(new SlotIndexEntry
                {
                    Id = slotId,
                    Name = trimmed,
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
                var idx = await ReadSlotsIndexAsync(_db, ct);
                var entry = idx.Slots.FirstOrDefault(s => s.Id == slotId);
                if (entry is null) return Result.FromError("Unknown slot id.");

                var snapshot = TakeSnapshot();
                if (snapshot is null) return Result.FromError("Failed to read current state for save.");

                var key = IndexedDbKey.String(slotId);
                var write = await _db.JsonPutSingleAsync(DndMapperLibrarySchema.LibraryStore, snapshot, key, ct);
                if (!write.IsSuccess)
                {
                    write.TryGetFailure(out var werr);
                    return Result.FromError($"Failed to write slot: {werr.Message}");
                }

                // Replace the entry with one carrying the new timestamp.
                idx.Slots.Remove(entry);
                idx.Slots.Add(entry with { UpdatedUtc = DateTime.UtcNow });
                var idxResult = await WriteSlotsIndexAsync(_db, idx, ct);
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
                var idx = await ReadSlotsIndexAsync(_db, ct);
                var entry = idx.Slots.FirstOrDefault(s => s.Id == slotId);
                if (entry is null) return Result.FromError("Unknown slot id.");

                var del = await _db.DeleteSingleAsync(DndMapperLibrarySchema.LibraryStore, IndexedDbKey.String(slotId), ct);
                if (!del.IsSuccess)
                {
                    del.TryGetFailure(out var derr);
                    return Result.FromError($"Failed to delete slot: {derr.Message}");
                }

                idx.Slots.Remove(entry);
                var idxResult = await WriteSlotsIndexAsync(_db, idx, ct);
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
                var idx = await ReadSlotsIndexAsync(_db, ct);
                var entry = idx.Slots.FirstOrDefault(s => s.Id == slotId);
                if (entry is null) return Result.FromError("Unknown slot id.");
                if (idx.Slots.Any(s => s.Id != slotId
                        && s.Kind == SlotKind.Manual
                        && string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                    return Result.FromError("A slot with that name already exists.");

                idx.Slots.Remove(entry);
                idx.Slots.Add(entry with { Name = trimmed });
                var idxResult = await WriteSlotsIndexAsync(_db, idx, ct);
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

        private LibrarySnapshot? TakeSnapshot()
        {
            var state = _state;
            if (state is null) return null;
            LibrarySnapshot? snapshot = null;
            var read = state.WithExclusiveRead(() => { snapshot = LibrarySnapshotMapper.FromState(state); });
            return read.IsSuccess ? snapshot : null;
        }

        private async ValueTask<SlotsIndex> ReadSlotsIndexAsync(IIndexedDatabase db, CancellationToken ct)
        {
            var res = await db.JsonGetSingleAsync<SlotsIndex>(
                DndMapperLibrarySchema.SlotsIndexStore,
                IndexedDbKey.String(DndMapperLibrarySchema.SlotsIndexKey), ct);
            if (res.TryGetSuccess(out var idx) && idx is not null) return idx;
            return new SlotsIndex();
        }

        private async ValueTask<Result> WriteSlotsIndexAsync(IIndexedDatabase db, SlotsIndex idx, CancellationToken ct)
        {
            var res = await db.JsonPutSingleAsync(
                DndMapperLibrarySchema.SlotsIndexStore, idx,
                IndexedDbKey.String(DndMapperLibrarySchema.SlotsIndexKey), ct);
            if (res.IsSuccess) return Result.Success;
            res.TryGetFailure(out var err);
            return Result.FromError(err.Message);
        }

        private async ValueTask TouchSlotEntryAsync(IIndexedDatabase db, string slotId, string name, SlotKind kind, CancellationToken ct)
        {
            var idx = await ReadSlotsIndexAsync(db, ct);
            var existing = idx.Slots.FirstOrDefault(s => s.Id == slotId);
            if (existing is not null) idx.Slots.Remove(existing);
            idx.Slots.Add(new SlotIndexEntry
            {
                Id = slotId,
                Name = name,
                Kind = kind,
                UpdatedUtc = DateTime.UtcNow,
            });
            var write = await WriteSlotsIndexAsync(db, idx, ct);
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
