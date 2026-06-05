using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;

namespace KnockBox.AlphaChain.Tests.Unit.Support
{
    /// <summary>
    /// Reads/writes the room-scoped card-state services for a started game, so tests can probe state
    /// that used to live on <see cref="AlphaChainPlayerState"/> (shield multiplier, queued time penalty,
    /// hijack ban, era-rolled card bans, the double-letter fact) after the migration to card-contributed
    /// services.
    /// </summary>
    internal static class RoomStateProbe
    {
        private static T Service<T>(AlphaChainGameState state) where T : class
            => state.Context!.EvaluationServices.Get<T>()
               ?? throw new InvalidOperationException($"Room service {typeof(T).Name} is not registered.");

        private static AlphaChainPlayerState Player(AlphaChainGameState state, Guid userId) => state.GamePlayers[userId];

        public static double ShieldMultiplier(AlphaChainGameState state, Guid userId)
            => Service<IShieldService>(state).GetMultiplier(Player(state, userId));

        public static void SetShieldMultiplier(AlphaChainGameState state, Guid userId, double value)
        {
            // Seed via the service: GrantFresh to 1.0, then decay down to the requested value.
            var shield = Service<IShieldService>(state);
            var player = Player(state, userId);
            shield.GrantFresh(player);
            if (value < 1.0)
                shield.Decay(player, 1.0 - value);
        }

        public static bool PrismUsedThisEra(AlphaChainGameState state, Guid userId)
            => Service<IPrismGuard>(state).HasConsumed(Player(state, userId));

        public static int QueuedTimePenalty(AlphaChainGameState state, Guid userId)
            => Service<ITimePenaltyService>(state).Peek(Player(state, userId));

        public static char? PersonalBannedLetter(AlphaChainGameState state, Guid userId)
            => Service<IHijackBanService>(state).Peek(Player(state, userId));

        public static void MarkDoubleLetterPlayed(AlphaChainGameState state, Guid userId)
            => Service<IDoubleLetterTracker>(state).Mark(Player(state, userId));

        public static bool PlayedDoubleLetterWordThisEra(AlphaChainGameState state, Guid userId)
            => Service<IDoubleLetterTracker>(state).HasPlayed(Player(state, userId));

        public static char? CardBan(AlphaChainGameState state, Guid userId, ModifierId card)
            => Service<ICardBanService>(state).BanFor(Player(state, userId), card);

        public static void SetCardBan(AlphaChainGameState state, Guid userId, ModifierId card, char letter)
            => Service<ICardBanService>(state).Roll(Player(state, userId), card, letter);

        public static IReadOnlyCollection<char> CardBans(AlphaChainGameState state, Guid userId)
            => Service<ICardBanService>(state).BansFor(Player(state, userId));
    }
}
