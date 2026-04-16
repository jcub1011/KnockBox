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
├── MyGame/                      # the plugin assembly (Razor Class Library)
│   ├── MyGame.csproj            # references KnockBox.Core only
│   ├── MyGameModule.cs          # IGameModule entry point
│   ├── MyGameGameState.cs       # per-room state : AbstractGameState
│   ├── MyGameGameEngine.cs      # stateless singleton : AbstractGameEngine
│   ├── Components/
│   │   └── MyGameTile.razor     # home-page tile content
│   ├── Pages/
│   │   └── MyGameLobby.razor    # @page "/room/my-game/{ObfuscatedRoomCode}"
│   └── _Imports.razor
├── MyGame.DevHost/              # local F5 harness (ASP.NET Core Web)
│   ├── MyGame.DevHost.csproj    # references KnockBox.Platform + the plugin
│   └── Program.cs               # AddKnockBoxPlatform(...) + AddGameModule<T>()
└── MyGame.Tests/                # MSTest + Moq test project
    ├── MyGame.Tests.csproj
    └── MyGameGameEngineTests.cs
```

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

- [`KnockBox.Core`](https://www.nuget.org/packages/KnockBox.Core) — contract package every plugin references.
- [`KnockBox.Platform`](https://www.nuget.org/packages/KnockBox.Platform) — hosting SDK; referenced by the generated DevHost.

## Uninstalling

```bash
dotnet new uninstall KnockBox.Templates
```

## License

MIT. See `LICENSE.txt` in the repository.
