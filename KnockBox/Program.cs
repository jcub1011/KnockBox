using KnockBox.Components;
using KnockBox.Data.DbContexts;
using KnockBox.Services.Navigation;
using KnockBox.Services.Registrations.Logic;
using KnockBox.Services.Registrations.Repositories;
using KnockBox.Services.Registrations.States;
using KnockBox.Services.Registrations.Validators;
using Microsoft.EntityFrameworkCore;
using Serilog;

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

            // Add logic
            builder.Services.RegisterLogic();

            // Add navigation
            builder.Services.AddScoped<INavigationService, NavigationService>();

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
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
