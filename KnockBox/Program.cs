using KnockBox.Components;
using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Drawing;
using KnockBox.Core.Services.Navigation;
using KnockBox.Services.Registrations.Logic;
using KnockBox.Services.Registrations.Repositories;
using KnockBox.Services.Registrations.States;
using KnockBox.Services.Registrations.Validators;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Serilog.Extensions.Logging;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("KnockBoxTests")]

namespace KnockBox
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "knockbox-.log");

            builder.Host.UseSerilog((context, services, loggerConfig) =>
                ApplySharedLoggerConfiguration(loggerConfig, context.Configuration, logPath)
                    .ReadFrom.Services(services));

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Add repositories
            builder.Services.RegisterRepositories();

            // Add validators
            builder.Services.RegisterValidators();

            // Add states
            builder.Services.RegisterStateServices();

            // Discover game plugins before registering logic. A second Serilog
            // pipeline is built here because DI (and therefore the host's
            // ILoggerFactory) is not yet wired up, but we still want plugin
            // discovery to produce real log entries with matching format. Both
            // writers target the same rolling file with shared: true -- kept
            // in sync via ApplySharedLoggerConfiguration.
            var bootstrapSerilog = ApplySharedLoggerConfiguration(
                    new Serilog.LoggerConfiguration(),
                    builder.Configuration,
                    logPath)
                .CreateLogger();
            using var bootstrapLoggerFactory = new SerilogLoggerFactory(bootstrapSerilog, dispose: true);
            var pluginLogger = bootstrapLoggerFactory.CreateLogger<PluginLoader>();
            var configuredPluginsPath = builder.Configuration["Plugins:Path"] ?? "games";
            var pluginsPath = Path.IsPathRooted(configuredPluginsPath)
                ? configuredPluginsPath
                : Path.Combine(AppContext.BaseDirectory, configuredPluginsPath);
            var pluginLoadResult = new PluginLoader(pluginLogger).LoadModules(pluginsPath);

            // Add logic
            var registrationLogger = bootstrapLoggerFactory.CreateLogger("KnockBox.Services.Registrations.Logic.LogicRegistrations");
            builder.Services.RegisterLogic(pluginLoadResult, registrationLogger);

            // Add navigation
            builder.Services.AddScoped<INavigationService, NavigationService>();

            // Add drawing services
            builder.Services.AddSingleton<ISvgClipboardService, SvgClipboardService>();

            var app = builder.Build();

            app.UseSerilogRequestLogging();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();

            MapPluginStaticAssets(app, pluginsPath);

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            LogBoundAddresses(app);

            app.Run();
        }

        /// <summary>
        /// Writes the addresses Kestrel actually bound to once the host has started,
        /// so the operator immediately sees the URL (e.g. <c>http://localhost:5276</c>)
        /// at which the web UI is reachable. Written twice: once via
        /// <see cref="Console.WriteLine(string)"/> so it is always visible on stdout
        /// regardless of configured Serilog minimum level / sink filters (this is
        /// vital startup information, not diagnostics), and once via
        /// <see cref="ILogger"/> so the rolling log file preserves a record of every
        /// bound address for post-hoc inspection.
        /// </summary>
        private static void LogBoundAddresses(WebApplication app)
        {
            var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            var logger = app.Services.GetRequiredService<ILogger<Program>>();

            lifetime.ApplicationStarted.Register(() =>
            {
                var server = app.Services.GetRequiredService<IServer>();
                var addressesFeature = server.Features.Get<IServerAddressesFeature>();
                var addresses = addressesFeature?.Addresses;

                if (addresses is null || addresses.Count == 0)
                {
                    const string noAddressMessage = "KnockBox host started, but no server addresses were reported.";
                    Console.WriteLine(noAddressMessage);
                    logger.LogInformation(noAddressMessage);
                    return;
                }

                foreach (var address in addresses)
                {
                    Console.WriteLine($"KnockBox web UI available at {address}");
                    logger.LogInformation("KnockBox web UI available at {Address}", address);
                }
            });
        }

        /// <summary>
        /// Applies the console + rolling-file sinks and formatting shared between
        /// the bootstrap Serilog logger (used during plugin discovery, before DI
        /// is built) and the host's <c>UseSerilog</c> configuration. Keeping both
        /// pipelines in a single helper guarantees they stay in sync -- notably
        /// <c>shared: true</c> on the file sink, which both writers need in order
        /// to coexist without file-lock errors.
        /// </summary>
        private static LoggerConfiguration ApplySharedLoggerConfiguration(
            LoggerConfiguration loggerConfig,
            IConfiguration configuration,
            string logPath)
        {
            const string outputTemplate =
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}";

            return loggerConfig
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console(outputTemplate: outputTemplate)
                .WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31,
                    shared: true,
                    outputTemplate: outputTemplate);
        }

        /// <summary>
        /// Mounts each discovered plugin's <c>wwwroot</c> folder under <c>/_content/{PluginName}</c>
        /// so that static assets (scoped CSS bundles, images, scripts) referenced by
        /// the plugin's Razor components resolve at runtime. Each mount is isolated
        /// by try/catch so a single misconfigured plugin doesn't prevent the host
        /// from starting. Duplicate plugin folder names are skipped with a warning.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1873:Avoid potentially expensive logging",
            Justification = "Startup-only path. Log volume is bounded by the number of plugins in games/; readability of structured mount/error messages is more valuable than LoggerMessage cache wins.")]
        private static void MapPluginStaticAssets(WebApplication app, string pluginsPath)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();

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
}
