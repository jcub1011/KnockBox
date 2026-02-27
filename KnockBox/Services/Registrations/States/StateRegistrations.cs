using KnockBox.Services.State.Games.DiceSimulator;

namespace KnockBox.Services.Registrations.States
{
    public static class StateRegistrations
    {
        public static IServiceCollection RegisterStateServices(this IServiceCollection services)
        {
            // Add states
            services.AddTransient<DiceSimulatorGameState>();
            return services;
        }
    }
}
