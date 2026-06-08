using System.Reflection;
using System.Runtime.Loader;
using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Drawing;
using KnockBox.Core.Services.Navigation;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Games;
using KnockBox.Platform.Plugins;
using KnockBox.Platform.Services.Storage.IndexedDb;
using KnockBox.Platform.Storage;
using KnockBox.Services.Drawing;
using KnockBox.Services.Navigation;
using KnockBox.Services.Registrations.Logic;
using KnockBox.Services.Registrations.Repositories;
using KnockBox.Services.Registrations.States;
using KnockBox.Services.Registrations.Validators;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Extensions.Logging;

namespace KnockBox.Platform;

/// <summary>
/// Extension methods for configuring KnockBox Platform services and middleware.
/// </summary>
public static class KnockBoxPlatformExtensions
{
    /// <summary>
    /// Registers all KnockBox Platform services, performs plugin discovery, and
    /// configures the Blazor component pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Registration order matters for host overrides.</b> The Platform
    /// registers default implementations of extensible services (currently
    /// <c>IGameAvailabilityService</c>) via <c>TryAddSingleton</c>. A host that
    /// wants to replace a default MUST call <c>builder.Services.AddSingleton&lt;...&gt;</c>
    /// BEFORE calling <c>AddKnockBoxPlatform</c>; registrations made afterwards
    /// will not win over the default instance Platform already installed.
    /// </para>
    /// <para>
    /// If the default <c>AllGamesEnabledService</c> ends up in DI (no prior
    /// registration was found), this method emits an Information log line so a
    /// misordered override surfaces at startup instead of appearing as silent
    /// "every game is enabled" behavior in production. The log is gated on
    /// <see cref="PluginDiscoveryMode.Directory"/> — DevHosts run in
    /// <see cref="PluginDiscoveryMode.Explicit"/> and are expected to rely on
    /// the default, so they stay quiet.
    /// </para>
    /// </remarks>
    public static WebApplicationBuilder AddKnockBoxPlatform(
        this WebApplicationBuilder builder,
        Action<KnockBoxPlatformOptions>? configure = null)
    {
        var options = new KnockBoxPlatformOptions();
        configure?.Invoke(options);

        // Guard against a silent misconfiguration: AddGameModule<T>() appends
        // to ExplicitModules, but callers must also opt into Explicit mode
        // themselves. Leaving PluginDiscovery at its default (Directory) while
        // registering explicit modules is the footgun this guard catches.
        if (options.PluginDiscovery == PluginDiscoveryMode.Directory
            && options.ExplicitModules.Count > 0)
        {
            throw new InvalidOperationException(
                $"KnockBoxPlatformOptions has {options.ExplicitModules.Count} explicit " +
                "module(s) registered but PluginDiscovery is set to Directory. " +
                "Either set PluginDiscovery = PluginDiscoveryMode.Explicit (to " +
                "use the registered modules) or remove the AddGameModule<T>() " +
                "call(s) so directory scanning is used.");
        }

        // Register the fully-populated options instance directly. Using
        // OptionsWrapper preserves IOptions<T> resolvability for downstream
        // consumers (e.g. MapKnockBoxPlatformEndpoints) without the
        // field-by-field copy that risked double-adding ExplicitModules.
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IOptions<KnockBoxPlatformOptions>>(
            new OptionsWrapper<KnockBoxPlatformOptions>(options));

        // MessagePack hub protocol — smaller wire format than the default JSON
        // protocol, faster to (de)serialize. Transparent to handlers and to
        // StateChangedEventManager subscribers.
        builder.Services.AddSignalR().AddMessagePackProtocol();

        builder.Services.AddRazorComponents()
            .AddInteractiveWebAssemblyComponents()
            .AddInteractiveServerComponents(o =>
            {
                // KnockBox owns its own reconnect grace period via
                // ISessionServiceProvider (1-minute eviction keyed on user-id).
                // The default 100 retained disconnected circuits is mostly
                // redundant on top of that and holds full render trees in
                // memory; lower it and align the retention window with the
                // session grace period.
                o.DisconnectedCircuitMaxRetained = 10;
                o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(1);
                // Lossy under backpressure is fine for a turn-based game UI;
                // 4 batches is enough headroom for normal tick-to-render lag
                // without retaining 10 batches per circuit.
                o.MaxBufferedUnacknowledgedRenderBatches = 4;
            })
            // MaximumReceiveMessageSize is modestly bumped from the 32 KB
            // Blazor default so the IndexedDb blob transport
            // (IJSStreamReference / DotNetStreamReference) can frame 64 KB
            // binary chunks with envelope headroom. Kept conservative so
            // per-message memory exposure stays bounded if anything else
            // pumps large payloads through the hub.
            .AddHubOptions(o => o.MaximumReceiveMessageSize = 64 * 1024);

        // Cross-circuit registry for IIndexedDbBlob.PublishForSharingAsync —
        // singleton so the /blob-share/{token} HTTP endpoint can resolve a
        // token published by any circuit. Entries hold a fetcher closure
        // capturing the originating circuit's blob; the host streams bytes
        // through the closure without ever persisting them.
        builder.Services.AddSingleton<BlobShareRegistry>();
        // Process-wide RAM cache that fronts the share endpoint so the
        // second+ fetcher of a token doesn't re-traverse SignalR against
        // the originating circuit. Bounded by SizeLimitBytes (LRU) and
        // wired into BlobShareRegistry's removal paths so revoked tokens
        // don't leave stale copies behind. See BlobShareByteCache for
        // policy rationale.
        builder.Services.AddSingleton<BlobShareByteCache>();

        // Per-circuit gateway to the browser's IndexedDB. Scoped so the cached
        // JS module reference stays bound to one Blazor circuit; the impl
        // disposes that reference when the scope ends.
        builder.Services.AddScoped<IIndexedDbService, IndexedDbService>();

        // ── WASM realtime transport (GameHub) ────────────────────────────────
        // Per-lobby connection bookkeeping, the single per-lobby projection
        // subscriber/fan-out, and the route→client-DLL asset resolver. All
        // singletons: the hub is transient per SignalR's model but these back it.
        builder.Services.AddSingleton<Hubs.GameConnectionRegistry>();
        builder.Services.AddSingleton<Hubs.GameViewCoordinator>();
        builder.Services.AddSingleton<Plugins.IPluginClientAssetService, Plugins.PluginClientAssetService>();

        // Core service registrations
        builder.Services.RegisterRepositories();
        builder.Services.RegisterValidators();
        builder.Services.RegisterStateServices();

        // Default IGameAvailabilityService — yields to an explicit registration
        // made by the host (e.g. the production host's file-backed service).
        // Hosts that need to override MUST register their implementation on
        // the service collection BEFORE calling AddKnockBoxPlatform; TryAdd
        // is order-sensitive.
        var hostAlreadyRegisteredAvailability = builder.Services.Any(
            d => d.ServiceType == typeof(IGameAvailabilityService));
        builder.Services.TryAddSingleton<IGameAvailabilityService, AllGamesEnabledService>();

        // Default IStoragePathService — yields to a host-registered impl the
        // same way as IGameAvailabilityService. Required by the per-plugin
        // IPluginContext factory (see LogicRegistrations) for
        // GetPluginDataDirectory; a host that doesn't register its own must
        // still have SOME default available.
        builder.Services.TryAddSingleton<IStoragePathService, DefaultStoragePathService>();

        // Single bootstrap logger factory used for both plugin discovery and
        // registration-time logging. Console-only here; the host's configured
        // Serilog pipeline takes over once DI is built.
        var bootstrapSerilog = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();
        using var bootstrapLoggerFactory = new SerilogLoggerFactory(bootstrapSerilog, dispose: true);

        if (!hostAlreadyRegisteredAvailability
            && options.PluginDiscovery == PluginDiscoveryMode.Directory)
        {
            // Visible signal so a production host that meant to override but
            // registered after AddKnockBoxPlatform sees at startup that the
            // default won. Explicit-mode (DevHost) callers are expected to
            // rely on the default, so we stay silent for them.
            bootstrapLoggerFactory
                .CreateLogger(typeof(KnockBoxPlatformExtensions).FullName!)
                .LogInformation(
                    "No IGameAvailabilityService registered before AddKnockBoxPlatform; " +
                    "using the default AllGamesEnabledService (every game enabled).");
        }

        // Plugin discovery
        PluginLoadResult pluginLoadResult;

        if (options.PluginDiscovery == PluginDiscoveryMode.Explicit)
        {
            // Explicit mode (DevHost) has no on-disk plugin.json; the module's
            // own Manifest property is authoritative and used directly.
            var explicitPlugins = options.ExplicitModules
                .Select(m => new LoadedPlugin(
                    Module: m,
                    Manifest: m.Manifest,
                    Assembly: m.GetType().Assembly,
                    LoadContext: AssemblyLoadContext.GetLoadContext(m.GetType().Assembly)
                                 ?? AssemblyLoadContext.Default))
                .ToList();

            pluginLoadResult = new PluginLoadResult(explicitPlugins, options.ExplicitAssemblies);
        }
        else
        {
            var pluginLogger = bootstrapLoggerFactory.CreateLogger<PluginLoader>();
            var loader = new PluginLoader(pluginLogger);

            // Combine library and game roots into a single LoadModules call so the
            // loader's library-first ordering applies across roots. Each plugin's
            // manifest still authoritatively decides whether it's a game or library —
            // the root folder is only a convention to keep first-party staging tidy.
            // Library paths are listed first to make the ordering visible to a reader
            // skimming logs, even though the loader re-sorts internally.
            var allRoots = new List<string>(options.LibrariesPaths.Count + options.PluginsPaths.Count);
            foreach (var rawPath in options.LibrariesPaths)
                allRoots.Add(ResolvePluginsPath(rawPath));
            foreach (var rawPath in options.PluginsPaths)
                allRoots.Add(ResolvePluginsPath(rawPath));

            pluginLoadResult = loader.LoadModules(allRoots);
        }

        // Navigation + drawing services. Registered BEFORE RegisterLogic so that
        // the plugin-registration denylist snapshot (captured at the top of
        // RegisterLogic) includes these — otherwise a plugin could shadow
        // INavigationService or ISvgClipboardService.
        builder.Services.AddScoped<INavigationService, NavigationService>();
        builder.Services.AddSingleton<ISvgClipboardService, SvgClipboardService>();

        // Plugin HTTP dispatcher singleton — backs the `/api/plugins/...`
        // endpoint mapped by MapKnockBoxPlatformEndpoints. Resolves engines
        // via keyed DI and rooms via ILobbyService.TryGetByUri.
        builder.Services.AddSingleton<PluginHttpDispatcher>();

        // Logic registrations (platform version — no admin services).
        // `LogicRegistrations` is static, so we can't use the generic overload —
        // typeof(...).FullName keeps the logger category in sync with renames
        // without needing a hardcoded string literal.
        var registrationLogger = bootstrapLoggerFactory
            .CreateLogger(typeof(LogicRegistrations).FullName!);
        builder.Services.RegisterLogic(pluginLoadResult, registrationLogger);

        return builder;
    }

    /// <summary>
    /// Convenience method that calls <see cref="UseKnockBoxPlatformMiddleware"/>
    /// followed by <see cref="MapKnockBoxPlatformEndpoints"/> with the default
    /// <c>PlatformApp</c> root component. Suitable for dev hosts that don't need
    /// to insert admin middleware.
    /// </summary>
    public static WebApplication UseKnockBoxPlatform(this WebApplication app)
    {
        app.UseKnockBoxPlatformMiddleware();
        app.MapKnockBoxPlatformEndpoints();
        return app;
    }

    /// <summary>
    /// Configures shared HTTP middleware: exception handler, HSTS, status code
    /// pages, HTTPS redirection, anti-forgery, and Serilog request logging.
    /// Call this before inserting any host-specific middleware (auth, admin port
    /// filtering, etc.).
    /// </summary>
    public static WebApplication UseKnockBoxPlatformMiddleware(this WebApplication app)
    {
        app.UseSerilogRequestLogging();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

        // Docker deployments without SSL termination or specific reverse-proxy
        // setups may need to skip the HTTPS redirect to avoid infinite loops.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KNOCKBOX_SKIP_HTTPS_REDIRECT")))
        {
            app.UseHttpsRedirection();
        }

        app.UseAntiforgery();

        return app;
    }

    /// <summary>
    /// Maps static assets, plugin static assets (in directory mode), and Blazor
    /// endpoints using the built-in <c>PlatformApp</c> root component.
    /// </summary>
    public static WebApplication MapKnockBoxPlatformEndpoints(this WebApplication app)
    {
        return app.MapKnockBoxPlatformEndpoints<Components.PlatformApp>();
    }

    /// <summary>
    /// Maps static assets, plugin static assets (in directory mode), and Blazor
    /// endpoints using the specified root component. Use this overload when the
    /// host provides its own <c>App.razor</c>.
    /// </summary>
    public static WebApplication MapKnockBoxPlatformEndpoints<TRootComponent>(
        this WebApplication app,
        params Assembly[] additionalClientAssemblies) where TRootComponent : IComponent
    {
        app.MapStaticAssets();

        var platformOptions = app.Services.GetRequiredService<IOptions<KnockBoxPlatformOptions>>().Value;

        if (platformOptions.PluginDiscovery == PluginDiscoveryMode.Directory)
        {
            foreach (var rawPath in platformOptions.PluginsPaths)
            {
                MapPluginStaticAssets(app, ResolvePluginsPath(rawPath));
            }
        }

        // The Platform assembly contains routable pages (Home, Error, NotFound).
        // The Core assembly contains shared Razor components (e.g. SvgDrawingEngine,
        // SvgDrawingToolbar) whose @onclick handlers require interactive registration
        // — without listing Core here, components from it render as static SSR even
        // when their parent is interactive, so click handlers never wire up.
        // Game plugin assemblies contain game-specific pages. All must be registered
        // so ASP.NET Core endpoint routing discovers them alongside the root
        // component's own assembly AND so Blazor wires interactive event handlers
        // for components defined there.
        var gamePluginAssemblies = app.Services.GetRequiredService<GamePluginAssemblies>();
        var additionalAssemblies = gamePluginAssemblies.Assemblies
            .Append(typeof(KnockBoxPlatformExtensions).Assembly)
            .Append(typeof(KnockBox.Core.Components.Shared.SvgDrawingEngine).Assembly)
            // The WASM client assembly carries routable pages rendered under
            // InteractiveWebAssembly (e.g. the Phase 0 spike page). It is passed in
            // by the host because Platform must not reference the client project.
            .Concat(additionalClientAssemblies);

        // Plugin HTTP dispatcher — `/api/plugins/{routeIdentifier}/{**subPath}`.
        // Inert until a plugin opts in by implementing IGameEngineHttpHandler;
        // unknown route / missing handler / unknown room all 404.
        app.MapPluginApi();

        // Realtime transport for the WASM client.
        app.MapHub<Hubs.GameHub>("/hubs/game");

        // Serve runtime-streamed plugin client UI assemblies + their integrity
        // manifests. This is the path that loads a DLL the trimmed WASM client
        // never referenced at build time.
        MapPluginClientAssets(app);

        app.MapRazorComponents<TRootComponent>()
            .AddInteractiveServerRenderMode()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies([.. additionalAssemblies]);

        return app;
    }

    /// <summary>
    /// Maps <c>GET /_plugins/{routeIdentifier}/client/manifest.json</c> (integrity
    /// manifest) and <c>GET /_plugins/{routeIdentifier}/client/{assembly}.dll</c>
    /// (raw IL bytes) so the WASM client can stream + verify + load a game's UI
    /// assembly at room-entry time.
    /// </summary>
    private static void MapPluginClientAssets(WebApplication app)
    {
        app.MapGet("/_plugins/{routeIdentifier}/client/manifest.json",
            (string routeIdentifier, Plugins.IPluginClientAssetService assets) =>
            {
                return assets.TryGetManifest(routeIdentifier, out var manifest)
                    ? Results.Json(manifest)
                    : Results.NotFound();
            });

        app.MapGet("/_plugins/{routeIdentifier}/client/{assembly}.dll",
            (string routeIdentifier, string assembly, Plugins.IPluginClientAssetService assets) =>
            {
                return assets.TryGetAssemblyPath(routeIdentifier, assembly, out var path)
                    ? Results.File(path, "application/octet-stream")
                    : Results.NotFound();
            });
    }

    /// <summary>
    /// Resolves a plugin path to an absolute path. Relative paths are
    /// anchored at <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    private static string ResolvePluginsPath(string path)
        => Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);

    /// <summary>
    /// Mounts each discovered plugin's <c>wwwroot</c> folder under <c>/_content/{PluginName}</c>
    /// so that static assets (scoped CSS bundles, images, scripts) referenced by
    /// the plugin's Razor components resolve at runtime.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1873:Avoid potentially expensive logging",
        Justification = "Startup-only path.")]
    internal static void MapPluginStaticAssets(WebApplication app, string pluginsPath)
    {
        var logger = app.Services.GetRequiredService<ILogger<PluginLoader>>();

        if (!Directory.Exists(pluginsPath))
        {
            logger.LogInformation(
                "Plugins directory [{PluginsPath}] does not exist; no plugin static assets will be mounted.",
                pluginsPath);
            return;
        }

        var pluginsRoot = PluginPathGuard.NormalizeDirectory(pluginsPath);
        var mountedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.GetDirectories(pluginsPath))
        {
            var pluginName = Path.GetFileName(dir);
            var wwwrootPath = Path.Combine(dir, "wwwroot");
            if (!Directory.Exists(wwwrootPath))
                continue;

            // Reject plugin folders whose wwwroot escapes the plugins root via a
            // symlink / NTFS junction. A malicious or misconfigured plugin could
            // otherwise serve any file on disk under /_content/{PluginName}.
            var fullWwwroot = Path.GetFullPath(wwwrootPath);
            string? rejection = null;
            if (!PluginPathGuard.IsInsideRoot(pluginsRoot, fullWwwroot))
            {
                rejection = "wwwroot resolves outside the plugins root.";
            }
            else if (!PluginPathGuard.HasNoReparsePointEscape(pluginsRoot, fullWwwroot, out var reason))
            {
                rejection = reason;
            }
            if (rejection is not null)
            {
                logger.LogError(
                    "Refusing to mount plugin static assets for [{PluginName}] from [{WwwRootPath}]: {Reason}",
                    pluginName,
                    wwwrootPath,
                    rejection);
                continue;
            }

            var requestPath = $"/_content/{pluginName}";

            if (!mountedPaths.Add(requestPath))
            {
                // Two plugin folders sharing a name means both would claim the
                // same /_content/{Name} route. That's a deployment error: the
                // second plugin's static assets would never resolve. Fail fast
                // instead of limping along with broken CSS.
                throw new InvalidOperationException(
                    $"Duplicate plugin folder name [{pluginName}] detected at [{dir}]. " +
                    $"Two plugins cannot share the request path [{requestPath}]; " +
                    "rename one of the plugin folders or remove the duplicate.");
            }

            try
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(wwwrootPath),
                    RequestPath = requestPath,
                });
                logger.LogInformation(
                    "Mounted plugin static assets for [{PluginName}] at [{RequestPath}].",
                    pluginName,
                    requestPath);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to mount plugin static assets for [{PluginName}] from [{WwwRootPath}].",
                    pluginName,
                    wwwrootPath);
            }
        }
    }
}
