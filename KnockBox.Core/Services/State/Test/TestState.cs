using KnockBox.Services.State.Shared;

namespace KnockBox.Services.State.Test
{
    public class TestState : AbstractState<TestState>
    {
        public double RandomNumber { get; private set; }
        public double RandomNumberDouble { get; private set; }

        public TestState()
        {
            // Register updaters here.
            RegisterUpdater(nameof(RandomNumber), async (ct) =>
            {
                await Task.Delay(100, ct); // Simulate database interaction.
                RandomNumber = 10.0;
            });

            RegisterUpdater(nameof(RandomNumberDouble), async (ct) =>
            {
                await Task.Delay(100, ct); // Simulate database interaction.
                RandomNumberDouble = RandomNumber * 2.0;
            },
            nameof(RandomNumber));
        }

        // Example usage
        public async Task UpdateAllPropertiesAsync(CancellationToken ct = default)
        {
            await UpdatePropertiesAsync(ct, nameof(RandomNumber), nameof(RandomNumberDouble));
        }
    }
}
