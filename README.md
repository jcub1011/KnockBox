# KnockBox

KnockBox is a Blazor Server host that ships party games as **runtime-loaded plugins**. The host (`KnockBox`) has no compile-time knowledge of any game — each game is a standalone Razor Class Library that the host discovers on startup.

This README is the **in-repo contributor** onboarding guide for adding a new game plugin alongside the host in this solution. If you're building a plugin **externally** against the published NuGet packages (`KnockBox.Core`, `KnockBox.Platform`, `KnockBox.Templates`), use [`docs/making-a-game-plugin.md`](docs/making-a-game-plugin.md) instead — it covers the `dotnet new knockbox-game` scaffold, the DevHost workflow, and shipping plugins into a host's `games/` folder.

The authoritative architecture reference (for both workflows) is [`KnockBox/Specs/knockbox-platform-architecture.md`](KnockBox/Specs/knockbox-platform-architecture.md) — consult it for the full rationale behind the patterns described here.

## Prerequisites

- .NET 10 SDK
- The solution file is `KnockBox.slnx` (new SLNX format)

### Common commands

| Task | Command |
| --- | --- |
| Build the solution | `dotnet build KnockBox.slnx` |
| Build the host (transitively builds every plugin and stages it to `games/`) | `dotnet build KnockBox/KnockBox.csproj` |
| Run the host locally | `dotnet run --project KnockBox/KnockBox.csproj` |
| Publish the host | `dotnet publish KnockBox/KnockBox.csproj -c Release` |
| Run all tests | `dotnet test KnockBox.slnx` |
| Docker (repo root) | `docker compose up --build` |

## How the plugin system works (1-minute mental model)

- The host `KnockBox.csproj` references plugins with `ReferenceOutputAssembly="false" Private="false"`. Those references exist **only** to make the plugins build transitively — the host takes no compile-time dependency on plugin types.
- Each plugin `.csproj` imports [`Directory.Plugin.targets`](Directory.Plugin.targets) (a shared MSBuild target at the repo root). After every plugin build, that target copies the plugin DLL, `.deps.json`, scoped-CSS bundle, and `wwwroot/` into `KnockBox/bin/{Config}/{TFM}/games/{PluginName}/`.
- At startup, `Program.cs` calls `PluginLoader.LoadModules(AppContext.BaseDirectory/games)`. Each plugin is loaded into its own `PluginLoadContext` (AssemblyLoadContext) rooted at `games/{PluginName}/`. `KnockBox.Core`, the BCL, and logging/DI abstractions are deferred to the default ALC so type identity is preserved across the host/plugin boundary.
- Each plugin exposes **exactly one** `IGameModule` (public parameterless constructor). Its `RegisterServices(IServiceCollection)` wires the game's engine into DI. `Program.cs` then maps each plugin's `wwwroot/` folder to `/_content/{PluginName}` and registers each plugin's assembly with Blazor routing so its `@page` components are reachable.

## Quickstart: add a new game

The example below walks through building a hypothetical `KnockBox.CoinFlip` game. Replace `CoinFlip` / `coin-flip` with your game's name / route identifier.

### Step 1 — Create the project

```bash
dotnet new razorclasslib -n KnockBox.CoinFlip
```

Replace the generated `.csproj` with the following template (mirrors `KnockBox.DiceSimulator/KnockBox.DiceSimulator.csproj`):

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AddRazorSupportForMvc>true</AddRazorSupportForMvc>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\KnockBox.Core\KnockBox.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
      <_Parameter1>KnockBox.CoinFlipTests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

  <Import Project="..\Directory.Plugin.targets" />

</Project>
```

Non-negotiables:

- **References only `KnockBox.Core`.** Do not reference any other game project. Do not reference `KnockBox` (the host).
- **Imports `Directory.Plugin.targets`.** Without this, nothing is staged to `games/` and the host will never see your plugin.
- **SDK is `Microsoft.NET.Sdk.Razor`.** Plain `Microsoft.NET.Sdk` will not compile Razor pages.

### Step 2 — Wire the project into the solution and host

1. Add the project to `KnockBox.slnx`.
2. Add a build-only reference to `KnockBox/KnockBox.csproj`, alongside the existing plugins:

   ```xml
   <ProjectReference Include="..\KnockBox.CoinFlip\KnockBox.CoinFlip.csproj"
                     ReferenceOutputAssembly="false" Private="false" />
   ```

   The attributes matter:
   - `ReferenceOutputAssembly="false"` — the host does **not** take a compile-time dependency on plugin types. This preserves the runtime-discovery architecture.
   - `Private="false"` — plugin binaries are **not** copied into the host's `bin/` root. They live under `bin/.../games/{PluginName}/` (staged by `Directory.Plugin.targets`).
   - The reference exists only so that building or publishing the host transitively builds every plugin, ensuring `games/` is populated before `CopyPluginsToPublish` runs.

### Step 3 — Subclass `AbstractGameState`

Per-room state lives on a subclass of `AbstractGameState` ([`KnockBox.Core/Services/State/Games/Shared/AbstractGameState.cs`](KnockBox.Core/Services/State/Games/Shared/AbstractGameState.cs)). The base class provides the host, the player list, a `SemaphoreSlim(1,1)` lock, the `StateChangedEventManager`, kick/register hooks, and scheduled-callback support.

```csharp
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.CoinFlip.Services.State.Games;

public class CoinFlipGameState(User host, ILogger<CoinFlipGameState> logger)
    : AbstractGameState(host, logger)
{
    public string? LastResult { get; set; }
}
```

Rules of the road:

- **Never mutate a field directly from outside.** All mutations must happen inside a `state.Execute(...)` / `state.ExecuteAsync(...)` lambda invoked from the engine. This is what keeps the room's state thread-safe and what fires `StateChangedEventManager` so subscribed Razor pages re-render.
- If you need collections inside the state that are *also* mutated from non-`Execute` contexts (rare — prefer keeping everything inside `Execute`), guard them with their own lock or use a concurrent collection. Existing plugins (see `DiceSimulatorGameState`) do this for history buffers.

### Step 4 — Subclass `AbstractGameEngine`

The engine is a **singleton**. It holds no per-room state — it simply operates on the state instance it is given. Every fallible operation returns a `Result` / `ValueResult<T>` ([`KnockBox.Core/Extensions/Returns/Result.cs`](KnockBox.Core/Extensions/Returns/Result.cs)).

```csharp
using KnockBox.CoinFlip.Services.State.Games;
using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.Games.Utilities;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.CoinFlip.Services.Logic.Games;

public class CoinFlipGameEngine(
    IRandomNumberService rng,
    ILogger<CoinFlipGameEngine> logger,
    ILogger<CoinFlipGameState> stateLogger) : AbstractGameEngine
{
    public override Task<ValueResult<AbstractGameState>> CreateStateAsync(
        User host, CancellationToken ct = default)
    {
        if (host is null)
            return Task.FromResult(ValueResult<AbstractGameState>.FromError(
                "Failed to create game state.",
                $"Parameter {nameof(host)} was null."));

        var state = new CoinFlipGameState(host, stateLogger);
        state.UpdateJoinableStatus(true);
        return Task.FromResult(ValueResult<AbstractGameState>.FromValue(state));
    }

    public override Task<Result> StartAsync(
        User host, AbstractGameState state, CancellationToken ct = default)
    {
        if (state is not CoinFlipGameState s)
            return Task.FromResult(Result.FromError(
                "Error starting game.",
                $"State of type [{state?.GetType().Name ?? "null"}] is not {nameof(CoinFlipGameState)}."));

        if (host != s.Host)
            return Task.FromResult(Result.FromError("Only the host can start the game."));

        return Task.FromResult(s.Execute(() => s.UpdateJoinableStatus(false)));
    }

    public Result Flip(CoinFlipGameState state) =>
        state.Execute(() => state.LastResult = rng.Next(0, 2) == 0 ? "Heads" : "Tails");
}
```

Things to internalize:

- **The engine is a singleton.** Never put per-room data on it.
- **Every mutation goes through `state.Execute` / `state.ExecuteAsync`.** The base class acquires the semaphore, runs your lambda, releases the semaphore, and *then* notifies `StateChangedEventManager` subscribers — notification happens **outside** the lock to keep hold time minimal and avoid reentrant deadlocks.
- Use `state.WithExclusiveRead` / `WithExclusiveReadAsync` for serialized, non-mutating reads (no notification fires afterward).
- Use `Result.Success`, `Result.FromError(publicMsg, internalMsg)`, `ValueResult<T>.FromValue(...)`, `ValueResult<T>.FromError(...)`. Callers consume the result via `TryGetSuccess(out var value)`, `TryGetFailure(out var error)`, or `IsCanceled`. Avoid exceptions for control flow.
- `PlayerUnregistered` fires **outside** the execute lock, so handlers (e.g., "advance the turn when someone disconnects") can safely call `Execute` without deadlocking.

### Step 5 — Build the Razor page(s)

Game pages live at `room/{route-identifier}/{ObfuscatedRoomCode}`. They must inherit `DisposableComponent` ([`KnockBox.Core/Components/Shared/DisposableComponent.cs`](KnockBox.Core/Components/Shared/DisposableComponent.cs)), which provides a `ComponentDetached` cancellation token and a `Dispose()` lifecycle hook.

`Pages/CoinFlipLobby.razor`:

```razor
@page "/room/coin-flip/{ObfuscatedRoomCode}"
@inherits DisposableComponent

<PageTitle>Coin Flip</PageTitle>

<HeadContent>
    <link href="_content/KnockBox.CoinFlip/KnockBox.CoinFlip.styles.css" rel="stylesheet" />
</HeadContent>

@if (GameState is null)
{
    <p>Loading...</p>
}
else
{
    <p>Last result: @(GameState.LastResult ?? "(none yet)")</p>
    <button @onclick="OnFlipClicked">Flip</button>
}
```

`Pages/CoinFlipLobby.razor.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using KnockBox.CoinFlip.Services.Logic.Games;
using KnockBox.CoinFlip.Services.State.Games;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.Logic.Navigation;
using KnockBox.Core.Services.State.Sessions;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.CoinFlip.Pages;

public partial class CoinFlipLobby : DisposableComponent
{
    [Inject] protected CoinFlipGameEngine Engine { get; set; } = default!;
    [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;
    [Inject] protected INavigationService NavigationService { get; set; } = default!;
    [Inject] protected IUserService UserService { get; set; } = default!;
    [Inject] protected ILogger<CoinFlipLobby> Logger { get; set; } = default!;

    [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

    private IDisposable? _stateSubscription;
    protected CoinFlipGameState? GameState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (UserService.CurrentUser is null)
            await UserService.InitializeCurrentUserAsync(ComponentDetached);

        if (!GameSessionService.TryGetCurrentSession(out var session))
        {
            NavigationService.ToHome();
            return;
        }

        if (!TryExtractRoomCode(session.LobbyRegistration.Uri, out var code)
            || code.Trim() != ObfuscatedRoomCode)
        {
            NavigationService.ToHome();
            return;
        }

        GameState = (CoinFlipGameState)session.LobbyRegistration.State;
        if (GameState.IsDisposed)
        {
            NavigationService.ToHome();
            return;
        }

        GameState.OnStateDisposed += HandleStateDisposed;
        _stateSubscription = GameState.StateChangedEventManager.Subscribe(
            async () => await InvokeAsync(StateHasChanged));

        await base.OnInitializedAsync();
    }

    private void OnFlipClicked()
    {
        var result = Engine.Flip(GameState!);
        if (result.TryGetFailure(out var error))
            Logger.LogError("Flip failed: {Error}", error);
    }

    private void HandleStateDisposed() =>
        InvokeAsync(() =>
        {
            GameSessionService.LeaveCurrentSession(navigateHome: false);
            NavigationService.ToHome();
        });

    public override void Dispose()
    {
        if (GameState is not null)
            GameState.OnStateDisposed -= HandleStateDisposed;
        _stateSubscription?.Dispose();
        base.Dispose();
    }

    private static bool TryExtractRoomCode(string uri, [NotNullWhen(true)] out string? code)
    {
        var split = uri.Trim().Trim('/').Split('/');
        if (split.Length > 0) { code = split[^1]; return true; }
        code = null; return false;
    }
}
```

Key points:

- The route segment after `room/` **must** match the `RouteIdentifier` returned by your `IGameModule` (see Step 6). Mismatch = 404.
- Subscribe to `StateChangedEventManager` by storing the `IDisposable` it returns and disposing it in `Dispose()`. If you skip that, the component leaks.
- Validate the session and URL in `OnInitializedAsync`. If either is missing or mismatched, redirect home — users commonly land on game URLs without a live session (refresh after disconnect, shared link, etc.).
- Plugin static assets (CSS bundles, JS, images) are served from `_content/KnockBox.CoinFlip/...`. The host mounts this automatically from your `wwwroot/` folder.

### Step 6 — Implement `IGameModule`

The host finds your plugin by reflecting for `IGameModule` ([`KnockBox.Core/Plugins/IGameModule.cs`](KnockBox.Core/Plugins/IGameModule.cs)). Exactly one implementation per plugin.

```csharp
using KnockBox.CoinFlip.Components;
using KnockBox.CoinFlip.Services.Logic.Games;
using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.CoinFlip;

public class CoinFlipModule : IGameModule
{
    public string Name => "Coin Flip";
    public string Description => "Flip a coin.";
    public string RouteIdentifier => "coin-flip";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddGameEngine<CoinFlipGameEngine>(RouteIdentifier);
    }

    public RenderFragment GetButtonContent() => builder =>
    {
        builder.OpenComponent<CoinFlipTile>(0);
        builder.CloseComponent();
    };
}
```

- **Public parameterless constructor is required.** `PluginLoader` activates it via reflection.
- `services.AddGameEngine<TEngine>(routeIdentifier)` (see [`KnockBox.Core/Plugins/GameModuleServiceCollectionExtensions.cs`](KnockBox.Core/Plugins/GameModuleServiceCollectionExtensions.cs)) registers `TEngine` as a singleton **and** re-exposes the same instance as a keyed `AbstractGameEngine` under `routeIdentifier`. Razor pages inject the concrete engine; `LobbyService.CreateLobbyAsync` resolves `GetKeyedService<AbstractGameEngine>(routeIdentifier)` when spinning up a room.
- **`RouteIdentifier` must match your page's `@page` route segment verbatim** — e.g., `"coin-flip"` ↔ `@page "/room/coin-flip/{ObfuscatedRoomCode}"`.
- `GetButtonContent()` is the inner fragment of the game's tile on the home screen. The host wraps it in a `<button>` that owns click handling, disabled state, aria-label, and layout sizing — your fragment just owns the visual design. It is typical to point this at a small Razor component under `Components/`.

### Step 7 — Add the test project

Every plugin has a matching `{Name}Tests` project (MSTest + Moq). Create `KnockBox.CoinFlipTests` with this `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="MSTest" Version="4.2.1" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\KnockBox.CoinFlip\KnockBox.CoinFlip.csproj" />
    <ProjectReference Include="..\KnockBox.Core\KnockBox.Core.csproj" />
  </ItemGroup>

</Project>
```

A minimal test that exercises the engine against a real state:

```csharp
[TestClass]
public class CoinFlipGameEngineTests
{
    [TestMethod]
    public async Task Flip_WritesResultToState()
    {
        var rng = new Mock<IRandomNumberService>();
        rng.Setup(r => r.Next(0, 2)).Returns(0);
        var logger = new Mock<ILogger<CoinFlipGameEngine>>();
        var stateLogger = new Mock<ILogger<CoinFlipGameState>>();
        var engine = new CoinFlipGameEngine(rng.Object, logger.Object, stateLogger.Object);

        var host = new User("Host", "host-id");
        var stateResult = await engine.CreateStateAsync(host);
        Assert.IsTrue(stateResult.TryGetSuccess(out var state));
        var coinState = (CoinFlipGameState)state;

        var result = engine.Flip(coinState);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("Heads", coinState.LastResult);
    }
}
```

Add the test project to `KnockBox.slnx` as well.

### Step 8 — Run it

```bash
dotnet build KnockBox/KnockBox.csproj
dotnet run --project KnockBox/KnockBox.csproj
```

Building the host transitively builds your plugin and stages it into `KnockBox/bin/{Config}/net10.0/games/KnockBox.CoinFlip/`. On startup, `PluginLoader` picks it up, the home page's tile list discovers `CoinFlipModule`, and `/room/coin-flip/{code}` routes to your page.

## Key APIs at a glance

| Type / file | Purpose |
| --- | --- |
| [`IGameModule`](KnockBox.Core/Plugins/IGameModule.cs) | The plugin entry-point interface. One public parameterless implementation per plugin. |
| [`GameModuleServiceCollectionExtensions.AddGameEngine<TEngine>`](KnockBox.Core/Plugins/GameModuleServiceCollectionExtensions.cs) | Registers an engine as both a concrete singleton and a keyed `AbstractGameEngine`. |
| [`AbstractGameState`](KnockBox.Core/Services/State/Games/Shared/AbstractGameState.cs) | Base for per-room state. Provides `Execute`/`ExecuteAsync`, `WithExclusiveRead`, `StateChangedEventManager`, `RegisterPlayer`/`KickPlayer`, `ScheduleCallback`, `PlayerUnregistered`, `OnStateDisposed`. |
| [`AbstractGameEngine`](KnockBox.Core/Services/Logic/Games/Engines/Shared/AbstractGameEngine.cs) | Base for game engines. Override `CreateStateAsync`, `StartAsync`, optionally `CanStartAsync`. Singletons — never store per-room data here. |
| [`DisposableComponent`](KnockBox.Core/Components/Shared/DisposableComponent.cs) | Base for game Razor pages. Exposes `ComponentDetached` cancellation token; subclasses override `Dispose()` to clean up state subscriptions. |
| [`Result`, `ValueResult<T>`, `ValueResult<T, TError>`](KnockBox.Core/Extensions/Returns/Result.cs) | Return types for fallible operations. Use `TryGetSuccess`, `TryGetFailure`, `IsCanceled`. |
| [`Directory.Plugin.targets`](Directory.Plugin.targets) | Shared MSBuild target each plugin imports. Copies DLL + `wwwroot/` into `KnockBox/bin/.../games/{PluginName}/`. |
| [`KnockBox/Program.cs`](KnockBox/Program.cs) | Host composition root — look here for `PluginLoader.LoadModules`, `RegisterLogic`, and `MapPluginStaticAssets`. |

## Critical invariants (do not violate)

- **No host → plugin compile reference.** `KnockBox.csproj` only references `KnockBox.Core` directly; plugin refs carry `ReferenceOutputAssembly="false" Private="false"`. Removing those attributes breaks the runtime-discovery architecture.
- **No `using` of plugin types from the host.** Types flow one direction: plugin → `KnockBox.Core` contracts.
- **All state mutation goes through `Execute` / `ExecuteAsync`.** Direct field writes bypass the lock and notifications — subscribers stop re-rendering.
- **Always dispose the `StateChangedEventManager` subscription** in the page's `Dispose()`. Otherwise the closure keeps the component alive after navigation.
- **`RouteIdentifier` must match the `@page` route segment exactly.** `"coin-flip"` ↔ `/room/coin-flip/...`. No leading slash, no casing drift.
- **Plugin `.csproj` must `<Import Project="..\Directory.Plugin.targets" />`.** Without it, nothing is staged to `games/` and the host never loads your plugin.

## Further reading

- [`KnockBox/Specs/knockbox-platform-architecture.md`](KnockBox/Specs/knockbox-platform-architecture.md) — the authoritative architecture reference (ALC isolation, session lifecycle, DI order, lobby routing).
- [`CLAUDE.md`](CLAUDE.md) — build/test commands and additional contributor notes.
- Existing plugins (`KnockBox.CardCounter`, `KnockBox.Codeword`, `KnockBox.DiceSimulator`, `KnockBox.DrawnToDress`, `KnockBox.Operator`) — concrete examples of the patterns above at varying complexity. `KnockBox.DiceSimulator` is the simplest starting reference.
