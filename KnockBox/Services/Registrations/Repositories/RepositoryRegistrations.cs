using KnockBox.Data.DbContexts;
using KnockBox.Data.Entities.Testing;
using KnockBox.Data.Services.ClientStorage;
using KnockBox.Data.Services.KeyProviders;
using KnockBox.Data.Services.KeyProviders.TestEntities;
using KnockBox.Data.Services.Repositories;

namespace KnockBox.Services.Registrations.Repositories
{
    public static class RepositoryRegistrations
    {
        public static IServiceCollection RegisterRepositories(this IServiceCollection services)
        {
            // Register client side storage
            services.AddScoped<ISessionStorageService, SessionStorageService>();
            services.AddScoped<ILocalStorageService, LocalStorageService>();

            // Register entity key providers
            services.AddSingleton<IEntityKeyProvider<TestEntity, ApplicationDbContext>, TestEntityKeyProvider>();

            // Register repositories
            services.AddSingleton(typeof(IRepository<>), typeof(BaseRepository<>));

            return services;
        }
    }
}
