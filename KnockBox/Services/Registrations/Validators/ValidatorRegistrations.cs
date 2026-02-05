using KnockBox.Data.Entities.Testing;

namespace KnockBox.Services.Registrations.Validators
{
    public static class ValidatorRegistrations
    {
        public static IServiceCollection RegisterValidators(this IServiceCollection services)
        {
            services.AddSingleton<TestEntityValidator>();

            return services;
        }
    }
}
