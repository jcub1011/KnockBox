# Plugin Architecture Refactor Implementation Plan

## Objective

Refactor the `KnockBox` application to use a true plugin architecture. The main application will no longer have build-time dependencies (`ProjectReference`) on the individual games. Games will be converted to Razor Class Libraries (RCLs) that contain their own UI, logic, and state. The main application will dynamically discover and load these games at startup.

---

## Phase 1: Define Core Contracts

### Requirements

1. Define a standard interface `IGameModule` in `KnockBox.Core` that all games will implement.
2. The interface must provide metadata about the game (Name, Description, Route string) replacing the need for the central `GameType` enum.
3. The interface must provide a method to register game-specific services into the DI container.
4. Update `LobbyRegistration` to store the string `RouteIdentifier` and `GameName` instead of the `GameType` enum.
5. Update `ILobbyService` to use string identifiers and keyed services to resolve `AbstractGameEngine`.

### Acceptance Criteria

- [ ] `IGameModule` interface exists in `KnockBox.Core.Plugins` (or similar namespace).
- [ ] The interface contains properties: `string Name`, `string Description`, `string RouteIdentifier`.
- [ ] The interface contains method: `void RegisterServices(IServiceCollection services)`.
- [ ] `KnockBox\Services\Logic\Games\Shared\ILobbyService.cs` updates `LobbyRegistration` to use strings for `GameName` and `RouteIdentifier` instead of `GameType`.
- [ ] `LobbyService.CreateLobbyAsync` uses `IServiceProvider.GetKeyedService<AbstractGameEngine>(routeIdentifier)` instead of a hardcoded switch statement.
- [ ] The existing `GameType` enum (`KnockBox\Services\Navigation\Games\GameTypes.cs`) is deprecated.

---

## Phase 2: Convert Games to Razor Class Libraries & Relocate UI

### Requirements

1. Update every game project file (`.csproj`) to be a Razor Class Library.
2. Move the Blazor UI components (`.razor`, `.razor.cs`, `.razor.css`) and static assets from the main `KnockBox` project into the respective game projects.
3. Implement the `IGameModule` interface in each game project.
4. Move game-specific service registrations into the game's `IGameModule` implementation. Register the `AbstractGameEngine` using Keyed Services.

### Acceptance Criteria

- [ ] The following `.csproj` files use `<Project Sdk="Microsoft.NET.Sdk.Razor">` and reference ASP.NET Core (`<FrameworkReference Include="Microsoft.AspNetCore.App" />`):
  - `KnockBox.CardCounter\KnockBox.CardCounter.csproj`
  - `KnockBox.ConsultTheCard\KnockBox.ConsultTheCard.csproj`
  - `KnockBox.DiceSimulator\KnockBox.DiceSimulator.csproj`
  - `KnockBox.DrawnToDress\KnockBox.DrawnToDress.csproj`
  - `KnockBox.Operator\KnockBox.Operator.csproj`
- [ ] All files under `KnockBox\Components\Pages\Games\{GameName}\` are moved to `{GameProject}\Pages\`.
- [ ] A new `_Imports.razor` is added to each game project to ensure the moved components still compile with global usings.
- [ ] The `@page` directives in the relocated `.razor` files exactly match the `RouteIdentifier` defined in their respective `IGameModule`.
- [ ] Namespaces in the moved `.razor` and `.razor.cs` files are updated to match the game project (e.g., `KnockBox.CardCounter.Pages`).
- [ ] Any static assets (e.g., CSS, images) required by the games are moved to the `{GameProject}\wwwroot\` directory.
- [ ] References to static assets in the `.razor` files are updated to use the RCL convention: `_content/{ProjectName}/{AssetPath}`.
- [ ] Each game project contains a class (e.g., `CardCounterModule.cs`) implementing `IGameModule`.
- [ ] Game-specific registrations (e.g., `services.AddKeyedSingleton<AbstractGameEngine, CardCounterGameEngine>("card-counter");`) are moved from `KnockBox\Services\Registrations\Logic\LogicRegistrations.cs` into the respective `IGameModule.RegisterServices` methods.

---

## Phase 3: Dynamic Discovery & Routing in KnockBox

### Requirements

1. Remove all direct project references to the games from the main `KnockBox.csproj`.
2. Implement a mechanism to dynamically discover assemblies implementing `IGameModule` from a designated `Plugins` folder.
3. Call `RegisterServices` on each discovered module during startup (`Program.cs`).
4. Update the Blazor `<Router>` to include the discovered game assemblies so pages can be routed.

### Acceptance Criteria

- [ ] `KnockBox\KnockBox.csproj` no longer contains `<ProjectReference>` entries for the 5 game projects.
- [ ] `Program.cs` contains logic to scan a `Plugins` directory (e.g., `Path.Combine(AppContext.BaseDirectory, "Plugins")`), load assemblies, find `IGameModule` implementations, and register them.
- [ ] A singleton service or static collection of discovered `Assembly` and `IGameModule` objects is created so the router and DI container can access them.
- [ ] `KnockBox\Components\Routes.razor` binds its `AdditionalAssemblies` parameter to the collection of discovered game assemblies.

---

## Phase 4: Dynamic UI Navigation

### Requirements

1. The Home page must list available games dynamically based on the discovered `IGameModule` instances.
2. The Main Layout header must display the correct game name dynamically.

### Acceptance Criteria

- [ ] `KnockBox\Components\Pages\Home\Home.razor` and its code-behind no longer hardcode the game list. They inject the collection of discovered `IGameModule`s and iterate over them to render the game selection buttons.
- [ ] `CreateLobby` method uses the string `RouteIdentifier` from `IGameModule` instead of `GameType`.
- [ ] `KnockBox\Components\Layout\MainLayout.razor` determines the current game name for the header using `currentSession.LobbyRegistration.GameName`.

---

## Phase 5: Build & Deployment Pipeline Integration

### Requirements

1. Ensure that when the solution is built, the compiled DLLs of the game projects are copied into the `Plugins` directory of the `KnockBox` project so they can be discovered at runtime.
2. Ensure Docker containers are built correctly given the removal of the project references.

### Acceptance Criteria

- [ ] Game `.csproj` files contain a post-build event or target that copies `$(TargetPath)` and `$(TargetDir)\*.dll` to `..\KnockBox\bin\$(Configuration)\net10.0\Plugins\` (or the appropriate runtime directory).
- [ ] Building the solution locally and running `KnockBox` successfully loads all games.
- [ ] The `Dockerfile` (in `KnockBox` and root) is updated to use a multi-stage build that builds the `.sln` (or individual game `.csproj` files) and copies their output to the final container's `Plugins` directory before running the main application.
