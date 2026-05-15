# M03 — Image Management

> **Goal**: implement DnD Mapper's image upload (in-process from the host's circuit), serve (anonymous HTTP), and lifecycle. Adds the four image engine verbs plus a `SaveImageAsync` upload verb, opts the plugin into the M02 HTTP dispatcher (for image GET only), enforces size/MIME caps, and ensures every uploaded file is deleted on the right cleanup triggers.
>
> **Dependencies**: M01 (state model + `MapImage` record + `DeleteMapAsync` + `EndSessionAsync`) and M02 (`IGameEngineHttpHandler` contract + dispatcher) must be merged before starting M03.
>
> **GDD references**: §5 (Map Images — entire section), §12 (image engine verbs in the verb list), §17 verification steps that involve image upload / cleanup.
>
> **Out of scope** (do NOT implement here): image upload UI / `<InputFile>` host control (M05). Image transform handles UI (M05). Layer reorder UI (M05). Display view image rendering (v1.x).
>
> **Auth model recap** (per M02): the HTTP dispatcher is anonymous-by-design. The `IGameEngineHttpHandler` signature does **not** carry a `User`. Image *upload* therefore does not run through HTTP — it runs in-process from the host's Blazor circuit (which has the user identity from `IUserService`) via a new engine method `SaveImageAsync`. Image *serve* (GET) runs through the dispatcher and is anonymous; the obfuscated room URI is the access token per GDD §5.4.

---

## 1. Context

M03 is the first plugin to opt into M02's `IGameEngineHttpHandler` contract. It exposes one HTTP endpoint — `GET images/{imageId}` (anonymous, streams via `IPluginStorage`) — and adds the four image engine verbs that mutate `Map.Images` plus an upload verb `SaveImageAsync` that the host's Blazor circuit calls directly (no HTTP). M03 also extends M01's `DeleteMapAsync` and `EndSessionAsync` to delete files from disk via `IPluginStorage`.

The platform invariant from `host/KnockBox/Specs/knockbox-platform-architecture.md` requires plugins to write files only through `IPluginStorage` (which rejects absolute paths and `..` traversal). The KB1001 analyzer enforces this at compile time. `IPluginStorage` is gated behind the `Storage` capability, which M03 adds to `plugin.json`. Plugins access `IPluginStorage` only via `IPluginContext.Storage` — `IPluginStorage` is in `DefaultPluginRegistration.AlwaysProtectedTypes` and is **not** resolvable as a regular DI service. The engine ctor accepts `IPluginContext` and stores `_storage = context.Storage` at construction.

The file-cleanup triggers from §5.5 are:
1. **Per-image removal** — `RemoveImageAsync` deletes one file.
2. **Map deletion** — `DeleteMapAsync` cascades to every image on the map.
3. **Session end** — `Dispose` enumerates the per-room storage prefix and deletes all remaining files.

All three must be implemented in M03. The per-room running byte total (used for the 10 MB cap) lives on `DndMapperGameState.BytesUsed` (added in M03).

---

## 2. Files to create / modify

### New files

```
host/KnockBox.DndMapper/Services/Logic/Games/Http/DndMapperHttpHandler.cs       ; could fold into engine; see §3.4
host/KnockBox.DndMapper/Services/Logic/Games/Http/MimeSniffer.cs                ; small helper
host/KnockBox.DndMapperTests/Unit/Logic/Games/ImageVerbsTests.cs
host/KnockBox.DndMapperTests/Unit/Logic/Games/Http/ImageHttpHandlerTests.cs
host/KnockBox.DndMapperTests/Helpers/InMemoryPluginStorage.cs
```

### Files to modify

- `host/KnockBox.DndMapper/plugin.json` — change `"capabilities": []` to `"capabilities": ["Storage"]`.
- `host/KnockBox.DndMapper/Services/Logic/Games/DndMapperGameEngine.cs` — implement `IGameEngineHttpHandler`; inject `IPluginContext` (and read `context.Storage`); add the four image verbs **plus the `SaveImageAsync` upload verb**; extend `DeleteMapAsync` and `EndSessionAsync` to call `IPluginStorage.Delete` on cascade.
- `host/KnockBox.DndMapper/Services/State/Games/DndMapperGameState.cs` — add `BytesUsed : long { get; private set; }` and `internal void SetBytesUsed(long value)`. (Mutated only inside `Execute` from engine verbs.) Add `SessionId : Guid` (set in ctor — used as storage prefix and cleanup key).
- `host/KnockBox.DndMapper/DndMapperModule.cs` — confirm `AddGameEngine<DndMapperGameEngine>()` still wires correctly with the new ctor signature. The platform's `Create<T>(sp)` (`sdk/KnockBox.Platform/Plugins/DefaultPluginRegistration.cs:218-220`) detects when an engine ctor takes `IPluginContext` and injects the keyed-by-route context via `ResolveContext(sp)`. No `DndMapperModule` change required.

### Files NOT touched in M03

- `host/KnockBox.DndMapper/Pages/*` — image upload UI lands in M05.
- `KnockBox.csproj`, `Program.cs` — neither needs changes.

---

## 3. Detailed work breakdown

### 3.1 `DndMapperGameState` extension

```csharp
public long BytesUsed { get; private set; }
internal void SetBytesUsed(long value) => BytesUsed = value;

internal void AdjustBytesUsed(long delta) => BytesUsed = Math.Max(0, BytesUsed + delta);
```

Used by `AddImageAsync` (`+= ByteSize`), `RemoveImageAsync` (`-= ByteSize`), `DeleteMapAsync` cascade, and the session-end cleanup.

### 3.2 Engine verbs

All four verbs are host-only. Each runs inside `state.Execute`. Each returns `Result` or `ValueResult<T>`. None of these verbs perform file I/O — file I/O happens in `RemoveImageAsync` (which calls `IPluginStorage.Delete`), in the cascading `DeleteMapAsync`, and in the HTTP handler's POST path BEFORE calling `AddImageAsync`.

#### `AddImageAsync(state, host, mapId, MapImage image) → ValueResult<MapImage>`

- Validate caller is host.
- Validate `state.Maps` contains a map with `Id == mapId` (else error).
- Inside `state.Execute`:
  - Set `image.LayerOrder = map.Images.Count` (append on top).
  - Append `image` to `map.Images`.
  - Increment `state.BytesUsed += image.ByteSize`.
- Return `image`.

#### `UpdateImageTransformAsync(state, host, mapId, imageId, x, y, width, height, rotation, opacity) → Result`

- Host-only.
- Validate map and image exist.
- Validate `width > 0`, `height > 0`, `0.0 ≤ opacity ≤ 1.0`.
- Inside `state.Execute`: mutate the `MapImage` in place — set `X, Y, Width, Height, Rotation, Opacity`.

#### `ReorderImageLayerAsync(state, host, mapId, imageId, newLayerOrder) → Result`

- Host-only.
- Validate map and image exist; `0 ≤ newLayerOrder < map.Images.Count`.
- Inside `state.Execute`: remove the image from its current layer position, insert at `newLayerOrder`, then renumber `LayerOrder` on every image in the map by their list position.

#### `RemoveImageAsync(state, host, mapId, imageId) → Result`

- Host-only.
- Validate map and image exist.
- Inside `state.Execute`:
  - Capture the `MapImage` reference.
  - Remove from `map.Images`.
  - Renumber `LayerOrder` of remaining images by their list position (compact).
  - `state.AdjustBytesUsed(-image.ByteSize)`.
- After `Execute` returns success, call `_storage.Delete(image.RelativePath)`. Errors during disk delete are logged but do not fail the verb (the in-memory state is the source of truth; orphan files are an inconvenience, not a correctness bug, and the session-end cleanup will mop up).

> **Why disk delete happens AFTER `Execute`**: keeps the lock hold time minimal. The state mutation reflects the new reality regardless of whether the file delete succeeds.

### 3.3 `DeleteMapAsync` extension (from M01)

M01's `DeleteMapAsync` already does the in-memory cascade. Extend it:

```csharp
public Result DeleteMapAsync(DndMapperGameState state, User caller, Guid mapId)
{
    if (caller.Id != state.Host.Id) return Result.FromError(...);

    List<MapImage> imagesToDelete = [];

    var executeResult = state.Execute(() =>
    {
        var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
        if (map is null) return Result.FromError("Map not found.");

        // Snapshot images BEFORE removing the map so we can delete files outside the lock.
        imagesToDelete.AddRange(map.Images);
        long deltaBytes = -map.Images.Sum(i => i.ByteSize);
        state.AdjustBytesUsed(deltaBytes);

        state.Maps.Remove(map);

        if (state.ActiveMapId == mapId)
        {
            var next = state.Maps.OrderBy(m => m.ListOrder).FirstOrDefault();
            state.SetActiveMapId(next?.Id);
        }

        return Result.Success;
    });

    if (executeResult.IsCanceled) return executeResult;
    if (!executeResult.TryGetSuccess(out _)) return executeResult;

    foreach (var image in imagesToDelete)
    {
        try { _storage.Delete(image.RelativePath); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete image file [{Path}]; will retry at session end.", image.RelativePath); }
    }

    return Result.Success;
}
```

### 3.4 HTTP handler implementation (GET only) + in-process upload verb

The simplest approach is to **make `DndMapperGameEngine` itself implement `IGameEngineHttpHandler`** (one engine class — no separate handler). The engine already has the `_storage` and verb methods it needs.

```csharp
public sealed class DndMapperGameEngine : AbstractGameEngine, IGameEngineHttpHandler
{
    private readonly IPluginStorage _storage;
    private readonly ILogger<DndMapperGameEngine> _logger;
    private readonly ILogger<DndMapperGameState> _stateLogger;
    private readonly IRandomNumberService _rng;

    // The platform's plugin registration detects the IPluginContext parameter and
    // injects the per-plugin keyed context (DefaultPluginRegistration.Create<T>).
    // IPluginStorage is in AlwaysProtectedTypes, so it is NOT resolvable as a
    // regular DI service — read it from context.Storage instead. Capability gating
    // (the manifest's "Storage" entry) is enforced when context.Storage is accessed.
    public DndMapperGameEngine(
        IPluginContext context,
        ILogger<DndMapperGameEngine> logger,
        ILogger<DndMapperGameState> stateLogger,
        IRandomNumberService rng)
    {
        _logger = logger;
        _stateLogger = stateLogger;
        _rng = rng;
        _storage = context.Storage;   // throws PluginCapabilityNotGrantedException
                                      // if "Storage" is missing from plugin.json.
    }

    public ValueTask<IResult> HandleAsync(
        HttpContext context, string roomUri, AbstractGameState abstractState, string subPath, CancellationToken ct)
    {
        if (abstractState is not DndMapperGameState state)
            return ValueTask.FromResult<IResult>(Results.NotFound());

        // Only GET images/{id} is exposed via HTTP. Upload is in-process (see SaveImageAsync).
        if (context.Request.Method == "GET" && subPath.StartsWith("images/", StringComparison.Ordinal))
            return ValueTask.FromResult(HandleImageServe(subPath["images/".Length..], state, context, ct));

        return ValueTask.FromResult<IResult>(Results.NotFound());
    }
}
```

#### Upload verb (in-process from the host's circuit)

The upload UI in M05 (`ImageUploadButton.razor`) calls this directly with the file stream from `<InputFile>`. No HTTP, no cookie, no separate auth check — the caller is the host's circuit-bound `User` so identity is trustworthy.

```csharp
public async ValueTask<ValueResult<MapImage>> SaveImageAsync(
    DndMapperGameState state,
    User caller,
    Guid mapId,
    Stream fileStream,
    long declaredLength,
    CancellationToken ct = default)
{
    // Host-only — UI button is host-only but defense-in-depth.
    if (caller.Id != state.Host.Id)
        return ValueResult<MapImage>.FromError("Only the host may upload images.");

    if (state.Maps.All(m => m.Id != mapId))
        return ValueResult<MapImage>.FromError("Map not found.");

    const long PerFileCap = 5L * 1024 * 1024;
    const long PerRoomCap = 10L * 1024 * 1024;

    if (declaredLength > PerFileCap)
        return ValueResult<MapImage>.FromError("Image exceeds 5 MB per-file cap.");

    long bytesUsed = 0;
    state.WithExclusiveRead(() => bytesUsed = state.BytesUsed);
    if (bytesUsed + declaredLength > PerRoomCap)
        return ValueResult<MapImage>.FromError("Room exceeds 10 MB total image cap.");

    // Buffer-and-sniff the first 16 bytes for MIME detection.
    var head = new byte[16];
    int read = await fileStream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
    var sniffedMime = MimeSniffer.Detect(head.AsSpan(0, read));
    if (sniffedMime is not ("image/png" or "image/jpeg" or "image/webp"))
        return ValueResult<MapImage>.FromError("Only PNG / JPEG / WebP images are accepted.");

    string ext = sniffedMime switch
    {
        "image/png"  => "png",
        "image/jpeg" => "jpg",
        "image/webp" => "webp",
        _ => throw new UnreachableException(),
    };

    string fileId = Guid.NewGuid().ToString();
    string relativePath = $"{state.SessionId}/images/{fileId}.{ext}";

    long writtenBytes;
    try
    {
        using var output = _storage.OpenWrite(relativePath);
        // Write the sniffed prefix first, then continue copying the rest of the stream.
        await output.WriteAsync(head.AsMemory(0, read), ct);
        writtenBytes = read;
        var copyBuffer = new byte[81920];
        int n;
        while ((n = await fileStream.ReadAsync(copyBuffer, ct)) > 0)
        {
            // Defense-in-depth: refuse to write past the per-file cap even if declaredLength lied.
            if (writtenBytes + n > PerFileCap)
            {
                output.Dispose();
                _storage.Delete(relativePath);
                return ValueResult<MapImage>.FromError("Image stream exceeded 5 MB per-file cap.");
            }
            await output.WriteAsync(copyBuffer.AsMemory(0, n), ct);
            writtenBytes += n;
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to write uploaded image to storage.");
        try { _storage.Delete(relativePath); } catch { /* best effort */ }
        return ValueResult<MapImage>.FromError("Storage write failed.");
    }

    var image = new MapImage
    {
        Id = Guid.NewGuid(),
        RelativePath = relativePath,
        X = 0, Y = 0,
        Width = 10, Height = 10,    // sensible default; host adjusts via UpdateImageTransformAsync
        Rotation = 0,
        Opacity = 1.0,
        LayerOrder = 0,             // overwritten by AddImageAsync to Images.Count
        Locked = false,
        ByteSize = writtenBytes,
    };

    var addResult = AddImageAsync(state, caller, mapId, image);
    if (!addResult.TryGetSuccess(out var added))
    {
        try { _storage.Delete(relativePath); } catch { /* best effort */ }
        return addResult;
    }
    return added;
}
```

> **Why declared length is checked then re-validated during write**: `<InputFile>`'s `OpenReadStream(maxAllowedSize)` will throw if the browser sends more bytes than the stated cap, but we don't trust browser-supplied length blindly — the second check inside the copy loop is a final guard. It's a couple of lines and prevents disk-fill via crafted multipart bodies.
>
> **Storage prefix = `state.SessionId.ToString()`** — chosen so the session-end cleanup (§3.6) is self-contained: the engine knows the prefix without needing to remember the room URI. Set `SessionId` in `DndMapperGameState`'s ctor (§3.7).

#### GET `images/{imageId}` flow

```csharp
private IResult HandleImageServe(string idStr, DndMapperGameState state, HttpContext context, CancellationToken ct)
{
    if (!Guid.TryParse(idStr, out var imageId))
        return Results.NotFound();

    // No further auth check — knowing the room URI is the access control,
    // matching how Blazor circuits load images via _content/...
    MapImage? image = null;
    state.WithExclusiveRead(() =>
    {
        foreach (var map in state.Maps)
        {
            var found = map.Images.FirstOrDefault(i => i.Id == imageId);
            if (found is not null) { image = found; break; }
        }
    });

    if (image is null) return Results.NotFound();

    // Stream from storage.
    Stream stream;
    try { stream = _storage.OpenRead(image.RelativePath); }
    catch (FileNotFoundException) { return Results.NotFound(); }

    string contentType = GetContentTypeFromExtension(image.RelativePath);

    // Write Cache-Control BEFORE returning the IResult. The result's ExecuteAsync
    // runs later and writes status/body, but headers set on context.Response.Headers
    // here propagate through because ExecuteAsync does not clear them.
    context.Response.Headers["Cache-Control"] = "private, max-age=3600";

    return Results.Stream(
        stream,
        contentType: contentType,
        enableRangeProcessing: true,
        lastModified: null,
        entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{image.Id:N}\""));
}
```

> **`EntityTagHeaderValue` namespace**: `Microsoft.Net.Http.Headers.EntityTagHeaderValue` — add the `using` (or fully qualify as shown). Confirmed present in `Microsoft.Net.Http.Headers` in net10's framework reference assemblies.
>
> **Cache-Control write order**: writing into `context.Response.Headers` before `Results.Stream(...)` is fine — `IResult.ExecuteAsync` does not clear the headers dictionary; it sets status code and writes the body, leaving prior headers in place. Verify on the smoke test: `curl -I` should show `Cache-Control: private, max-age=3600`.
>
> **`Cache-Control: private`** ensures shared caches (CDN, proxy) do not cache room-scoped images. The image is "discoverable only to clients who know the room URL," and `private` keeps that property even with intermediate caches.

### 3.5 MIME sniffing helper

`Services/Logic/Games/Http/MimeSniffer.cs`:

```csharp
internal static class MimeSniffer
{
    public static string? Detect(ReadOnlySpan<byte> head)
    {
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
            return "image/png";
        // JPEG: FF D8 FF
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            return "image/jpeg";
        // WebP: "RIFF" .... "WEBP"
        if (head.Length >= 12 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F'
            && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P')
            return "image/webp";
        return null;
    }
}
```

This is a deliberately tight allow-list. SVG is rejected explicitly because its content can carry embedded scripts; even though the GET endpoint serves images with `Content-Disposition: inline`, the browser would happily execute SVG scripts.

### 3.6 Session-end cleanup

Extend `EndSessionAsync` and the state's `Dispose` (the GDD §5.5 says cleanup runs in `DndMapperGameState.Dispose`, which fires from `EndSessionAsync` and from circuit-grace-expiry):

`DndMapperGameEngine` subscribes to `state.OnStateDisposed` in `CreateStateAsync`:

```csharp
state.OnStateDisposed += () => CleanupRoomStorage(state.SessionId);
```

> **Subscription lifetime — verify during M03 implementation.** The closure captures `state.SessionId` (a per-state Guid), not the state instance, so there's no cross-session leak. But the engine is a singleton: every state ever created adds one entry to its own `OnStateDisposed` invocation list (per state). Each state's invocation list is freed when the state is disposed *if* `AbstractGameState.Dispose` clears `OnStateDisposed`. Today the dispose body clears `PlayerUnregistered` (verified at `AbstractGameState.cs:682`) but **does not visibly clear `OnStateDisposed` itself**. Confirm by re-reading the dispose method end-to-end during implementation; if `OnStateDisposed = null` is missing, file a one-line SDK fix alongside M03.
>
> Either way, this is not a runtime correctness bug — handlers fire once, then the state is GCable. It is only a memory-cleanliness concern.

`CleanupRoomStorage(Guid sessionId)`:

```csharp
private void CleanupRoomStorage(Guid sessionId)
{
    string prefix = $"{sessionId}/images";
    try
    {
        foreach (var rel in _storage.EnumerateFiles(prefix, "*"))
        {
            try { _storage.Delete(rel); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete [{Path}] during session cleanup.", rel); }
        }
    }
    catch (DirectoryNotFoundException) { /* no images uploaded — nothing to clean up */ }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Storage cleanup failed for session [{SessionId}].", sessionId);
    }
}
```

`EndSessionAsync` from M01 already disposes the state; no body change needed beyond the new `OnStateDisposed` subscription.

### 3.7 `DndMapperGameState.SessionId`

Add to `DndMapperGameState`:

```csharp
public Guid SessionId { get; } = Guid.NewGuid();
```

Initialized at construction. Used as the per-room storage prefix and as the cleanup key.

### 3.8 Plugin manifest

`plugin.json`:

```json
{
  "schemaVersion": 1,
  "name": "DnD Mapper",
  "description": "Collaborative tabletop map building.",
  "routeIdentifier": "dnd-mapper",
  "version": "1.0.0",
  "entryAssembly": "KnockBox.DndMapper",
  "capabilities": ["Storage"]
}
```

Without `"Storage"`, `IPluginContext.Storage` throws when the engine ctor resolves `IPluginStorage`.

---

## 4. Acceptance criteria

- [ ] `dotnet build host/KnockBox.Host.slnx` succeeds.
- [ ] `KnockBox.DndMapper.dll` and updated `plugin.json` stage into `host/KnockBox/bin/{Config}/{TFM}/games/KnockBox.DndMapper/`.
- [ ] `dotnet test host/KnockBox.DndMapperTests/KnockBox.DndMapperTests.csproj` is green, including the new `ImageVerbsTests` and `ImageHttpHandlerTests`.
- [ ] M01 tests still green (no regressions in maps / tokens / sheets / dice / lifecycle).
- [ ] M02 tests still green.
- [ ] Plugin still passes the analyzer (KB1001–KB1004) — image-write code path goes through `IPluginStorage`, never `System.IO.File`.
- [ ] `KnockBox.csproj` still has zero `using KnockBox.DndMapper.*`; `Program.cs` unchanged.

---

## 5. Manual verification

Required for M03 — the GET path needs an end-to-end smoke test before M05 builds UI on top.

The upload path is in-process, so smoke-testing it requires either (a) a debug verb invoking `SaveImageAsync` from a Razor page, or (b) waiting until M05's `ImageUploadButton.razor` lands. Option (b) is cleaner; defer upload smoke until M05 and verify only the GET path here.

1. Start host: `dotnet run --project host/KnockBox/KnockBox.csproj`.
2. Open a browser, create a DnD Mapper room. Note the room URL — copy `{guidA}-{guidB}` from `room/dnd-mapper/{guidA}-{guidB}`.
3. Pre-seed an image: either run a debug verb to call `SaveImageAsync`, or write a file directly to `host/KnockBox/data/plugins/dnd-mapper/{sessionId}/images/{guid}.png` and inject a `MapImage` record matching it via a debug `AddImageAsync` call.
4. Run `curl -v http://localhost:5000/api/plugins/dnd-mapper/{guidA}-{guidB}/images/{imageId} --output /tmp/out.png` — **no auth cookie needed**. Expect 200 with `Content-Type: image/png` (or jpeg/webp), `Cache-Control: private, max-age=3600`, and bytes matching the seed.
5. `curl http://localhost:5000/api/plugins/dnd-mapper/{guidA}-{guidB}/images/00000000-0000-0000-0000-000000000000` → 404 (image id not found).
6. `curl http://localhost:5000/api/plugins/dnd-mapper/bogus-room/images/{anything}` → 404 (room URI not found).
7. Close the room (host clicks End Session — wait for M06 UI, OR run `state.Dispose()` from a debug hook). Verify `host/KnockBox/data/plugins/dnd-mapper/{sessionId}/images/` is empty.

If pre-seeding proves cumbersome, this manual block can be deferred to M05 once the upload UI is available; M03 sign-off then rests on the unit tests in §6.

---

## 6. Inline unit test plan

### 6.1 Test fixtures

`Helpers/InMemoryPluginStorage.cs` — in-memory `IPluginStorage` that backs files with a `Dictionary<string, byte[]>`. Tests inject this in place of the real storage. Surface area to mock: `OpenRead`, `OpenWrite`, `Exists`, `Delete`, `EnumerateFiles`. Path normalization (forward slashes only, no leading slash) mirrors the real implementation.

### 6.2 `ImageVerbsTests`

- `AddImageAsync_HostCaller_AppendsAndIncrementsBytes` — happy path; assert `Map.Images.Count`, `BytesUsed`, returned `MapImage.LayerOrder`.
- `AddImageAsync_NonHostCaller_ReturnsError`.
- `AddImageAsync_UnknownMapId_ReturnsError`.
- `UpdateImageTransformAsync_HostCaller_MutatesInPlace`.
- `UpdateImageTransformAsync_NegativeWidth_ReturnsError`.
- `UpdateImageTransformAsync_OpacityOutOfRange_ReturnsError`.
- `UpdateImageTransformAsync_NonHostCaller_ReturnsError`.
- `ReorderImageLayerAsync_HostCaller_RenumbersLayerOrder`.
- `ReorderImageLayerAsync_NewOrderOutOfRange_ReturnsError`.
- `RemoveImageAsync_HostCaller_RemovesFromListDeletesFromStorageDecrementsBytes`.
- `RemoveImageAsync_StorageDeleteFails_ReturnsSuccessAndLogs` — fake storage throws on `Delete`; verb returns success; logger captures warning.
- `DeleteMapAsync_CascadesAllImageDeletes` — pre-populate map with 3 images; delete map; assert all 3 files removed from storage and `BytesUsed = 0`.
- `EndSessionAsync_DisposingState_FiresStorageCleanup` — pre-populate two maps with images; dispose state; assert all files in `{sessionId}/images/` removed.

### 6.3 `SaveImageAsyncTests` (in-process upload verb)

Tests run against the engine method directly — no `HttpContext`, no form data. Use the in-memory storage and assert disk effects + state mutations.

- `SaveImageAsync_HostHappyPath_PersistsAndReturnsMapImage` — happy path with a small PNG byte stream.
- `SaveImageAsync_NonHostCaller_ReturnsError` — caller is a player.
- `SaveImageAsync_DeclaredLengthOverPerFileCap_ReturnsErrorBeforeStreamRead`.
- `SaveImageAsync_OverRoomCap_ReturnsError` — pre-set `state.BytesUsed = 9 MB`, upload 2 MB.
- `SaveImageAsync_BadMime_ReturnsError` — pre-craft bytes that don't match PNG/JPEG/WebP magic.
- `SaveImageAsync_SvgRejected_ReturnsError` — bytes start with `<svg`.
- `SaveImageAsync_StreamLongerThanDeclared_DeletesPartialFileAndReturnsError` — stream provides 6 MB while `declaredLength` was 4 MB; assert the partial file is removed and `BytesUsed` is unchanged.
- `SaveImageAsync_StorageWriteFailure_RollsBackAndReturnsError` — fake storage throws on `OpenWrite`; assert no `MapImage` in state.
- `SaveImageAsync_AddImageRejection_DeletesDiskFile` — provide an unknown `mapId`; assert the on-disk file written before the engine reject is then deleted.
- `SaveImageAsync_UnknownMapId_ReturnsError`.

### 6.4 `ImageHttpHandlerTests` (GET only)

Use a hand-rolled `DefaultHttpContext` for the GET path. Use the in-memory storage to assert disk effects.

- `HandleAsync_GetImage_HappyPath_StreamsBytesWithCorrectContentType`.
- `HandleAsync_GetImage_HappyPath_SetsCacheControlHeader` — assert `Cache-Control: private, max-age=3600`.
- `HandleAsync_GetImage_NotFoundInState_Returns404`.
- `HandleAsync_GetImage_StorageFileMissing_Returns404` — image record exists, file does not.
- `HandleAsync_GetImage_AnonymousCaller_StillSucceeds` — `HttpContext.User.Identity.IsAuthenticated == false`; assert handler returns 200 (proves anonymous-by-design).
- `HandleAsync_PostMethod_Returns404` — POST is not exposed via HTTP; assert 404 (not 405) — handler returns NotFound from the switch fallback.
- `HandleAsync_UnknownPath_Returns404` — e.g. `GET foo/bar`.

---

## 7. Implementation choices to flag during PR

- **Upload runs in-process, not HTTP** — the most consequential M03 choice. The host's circuit calls `engine.SaveImageAsync(state, currentUser, mapId, fileStream, declaredLength)` directly. No HTTP boundary means no cookie complexity, no antiforgery concerns, no `FormOptions.MultipartBodyLengthLimit` host-config needed (the cap is enforced inline). Trade-off: no out-of-process / external-tool upload path. If a future feature requires that (e.g. CLI bulk import), it would need a follow-on platform milestone introducing a user-identity HTTP cookie.
- **Storage prefix = `state.SessionId.ToString()`** — chosen for self-containment of session-end cleanup. The trade-off is one extra Guid stored on state.
- **`enableRangeProcessing: true` on GET**: enables HTTP range requests so browsers can seek. Reasonable for image streaming; effectively free.
- **Cache-Control header**: written into `context.Response.Headers` *before* returning the `IResult`. Confirm by `curl -I` during smoke; if a future ASP.NET update changes `Results.Stream`'s header behavior, adjust.
- **`Cache-Control: private`** ensures shared caches (CDN, proxy) don't cache room-scoped images. The image is "discoverable only to clients who know the room URL," and `private` keeps that property even with intermediate caches.
- **Engine-as-handler vs separate handler class**: M03 chose engine-as-handler. If the engine grows past ~800 lines, factor `HandleAsync` into a helper class injected via DI; for now, keep it on the engine.
- **Analyzer compliance (KB1001–KB1004)**: M03's added code stays inside the analyzer's allowed surface. `MimeSniffer` reads only `ReadOnlySpan<byte>` (no I/O). All disk I/O goes through `IPluginStorage` (the engine's `_storage` field, populated from `IPluginContext.Storage` at ctor — see §3.4). The HTTP handler reads from `HttpContext` provided by the host platform — that's framework input, not a forbidden API. Add a one-line PR-checklist item: "`dotnet build` shows zero KB-prefixed diagnostics."
- **`IPluginStorage` ctor injection** ❌ — do **not** add `IPluginStorage` directly to the engine ctor; it's in `DefaultPluginRegistration.AlwaysProtectedTypes` and `ActivatorUtilities.CreateInstance` will fail to resolve it. Take `IPluginContext` and read `.Storage` from there. (Earlier drafts of this plan got this wrong.)
