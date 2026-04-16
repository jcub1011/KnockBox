# KnockBox.Platform

**Hosting SDK for KnockBox game plugins.**

KnockBox is a Blazor Server host that loads party games as runtime-discovered plugins. This package provides the bootstrap surface a host (dev or production) needs to discover plugins, wire their services into DI, serve their static assets, and render them through the platform's Home / Error / NotFound chrome.

> **Who references this?** Your host project — a local DevHost for development, or a production host binary.
> **Who does NOT reference this?** Game plugins. A plugin referencing `KnockBox.Platform` breaks runtime `AssemblyLoadContext` isolation and will cause type-identity failures at plugin load. Plugins reference only [`KnockBox.Core`](https://www.nuget.org/packages/KnockBox.Core).

## Minimal host

```csharp
using KnockBox.Platform;

var builder = WebApplication.CreateBuilder(args);

// Explicit-mode: register each game module directly. Ideal for DevHosts.
builder.AddKnockBoxPlatform(options =>
{
    options.AddGameModule<MyGameModule>();
    // options.AddGameModule<AnotherGameModule>();
});

var app = builder.Build();
app.UseKnockBoxPlatform();
app.Run();
```

For a production host that loads plugins from a directory:

```csharp
builder.AddKnockBoxPlatform(options =>
{
    options.PluginDiscovery = PluginDiscoveryMode.Directory;
    options.PluginsPath = "games";            // relative to AppContext.BaseDirectory
    options.AppTitle = "My KnockBox Party";
    options.HomeHeroTitle = "My KnockBox Party";
});
```

Each plugin subfolder under `PluginsPath` (`games/MyGame/`, `games/AnotherGame/`, …) is loaded into its own `AssemblyLoadContext`. The loader reflects each assembly for `IGameModule` implementations, activates them, and calls `RegisterServices`.

## Plugin discovery modes

| Mode | When to use | Behaviour |
| --- | --- | --- |
| `PluginDiscoveryMode.Directory` *(default)* | Production hosts, or any scenario where plugins ship as loose DLL folders. | Scans `PluginsPath` for subfolders and loads each into its own ALC. |
| `PluginDiscoveryMode.Explicit` | Dev hosts, tests, and any scenario with direct `ProjectReference`s to the plugin. | Uses modules registered via `options.AddGameModule<T>()`. No directory scan, no ALC isolation. `AddGameModule<T>()` flips the mode automatically. |

## Configuration options

| Option | Purpose |
| --- | --- |
| `AppTitle` | Header title shown once a game session is active. |
| `HomeHeroTitle` | Large hero title on the home page. |
| `HomePageTitle` | Browser tab / `<title>` on the home page. |
| `PluginDiscovery` | `Directory` (default) or `Explicit`. |
| `PluginsPath` | Relative or absolute path to the plugin folder root. Default: `games`. |

## Extension points

- **`AddKnockBoxPlatform(configure)`** — registers all platform services, performs plugin discovery, configures Razor components.
- **`UseKnockBoxPlatform()`** — convenience wrapper around `UseKnockBoxPlatformMiddleware()` + `MapKnockBoxPlatformEndpoints()`. Good for dev hosts with no host-specific middleware.
- **`UseKnockBoxPlatformMiddleware()`** — Serilog request logging, exception handler, HSTS, status-code pages, HTTPS redirect, anti-forgery. Call this before any host-specific middleware (auth, rate limiting, admin port filtering).
- **`MapKnockBoxPlatformEndpoints()` / `MapKnockBoxPlatformEndpoints<TRootComponent>()`** — maps static assets, per-plugin `/_content/{PluginName}` mounts, and Blazor endpoints. Use the generic overload if your host supplies its own `App.razor`.
- **`IGameAvailabilityService`** — override with a host-supplied implementation (e.g., file-backed or admin-toggled) to gate individual games. The platform registers a default "all enabled" fallback via `TryAddSingleton`.

## Package contents at a glance

- **Bootstrap extensions:** `KnockBoxPlatformExtensions`, `KnockBoxPlatformOptions`, `KnockBoxPlatformOptionsExtensions`.
- **Platform pages:** `Home`, `Error`, `NotFound`, `MainLayout`, `ReconnectModal` (served at `/`, `/error`, `/not-found`).
- **Lobby management:** `LobbyService`, `LobbyCodeService` (6-character profanity-filtered codes).
- **Session infrastructure:** `SessionServiceProvider`, `SessionTokenProvider`, `UserService`, `TickService`.
- **Client storage:** `ILocalStorageService`, `ISessionStorageService` (JS-interop browser storage wrappers).
- **Profanity filter:** Aho-Corasick automaton over an embedded English word list; used by `LobbyCodeService`.

## Developer reference

Full end-to-end walkthrough for building a plugin (from scaffolding through shipping):

https://github.com/jcub1011/KnockBox/blob/main/docs/making-a-game-plugin.md

## Related packages

- [`KnockBox.Core`](https://www.nuget.org/packages/KnockBox.Core) — the contract package plugins reference.
- [`KnockBox.Templates`](https://www.nuget.org/packages/KnockBox.Templates) — `dotnet new` scaffolding for a plugin + dev host + tests.

## License

MIT. See `LICENSE.txt` in the repository.
