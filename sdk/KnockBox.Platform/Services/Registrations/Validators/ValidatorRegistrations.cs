using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Services.Registrations.Validators
{
    public static class ValidatorRegistrations
    {
        public static IServiceCollection RegisterValidators(this IServiceCollection services)
        {
            return services;
        }
    }
}
