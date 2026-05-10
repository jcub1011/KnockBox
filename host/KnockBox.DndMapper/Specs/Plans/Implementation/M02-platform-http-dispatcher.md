# M02 — Platform HTTP Dispatcher (`IGameEngineHttpHandler`)

> **Goal**: introduce a generic plugin-route HTTP dispatcher in the platform so plugins can opt into HTTP endpoints without leaking plugin-specific names into `Program.cs`. Defines a new SDK contract (`IGameEngineHttpHandler`), a dispatcher in `KnockBox.Platform`, and a URI-indexed lobby lookup. M02 ships **without any DnD-Mapper-specific code** — it's a pure platform extension.
>
> **Dependencies**: none. Can be implemented in parallel with M01 if desired (the milestones don't share files), though the recommended sequence is M01 → M02 → M03.
>
> **GDD references**: §5.4 (image serve — the obfuscated room URI is the access token; "no further auth check"). §13 platform-extension callout (note: the v1.x display-view observer-attach is a different, separate platform addition — NOT part of M02).
>
> **Out of scope** (do NOT implement here): DnD-Mapper image verbs, image upload UI, image-serve handler implementation (M03 — DndMapper opts into the contract there). Display view, observer-attach (v1.x).
>
> **Design decision (auth model)**: M02's dispatcher is **anonymous at the route level** — plugin endpoints do *not* sit behind `RequireAuthorization()`. The obfuscated room URI's two random GUIDs are treated as the access token, matching GDD §5.4 verbatim. This works for v1's only HTTP need, which is image serve (`GET .../images/{id}`). Image *upload* (POST) does not go through this dispatcher in v1 — it runs in-process from the host's Blazor circuit (which already has the user identity from `IUserService`). See M03 for the in-process upload path. This avoids needing a user-identity HTTP cookie that does not exist today (the only cookie scheme is `.KnockBox.Admin`, scoped to `/admin/login`).

---

## 1. Context

The DnD Mapper plugin needs an HTTP endpoint for image serve — `GET /api/plugins/dnd-mapper/{ObfuscatedRoomCode}/images/{imageId}` — so the browser's `<image href=...>` SVG element can fetch uploaded map background images directly. The platform's compile-time isolation invariant says the host project (`KnockBox`) must not have any `using` references to plugin types; that means `Program.cs` cannot directly map plugin-specific routes. Instead, the platform provides one generic dispatcher and plugins opt in via a contract.

This is a real platform addition that benefits **future plugins beyond DnD Mapper** — any game that needs file streaming, webhook-style endpoints, or anonymous-room-token-gated HTTP can opt into the same contract. v1.x will likely add a parallel observer-attach for the display view, but that is structurally distinct (observer pattern, not a request/response handler) and lives in its own milestone.

Image *upload* (host-only, multipart) is **not** routed through this dispatcher in v1. Upload runs in-process from the host's Blazor circuit via a new engine method `SaveImageAsync(state, host, stream, contentType, mapId)` introduced in M03. The circuit already has the user's identity (`IUserService.CurrentUser`); writing to disk and updating state is a straightforward engine call with no HTTP boundary. Keeping upload off HTTP in v1 sidesteps the user-identity-cookie gap (see "Auth model" callout above) and is structurally simpler.

**Confirmed during exploration**:
- No `/api/plugins/...` endpoints exist today — `host/KnockBox/Program.cs` has only an admin log download route.
- The only cookie scheme wired in `Program.cs` is `.KnockBox.Admin` (scoped to `/admin/login`). There is no user-circuit identity cookie. `IUserService` holds user identity per Blazor circuit, not via HTTP cookies.
- `LobbyService._lobbies` is keyed by the **short alphanumeric lobby code** (e.g. `ABCD1234`), not the URI's `{ObfuscatedRoomCode}` segment (the two-GUID `{guidA}-{guidB}`). The dispatcher needs a URI-indexed lookup, which M02 adds.
- `KnockBox.Platform` is in the host build (not a plugin); analyzers KB1001–KB1004 do NOT fire on platform code, so `System.IO`, `HttpContext`, etc. are usable here directly.

---

## 2. Files to create / modify

### New files

```
sdk/KnockBox.Core/Plugins/IGameEngineHttpHandler.cs
sdk/KnockBox.Platform/Services/Logic/Plugins/PluginHttpDispatcher.cs
sdk/KnockBox.Platform/Services/Logic/Plugins/PluginApiEndpointExtensions.cs
sdk/KnockBox.PlatformTests/Unit/PluginHttpDispatcherTests.cs
sdk/KnockBox.PlatformTests/Helpers/FakeGameEngineHttpHandler.cs
sdk/KnockBox.PlatformTests/Helpers/FakeAbstractGameEngine.cs
```

### Files to modify

- `sdk/KnockBox.Platform/Services/Logic/Games/Shared/LobbyService.cs` — add a secondary `ConcurrentDictionary<string, LobbyRegistration> _lobbiesByUri`, populate it on `TryAdd`, remove on `TryRemove`, expose `bool TryGetByUri(string uri, out LobbyRegistration registration)`.
- `sdk/KnockBox.Core/Services/Logic/Games/Shared/ILobbyService.cs` — add `bool TryGetByUri(string uri, out LobbyRegistration registration)` (or whatever the precise existing interface name is — verify when implementing).
- `sdk/KnockBox.Platform/KnockBoxPlatformExtensions.cs` (the `MapKnockBoxPlatformEndpoints` extension) — call `app.MapPluginApi()` after the existing `MapStaticAssets()` and before `MapRazorComponents`.

### Files NOT touched in M02

- `host/KnockBox/Program.cs` — no DnD-Mapper-specific references. The `app.UseAuthentication()` / `app.UseAuthorization()` middleware is already in place (cookie auth is already wired); the dispatcher just adds another endpoint.
- Any plugin project, including `KnockBox.DndMapper` — M03 is the first plugin to opt in.

---

## 3. Detailed work breakdown

### 3.1 `IGameEngineHttpHandler` contract

`sdk/KnockBox.Core/Plugins/IGameEngineHttpHandler.cs`:

```csharp
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.Core.Plugins;

/// <summary>
/// Optional opt-in contract for game engines that expose HTTP endpoints under
/// the platform's generic plugin-route dispatcher (<c>/api/plugins/{routeIdentifier}/{**path}</c>).
/// </summary>
/// <remarks>
/// The dispatcher invokes <see cref="HandleAsync"/> only after it has:
/// <list type="bullet">
///   <item>resolved the engine via the route identifier (keyed-DI),</item>
///   <item>verified the engine implements this interface,</item>
///   <item>resolved the room by its obfuscated URI segment,</item>
/// </list>
/// so the handler can assume <paramref name="state"/> is non-null.
/// <para>
/// The dispatcher does <b>not</b> authenticate the caller. The obfuscated room URI's two
/// random GUIDs are treated as the access token (matching the existing room-URL convention).
/// Handlers that need a stronger identity check must read <c>context.User</c> themselves;
/// for v1, no plugin endpoint requires this.
/// </para>
/// <para>
/// The handler is responsible for any further sub-path routing within its prefix
/// (e.g. <c>images</c> vs <c>images/{id}</c>) and for HTTP method discrimination.
/// </para>
/// </remarks>
public interface IGameEngineHttpHandler
{
    /// <summary>
    /// Handle a plugin-routed HTTP request.
    /// </summary>
    /// <param name="context">The ASP.NET Core HTTP context. The handler may read the body, write a response, set headers, etc.</param>
    /// <param name="roomUri">The obfuscated room URI segment (<c>{guidA}-{guidB}</c>) — same as <c>LobbyRegistration.Uri</c>'s trailing segment.</param>
    /// <param name="state">The resolved game state for the room. Mutations must go through <c>state.Execute*</c> per platform contract.</param>
    /// <param name="subPath">Path components after the obfuscated room URI, joined with '/' (no leading slash). Empty string if no sub-path.</param>
    /// <param name="ct">Cancellation token tied to the request lifetime.</param>
    /// <returns>An <see cref="IResult"/> the dispatcher will execute.</returns>
    ValueTask<IResult> HandleAsync(
        HttpContext context,
        string roomUri,
        AbstractGameState state,
        string subPath,
        CancellationToken ct);
}
```

> **Note on `IResult`**: this is `Microsoft.AspNetCore.Http.IResult` — the standard ASP.NET endpoint return type. Adding the `Microsoft.AspNetCore.Http.Abstractions` reference (already transitively present in Core via the existing component model) is fine. Confirm during implementation; if `KnockBox.Core` doesn't already depend on it, add the package — `IResult` is what makes the dispatcher's return path uniform.

### 3.2 URI-indexed lobby lookup

In `LobbyService`:

```csharp
private readonly ConcurrentDictionary<string, LobbyRegistration> _lobbiesByUri = new(StringComparer.OrdinalIgnoreCase);

// Inside CreateLobbyAsync after _lobbies.TryAdd succeeds:
_lobbiesByUri.TryAdd(NormalizeUri(lobbyRegistration.Uri), lobbyRegistration);

// Inside CloseLobbyAsync (and StopAsync's snapshot loop) after _lobbies.TryRemove:
_lobbiesByUri.TryRemove(NormalizeUri(removed.Uri), out _);
```

Plus a public method on `ILobbyService`:

```csharp
bool TryGetByUri(string uri, out LobbyRegistration registration);
```

`NormalizeUri` strategy: the URI stored in `LobbyRegistration.Uri` is `room/{routeIdentifier}/{guidA}-{guidB}`. The dispatcher receives just the `{guidA}-{guidB}` segment from the URL template. Decide on one of:

1. **Index by the full `Uri` string** (`room/dnd-mapper/{guidA}-{guidB}`) and have the dispatcher reconstruct it from `routeIdentifier + obfuscatedRoomCode`. Most explicit; least surprising.
2. **Index by the trailing `{guidA}-{guidB}` only** and trust uniqueness of the GUID pair across all routes. GUIDs are 128-bit; collision risk is zero in practice; saves a string concat. But two rooms in different games could *theoretically* collide — keep them disambiguated.

**Pick option 1.** Index by the full URI; the dispatcher constructs `$"room/{routeIdentifier}/{obfuscatedRoomCode}"` for lookup.

### 3.3 Dispatcher implementation

`sdk/KnockBox.Platform/Services/Logic/Plugins/PluginHttpDispatcher.cs`:

```csharp
internal sealed class PluginHttpDispatcher
{
    private readonly IServiceProvider _sp;
    private readonly ILobbyService _lobbyService;
    private readonly ILogger<PluginHttpDispatcher> _logger;

    public PluginHttpDispatcher(IServiceProvider sp, ILobbyService lobbyService, ILogger<PluginHttpDispatcher> logger)
    {
        _sp = sp;
        _lobbyService = lobbyService;
        _logger = logger;
    }

    public async ValueTask<IResult> DispatchAsync(
        string routeIdentifier,
        string subPath,
        HttpContext context,
        CancellationToken ct)
    {
        // 1. Resolve engine via keyed DI.
        var engine = _sp.GetKeyedService<AbstractGameEngine>(routeIdentifier);
        if (engine is null)
            return Results.NotFound(new { error = "Unknown plugin route." });

        // 2. Plugin must opt in.
        if (engine is not IGameEngineHttpHandler handler)
            return Results.NotFound(new { error = "Plugin does not expose HTTP endpoints." });

        // 3. Parse the leading segment of subPath as the obfuscated room URI.
        if (string.IsNullOrEmpty(subPath))
            return Results.NotFound(new { error = "Missing room identifier." });

        int slash = subPath.IndexOf('/');
        string obfuscatedRoomCode = slash < 0 ? subPath : subPath[..slash];
        string handlerSubPath = slash < 0 ? string.Empty : subPath[(slash + 1)..];

        // 4. Resolve room. The obfuscated URI's two random GUIDs serve as the
        //    access token; no further auth check is performed by the dispatcher.
        //    Handlers may read context.User themselves if they need to gate.
        string fullUri = $"room/{routeIdentifier}/{obfuscatedRoomCode}";
        if (!_lobbyService.TryGetByUri(fullUri, out var registration))
            return Results.NotFound(new { error = "Unknown room." });

        // 5. Delegate.
        try
        {
            return await handler.HandleAsync(context, obfuscatedRoomCode, registration.State, handlerSubPath, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin handler [{Route}] threw.", routeIdentifier);
            return Results.Problem(detail: "Plugin handler error.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
```

> **No user resolution** in the dispatcher. The v1 endpoints are anonymous-by-design (image GET treats the obfuscated URI as the access token, per GDD §5.4). If a future handler needs user identity, the right path is to add an HTTP-readable user-circuit cookie in a separate platform milestone — out of scope for M02. Until then, handlers that absolutely require identity should be served via the in-process / circuit path instead (as M03's image upload is).

### 3.4 Endpoint mapping extension

`sdk/KnockBox.Platform/Services/Logic/Plugins/PluginApiEndpointExtensions.cs`:

```csharp
public static class PluginApiEndpointExtensions
{
    public static IEndpointRouteBuilder MapPluginApi(this IEndpointRouteBuilder app)
    {
        app.MapMethods(
                "/api/plugins/{routeIdentifier}/{**subPath}",
                new[] { "GET", "POST", "PUT", "DELETE" },
                async (string routeIdentifier, string? subPath, HttpContext ctx, PluginHttpDispatcher dispatcher, CancellationToken ct) =>
                    await dispatcher.DispatchAsync(routeIdentifier, subPath ?? string.Empty, ctx, ct))
            .AllowAnonymous();
            // .DisableAntiforgery() is intentionally omitted — v1 has no plugin-routed
            // POST endpoint that accepts multipart/form-data. Image upload runs via the
            // in-process circuit path (M03 §3.4) and never hits this dispatcher.

        return app;
    }
}
```

`PluginHttpDispatcher` itself is registered as a singleton in DI:

```csharp
// In KnockBoxPlatformExtensions.AddKnockBoxPlatform's RegisterLogic step:
services.AddSingleton<PluginHttpDispatcher>();
```

`MapKnockBoxPlatformEndpoints` (whichever method maps endpoints today) calls `app.MapPluginApi()` AFTER `app.MapStaticAssets()` and BEFORE `app.MapRazorComponents<TRootComponent>()`.

### 3.5 Auth / antiforgery notes

- `AllowAnonymous()` is explicit (rather than relying on the default fallback) so a future global authorization policy doesn't accidentally lock down plugin endpoints. The obfuscated-URI access-token model is a **deliberate choice** in M02 to satisfy GDD §5.4 ("the obfuscated room code is treated as the access token") and to leave the door open for the v1.x display view to fetch images without per-circuit identity.
- The dispatcher does NOT enforce any caller-side authorization — that's the **handler's responsibility**. v1's only handler (M03's image GET) does no further auth check; the GET succeeds for any caller who knows the room URI and the image id.
- POST/PUT/DELETE are routable through the dispatcher today but no v1 plugin uses them. If a future plugin needs them, it will need an authorization story — likely the user-identity-cookie addition mentioned in §1, scoped to its own platform milestone.

---

## 4. Acceptance criteria

- [ ] `dotnet build sdk/KnockBox.Sdk.slnx` succeeds.
- [ ] `dotnet build host/KnockBox.Host.slnx` succeeds (no warnings introduced).
- [ ] `dotnet test sdk/KnockBox.Sdk.slnx` is green, including the new `PluginHttpDispatcherTests`.
- [ ] `dotnet test host/KnockBox.Host.slnx` is green (no plugin regressions).
- [ ] `KnockBox.csproj` still has zero `using KnockBox.DndMapper.*`.
- [ ] `Program.cs` is unchanged (the dispatcher is wired through `MapKnockBoxPlatformEndpoints`, which is the existing platform-level extension).
- [ ] A request to `/api/plugins/some-unknown-route/...` returns 404.
- [ ] A request to a valid plugin route whose engine does not implement `IGameEngineHttpHandler` returns 404.
- [ ] A request to `/api/plugins/{any}/...` from an anonymous (no cookie) caller is **not** rejected at the route layer — the dispatcher runs and the response is determined by handler / room-resolution outcome (likely 404 if the room URI is unknown). v1's auth model treats the obfuscated URI as the access token.

---

## 5. Manual verification

Optional — M03 will exercise the full path with real bytes. For M02 alone:

- Run the host: `dotnet run --project host/KnockBox/KnockBox.csproj`.
- Hit `curl http://localhost:5000/api/plugins/no-such-game/foo` — expect 404 (no cookie required; the route is anonymous and there is no engine for that route).
- Hit `curl http://localhost:5000/api/plugins/dnd-mapper/foo/bar` — expect 404 (DnD Mapper does not yet implement `IGameEngineHttpHandler`; that's M03).

---

## 6. Inline unit test plan

`sdk/KnockBox.PlatformTests/Unit/PluginHttpDispatcherTests.cs`:

### 6.1 Test fixtures

`Helpers/FakeGameEngineHttpHandler.cs` — a fake plugin engine that implements both `AbstractGameEngine` (return `IsJoinable = true` from `CreateStateAsync`) and `IGameEngineHttpHandler` (records the args of `HandleAsync` and returns a fixed `Results.Ok(new { ok = true })`).

`Helpers/FakeAbstractGameEngine.cs` — a fake plugin engine that implements `AbstractGameEngine` only (no `IGameEngineHttpHandler`). Used to test the "plugin doesn't opt in" 404.

Use a hand-rolled `DefaultHttpContext` + a manually-constructed `ServiceProvider`. The project's existing tests use MSTest + `Moq.AutoMock` and don't currently include `WebApplicationFactory`; adding the test-host dependency just for two endpoint tests is overkill. Stick with hand-rolled.

### 6.2 Test methods

- `DispatchAsync_UnknownRouteIdentifier_Returns404` — no engine registered for the route.
- `DispatchAsync_EngineDoesNotImplementHandler_Returns404`.
- `DispatchAsync_EmptySubPath_Returns404` — `subPath = ""`.
- `DispatchAsync_UnknownRoomUri_Returns404` — `LobbyService.TryGetByUri` returns false.
- `DispatchAsync_AnonymousCaller_StillReachesHandler` — explicit assertion that the dispatcher does NOT reject anonymous callers (the route is `AllowAnonymous`). Use a `DefaultHttpContext` whose `User` is unauthenticated; assert `HandleAsync` is invoked.
- `DispatchAsync_HappyPath_DelegatesToHandlerWithCorrectArgs` — register a `FakeGameEngineHttpHandler`; dispatch a request; assert the handler received: the matching `roomUri`, the resolved `state`, and the trailing `subPath`. (No user param — verify the contract signature matches the new shape.)
- `DispatchAsync_HandlerThrowsOperationCanceled_ReturnsClientClosedRequest` — handler throws `OperationCanceledException` with the request CT.
- `DispatchAsync_HandlerThrowsGenericException_ReturnsProblem500`.
- `DispatchAsync_SubPathWithMultipleSegments_PassesTrailingPathToHandler` — request `/api/plugins/foo/{room}/images/{id}/extra` → handler receives `subPath = "images/{id}/extra"`, `roomUri = "{room}"`.

### 6.3 LobbyService URI lookup tests

Add to existing `sdk/KnockBox.PlatformTests/Unit/LobbyServiceTests.cs` (or create if absent):

- `TryGetByUri_AfterCreateLobby_ReturnsRegistration`.
- `TryGetByUri_AfterCloseLobby_ReturnsFalse`.
- `TryGetByUri_DuringShutdown_StaleLookupReturnsFalse` (after `StopAsync`).
- `TryGetByUri_UnknownUri_ReturnsFalse`.

---

## 7. Files NOT to create / modify

- Do NOT add any DnD-Mapper-specific code. M03 is where the plugin opts in.
- Do NOT add a separate `IGameEngineWebSocketHandler` or observer-attach contract — the v1.x display view will be a different milestone. M02 is *only* request/response HTTP.
- Do NOT touch `Program.cs` directly. All endpoint mapping flows through `MapKnockBoxPlatformEndpoints`.
- Do NOT add a per-plugin sub-route registration helper (e.g. `IPluginRegistration.MapApiHandler<T>()`). The contract is "implement `IGameEngineHttpHandler` on the engine"; sub-path routing is the engine's responsibility. Adding a per-route helper is over-engineering for v1.

---

## 8. Implementation choices to flag during PR

- **Anonymous-by-design**: this is the most consequential decision in M02. The dispatcher does not run authentication and the handler signature does not carry a `User`. This matches GDD §5.4 ("the obfuscated room code is treated as the access token") and avoids inventing a user-circuit cookie before there is a clear need. Document this prominently in the PR; a future plugin that needs HTTP-bound user identity will require a follow-on platform milestone.
- **`MapMethods` vs separate `MapGet` / `MapPost`**: `MapMethods` allows one route definition for all verbs and lets the handler discriminate. Cleaner. Stick with it unless ASP.NET routing complications arise.
- **Concurrency on `_lobbiesByUri`**: `ConcurrentDictionary` is sufficient; the URI is unique per room and registered/removed atomically alongside `_lobbies` (do BOTH operations under the same code path, even if not in a lock — the existing code path already uses `TryAdd` / `TryRemove` on `_lobbies`, so adding a parallel `_lobbiesByUri` mutation is safe).
- **`Microsoft.AspNetCore.Http.Abstractions` in `KnockBox.Core`**: this package brings `HttpContext` and `IResult` into the SDK. If the team prefers to keep `KnockBox.Core` framework-agnostic, an alternative is to put `IGameEngineHttpHandler` in a separate `KnockBox.Plugins.Http` SDK assembly. M02 picks the simpler path (in `Core`) — flag for review during PR.
- **`UserFactory.Create(name, id)` truncates name to 12 characters** (verified at `sdk/KnockBox.Core/Services/State/Users/IUserService.cs:55-60`). M02's dispatcher no longer constructs `User` objects, so this no longer matters at the dispatcher boundary. Recorded here in case a future revision adds back HTTP-side user resolution: identity comparisons must use `User.Id`, not `User.Name`.
