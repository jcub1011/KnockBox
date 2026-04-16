using KnockBox.Admin;
using KnockBox.Components;
using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Drawing;
using KnockBox.Core.Services.Navigation;
using KnockBox.Services.Logic.Admin;
using KnockBox.Services.Registrations.Logic;
using KnockBox.Services.Registrations.Repositories;
using KnockBox.Services.Registrations.States;
using KnockBox.Services.Registrations.Validators;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
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

            // Bind Admin config early -- we need the port to extend the Urls string
            // before Kestrel reads it (Kestrel resolves Urls during builder.Build()).
            builder.Services.Configure<AdminOptions>(
                builder.Configuration.GetSection(AdminOptions.SectionName));
            var adminOptions = builder.Configuration
                .GetSection(AdminOptions.SectionName)
                .Get<AdminOptions>() ?? new AdminOptions();

            if (string.IsNullOrWhiteSpace(adminOptions.Username) || string.IsNullOrWhiteSpace(adminOptions.Password))
            {
                throw new InvalidOperationException("Admin Username and Password must be explicitly configured in appsettings.json.");
            }

            var logDirectory = Path.IsPathRooted(adminOptions.LogDirectory)
                ? adminOptions.LogDirectory
                : Path.Combine(AppContext.BaseDirectory, adminOptions.LogDirectory);
            var logPath = Path.Combine(logDirectory, "knockbox-.log");

            builder.Host.UseSerilog((context, services, loggerConfig) =>
                ApplySharedLoggerConfiguration(loggerConfig, context.Configuration, logPath)
                    .ReadFrom.Services(services));

            ConfigureAdminPort(builder, adminOptions.Port);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Razor Pages is used only for the admin login/logout endpoints,
            // because cookie auth sign-in must happen during an HTTP request
            // (not inside a Blazor circuit). Point the scanner at Admin/Pages
            // so the admin concerns stay colocated under /Admin/ instead of
            // a top-level /Pages folder used by the player-facing app.
            builder.Services.AddRazorPages(options =>
            {
                options.RootDirectory = "/Admin/Pages";
            });

            // Cookie auth + AdminOnly policy. The cookie is named uniquely so it
            // can't be confused with anything the player-facing surface might
            // later set.
            builder.Services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.Cookie.Name = ".KnockBox.Admin";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.LoginPath = "/admin/login";
                    options.LogoutPath = "/admin/logout";
                    options.AccessDeniedPath = "/admin/login";
                    options.SlidingExpiration = true;
                    options.ExpireTimeSpan = TimeSpan.FromHours(8);
                });
            builder.Services.AddAuthorization();

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

            // Port split: admin paths only on admin port, everything else on
            // the main port. Registered BEFORE anti-forgery/static-assets so
            // 404s for cross-port access don't leak through auth/cookies.
            app.UseMiddleware<AdminPortMiddleware>(adminOptions.Port);

            app.UseAntiforgery();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();

            MapPluginStaticAssets(app, pluginsPath);

            // Razor Pages host the admin login/logout endpoints.
            app.MapRazorPages();

            // Admin log download endpoint. Kept outside the Blazor router
            // because Blazor pages can't cleanly return a file stream.
            MapAdminLogDownload(app, adminOptions.Port);

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            LogBoundAddresses(app);

            app.Run();
        }

        /// <summary>
        /// Appends the admin port to the effective <c>Urls</c> configuration so
        /// Kestrel binds it alongside the main application URL. Called before
        /// <c>builder.Build()</c> because Kestrel reads <c>Urls</c> at build time.
        /// </summary>
        private static void ConfigureAdminPort(WebApplicationBuilder builder, int adminPort)
        {
            if (adminPort <= 0) return;

            var existing = builder.Configuration["Urls"] ?? "http://+:5276";
            var adminUrl = $"http://+:{adminPort}";

            // Only append if the port isn't already present in Urls (useful
            // when the operator has overridden Urls explicitly).
            if (!existing.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Any(u => u.Contains($":{adminPort}", StringComparison.Ordinal)))
            {
                builder.Configuration["Urls"] = $"{existing};{adminUrl}";
            }
        }

        /// <summary>
        /// Registers the /admin/logs/download/{fileName} endpoint used by the
        /// admin UI to download raw log files. Constrained to the admin port
        /// and guarded by the AdminOnly-equivalent auth requirement so the
        /// endpoint can never serve a file to an unauthenticated caller.
        /// </summary>
        private static void MapAdminLogDownload(WebApplication app, int adminPort)
        {
            app.MapGet("/admin/logs/download/{fileName}", (string fileName, IAdminLogService logs) =>
            {
                var absolutePath = logs.GetValidatedAbsolutePath(fileName);
                if (absolutePath is null)
                    return Results.NotFound();

                return Results.File(
                    absolutePath,
                    contentType: "text/plain",
                    fileDownloadName: fileName,
                    enableRangeProcessing: false);
            })
            .RequireAuthorization();
        }

        /// <summary>
        /// Writes the addresses Kestrel actually bound to once the host has started,
        /// so the operator immediately sees the URL (e.g. <c>http://+:5276</c>)
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
            var metrics = app.Services.GetRequiredService<IAdminMetricsService>();

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

                metrics.SetBoundAddresses(addresses);

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
