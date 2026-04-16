# KnockBox SDK v2 Design

## Context

External developers want to build game plugins for KnockBox without cloning the repository. Today, KnockBox.Core is published as a NuGet package with contracts (IGameModule, AbstractGameEngine, AbstractGameState, etc.), but there's no way for an external developer to run and test their plugin without the full host.

This design introduces a development SDK that lets developers scaffold, build, and test game plugins with a `dotnet new` template and a lightweight dev host that provides the full lobby/session/multiplayer UI.

## Architecture

### Package Structure

Three NuGet packages:

| Package | Purpose | Depends On |
|---------|---------|------------|
| `KnockBox.Core` | Contracts, base classes, result types | (nothing) |
| `KnockBox.Platform` | Shared host runtime: lobby flow, session management, UI, plugin loading | `KnockBox.Core` |
| `KnockBox.Templates` | `dotnet new knockbox-game` project template | (install-time only) |

### Dependency Graph

```
KnockBox.Core           (existing NuGet - contracts)
       |
KnockBox.Platform       (NEW NuGet - shared host runtime)
       |
   +---+---+
   |       |
KnockBox  DevHost       (production host / developer's test host)
```

Both the production KnockBox host and the SDK dev host consume KnockBox.Platform. The production host adds admin features (dashboard, metrics, auth) on top.

## Developer Workflow

```bash
dotnet new install KnockBox.Templates
dotnet new knockbox-game -n TriviaBlitz --routeIdentifier trivia-blitz
cd TriviaBlitz
dotnet run --project TriviaBlitz.DevHost
# Browser opens → full lobby UI, create/join rooms, multiplayer testing
```

## KnockBox.Platform

### What Moves to Platform

**Services (all platform-generic):**
- LobbyService, LobbyCodeService, ProfanityFilter, RandomNumberService
- TickService, SessionServiceProvider, SessionTokenProvider, UserService
- Client storage services (session/local storage)
- Registration methods (RegisterRepositories, RegisterValidators, RegisterStateServices, RegisterLogic — minus admin services)
- `AllGamesEnabledService` — default no-op `IGameAvailabilityService` (all games always enabled)

**UI Components:**
- `PlatformApp.razor` — HTML shell document (from current `App.razor`)
- `PlatformRoutes.razor` — Router with `AdditionalAssemblies` for plugin route discovery, no auth wrapping
- `MainLayout.razor` — header, room code UI, transitions (branding via `IOptions<KnockBoxPlatformOptions>`)
- `Home.razor` — lobby create/join, game grid (titles configurable via options)
- `Error.razor`, `NotFound.razor`, `ReconnectModal.razor`

**Infrastructure:**
- `MapPluginStaticAssets` — dynamic wwwroot mounting for directory-loaded plugins
- Serilog configuration helpers
- HTTP pipeline setup (exception handlers, HTTPS, static assets, Blazor endpoints)

**What Stays in KnockBox Host:**
- All `Admin/*` (dashboard, metrics, log viewer, port middleware, cookie auth)
- `AdminMetricsService`, `AdminLogService`, `AdminCircuitTracker`
- File-backed `GameAvailabilityService` (overrides Platform's no-op default)
- Custom `App.razor` with `CascadingAuthenticationState`

### Project Structure

```
KnockBox.Platform/
├── KnockBox.Platform.csproj
├── KnockBoxPlatformOptions.cs
├── KnockBoxPlatformExtensions.cs
├── PluginDiscoveryMode.cs
├── AllGamesEnabledService.cs
├── Components/
│   ├── PlatformApp.razor
│   ├── PlatformRoutes.razor
│   ├── Layout/
│   │   ├── MainLayout.razor (+.cs, +.css)
│   │   └── ReconnectModal.razor
│   └── Pages/
│       ├── Home/ (Home.razor +.cs +.css)
│       ├── Error.razor
│       └── NotFound.razor
├── Services/
│   ├── Logic/
│   │   ├── Filtering/ (IProfanityFilter, ProfanityFilter)
│   │   └── Games/ (ILobbyService, LobbyService, ILobbyCodeService, LobbyCodeService, IGameAvailabilityService)
│   ├── State/ (TickService, SessionServiceProvider, SessionTokenProvider)
│   ├── Users/ (UserService)
│   └── ClientStorage/
├── Registrations/ (RegisterRepositories, RegisterValidators, RegisterStateServices, RegisterLogic)
├── Data/Statics/Profanities/English.txt (embedded resource)
└── wwwroot/app.css
```

### API Design

#### KnockBoxPlatformOptions

```csharp
public sealed class KnockBoxPlatformOptions
{
    public string AppTitle { get; set; } = "Knockbox";
    public string HomeHeroTitle { get; set; } = "Knockbox";
    public string HomePageTitle { get; set; } = "Knockbox Games";
    public PluginDiscoveryMode PluginDiscovery { get; set; } = PluginDiscoveryMode.Directory;
    public string PluginsPath { get; set; } = "games";
    internal List<IGameModule> ExplicitModules { get; } = [];
    internal List<Assembly> ExplicitAssemblies { get; } = [];
}

public enum PluginDiscoveryMode { Directory, Explicit }
```

#### Extension Methods

```csharp
// Fluent plugin registration (sets mode to Explicit automatically)
public static KnockBoxPlatformOptions AddGameModule<TModule>(
    this KnockBoxPlatformOptions options) where TModule : IGameModule, new();

// Service registration (builder phase)
public static WebApplicationBuilder AddKnockBoxPlatform(
    this WebApplicationBuilder builder,
    Action<KnockBoxPlatformOptions>? configure = null);

// Combined middleware + endpoints (dev host convenience)
public static WebApplication UseKnockBoxPlatform(this WebApplication app);

// Split API for production host (insert admin middleware between these)
public static WebApplication UseKnockBoxPlatformMiddleware(this WebApplication app);
public static WebApplication MapKnockBoxPlatformEndpoints(this WebApplication app);
public static WebApplication MapKnockBoxPlatformEndpoints<TRootComponent>(
    this WebApplication app) where TRootComponent : IComponent;
```

#### Plugin Discovery Modes

**Directory mode** (production default):
- `PluginLoader.LoadModules(pluginsPath)` scans `games/` directory
- Each plugin loaded into its own `PluginLoadContext` (ALC isolation)
- `MapPluginStaticAssets` mounts each plugin's wwwroot

**Explicit mode** (dev host):
- `AddGameModule<T>()` registers modules directly from project references
- No directory scanning, no ALC isolation needed
- Static assets served via standard RCL `_content/{AssemblyName}` convention
- Hot-reload works because the plugin is a direct project reference

### Branding Customization

Components inject `IOptions<KnockBoxPlatformOptions>` and use configurable values:

```razor
@inject IOptions<KnockBoxPlatformOptions> PlatformOptions
<span>@PlatformOptions.Value.AppTitle</span>
```

Production host keeps defaults. Dev host developers can customize:
```csharp
builder.AddKnockBoxPlatform(options =>
{
    options.AppTitle = "Party Night";
    options.AddGameModule<MyGameModule>();
});
```

### IGameAvailabilityService Strategy

Platform registers `AllGamesEnabledService` via `TryAddSingleton` (yields to explicit registration). Production host overrides with its file-backed `GameAvailabilityService` via `AddSingleton`. Dev host gets the no-op default — all games always enabled.

## Production Host Refactoring

After extraction, the production host's `Program.cs` becomes:

```csharp
builder.AddKnockBoxPlatform(options =>
{
    options.PluginsPath = builder.Configuration["Plugins:Path"] ?? "games";
});

// Admin-specific service overrides
builder.Services.AddSingleton<IGameAvailabilityService, GameAvailabilityService>();
builder.Services.AddSingleton<AdminMetricsService>();
// ... other admin services ...

app.UseKnockBoxPlatformMiddleware();
// Admin middleware insertion point
app.UseMiddleware<AdminPortMiddleware>(adminOptions.Port);
app.UseAuthentication();
app.UseAuthorization();
app.MapKnockBoxPlatformEndpoints<App>(); // custom App.razor with auth
app.MapRazorPages(); // admin login/logout
```

## Template: KnockBox.Templates

### Template Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-n` / `--name` | `MyGame` | Solution and project name |
| `--routeIdentifier` | `my-game` | URL segment for the game route |

### Scaffolded Structure

```
{Name}/
├── {Name}.slnx
├── {Name}/                              (Razor Class Library - plugin)
│   ├── {Name}.csproj
│   ├── {Name}Module.cs                  (IGameModule implementation)
│   ├── Services/
│   │   └── {Name}GameEngine.cs          (AbstractGameEngine subclass)
│   ├── State/
│   │   └── {Name}GameState.cs           (AbstractGameState subclass)
│   ├── Pages/
│   │   └── {Name}Lobby.razor            (@page "/room/{route}/{ObfuscatedRoomCode}")
│   ├── Components/
│   │   └── {Name}Tile.razor             (home page tile)
│   ├── _Imports.razor
│   └── wwwroot/
├── {Name}.DevHost/                      (Blazor Server - dev runner)
│   ├── {Name}.DevHost.csproj
│   ├── Program.cs
│   ├── appsettings.json
│   └── appsettings.Development.json
└── {Name}.Tests/                        (MSTest + Moq - game tests)
    ├── {Name}.Tests.csproj
    └── {Name}GameEngineTests.cs         (starter test)
```

### Scaffolded Dev Host Program.cs

```csharp
using KnockBox.Platform;
using MyGame;

var builder = WebApplication.CreateBuilder(args);

builder.AddKnockBoxPlatform(options =>
{
    options.AddGameModule<MyGameModule>();
});

var app = builder.Build();
app.UseKnockBoxPlatform();
app.Run();
```

### Scaffolded Game Module

```csharp
public class MyGameModule : IGameModule
{
    public string Name => "My Game";
    public string Description => "A party game.";
    public string RouteIdentifier => "my-game";

    public void RegisterServices(IServiceCollection services)
        => services.AddGameEngine<MyGameGameEngine>(RouteIdentifier);

    public RenderFragment GetButtonContent() => builder =>
    {
        builder.OpenComponent<MyGameTile>(0);
        builder.CloseComponent();
    };
}
```

### Scaffolded Engine (Minimal Working Skeleton)

```csharp
public class MyGameGameEngine(
    ILogger<MyGameGameEngine> logger,
    ILogger<MyGameGameState> stateLogger) : AbstractGameEngine(2, 8)
{
    public override Task<ValueResult<AbstractGameState>> CreateStateAsync(
        User host, CancellationToken ct = default)
    {
        var state = new MyGameGameState(host, stateLogger);
        state.UpdateJoinableStatus(true);
        return Task.FromResult<ValueResult<AbstractGameState>>(state);
    }

    public override Task<Result> StartAsync(
        User host, AbstractGameState state, CancellationToken ct = default)
    {
        // TODO: Implement game start logic
        return Task.FromResult(Result.Success);
    }
}
```

### Scaffolded Starter Test

```csharp
[TestClass]
public class MyGameGameEngineTests
{
    private MyGameGameEngine _engine = null!;

    [TestInitialize]
    public void Setup()
    {
        _engine = new MyGameGameEngine(
            Mock.Of<ILogger<MyGameGameEngine>>(),
            Mock.Of<ILogger<MyGameGameState>>());
    }

    [TestMethod]
    public async Task CreateStateAsync_ReturnsState()
    {
        var host = new User("Host", Guid.CreateVersion7().ToString());
        var result = await _engine.CreateStateAsync(host);
        Assert.IsTrue(result.IsSuccess);
    }
}
```

## CSS Strategy

Platform ships `app.css` in its `wwwroot/`. Scoped CSS for platform components ships as `KnockBox.Platform.styles.css` via standard RCL bundling. The `PlatformApp.razor` references both automatically. The production host's custom `App.razor` must include:
```html
<link rel="stylesheet" href="_content/KnockBox.Platform/app.css" />
<link rel="stylesheet" href="_content/KnockBox.Platform/KnockBox.Platform.styles.css" />
<link rel="stylesheet" href="KnockBox.styles.css" />
```

## NuGet Packaging

**KnockBox.Platform.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PackageId>KnockBox.Platform</PackageId>
    <Description>Runtime host for KnockBox party game plugins.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\KnockBox.Core\KnockBox.Core.csproj" />
    <!-- Serilog, etc. -->
  </ItemGroup>
</Project>
```

**KnockBox.Templates.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageType>Template</PackageType>
    <PackageId>KnockBox.Templates</PackageId>
    <IncludeContentInPack>true</IncludeContentInPack>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <ContentTargetFolders>content</ContentTargetFolders>
  </PropertyGroup>
  <ItemGroup>
    <Content Include="templates\**\*" Exclude="templates\**\bin\**;templates\**\obj\**" />
  </ItemGroup>
</Project>
```

Release workflow (`release.yml`) needs updated to pack and push both new NuGet packages alongside the existing `KnockBox.Core` package.

## Verification Plan

1. **Build**: `dotnet build KnockBox.slnx` — all projects compile
2. **Tests**: `dotnet test KnockBox.slnx` — all existing tests pass
3. **Production host**: `dotnet run --project KnockBox/KnockBox.csproj` — full app works as before (lobby, join, play games, admin dashboard)
4. **Template install**: `dotnet new install ./KnockBox.Templates` — template appears in `dotnet new list`
5. **Template scaffold**: `dotnet new knockbox-game -n TestGame --routeIdentifier test-game` — creates three-project solution
6. **Dev host run**: `cd TestGame && dotnet run --project TestGame.DevHost` — lightweight host starts, home page shows TestGame tile, can create lobby and join from multiple tabs
7. **Template tests**: `cd TestGame && dotnet test` — starter test passes
8. **Docker**: `docker compose up --build` — production host works in container

## Known Considerations

1. **Scoped CSS across NuGet boundary**: Platform components' scoped CSS ships as `_content/KnockBox.Platform/KnockBox.Platform.styles.css`. The `PlatformApp.razor` references this automatically. The production host's custom `App.razor` must include it explicitly.

2. **MapPluginStaticAssets in explicit mode**: When using `PluginDiscoveryMode.Explicit`, no `games/` directory exists. Plugin static assets are served by standard RCL `_content/{AssemblyName}` convention via `MapStaticAssets()`. `MapPluginStaticAssets` is skipped.

3. **PlatformRoutes vs production Routes**: The platform's `PlatformRoutes.razor` has no auth wrapping. The production host provides its own `Routes.razor` with `CascadingAuthenticationState` + `AuthorizeRouteView`, embedded in its custom `App.razor`.

4. **IGameAvailabilityService**: Platform registers default via `TryAddSingleton`. Production host overrides via `AddSingleton`. No interface changes needed.
