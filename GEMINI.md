# GEMINI.md - KnockBox Project Context

This file serves as the primary instructional context for Gemini CLI when working on the KnockBox project.

## Project Overview
KnockBox is a Blazor Server application that hosts party games as **runtime-loaded plugins**. The architecture is designed for extreme decoupling: the host has no compile-time knowledge of specific games.

- **Host (`KnockBox/`)**: Manages routing, DI bootstrapping, and plugin discovery.
- **Core (`KnockBox.Core/`)**: Contains shared abstractions (`IGameModule`, `AbstractGameState`, `AbstractGameEngine`), session management, and thread-safety utilities.
- **Plugins (`KnockBox.GameName/`)**: Standalone Razor Class Libraries (RCLs) that implement the game logic and UI.
- **Tests**: MSTest projects for Core, Host, and each Plugin.

## Technical Architecture
- **Plugin System**: Games are discovered at runtime in the `games/` directory. Each plugin is loaded into its own `PluginLoadContext` (AssemblyLoadContext) to isolate dependencies.
- **State Management**: Per-lobby state lives in subclasses of `AbstractGameState`. Mutations **MUST** be wrapped in `state.Execute(() => ...)` or `state.ExecuteAsync(...)` to ensure thread safety (via `SemaphoreSlim`) and to trigger UI re-renders.
- **Communication**: Components subscribe to `StateChangedEventManager` on the state object. Subscriptions **MUST** be disposed in the component's `Dispose()` method.
- **Result Pattern**: Uses `Result` and `ValueResult<T>` types for fallible operations instead of exceptions for control flow.
- **Routing**: Game pages follow the pattern `/room/{route-identifier}/{ObfuscatedRoomCode}`.

## Building and Running
| Task | Command |
| --- | --- |
| Build Solution | `dotnet build KnockBox.slnx` |
| Build & Stage Plugins | `dotnet build KnockBox/KnockBox.csproj` |
| Run Locally | `dotnet run --project KnockBox/KnockBox.csproj` |
| Run Tests | `dotnet test KnockBox.slnx` |
| Docker | `docker compose up --build` |

> **Note**: Building the host project transitively builds all plugins and stages them to the `bin/.../games/` folder via `Directory.Plugin.targets`.

## Development Conventions

### Creating a New Game Plugin
1. **Project**: Create a Razor Class Library (`KnockBox.Name`).
2. **References**: Reference `KnockBox.Core` only. **Do not** reference the Host or other games.
3. **MSBuild**: Include `<Import Project="..\Directory.Plugin.targets" />` in the `.csproj`.
4. **State**: Subclass `AbstractGameState` for per-room data.
5. **Engine**: Subclass `AbstractGameEngine` (Singleton) for logic. Register via `services.AddGameEngine<TEngine>(RouteIdentifier)`.
6. **Module**: Implement `IGameModule` to define the plugin's metadata and DI registrations.
7. **UI**: Inherit from `DisposableComponent`. Use `@page "/room/{route-identifier}/{ObfuscatedRoomCode}"`.
8. **Host Reference**: In `KnockBox.csproj`, add a reference with `ReferenceOutputAssembly="false" Private="false"` to ensure transitive builds without compile-time coupling.

### Coding Standards
- **Thread Safety**: Never mutate state fields directly from outside the state's `Execute` lock.
- **Disposal**: Always use `DisposableComponent` for game pages and dispose of all event subscriptions.
- **Async**: Prefer `ValueTask` for high-frequency notifications and state operations.
- **Naming**: Projects follow `KnockBox.{ModuleName}` and `KnockBox.{ModuleName}Tests`.
- **Target Framework**: .NET 10.0.
- **C# Version**: C# 13.

## Key Files & Locations
- `KnockBox/Specs/`: Detailed architectural and refactor specifications.
- `KnockBox/Program.cs`: Composition root and plugin loading logic.
- `KnockBox.Core/Plugins/`: `PluginLoader` and `IGameModule` definitions.
- `Directory.Plugin.targets`: The build-time "glue" that stages plugins for discovery.
