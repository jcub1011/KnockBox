using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Drawing;
using KnockBox.Core.Services.Navigation;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Registrations.Logic;
using KnockBox.Services.Registrations.Repositories;
using KnockBox.Services.Registrations.States;
using KnockBox.Services.Registrations.Validators;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
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
    public static WebApplicationBuilder AddKnockBoxPlatform(
        this WebApplicationBuilder builder,
        Action<KnockBoxPlatformOptions>? configure = null)
    {
        var options = new KnockBoxPlatformOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(Options.Create(options));

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Core service registrations
        builder.Services.RegisterRepositories();
        builder.Services.RegisterValidators();
        builder.Services.RegisterStateServices();

        // Default IGameAvailabilityService — yields to an explicit registration
        // made by the host (e.g. the production host's file-backed service).
        builder.Services.TryAddSingleton<IGameAvailabilityService, AllGamesEnabledService>();

        // Plugin discovery
        PluginLoadResult pluginLoadResult;

        if (options.PluginDiscovery == PluginDiscoveryMode.Explicit)
        {
            pluginLoadResult = new PluginLoadResult(
                options.ExplicitModules,
                options.ExplicitAssemblies);
        }
        else
        {
            var configuredPath = options.PluginsPath;
            var pluginsPath = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(AppContext.BaseDirectory, configuredPath);

            // Create a minimal bootstrap logger for plugin discovery.
            var bootstrapSerilog = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateLogger();
            using var bootstrapLoggerFactory = new SerilogLoggerFactory(bootstrapSerilog, dispose: true);
            var pluginLogger = bootstrapLoggerFactory.CreateLogger<PluginLoader>();

            pluginLoadResult = new PluginLoader(pluginLogger).LoadModules(pluginsPath);
        }

        // Logic registrations (platform version — no admin services)
        var registrationLogger = LoggerFactory
            .Create(b => b.AddConsole())
            .CreateLogger("KnockBox.Services.Registrations.Logic.LogicRegistrations");
        builder.Services.RegisterLogic(pluginLoadResult, registrationLogger);

        // Navigation + drawing services
        builder.Services.AddScoped<INavigationService, NavigationService>();
        builder.Services.AddSingleton<ISvgClipboardService, SvgClipboardService>();

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
        app.UseHttpsRedirection();
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
        this WebApplication app) where TRootComponent : IComponent
    {
        app.MapStaticAssets();

        var platformOptions = app.Services.GetRequiredService<IOptions<KnockBoxPlatformOptions>>().Value;

        if (platformOptions.PluginDiscovery == PluginDiscoveryMode.Directory)
        {
            var pluginsPath = Path.IsPathRooted(platformOptions.PluginsPath)
                ? platformOptions.PluginsPath
                : Path.Combine(AppContext.BaseDirectory, platformOptions.PluginsPath);
            MapPluginStaticAssets(app, pluginsPath);
        }

        app.MapRazorComponents<TRootComponent>()
            .AddInteractiveServerRenderMode();

        return app;
    }

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

        var mountedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.GetDirectories(pluginsPath))
        {
            var pluginName = Path.GetFileName(dir);
            var wwwrootPath = Path.Combine(dir, "wwwroot");
            if (!Directory.Exists(wwwrootPath))
                continue;

            var requestPath = $"/_content/{pluginName}";

            if (!mountedPaths.Add(requestPath))
            {
                logger.LogWarning(
                    "Duplicate plugin folder name [{PluginName}] detected at [{Dir}]; skipping to avoid route collision.",
                    pluginName,
                    dir);
                continue;
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
