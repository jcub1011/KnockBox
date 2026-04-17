using KnockBox.Admin;
using KnockBox.Components;
using KnockBox.Core.Plugins;
using KnockBox.Platform;
using KnockBox.Services.Logic.Admin;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Logic.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Serilog;

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

            // Register IStoragePathService early so we can use it for logging and plugin discovery.
            var storagePath = new StoragePathService();
            builder.Services.AddSingleton<IStoragePathService>(storagePath);

            var logDirectory = storagePath.GetLogDirectory();
            var logPath = Path.Combine(logDirectory, "knockbox-.log");

            builder.Host.UseSerilog((context, services, loggerConfig) =>
                ApplySharedLoggerConfiguration(loggerConfig, context.Configuration, logPath)
                    .ReadFrom.Services(services));

            ConfigureAdminPort(builder, adminOptions.Port);

            // ── Platform services ────────────────────────────────────────────
            // Register the file-backed GameAvailabilityService BEFORE Platform
            // so Platform's TryAddSingleton yields to it.
            builder.Services.AddSingleton<IGameAvailabilityService, GameAvailabilityService>();

            builder.AddKnockBoxPlatform(options =>
            {
                options.PluginsPaths.Clear();
                options.PluginsPaths.Add(storagePath.GetFirstPartyPluginsDirectory());
                options.PluginsPaths.Add(storagePath.GetExternalPluginsDirectory());
            });

            // ── Admin-specific services ──────────────────────────────────────
            builder.Services.AddSingleton<IAdminLogService, AdminLogService>();

            builder.Services.AddSingleton<AdminMetricsService>();
            builder.Services.AddSingleton<IAdminMetricsService>(sp => sp.GetRequiredService<AdminMetricsService>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<AdminMetricsService>());

            builder.Services.AddScoped<CircuitHandler, AdminCircuitTracker>();

            // Razor Pages is used only for the admin login/logout endpoints.
            builder.Services.AddRazorPages(options =>
            {
                options.RootDirectory = "/Admin/Pages";
            });

            // Cookie auth + AdminOnly policy.
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

            var app = builder.Build();

            // ── Middleware pipeline ───────────────────────────────────────────
            app.UseKnockBoxPlatformMiddleware();

            // Port split: admin paths only on admin port.
            app.UseMiddleware<AdminPortMiddleware>(adminOptions.Port);

            app.UseAuthentication();
            app.UseAuthorization();

            // ── Endpoints ────────────────────────────────────────────────────
            app.MapKnockBoxPlatformEndpoints<App>();

            // Razor Pages host the admin login/logout endpoints.
            app.MapRazorPages();

            // Admin log download endpoint.
            MapAdminLogDownload(app, adminOptions.Port);

            LogBoundAddresses(app);

            app.Run();
        }

        /// <summary>
        /// Appends the admin port to the effective <c>Urls</c> configuration so
        /// Kestrel binds it alongside the main application URL.
        /// </summary>
        private static void ConfigureAdminPort(WebApplicationBuilder builder, int adminPort)
        {
            if (adminPort <= 0) return;

            var existing = builder.Configuration["Urls"] ?? "http://+:5276";
            var adminUrl = $"http://+:{adminPort}";

            if (!existing.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Any(u => u.Contains($":{adminPort}", StringComparison.Ordinal)))
            {
                builder.Configuration["Urls"] = $"{existing};{adminUrl}";
            }
        }

        /// <summary>
        /// Registers the /admin/logs/download/{fileName} endpoint.
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
        /// Writes the addresses Kestrel actually bound to once the host has started.
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
        /// Applies the console + rolling-file sinks shared between the bootstrap
        /// Serilog logger and the host's <c>UseSerilog</c> configuration.
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
    }
}
