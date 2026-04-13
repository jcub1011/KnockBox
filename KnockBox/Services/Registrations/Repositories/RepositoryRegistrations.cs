using KnockBox.Data.Services.ClientStorage;

namespace KnockBox.Services.Registrations.Repositories
{
    public static class RepositoryRegistrations
    {
        public static IServiceCollection RegisterRepositories(this IServiceCollection services)
        {
            // Register client side storage
            services.AddScoped<ISessionStorageService, SessionStorageService>();
            services.AddScoped<ILocalStorageService, LocalStorageService>();

            return services;
        }
    }
}
