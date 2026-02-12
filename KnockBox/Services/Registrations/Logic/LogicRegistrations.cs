using KnockBox.Services.Logic.Filtering;

namespace KnockBox.Services.Registrations.Logic
{
    public static class LogicRegistrations
    {
        public static IServiceCollection RegisterLogic(this IServiceCollection services)
        {
            services.AddSingleton<IProfanityFilter, ProfanityFilter>();

            return services;
        }
    }
}
