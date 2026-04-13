using KnockBox.Components;
using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Drawing;
using KnockBox.Core.Services.Navigation;
using KnockBox.Services.Registrations.Logic;
using KnockBox.Services.Registrations.Repositories;
using KnockBox.Services.Registrations.States;
using KnockBox.Services.Registrations.Validators;
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

            builder.Host.UseSerilog((context, services, loggerConfig) =>
                loggerConfig
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .WriteTo.Console());

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Add repositories
            builder.Services.RegisterRepositories();

            // Add validators
            builder.Services.RegisterValidators();

            // Add states
            builder.Services.RegisterStateServices();

            // Discover game plugins before registering logic
            var bootstrapSerilog = new Serilog.LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateLogger();
            using var bootstrapLoggerFactory = new SerilogLoggerFactory(bootstrapSerilog, dispose: true);
            var pluginLogger = bootstrapLoggerFactory.CreateLogger<PluginLoader>();
            var pluginsPath = Path.Combine(AppContext.BaseDirectory, "games");
            var pluginLoadResult = new PluginLoader(pluginLogger).LoadModules(pluginsPath);

            // Add logic
            builder.Services.RegisterLogic(pluginLoadResult);

            // Add navigation
            builder.Services.AddScoped<INavigationService, NavigationService>();

            // Add drawing services
            builder.Services.AddSingleton<ISvgClipboardService, SvgClipboardService>();

            var app = builder.Build();

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

            app.Run();
        }

        /// <summary>
        /// Mounts each discovered plugin's <c>wwwroot</c> folder under <c>/_content/{PluginName}</c>
        /// so that static assets (scoped CSS bundles, images, scripts) referenced by
        /// the plugin's Razor components resolve at runtime. Each mount is isolated
        /// by try/catch so a single misconfigured plugin doesn't prevent the host
        /// from starting. Duplicate plugin folder names are skipped with a warning.
        /// </summary>
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
