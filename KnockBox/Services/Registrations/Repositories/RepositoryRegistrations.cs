using KnockBox.Data.DbContexts;
using KnockBox.Data.Models.Testing;
using KnockBox.Data.Services.KeyProviders;
using KnockBox.Data.Services.KeyProviders.TestModels;
using KnockBox.Data.Services.Repositories;

namespace KnockBox.Services.Registrations.Repositories
{
    public static class RepositoryRegistrations
    {
        public static IServiceCollection AddRepositoryRegistrations(this IServiceCollection services)
        {
            // Register entity key providers
            services.AddSingleton<IEntityKeyProvider<TestModel, ApplicationDbContext>, TestModelKeyProvider>();

            // Register repositories
            services.AddSingleton(typeof(IRepository<>), typeof(BaseRepository<>));

            return services;
        }
    }
}
