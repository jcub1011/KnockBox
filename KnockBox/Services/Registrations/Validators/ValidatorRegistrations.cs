using KnockBox.Data.Models.Testing;

namespace KnockBox.Services.Registrations.Validators
{
    public static class ValidatorRegistrations
    {
        public static IServiceCollection RegisterValidators(this IServiceCollection services)
        {
            services.AddSingleton<TestModelValidator>();

            return services;
        }
    }
}
