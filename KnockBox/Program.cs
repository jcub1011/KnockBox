using KnockBox.Components;
using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Drawing;
using KnockBox.Data.DbContexts;
using KnockBox.Core.Services.Navigation;
using KnockBox.Services.Registrations.Logic;
using KnockBox.Services.Registrations.Repositories;
using KnockBox.Services.Registrations.States;
using KnockBox.Services.Registrations.Validators;
using Microsoft.EntityFrameworkCore;
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

            // Add dbcontext
            builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
                options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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

            if (Directory.Exists(pluginsPath))
            {
                foreach (var dir in Directory.GetDirectories(pluginsPath))
                {
                    var pluginName = Path.GetFileName(dir);
                    var wwwrootPath = Path.Combine(dir, "wwwroot");
                    if (Directory.Exists(wwwrootPath))
                    {
                        app.UseStaticFiles(new StaticFileOptions
                        {
                            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wwwrootPath),
                            RequestPath = $"/_content/{pluginName}"
                        });
                    }
                }
            }

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
