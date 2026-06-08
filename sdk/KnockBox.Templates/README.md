# KnockBox.Templates

**`dotnet new` template pack for KnockBox game plugins.**

Installs the `knockbox-game` template, which scaffolds a complete starter solution for a new KnockBox party-game plugin — plugin assembly, local dev host, and tests — in a single command.

## Install

```bash
dotnet new install KnockBox.Templates
```

## Use

```bash
dotnet new knockbox-game -n MyGame --routeIdentifier my-game
```

Parameters:

| Parameter | Description |
| --- | --- |
| `-n MyGame` | Project/solution name. Replaces `MyGame` across every generated file and folder. |
| `--routeIdentifier my-game` | URL-safe game identifier. Must be lowercase, hyphen-separated. Replaces `my-game` across every generated file, including each page's `@page` route. |

## What you get

```
MyGame/
├── MyGame.slnx
├── MyGame/                      # the SERVER plugin assembly (Razor Class Library)
│   ├── MyGame.csproj            # references KnockBox.Core + MyGame.Contracts
│   ├── MyGameModule.cs          # IGameModule entry point
│   ├── MyGameGameState.cs       # per-room state : AbstractGameState
│   ├── MyGameGameEngine.cs      # stateless singleton : AbstractGameEngine
│   ├── Pages/
│   │   └── MyGameLobby.razor    # @page "/room/my-game/{ObfuscatedRoomCode}"
│   ├── wwwroot/
│   │   └── tile.svg             # home-page tile (referenced from plugin.json)
│   └── _Imports.razor
├── MyGame.Contracts/            # shared DTOs (commands + projected view) — NO KnockBox refs
│   ├── MyGame.Contracts.csproj
│   └── GameContracts.cs         # GameView record + GameCommands + JSON source-gen context
├── MyGame.Client/               # browser UI (Razor Class Library, runtime-loaded into WASM)
│   ├── MyGame.Client.csproj     # references MyGame.Contracts + KnockBox.Core.Client
│   ├── GameRoot.razor           # : HubLobbyPageBase<GameView> — the UI entry point
│   ├── GameClientModule.cs      # IGameClientModule (declares route + root component)
│   └── _Imports.razor
├── MyGame.DevHost/              # local F5 harness (ASP.NET Core Web)
│   ├── MyGame.DevHost.csproj    # references KnockBox.Platform + the plugin + the client
│   └── Program.cs               # AddKnockBoxPlatform(...) + AddGameModule<T>()
└── MyGame.Tests/                # MSTest + Moq test project
    ├── MyGame.Tests.csproj
    └── MyGameGameEngineTests.cs
```

### The plugin tri-split

A game is split into three assemblies so its UI can run in the browser over WebAssembly:

- **`MyGame`** — server logic (engine, per-room state, `IGameModule`). Loaded by the host as today.
- **`MyGame.Contracts`** — pure shared DTOs: the commands the client sends and the per-player view the
  server projects back. Loads in **both** runtimes; has **zero `KnockBox.*` references**.
- **`MyGame.Client`** — the browser UI, downloaded at runtime. References only `MyGame.Contracts` and
  the WASM-safe `KnockBox.Core.Client` SDK. Build-time analyzers enforce the boundary: **KB1005** (no
  server-only type references), **KB1006** (the hub view type must be a `*.Contracts` DTO), **KB1008**
  (no server `System.IO` — use browser File/Blob); on the server, **KB1007** keeps projector view types
  serializable.

> **DevHost scope:** the scaffolded DevHost runs Blazor **Server** with explicit plugin registration,
> so it builds the whole tri-split but does **not** itself serve the runtime WASM client UI. Deploy the
> staged `MyGame.DevHost/bin/.../games/MyGame/client/` assets to a full WASM-capable KnockBox host to
> exercise the browser UI end-to-end.

Every generated file carries inline comments explaining what it's for, which parts to edit, and which invariants to leave alone — so you can read the scaffold top-to-bottom and understand the shape of a plugin without leaving the IDE.

## Run it

```bash
cd MyGame
dotnet run --project MyGame.DevHost
```

Browse to the printed URL, click the tile, create a lobby, open a second browser (or incognito window) to join. Clicking **Start Game** runs your engine's `StartAsync` and transitions the lobby into gameplay.

## Next steps

- Add your game state fields to `MyGameGameState`, mutating them from inside `state.Execute(...)` blocks on the engine.
- Add command methods to `MyGameGameEngine` that return `Result` / `ValueResult<T>`.
- Render the in-game UI in `MyGameLobby.razor` by branching on `GameState.IsJoinable` (or a phase enum you add yourself).
- Run `dotnet test` to exercise your engine against a real state with logger mocks.

## Developer reference

Full end-to-end walkthrough — scaffolding, state, engine, Razor, DevHost, tests, shipping, advanced patterns, troubleshooting:

https://github.com/jcub1011/KnockBox/blob/main/docs/making-a-game-plugin.md

## Related packages

- [`KnockBox.Core`](https://www.nuget.org/packages/KnockBox.Core) — contract package every server plugin references.
- [`KnockBox.Core.Client`](https://www.nuget.org/packages/KnockBox.Core.Client) — WASM-safe client SDK referenced by the generated `.Client` UI.
- [`KnockBox.Platform`](https://www.nuget.org/packages/KnockBox.Platform) — hosting SDK; referenced by the generated DevHost.
- [`KnockBox.Plugins.Analyzer`](https://www.nuget.org/packages/KnockBox.Plugins.Analyzer) — build-time sandbox + client/server boundary analyzers (KB1001–KB1008).

## Uninstalling

```bash
dotnet new uninstall KnockBox.Templates
```

## Notes for template maintainers

The `routeIdentifier` symbol in `.template.config/template.json` uses `"replaces": "my-game"`, which substitutes **every literal occurrence** of `my-game` across the scaffold — including comments and XML-doc examples. Do not introduce illustrative `my-game` strings anywhere in the template that are meant to remain literal after scaffolding (they won't). Same caution applies to `MyGame`, which is substituted by the `-n` name parameter.

`MyGame/plugin.json` declares `clientAssets` with a **placeholder** all-zero `sha256` — the schema's cross-field rule requires a `clientAssets` entry whenever `clientAssembly` is set, but the real integrity hash is computed at build time into `games/MyGame/client/assets.sha256.json` (and a host falls back to hashing at serve time). Leave the placeholder as-is.

## License

MIT. See `LICENSE.txt` in the repository.
